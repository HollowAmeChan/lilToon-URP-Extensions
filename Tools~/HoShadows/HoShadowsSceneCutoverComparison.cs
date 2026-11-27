using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HoShader.EffectURP.Editor;
using HoShader.HairURP.Editor;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class HoShadowsSceneCutoverComparison
{
    private const int Width = 640;
    private const int Height = 360;
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    private static readonly string[] Scenes =
    {
        "Assets/00动画/角色/羽啾/羽啾.unity",
        "Assets/00动画/MMD场景/H2Cu阿相/圣诞万花镜/圣诞万花镜.unity"
    };
    private static readonly HashSet<string> SupportedShaders = new()
    {
        "Ho/Skin/URP/Stylized", "Ho/Skin/URP/Eye", "Ho/Cloth/URP/Cloth", "Ho/Environment/URP/Lit",
        "Ho/Hair/URP/Hair", EffectMaterialUtility.ShaderName
    };
    private const string PreviewKeyword = "_HO_SHADOWS_SCREEN_PREVIEW";
    private const string EffectSurfaceKeyword = "_EFFECT_SCREEN_SHADOW_SURFACE";
    private const string LateHairKeyword = "_HAIR_TRANSPARENT_SCREEN_SHADOW_SURFACE";
    private const string EarlyHairKeyword = "_HAIR_EARLY_TRANSPARENT_SCREEN_SHADOW_SURFACE";

    [Serializable] private sealed class MigrationPlan
    {
        public string project = null;
        public MigrationMaterial[] materialRecords = null;
    }

    [Serializable] private sealed class MigrationMaterial
    {
        public string path = null, action = null, sha256 = null;
    }

    [Serializable] private sealed class SceneCase
    {
        public string scene, camera;
        public bool reframedSubject;
        public Vector3 cameraPosition;
        public float fieldOfView;
        public int previewMaterials, upgradedToFull, changedPixels, stableChangedPixels, baselineNoisePixels;
        public int changedAtBudget16, previewHair, previewEffect, upgradedOnEnabledRenderers;
        public int earlyTransparentHair, lateTransparentHair;
        public string[] fullCandidateAssets;
        public float maximum, mean, baselineNoise, maximumAtBudget16, meanAtBudget16, legacyPeak;
        public int legacyScenePixels, witnessRenderers;
        public float legacySubmitMilliseconds, managedSubmitMilliseconds, wideManagedSubmitMilliseconds;
        public List<IsolationCase> isolation = new();
    }

    [Serializable] private sealed class IsolationCase
    {
        public string arm;
        public int previewMaterials, stableChangedPixels;
        public float maximum;
    }

    [Serializable] private sealed class Report
    {
        public string unity, graphics, error, migrationPlan;
        public bool finished, authoredEffects;
        public List<SceneCase> scenes = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_OUTPUT");
        if (!Directory.Exists(output) || File.Exists(Path.Combine(output, "scene-cutover.json")))
            throw new InvalidOperationException("A fresh output directory is required.");
        string backup = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_PILOT_OUTPUT");
        if (!Directory.Exists(backup) || Directory.EnumerateFileSystemEntries(backup).Any())
            throw new InvalidOperationException("An empty backup directory is required for the pilot cutover.");
        string project = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_PILOT_PROJECT");
        if (!string.Equals(Application.dataPath.Replace('\\', '/'), project?.Replace('\\', '/').TrimEnd('/') + "/Assets",
            StringComparison.OrdinalIgnoreCase) || !Application.dataPath.Replace('\\', '/').StartsWith("D:/Temp/HoShadows-P5-"))
            throw new InvalidOperationException("This comparison requires the isolated Ho Shadows scene snapshot.");

        var report = new Report
        {
            unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
            authoredEffects = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_AUTHORED") == "1",
            migrationPlan = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_PLAN")
        };
        string filter = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_FILTER");
        string selectedScene = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_SCENE");
        if (!string.IsNullOrWhiteSpace(selectedScene) && (!selectedScene.StartsWith("Assets/", StringComparison.Ordinal)
            || !selectedScene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || selectedScene.Contains("..")))
            throw new InvalidOperationException("Use the saved scene's project-relative asset path.");
        var comparisonScenes = string.IsNullOrWhiteSpace(selectedScene) ? Scenes : new[] { selectedScene };
        var rows = new List<(string path, SceneCase row, Color[] legacy, Color[] repeat)>();
        try
        {
            var fullCandidates = ReadFullCandidates(report.migrationPlan);
            foreach (string path in comparisonScenes.Where(path => string.IsNullOrWhiteSpace(filter) || path.Contains(filter)))
            {
                var row = new SceneCase { scene = path };
                report.scenes.Add(row);
                var first = Render(path, row, output, "legacy", 0, report.authoredEffects, fullCandidates, out float milliseconds);
                var second = Render(path, row, output, "legacy-repeat", 0, report.authoredEffects, fullCandidates, out _);
                row.legacySubmitMilliseconds = milliseconds;
                row.legacyPeak = first.Max(MaxRgb);
                row.legacyScenePixels = SceneSignalPixels(first);
                if (row.legacyScenePixels == 0) throw new InvalidOperationException("Missing legacy scene color for " + path);
                rows.Add((path, row, first, second));
            }

            if (rows.Count == 0) throw new InvalidOperationException("No saved scenes match the comparison filter.");

            HoShadowsPilotCutover.Run();

            foreach (var entry in rows)
            {
                var row = entry.row;
                var first = Render(entry.path, row, output, "managed-budget4", 4, report.authoredEffects, fullCandidates, out row.managedSubmitMilliseconds);
                var second = Render(entry.path, row, output, "managed-budget16", 16, report.authoredEffects, fullCandidates, out row.wideManagedSubmitMilliseconds);
                double sum = 0;
                double sumWide = 0;
                for (int index = 0; index < first.Length; index++)
                {
                    float noise = MaxRgb(entry.legacy[index] - entry.repeat[index]);
                    float difference = MaxRgb(first[index] - entry.legacy[index]);
                    float wider = MaxRgb(second[index] - entry.legacy[index]);
                    row.baselineNoise = Mathf.Max(row.baselineNoise, noise);
                    row.maximum = Mathf.Max(row.maximum, difference);
                    row.maximumAtBudget16 = Mathf.Max(row.maximumAtBudget16, wider);
                    sum += difference;
                    sumWide += wider;
                    if (noise > .002f) row.baselineNoisePixels++;
                    if (difference > .002f)
                    {
                        row.changedPixels++;
                        if (noise <= .002f && MaxRgb(first[index] - entry.repeat[index]) > .002f) row.stableChangedPixels++;
                    }
                    if (wider > .002f) row.changedAtBudget16++;
                }
                row.mean = (float)(sum / first.Length);
                row.meanAtBudget16 = (float)(sumWide / first.Length);
                if (Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_ISOLATE") == "1")
                    foreach (string arm in new[] { "producer-only", "core-only", "hair-only", "effect-only", "disable-character", "disable-capture" })
                    {
                        var sample = new SceneCase();
                        var image = Render(entry.path, sample, output, arm, 4, report.authoredEffects, fullCandidates, out _, arm);
                        var result = new IsolationCase { arm = arm, previewMaterials = sample.previewMaterials };
                        for (int index = 0; index < image.Length; index++)
                        {
                            float difference = MaxRgb(image[index] - entry.legacy[index]);
                            result.maximum = Mathf.Max(result.maximum, difference);
                            if (difference > .002f && MaxRgb(entry.repeat[index] - entry.legacy[index]) <= .002f
                                && MaxRgb(image[index] - entry.repeat[index]) > .002f) result.stableChangedPixels++;
                        }
                        row.isolation.Add(result);
                        Debug.Log($"HO_SHADOWS_SCENE_ISOLATION {arm}, preview={result.previewMaterials}, changed={result.stableChangedPixels}, max={result.maximum}");
                    }
                Debug.Log($"HO_SHADOWS_SCENE_CUTOVER {row.camera}, preview={row.previewMaterials}, upgrade={row.upgradedToFull}, max={row.maximum} mean={row.mean} wide={row.meanAtBudget16}");
                File.WriteAllText(Path.Combine(output, "scene-cutover.json"), JsonUtility.ToJson(report, true));
            }
            report.finished = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally { File.WriteAllText(Path.Combine(output, "scene-cutover.json"), JsonUtility.ToJson(report, true)); }
    }

    private static Dictionary<string, string> ReadFullCandidates(string filename)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(filename)) return result;
        var plan = JsonUtility.FromJson<MigrationPlan>(File.ReadAllText(filename));
        if (string.IsNullOrWhiteSpace(plan?.project) || plan.materialRecords == null)
            throw new InvalidOperationException("The migration plan has no source project or material inventory.");
        string project = plan.project.TrimEnd('/') + "/";
        foreach (var item in plan.materialRecords.Where(material => material.action == "review_full_candidate"))
        {
            if (!item.path.StartsWith(project + "Assets/", StringComparison.OrdinalIgnoreCase) || item.sha256?.Length != 64)
                throw new InvalidOperationException("Invalid asset path or SHA-256 in the Full candidate list.");
            result.Add(item.path.Substring(project.Length), item.sha256);
        }
        return result;
    }

    private static Color[] Render(string path, SceneCase item, string output, string label, int budget, bool authoredEffects,
        Dictionary<string, string> fullCandidates, out float milliseconds, string isolation = null)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Camera camera = Camera.main;
        if (camera == null || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base)
            throw new InvalidOperationException("No saved Base Main Camera for " + path);
        item.camera = camera.name;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_WITNESSES_ONLY") == "1")
            item.witnessRenderers = KeepCandidateDraws(scene, fullCandidates);
        if (Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_FRAME_SUBJECT") == "1")
        {
            FrameSubject(scene, camera);
            item.reframedSubject = true;
        }
        item.cameraPosition = camera.transform.position;
        item.fieldOfView = camera.fieldOfView;
        if (!authoredEffects)
        {
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
        }
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var debugHud = renderer.rendererFeatures.OfType<HoDebugTileRendererFeature>().SingleOrDefault();
        bool hudActive = debugHud != null && debugHud.isActive;
        if (hudActive) debugHud.SetActive(false);
        var planar = renderer.rendererFeatures.OfType<HoPlanarReflectionRendererFeature>().SingleOrDefault();
        bool planarActive = !authoredEffects && planar != null && planar.isActive;
        if (planarActive) planar.SetActive(false);
        var restore = new Dictionary<Material, (bool keyword, bool effectSurface, bool earlyHair, bool lateHair, float mode)>();
        var upgraded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledRenderers = new HashSet<Material>();
        var managed = renderer.rendererFeatures.OfType<HoShadowsRendererFeature>().SingleOrDefault();
        var capture = renderer.rendererFeatures.OfType<HoCharacterSpecializationRendererFeature>().SingleOrDefault();
        bool previousCharacter = managed != null && managed.ManagedCharacterShadowsEnabled;
        bool previousCapture = capture != null && capture.CaptureShadowInputsEnabled;
        bool previewTransparentHair = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_HAIR_TRANSPARENT") == "1";
        try
        {
            if (budget > 0)
            {
                if (managed == null || !managed.isActive) throw new InvalidOperationException("Missing managed renderer shadow feature.");
                var state = new SerializedObject(managed);
                state.FindProperty("additionalLightBudget").intValue = budget;
                state.ApplyModifiedPropertiesWithoutUndo();
                if (isolation == "disable-character") managed.ManagedCharacterShadowsEnabled = false;
                if (isolation == "disable-capture" && capture != null) capture.CaptureShadowInputsEnabled = false;

                foreach (var mesh in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
                {
                    foreach (Material material in mesh.sharedMaterials)
                    {
                        if (material && mesh.enabled && mesh.gameObject.activeInHierarchy) enabledRenderers.Add(material);
                        if (!material || !material.shader || !SupportedShaders.Contains(material.shader.name) || restore.ContainsKey(material))
                            continue;
                        bool hair = material.shader.name == "Ho/Hair/URP/Hair";
                        bool effect = material.shader.name == EffectMaterialUtility.ShaderName;
                        if (isolation == "producer-only" || (isolation == "core-only" && (hair || effect))
                            || (isolation == "hair-only" && !hair) || (isolation == "effect-only" && !effect)) continue;
                        if (!material.shader.keywordSpace.FindKeyword(PreviewKeyword).isValid)
                            continue;
                        if (material.renderQueue > 2500 && material.shader.name == "Ho/Environment/URP/Lit")
                            continue;
                        if (material.renderQueue > 2500 && material.shader.name == "Ho/Cloth/URP/Cloth"
                            && (!material.HasProperty("_UseHoOIT") || material.GetFloat("_UseHoOIT") < .5f))
                            continue;
                        bool hairBlend = hair && material.GetFloat("_SurfaceMode") > 1.5f;
                        if (hairBlend && (!previewTransparentHair || (material.renderQueue <= 2500
                            ? !managed.EarlyTransparentShadowsEnabled : !managed.TransparentEyeShadowsEnabled)))
                            continue;
                        if (hair && !hairBlend && material.renderQueue > 2500) continue;
                        float mode = material.HasProperty("_HoRPShadowMode") ? material.GetFloat("_HoRPShadowMode") : 0;
                        restore.Add(material, (material.IsKeywordEnabled(PreviewKeyword),
                            effect && material.IsKeywordEnabled(EffectSurfaceKeyword),
                            hair && material.IsKeywordEnabled(EarlyHairKeyword), hair && material.IsKeywordEnabled(LateHairKeyword), mode));
                        if (effect) EffectMaterialUtility.SetScreenShadowPreview(material, true);
                        else if (hair) HairMaterialUtility.SetScreenShadowPreview(material, true);
                        else material.EnableKeyword(PreviewKeyword);
                        string assetPath = AssetDatabase.GetAssetPath(material);
                        if (mode >= .5f && mode < 1.5f && fullCandidates.TryGetValue(assetPath, out string expectedHash))
                        {
                            using var sha256 = SHA256.Create();
                            string savedPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
                            using var savedMaterial = File.OpenRead(savedPath);
                            string actualHash = BitConverter.ToString(sha256.ComputeHash(savedMaterial)).Replace("-", "");
                            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("The saved Full candidate does not match the planned material: " + assetPath);
                            material.SetFloat("_HoRPShadowMode", 2);
                            upgraded.Add(assetPath);
                        }
                    }
                }
                item.previewMaterials = restore.Count;
                item.previewHair = restore.Keys.Count(material => material.shader.name == "Ho/Hair/URP/Hair");
                item.earlyTransparentHair = restore.Keys.Count(material => material.IsKeywordEnabled(EarlyHairKeyword));
                item.lateTransparentHair = restore.Keys.Count(material => material.IsKeywordEnabled(LateHairKeyword));
                item.previewEffect = restore.Keys.Count(material => material.shader.name == EffectMaterialUtility.ShaderName
                    && material.IsKeywordEnabled(EffectSurfaceKeyword));
                item.upgradedToFull = upgraded.Count;
                item.upgradedOnEnabledRenderers = enabledRenderers.Count(material => upgraded.Contains(AssetDatabase.GetAssetPath(material)));
                item.fullCandidateAssets = upgraded.OrderBy(asset => asset, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            string stem = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "-" + label + ".exr");
            Debug.Log($"HO_SHADOWS_SCENE_SNAPSHOT {path} {label}");
            return Read(camera, stem, authoredEffects, out milliseconds);
        }
        finally
        {
            if (hudActive) debugHud.SetActive(true);
            if (planarActive) planar.SetActive(true);
            if (isolation == "disable-character" && managed != null) managed.ManagedCharacterShadowsEnabled = previousCharacter;
            if (isolation == "disable-capture" && capture != null) capture.CaptureShadowInputsEnabled = previousCapture;
            foreach (var entry in restore)
            {
                entry.Key.SetKeyword(new LocalKeyword(entry.Key.shader, PreviewKeyword), entry.Value.keyword);
                if (entry.Key.shader.name == EffectMaterialUtility.ShaderName)
                    entry.Key.SetKeyword(new LocalKeyword(entry.Key.shader, EffectSurfaceKeyword), entry.Value.effectSurface);
                if (entry.Key.shader.name == "Ho/Hair/URP/Hair")
                {
                    entry.Key.SetKeyword(new LocalKeyword(entry.Key.shader, EarlyHairKeyword), entry.Value.earlyHair);
                    entry.Key.SetKeyword(new LocalKeyword(entry.Key.shader, LateHairKeyword), entry.Value.lateHair);
                }
                if (entry.Key.HasProperty("_HoRPShadowMode")) entry.Key.SetFloat("_HoRPShadowMode", entry.Value.mode);
            }
        }
    }

    private static Color[] Read(Camera camera, string filename, bool authoredEffects, out float elapsed)
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var result = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        RenderTexture active = RenderTexture.active;
        try
        {
            void Submit()
            {
                if (authoredEffects)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            Submit();
            Submit();
            var timing = System.Diagnostics.Stopwatch.StartNew();
            Submit();
            RenderTexture.active = target;
            result.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            elapsed = (float)timing.Elapsed.TotalMilliseconds;
            result.Apply(false);
            File.WriteAllBytes(filename, result.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            return result.GetPixels();
        }
        finally
        {
            RenderTexture.active = active;
            UnityEngine.Object.DestroyImmediate(result);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static int SceneSignalPixels(Color[] image)
    {
        Color reference = image[(Height / 2) * Width + Width / 2];
        int count = 0;
        for (int y = Height / 4; y < Height * 3 / 4; y++)
            for (int x = Width / 4; x < Width * 3 / 4; x++)
                if (MaxRgb(image[y * Width + x] - reference) > .01f) count++;
        return count;
    }

    private static void FrameSubject(Scene scene, Camera camera)
    {
        var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SkinnedMeshRenderer>());
        var portraits = renderers.Where(mesh => mesh.enabled && mesh.sharedMaterials.Any(material => material != null
            && material.shader != null && material.shader.name == "Ho/Skin/URP/Stylized")).ToArray();
        if (portraits.Length == 0) throw new InvalidOperationException("No visible Skin subject to reframe in " + scene.path);
        Bounds subject = portraits[0].bounds;
        foreach (var mesh in portraits.Skip(1)) subject.Encapsulate(mesh.bounds);
        if (subject.extents.sqrMagnitude <= .001f || camera.orthographic)
            throw new InvalidOperationException("A perspective camera and nonempty subject bounds are required for reframing.");

        float radius = subject.extents.magnitude;
        float fieldOfView = Mathf.Max(camera.fieldOfView, 25f);
        float distance = radius / Mathf.Sin(fieldOfView * .5f * Mathf.Deg2Rad) + camera.nearClipPlane;
        camera.fieldOfView = fieldOfView;
        camera.transform.position = subject.center - camera.transform.forward * distance;
        camera.useOcclusionCulling = false;
    }

    private static int KeepCandidateDraws(Scene scene, Dictionary<string, string> fullCandidates)
    {
        var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>());
        int witnesses = 0;
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
            if (renderer.sharedMaterials.Any(material => material != null && fullCandidates.ContainsKey(AssetDatabase.GetAssetPath(material))))
            {
                witnesses++;
                continue;
            }
            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            else
                renderer.enabled = false;
        }
        if (witnesses == 0) throw new InvalidOperationException("The scene contains no eligible Full-candidate renderers.");
        return witnesses;
    }

    private static float MaxRgb(Color value) => Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b));
}
