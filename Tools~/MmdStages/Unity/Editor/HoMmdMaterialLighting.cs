using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Explicit external-material edits; never reapply a stage recipe or change a scene.</summary>
public static class HoMmdMaterialLighting
{
    public static void Probe(HoMmdStageWork.Request request, Action<string> completed, Action<Exception> failed)
    {
        if (string.IsNullOrEmpty(request.scenePath) || string.IsNullOrEmpty(request.stageFolder) ||
            !request.scenePath.StartsWith(request.stageFolder.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new ArgumentException("Provide a scene and its explicit stage folder.");
        string output = Path.GetFullPath(request.outputPath);
        Directory.CreateDirectory(output);
        string[] names = { "authored", "full-neutral", "full-2x", "full-no-additional", "full-unshadowed-additional", "full-2x-headroom" };
        int variant = 0;
        Action next = null;
        next = () =>
        {
            if (variant == names.Length) { completed(output); return; }
            int current = variant++;
            var clones = new List<Material>();
            Action<ScriptableRenderContext, Camera> observer = null;
            int frames = 0;
            Action cleanup = () => {
                if (observer != null) RenderPipelineManager.endCameraRendering -= observer;
                foreach (var clone in clones) if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            };
            try
            {
                HoMmdStageCapture.Begin(request.scenePath, Path.Combine(output, names[current] + ".png"),
                    request.width, request.height, _ => {
                        cleanup();
                        EditorApplication.CallbackFunction resume = null;
                        resume = () => { EditorApplication.update -= resume; next(); };
                        EditorApplication.update += resume;
                        EditorApplication.QueuePlayerLoopUpdate();
                    }, error => { cleanup(); failed(error); }, camera => {
                        var sourceToClone = new Dictionary<Material, Material>();
                        foreach (var renderer in camera.gameObject.scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
                        {
                            var assigned = renderer.sharedMaterials;
                            for (int i = 0; i < assigned.Length; i++)
                            {
                                var original = assigned[i];
                                if (original == null || !original.HasProperty("_HoAdditionalLightIntensity") ||
                                    !AssetDatabase.GetAssetPath(original).StartsWith(request.stageFolder.TrimEnd('/') + "/", StringComparison.Ordinal)) continue;
                                if (!sourceToClone.TryGetValue(original, out var clone))
                                {
                                    clone = new Material(original) { name = original.name + " (temporary lighting probe)", hideFlags = HideFlags.HideAndDontSave };
                                    sourceToClone.Add(original, clone); clones.Add(clone);
                                    if (current > 0)
                                    {
                                        clone.SetFloat("_HoRPShadowMode", 2);
                                        clone.SetFloat("_HoAdditionalLightIntensity", current == 3 ? 0 : current == 2 || current == 5 ? 2 : 1);
                                        clone.SetFloat("_HoAdditionalLightShadowStrength", current == 4 ? 0 : 1);
                                        if (current == 5) clone.SetFloat("_LightMaxLimit", 32);
                                    }
                                }
                                assigned[i] = clone;
                            }
                            renderer.sharedMaterials = assigned;
                        }
                        File.WriteAllText(Path.Combine(output, names[current] + "-materials.json"),
                            JsonUtility.ToJson(new Report { action = "temporary-probe", folder = request.stageFolder,
                                materials = sourceToClone.Select(pair => new MaterialRow { material = AssetDatabase.GetAssetPath(pair.Key),
                                    shader = pair.Key.shader.name, before = Read(pair.Key), after = Read(pair.Value) }).ToArray() }, true));
                        observer = (context, rendered) => {
                            if (rendered != camera || ++frames < Math.Max(1, request.warmup - 1)) return;
                            RenderPipelineManager.endCameraRendering -= observer;
                            string audit = HoLightingAudit.Capture();
                            File.Copy(audit, Path.Combine(output, names[current] + "-lighting.json"), true);
                        };
                        RenderPipelineManager.endCameraRendering += observer;
                    }, request.warmup);
            }
            catch (Exception error) { cleanup(); failed(error); }
        };
        next();
    }

    [Serializable] public sealed class Plan { public Change[] changes; }
    [Serializable] public sealed class Change { public string material; public Value[] properties; }
    [Serializable] public sealed class Value { public string name; public float value; }
    [Serializable] public sealed class PropertyRow
    {
        public string name;
        public float value;
        public bool explicitlySaved;
    }
    [Serializable] public sealed class MaterialRow
    {
        public string material, shader, backup;
        public PropertyRow[] before, after;
    }
    [Serializable] public sealed class Report
    {
        public string action, folder, createdUtc;
        public int changed;
        public MaterialRow[] materials;
    }

    static readonly string[] Inputs = {
        "_HoRPShadowMode", "_HoAdditionalLightIntensity", "_HoAdditionalLightShadowStrength",
        "_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_ShadowEnvStrength",
        "_UseShadow", "_ShadowStrength", "_ShadowReceive", "_Shadow2ndReceive",
        "_UseRealtimeAO", "_AOContrast", "_AODarkStrength"
    };
    static readonly HashSet<string> EditableInputs = new HashSet<string>(Inputs, StringComparer.Ordinal);

    // Full waits for per-light Ho ShadowCast matching; the current receiver aggregates the pool.
    public static void InitializeStageMaterial(Material material)
    {
        SetIfSupported(material, "_HoRPShadowMode", 1);
        SetIfSupported(material, "_HoAdditionalLightIntensity", 1);
        SetIfSupported(material, "_HoAdditionalLightShadowStrength", 1);
    }

    public static string Execute(HoMmdStageWork.Request request)
    {
        string folder = (request.stageFolder ?? "").Replace('\\', '/').TrimEnd('/');
        if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Split('/').Contains("..") ||
            !AssetDatabase.IsValidFolder(folder))
            throw new ArgumentException("Provide an explicit existing Assets folder.");
        bool patch = request.action == "material-lighting-patch";
        if (!patch && request.action != "material-lighting-audit")
            throw new ArgumentException("Unsupported material operation.");
        string output = string.IsNullOrEmpty(request.outputPath)
            ? Path.GetFullPath("Library/HoMmdStages/MaterialLighting/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json")
            : Path.GetFullPath(request.outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Change[] changes = patch ? ReadPlan(request.settingsPath).changes : Array.Empty<Change>();
        var targets = patch ? changes.Select(c => c.material).ToArray() :
            AssetDatabase.FindAssets("t:Material", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new ArgumentException("Specify each material once.");

        // Preflight every target and field before saving the first asset.
        var materials = new List<Material>();
        for (int i = 0; i < targets.Length; i++)
        {
            string path = targets[i];
            if (!path.StartsWith(folder + "/", StringComparison.Ordinal) || path.Split('/').Contains(".."))
                throw new ArgumentException("Material is outside the requested folder: " + path);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || !material.HasProperty("_HoRPShadowMode"))
            {
                if (patch) throw new InvalidOperationException("Not a supported Ho lilToon material: " + path);
                materials.Add(null); continue;
            }
            if (patch)
            {
                if (EditorUtility.IsDirty(material))
                    throw new InvalidOperationException("Material has unsaved edits: " + path);
                foreach (var value in changes[i].properties ?? Array.Empty<Value>())
                {
                    if (!EditableInputs.Contains(value.name) || !material.HasProperty(value.name) ||
                        float.IsNaN(value.value) || float.IsInfinity(value.value))
                        throw new ArgumentException("Unsupported material field/value: " + path + " " + value.name);
                    if (value.name == "_HoRPShadowMode" && value.value != 1 && value.value != 2)
                        throw new ArgumentException("HoRP shadow mode must be Main=1 or Full=2.");
                }
            }
            materials.Add(material);
        }

        var rows = new List<MaterialRow>();
        int changed = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;
            var row = new MaterialRow { material = targets[i], shader = material.shader.name, before = Read(material) };
            if (patch && (changes[i].properties ?? Array.Empty<Value>()).Any(
                    v => !Mathf.Approximately(material.GetFloat(v.name), v.value) ||
                         !row.before.Any(p => p.name == v.name && p.explicitlySaved)))
            {
                row.backup = Path.Combine(Path.GetDirectoryName(output), "Backups",
                    AssetDatabase.AssetPathToGUID(targets[i]) + ".mat");
                Directory.CreateDirectory(Path.GetDirectoryName(row.backup));
                if (!File.Exists(row.backup)) File.Copy(targets[i], row.backup);
                Undo.RecordObject(material, "Ho 场景光照参数调整");
                foreach (var value in changes[i].properties) material.SetFloat(value.name, value.value);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                changed++;
            }
            row.after = Read(material);
            rows.Add(row);
        }
        File.WriteAllText(output, JsonUtility.ToJson(new Report {
            action = request.action, folder = folder, createdUtc = DateTime.UtcNow.ToString("O"),
            changed = changed, materials = rows.ToArray()
        }, true));
        return output;
    }

    static Plan ReadPlan(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new ArgumentException("Provide --settings with an explicit material patch plan.");
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(path));
        if (plan == null || plan.changes == null) throw new ArgumentException("Missing changes array.");
        return plan;
    }

    static PropertyRow[] Read(Material material)
    {
        var saved = new HashSet<string>(StringComparer.Ordinal);
        using (var serialized = new SerializedObject(material))
        {
            var floats = serialized.FindProperty("m_SavedProperties.m_Floats");
            for (int i = 0; floats != null && i < floats.arraySize; i++)
                saved.Add(floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue);
        }
        return Inputs.Where(material.HasProperty).Select(name => new PropertyRow {
            name = name, value = material.GetFloat(name), explicitlySaved = saved.Contains(name)
        }).ToArray();
    }
    static void SetIfSupported(Material material, string name, float value)
    { if (material != null && material.HasProperty(name)) material.SetFloat(name, value); }
}
