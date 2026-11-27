using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class HoShadowCastP0SceneProbe
{
    private const int Width = 640;
    private const int Height = 360;
    private const float ChangeThreshold = 0.002f;
    private static readonly string[] Scenes = {
        "Assets/00动画/MMD场景/H2Cu阿相/圣诞万花镜/圣诞万花镜.unity",
        "Assets/00动画/角色/羽啾/羽啾.unity"
    };

    [Serializable] private sealed class Measurement
    {
        public string scene, camera, error;
        public string[] images;
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView, maxDifference, meanDifference, resetMaxDifference, peakOn;
        public int changedPixels, resetChangedPixels, changedOnStablePixels, missingScripts, visibleHoLights, punctualSlices, secondDirectionalSlices, missingMaterials, renderedMeshes;
        public bool nativeAdditional, hoAtlasOn, hoAtlasOff, rtActive, characterActive;
    }
    [Serializable] private sealed class Report
    {
        public string unity, graphics, gpu;
        public bool skipPlanarReflection, disableCameraPostEffects;
        public int width = Width, height = Height;
        public int warmupFrames;
        public float threshold = ChangeThreshold;
        public List<Measurement> scenes = new();
    }

    public static void Run()
    {
        string project = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_SCENE_PROJECT");
        if (string.IsNullOrWhiteSpace(project) || !string.Equals(Path.GetFullPath(Path.Combine(project, "Assets")),
            Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set HO_SHADOWS_P0_SCENE_PROJECT to the physical scene snapshot, not production.");
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_SCENE_OUT");
        if (!Directory.Exists(output) || File.Exists(Path.Combine(output, "scene-ab.json")))
            throw new InvalidOperationException("A fresh output folder is required.");

        var report = new Report { unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName };
        string filter = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_SCENE_FILTER");
        report.skipPlanarReflection = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_SKIP_PLANAR") == "1";
        report.disableCameraPostEffects = Environment.GetEnvironmentVariable("HO_SHADOWS_P0_NO_CAMERA_POST") == "1";
        int.TryParse(Environment.GetEnvironmentVariable("HO_SHADOWS_P0_WARMUP"), out report.warmupFrames);
        report.warmupFrames = Mathf.Clamp(report.warmupFrames, 0, 16);
        foreach (string scene in Scenes.Where(path => string.IsNullOrEmpty(filter) || path.Contains(filter)))
        {
            var item = new Measurement { scene = scene };
            report.scenes.Add(item);
            try { Measure(item, output, report.skipPlanarReflection, report.disableCameraPostEffects, report.warmupFrames); }
            catch (Exception ex) { item.error = ex.ToString(); Debug.LogError(ex); }
            File.WriteAllText(Path.Combine(output, "scene-ab.json"), JsonUtility.ToJson(report, true));
        }
        if (report.scenes.Count == 0 || report.scenes.Any(scene => !string.IsNullOrEmpty(scene.error)))
            throw new InvalidOperationException("One or more saved-scene measurements failed.");
    }

    private static void Measure(Measurement item, string output, bool skipPlanar, bool noCameraPost, int warmupFrames)
    {
        Scene scene = EditorSceneManager.OpenScene(item.scene, OpenSceneMode.Single);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject).ToArray();
        item.missingScripts = objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
        var meshes = objects.Select(go => go.GetComponent<Renderer>()).Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
        item.renderedMeshes = meshes.Length;
        item.missingMaterials = meshes.SelectMany(renderer => renderer.sharedMaterials)
            .Count(material => !material || !material.shader || material.shader.name == "Hidden/InternalErrorShader");
        Camera camera = Camera.main;
        if (!camera || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base)
            throw new InvalidOperationException("No active Base MainCamera.");
        var cameraData = camera.GetUniversalAdditionalCameraData();
        bool renderPostProcessing = cameraData.renderPostProcessing;
        var antialiasing = cameraData.antialiasing;
        if (noCameraPost)
        {
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
        }
        item.camera = camera.name;
        item.position = camera.transform.position;
        item.rotation = camera.transform.rotation;
        item.fieldOfView = camera.fieldOfView;
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var planar = renderer.rendererFeatures.OfType<lilToon.URP.Extensions.PlanarReflection.HoPlanarReflectionRendererFeature>().SingleOrDefault();
        bool planarWasActive = planar != null && planar.isActive;
        if (skipPlanar && planarWasActive) planar.SetActive(false);
        var feature = renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().Single();
        Debug.Log($"HO_SHADOW_CAST_SCENE_MEASURE {scene.name} camera={camera.name} renderers={item.renderedMeshes} missing={item.missingScripts}/{item.missingMaterials} skipPlanar={skipPlanar}");
        var authored = feature.Settings.enabled;
        try
        {
            if (!authored || !feature.isActive)
                throw new InvalidOperationException("The snapshot does not enable the legacy feature.");
            string stem = Path.Combine(output, Path.GetFileNameWithoutExtension(item.scene));
            item.images = new[] { Path.GetFileName(stem) + "-off.exr",
                Path.GetFileName(stem) + "-on.exr", Path.GetFileName(stem) + "-off-repeat.exr" };
            feature.Settings.enabled = false;
            for (int index = 0; index < warmupFrames; index++) ReadCamera(camera, null);
            Color[] off = ReadCamera(camera, stem + "-off");
            feature.Settings.enabled = true;
            Color[] on = ReadCamera(camera, stem + "-on");
            var snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
            item.visibleHoLights = snapshot.VisibleLightCount;
            item.punctualSlices = snapshot.SliceCount;
            item.secondDirectionalSlices = snapshot.SecondDirectionalSliceCount;
            item.nativeAdditional = Shader.IsKeywordEnabled("_ADDITIONAL_LIGHT_SHADOWS");
            item.hoAtlasOn = Shader.GetGlobalFloat("_HoShadowCastActive") > 0.5f;
            item.rtActive = Shader.GetGlobalFloat("_HoRTShadowActive") > 0.5f;
            item.characterActive = Shader.GetGlobalFloat("_HoCSActive") > 0.5f;
            feature.Settings.enabled = false;
            Color[] repeat = ReadCamera(camera, stem + "-off-repeat");
            item.hoAtlasOff = Shader.GetGlobalFloat("_HoShadowCastActive") > 0.5f;
            double sum = 0;
            for (int i = 0; i < on.Length; i++)
            {
                float delta = MaxRgb(on[i] - off[i]);
                if (!float.IsFinite(delta))
                    throw new InvalidOperationException("Non-finite RGB difference.");
                item.maxDifference = Mathf.Max(item.maxDifference, delta);
                item.peakOn = Mathf.Max(item.peakOn, MaxRgb(on[i]));
                float repeatDelta = MaxRgb(repeat[i] - off[i]);
                item.resetMaxDifference = Mathf.Max(item.resetMaxDifference, repeatDelta);
                if (repeatDelta > ChangeThreshold) item.resetChangedPixels++;
                if (delta > ChangeThreshold)
                {
                    item.changedPixels++;
                    if (repeatDelta <= ChangeThreshold && MaxRgb(on[i] - repeat[i]) > ChangeThreshold)
                        item.changedOnStablePixels++;
                }
                sum += delta;
            }
            item.meanDifference = (float)(sum / on.Length);
            if (item.peakOn < 0.01f || item.hoAtlasOff)
                throw new InvalidOperationException("No camera signal or old Atlas still active while disabled.");
            Debug.Log($"HO_SHADOW_CAST_SCENE_AB {scene.name} {camera.name} slices={item.punctualSlices}/{item.secondDirectionalSlices}"
                + $" changed={item.changedPixels} stableChanged={item.changedOnStablePixels} resetChanged={item.resetChangedPixels} max={item.maxDifference} reset={item.resetMaxDifference} missing={item.missingScripts}/{item.missingMaterials}");
        }
        finally
        {
            feature.Settings.enabled = authored;
            if (skipPlanar && planarWasActive) planar.SetActive(true);
            cameraData.renderPostProcessing = renderPostProcessing;
            cameraData.antialiasing = antialiasing;
        }
    }

    private static float MaxRgb(Color c) => Mathf.Max(Mathf.Abs(c.r), Mathf.Abs(c.g), Mathf.Abs(c.b));

    private static Color[] ReadCamera(Camera camera, string output)
    {
        using var destination = new Scope<RenderTexture>(new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        using var readback = new Scope<Texture2D>(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
        RenderTexture old = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = destination.Value };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("Render request not supported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = destination.Value;
            readback.Value.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Value.Apply(false);
            Color[] pixels = readback.Value.GetPixels();
            if (output != null) File.WriteAllBytes(output + ".exr", readback.Value.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            return pixels;
        }
        finally { RenderTexture.active = old; }
    }

    private sealed class Scope<T> : IDisposable where T : Object
    {
        public T Value { get; }
        public Scope(T value) => Value = value;
        public void Dispose() => Object.DestroyImmediate(Value);
    }
}
