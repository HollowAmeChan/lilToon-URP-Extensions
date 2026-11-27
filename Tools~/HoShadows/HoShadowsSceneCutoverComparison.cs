using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        "Ho/Skin/URP/Stylized", "Ho/Skin/URP/Eye", "Ho/Cloth/URP/Cloth", "Ho/Environment/URP/Lit"
    };

    [Serializable] private sealed class SceneCase
    {
        public string scene, camera;
        public int previewMaterials, upgradedToFull, changedPixels, stableChangedPixels, baselineNoisePixels;
        public int changedAtBudget16;
        public float maximum, mean, baselineNoise, maximumAtBudget16, meanAtBudget16, legacyPeak;
        public float legacySubmitMilliseconds, managedSubmitMilliseconds, wideManagedSubmitMilliseconds;
    }

    [Serializable] private sealed class Report
    {
        public string unity, graphics, error;
        public bool finished, authoredEffects;
        public List<SceneCase> scenes = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_OUTPUT");
        if (!Directory.Exists(output) || File.Exists(Path.Combine(output, "scene-cutover.json")))
            throw new InvalidOperationException("A fresh output directory is required.");
        string project = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_PILOT_PROJECT");
        if (!string.Equals(Application.dataPath.Replace('\\', '/'), project?.Replace('\\', '/').TrimEnd('/') + "/Assets",
            StringComparison.OrdinalIgnoreCase) || !Application.dataPath.Replace('\\', '/').StartsWith("D:/Temp/HoShadows-P5-"))
            throw new InvalidOperationException("This comparison requires the isolated Ho Shadows scene snapshot.");

        var report = new Report
        {
            unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
            authoredEffects = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_COMPARE_AUTHORED") == "1"
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
            foreach (string path in comparisonScenes.Where(path => string.IsNullOrWhiteSpace(filter) || path.Contains(filter)))
            {
                var row = new SceneCase { scene = path };
                report.scenes.Add(row);
                var first = Render(path, row, output, "legacy", 0, report.authoredEffects, out float milliseconds);
                var second = Render(path, row, output, "legacy-repeat", 0, report.authoredEffects, out _);
                row.legacySubmitMilliseconds = milliseconds;
                row.legacyPeak = first.Max(MaxRgb);
                if (row.legacyPeak < .01f) throw new InvalidOperationException("Missing legacy camera signal for " + path);
                rows.Add((path, row, first, second));
            }

            if (rows.Count == 0) throw new InvalidOperationException("No saved scenes match the comparison filter.");

            HoShadowsPilotCutover.Run();

            foreach (var entry in rows)
            {
                var row = entry.row;
                var first = Render(entry.path, row, output, "managed-budget4", 4, report.authoredEffects, out row.managedSubmitMilliseconds);
                var second = Render(entry.path, row, output, "managed-budget16", 16, report.authoredEffects, out row.wideManagedSubmitMilliseconds);
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

    private static Color[] Render(string path, SceneCase item, string output, string label, int budget, bool authoredEffects, out float milliseconds)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Camera camera = Camera.main;
        if (camera == null || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base)
            throw new InvalidOperationException("No saved Base Main Camera for " + path);
        item.camera = camera.name;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (!authoredEffects)
        {
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
        }
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var planar = renderer.rendererFeatures.OfType<HoPlanarReflectionRendererFeature>().SingleOrDefault();
        bool planarActive = !authoredEffects && planar != null && planar.isActive;
        if (planarActive) planar.SetActive(false);
        var restore = new Dictionary<Material, (bool keyword, float mode)>();
        var managed = renderer.rendererFeatures.OfType<HoShadowsRendererFeature>().SingleOrDefault();
        try
        {
            if (budget > 0)
            {
                if (managed == null || !managed.isActive) throw new InvalidOperationException("Missing managed renderer shadow feature.");
                var state = new SerializedObject(managed);
                state.FindProperty("additionalLightBudget").intValue = budget;
                state.ApplyModifiedPropertiesWithoutUndo();

                foreach (var mesh in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
                {
                    foreach (Material material in mesh.sharedMaterials)
                    {
                        if (!material || !material.shader || !SupportedShaders.Contains(material.shader.name) || restore.ContainsKey(material))
                            continue;
                        if (material.renderQueue > 2500 && material.shader.name == "Ho/Environment/URP/Lit")
                            continue;
                        if (material.renderQueue > 2500 && material.shader.name == "Ho/Cloth/URP/Cloth"
                            && (!material.HasProperty("_UseHoOIT") || material.GetFloat("_UseHoOIT") < .5f))
                            continue;
                        float mode = material.HasProperty("_HoRPShadowMode") ? material.GetFloat("_HoRPShadowMode") : 0;
                        restore.Add(material, (material.IsKeywordEnabled("_HO_SHADOWS_SCREEN_PREVIEW"), mode));
                        material.EnableKeyword("_HO_SHADOWS_SCREEN_PREVIEW");
                        if (material.HasProperty("_HoRPShadowMode") && mode >= .5f && mode < 1.5f
                            && material.HasProperty("_UseShadow") && material.GetFloat("_UseShadow") > .5f)
                            material.SetFloat("_HoRPShadowMode", 2);
                    }
                }
                item.previewMaterials = restore.Count;
                item.upgradedToFull = restore.Count(entry => entry.Value.mode >= .5f && entry.Value.mode < 1.5f
                    && entry.Key.GetFloat("_HoRPShadowMode") >= 1.5f);
            }

            string stem = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "-" + label + ".exr");
            Debug.Log($"HO_SHADOWS_SCENE_SNAPSHOT {path} {label}");
            return Read(camera, stem, out milliseconds);
        }
        finally
        {
            if (planarActive) planar.SetActive(true);
            foreach (var entry in restore)
            {
                if (entry.Value.keyword) entry.Key.EnableKeyword("_HO_SHADOWS_SCREEN_PREVIEW");
                else entry.Key.DisableKeyword("_HO_SHADOWS_SCREEN_PREVIEW");
                if (entry.Key.HasProperty("_HoRPShadowMode")) entry.Key.SetFloat("_HoRPShadowMode", entry.Value.mode);
            }
        }
    }

    private static Color[] Read(Camera camera, string filename, out float elapsed)
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var result = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        RenderTexture active = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var timing = System.Diagnostics.Stopwatch.StartNew();
            RenderPipeline.SubmitRenderRequest(camera, request);
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

    private static float MaxRgb(Color value) => Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b));
}
