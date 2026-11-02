// Read-only inspection of loaded scenes and the last rendered shadow diagnostics.
// Capture has no scene switches, renders, asset saves, component additions or light setters.
// CaptureRenderedCamera explicitly renders one small diagnostic frame from the existing main camera.
#pragma warning disable CS0618
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.VolumeLight;
using Object = UnityEngine.Object;

public static class HoLightingAudit
{
    public static void RunBatch()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-hoScene");
            if (index < 0) throw new ArgumentException("Provide -hoScene Assets/...unity.");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            int updates = 0;
            EditorApplication.CallbackFunction once = null;
            once = () =>
            {
                if (++updates < 3) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                EditorApplication.update -= once;
                try { Debug.Log("HO_LIGHTING_AUDIT=" + CaptureRenderedCamera()); EditorApplication.Exit(0); }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
            EditorApplication.update += once;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [Serializable] public sealed class Report
    {
        public string createdUtc, unity, pipeline, quality, sun, note, characterShadow;
        public int currentFrame, lastShadowFrame;
        public bool lastShadowValid;
        public string lastShadowCamera;
        public SceneRow[] scenes;
        public LightRow[] lights;
        public FeatureRow[] features;
        public AcceptedRow[] lastAccepted;
        public MaterialRow[] materials;
        public string[] globals;
    }
    [Serializable] public sealed class SceneRow { public string path; public bool dirty; }
    [Serializable] public sealed class LightRow
    {
        public string path, scene, type, shadows, mode, bakedMode, mixedMode, shadowResolution, cookie;
        public int instanceId, nativeResolutionTier, customShadowResolution, cullingMask;
        public uint lightShadowLayers, lightingLayers, shadowLayers;
        public bool enabled, active, isBaked, customShadowLayers, usePipelineBias, acceptedByName, duplicateName;
        public float intensity, shadowStrength, bias, normalBias, nearPlane, range, angle, colorTemperature;
        public Color color;
        public Vector3 position, direction;
        public string softShadowQuality, volume, rtDeclaration;
    }
    [Serializable] public sealed class FeatureRow
    { public string asset, name, type, settings, status; public bool active; }
    [Serializable] public sealed class AcceptedRow
    { public string name, stage, type; public int resolution, slices; }
    [Serializable] public sealed class FloatRow { public string name; public float value; }
    [Serializable] public sealed class MaterialRow
    { public string path, name, shader; public FloatRow[] properties; public string[] renderers; }

    public static string Capture()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Where(x => x.gameObject.scene.IsValid() && x.gameObject.scene.isLoaded).ToArray();
        var snapshot = HoShadowCastRuntimeDiagnostics.CurrentSnapshot;
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var report = new Report
        {
            createdUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            pipeline = AssetDatabase.GetAssetPath(pipeline),
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            sun = RenderSettings.sun == null ? null : PathOf(RenderSettings.sun.transform),
            note = "Capture reads the last rendered camera; lighting-audit-render explicitly renders once beforehand. No asset saves or lighting changes. Name-based atlas matches are ambiguous for duplicate light names.",
            currentFrame = Time.frameCount, lastShadowFrame = snapshot.FrameCount, lastShadowCamera = snapshot.CameraName,
            lastShadowValid = snapshot.IsValid,
            characterShadow = HoCharacterShadowRendererFeature.LastCullStatus,
            scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
                .Select(s => new SceneRow { path = s.path, dirty = s.isDirty }).ToArray(),
            lights = lights.Select(light => Describe(light, lights, snapshot)).ToArray(),
            lastAccepted = (snapshot.AcceptedLights ?? Array.Empty<HoShadowCastRuntimeDiagnosticLight>())
                .Select(x => new AcceptedRow { name = x.Name, stage = x.Stage, type = x.Type.ToString(),
                    resolution = x.Resolution, slices = x.SliceCount }).ToArray(),
            features = Features(pipeline).ToArray(),
            materials = Materials(),
            globals = new[] { "_HoShadowCastActive", "_HoShadowCastLightCount", "_HoShadowCastSliceCount",
                "_HoCSActive", "_HoCSCount", "_HoRTShadowActive" }
                .Select(name => name + "=" + Shader.GetGlobalFloat(name)).ToArray()
        };
        string directory = Path.GetFullPath("Library/HoLightingAudit");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "current-scene.json");
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        return path;
    }

    public static string CaptureRenderedCamera()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use the read-only snapshot while playing.");
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No active MainCamera; no camera was moved or created.");
        var target = RenderTexture.GetTemporary(320, 180, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var previousActive = RenderTexture.active;
        try
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new NotSupportedException("Current pipeline does not support StandardRequest.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            return Capture();
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static LightRow Describe(Light light, Light[] all, HoShadowCastRuntimeDiagnosticSnapshot snapshot)
    {
        // GetUniversalAdditionalLightData() would add a component; never use it in this inspection.
        var extra = light.GetComponent<UniversalAdditionalLightData>();
        var volume = light.GetComponent<HoVolumeLightSource>();
        var rt = light.GetComponent<HoRTShadowLight>();
        var baking = light.bakingOutput;
        return new LightRow
        {
            path = PathOf(light.transform), scene = light.gameObject.scene.path, instanceId = light.GetInstanceID(),
            type = light.type.ToString(), enabled = light.enabled, active = light.gameObject.activeInHierarchy,
            shadows = light.shadows.ToString(), mode = light.lightmapBakeType.ToString(),
            bakedMode = baking.lightmapBakeType.ToString(), mixedMode = baking.mixedLightingMode.ToString(), isBaked = baking.isBaked,
            intensity = light.intensity, color = light.color, colorTemperature = light.useColorTemperature ? light.colorTemperature : 0,
            position = light.transform.position, direction = light.transform.forward,
            shadowStrength = light.shadowStrength, bias = light.shadowBias, normalBias = light.shadowNormalBias,
            nearPlane = light.shadowNearPlane, range = light.range, angle = light.spotAngle,
            shadowResolution = light.shadowResolution.ToString(), customShadowResolution = light.shadowCustomResolution,
            cullingMask = light.cullingMask, lightShadowLayers = (uint)light.renderingLayerMask,
            cookie = AssetDatabase.GetAssetPath(light.cookie),
            lightingLayers = extra != null ? (uint)extra.renderingLayers : 1u,
            shadowLayers = extra != null ? (uint)extra.shadowRenderingLayers : 1u,
            customShadowLayers = extra != null && extra.customShadowLayers,
            usePipelineBias = extra == null || extra.usePipelineSettings,
            nativeResolutionTier = extra != null ? extra.additionalLightsShadowResolutionTier : -99,
            softShadowQuality = extra != null ? extra.softShadowQuality.ToString() : "no AdditionalLightData",
            acceptedByName = snapshot.AcceptedLights != null && snapshot.AcceptedLights.Any(x => x.Name == light.name),
            duplicateName = all.Count(x => x.name == light.name) > 1,
            volume = volume == null ? null : JsonUtility.ToJson(volume),
            rtDeclaration = rt == null ? null : JsonUtility.ToJson(rt)
        };
    }

    static IEnumerable<FeatureRow> Features(UniversalRenderPipelineAsset pipeline)
    {
        if (pipeline == null) yield break;
        using (var serialized = new SerializedObject(pipeline))
        {
            var array = serialized.FindProperty("m_RendererDataList");
            if (array == null) yield break;
            for (int i = 0; i < array.arraySize; ++i)
            {
                var data = array.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null) continue;
                    var type = feature.GetType();
                    if (!(feature is HoShadowCastRendererFeature || feature is HoCharacterShadowRendererFeature ||
                          feature is HoRTShadowRendererFeature || feature is HoVolumeLightRendererFeature)) continue;
                    var field = type.GetField("settings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var status = type.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
                    yield return new FeatureRow { asset = AssetDatabase.GetAssetPath(data), name = feature.name,
                        type = type.FullName, active = feature.isActive,
                        settings = field == null ? null : JsonUtility.ToJson(field.GetValue(feature)),
                        status = status == null ? null : Convert.ToString(status.GetValue(feature)) };
                }
            }
        }
    }

    static MaterialRow[] Materials()
    {
        string[] properties = { "_UseShadow", "_ShadowReceive", "_Shadow2ndReceive", "_Shadow3rdReceive",
            "_MultiLightIntensity", "_MultiLightCastShadowStrength", "_VertexLightStrength", "_LightMinLimit",
            "_LightMaxLimit", "_AsUnlit", "_MonochromeLighting", "_ShadowEnvStrength", "_ApplySpecularFA" };
        var usage = new Dictionary<Material, List<string>>();
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || !material.shader.name.Contains("lilToon")) continue;
                if (!usage.TryGetValue(material, out var paths)) usage[material] = paths = new List<string>();
                paths.Add(PathOf(renderer.transform) + " | cast=" + renderer.shadowCastingMode + " receive=" + renderer.receiveShadows +
                    " renderingLayers=" + renderer.renderingLayerMask + " gameObjectLayer=" + renderer.gameObject.layer);
            }
        }
        return usage.Select(pair => new MaterialRow { path = AssetDatabase.GetAssetPath(pair.Key),
            name = pair.Key.name, shader = pair.Key.shader.name, renderers = pair.Value.Distinct().ToArray(),
            properties = properties.Where(pair.Key.HasProperty).Select(name => new FloatRow {
                name = name, value = pair.Key.GetFloat(name) }).ToArray() }).OrderBy(x => x.path).ToArray();
    }
    static string PathOf(Transform transform)
    {
        var names = new Stack<string>();
        for (; transform != null; transform = transform.parent) names.Push(transform.name);
        return string.Join("/", names);
    }
}
