using System;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Targeted camera/Volume diagnostics. Does not rebuild an authored stage.</summary>
public static class HoMmdRenderDiagnostics
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";

    public static string Inspect(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetSceneByPath(request.scene);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var volumes = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Volume>(true)).ToArray();
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
        var report = new Report {
            scene = scene.path, pipeline = AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),
            renderer = RendererPath,
            features = renderer.rendererFeatures.Select(x => x == null ? "<missing>" :
                x.GetType().Name + " active=" + x.isActive).ToArray(),
            cameras = cameras.Select(camera =>
            {
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                int mask = data != null ? data.volumeLayerMask.value : 1;
                var applicable = volumes.Where(x => x.isActiveAndEnabled && x.weight > 0 &&
                    (mask & (1 << x.gameObject.layer)) != 0 && x.sharedProfile != null).ToArray();
                return new CameraReport {
                    name = camera.name, enabled = camera.enabled, active = camera.gameObject.activeInHierarchy,
                    automaticRendering = camera.isActiveAndEnabled,
                    postProcessing = data != null && data.renderPostProcessing, volumeMask = mask,
                    tag = camera.tag, display = camera.targetDisplay,
                    effects = ReadStack(camera, mask), volumes = applicable.Select(x => x.name + " -> " +
                        AssetDatabase.GetAssetPath(x.sharedProfile)).ToArray(),
                    enabledScripts = camera.GetComponents<MonoBehaviour>().Where(x => x != null && x.enabled)
                        .Select(x => x.GetType().Name).ToArray()
                };
            }).ToArray(),
            volumes = volumes.Select(x => new VolumeReport {
                name = x.name, layer = x.gameObject.layer, active = x.isActiveAndEnabled,
                weight = x.weight, priority = x.priority, global = x.isGlobal,
                profile = AssetDatabase.GetAssetPath(x.sharedProfile)
            }).ToArray(),
            sceneViews = SceneView.sceneViews.Cast<SceneView>().Where(x => x.camera != null).Select(view =>
            {
                var mainData = Camera.main != null ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null;
                var ownData = view.camera.GetComponent<UniversalAdditionalCameraData>();
                int mask = ownData != null ? ownData.volumeLayerMask.value :
                    mainData != null ? mainData.volumeLayerMask.value : 1;
                return new SceneViewReport {
                    name = view.titleContent.text, camera = view.camera.name,
                    postProcessing = CoreUtils.ArePostProcessesEnabled(view.camera),
                    sceneLighting = view.sceneLighting, volumeMask = mask,
                    effects = ReadStack(view.camera, mask)
                };
            }).ToArray()
        };
        string output = Path.GetFullPath("Library/HoMmdVideo/Diagnostics/camera-post-processing.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        return output;
    }

    public static string ConfigureCameraPost(HoMmdVideoWorkshop.Request request)
    {
        if (string.IsNullOrEmpty(request.cameraName))
            throw new ArgumentException("Choose the camera explicitly.");
        var scene = SceneManager.GetSceneByPath(request.scene);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true))
            .Single(x => x.name == request.cameraName);
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
        Undo.RecordObject(data, "Match camera post-processing Volume layers");
        int mask = data.volumeLayerMask.value;
        foreach (var volume in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Volume>(true))
                     .Where(x => x.isGlobal && x.isActiveAndEnabled && x.weight > 0 && x.sharedProfile != null))
            mask |= 1 << volume.gameObject.layer;
        data.volumeLayerMask = mask;
        data.renderPostProcessing = true;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Inspect(request);
    }

    public static string EnableScenePost(HoMmdVideoWorkshop.Request request)
    {
        if (SceneManager.GetActiveScene().path != request.scene)
            throw new InvalidOperationException("Only adjust the preview for the currently loaded scene.");
        var view = SceneView.lastActiveSceneView;
        if (view == null) throw new InvalidOperationException("No active Scene View.");
        view.sceneViewState.fxEnabled = true;
        view.sceneViewState.showImageEffects = true;
        view.Repaint();
        return Inspect(request);
    }

    public static string RemoveBuiltinFilters(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != request.scene)
            throw new InvalidOperationException("Only clean the currently loaded requested scene.");
        var volumes = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Volume>(true)).ToArray();
        var profiles = volumes.SelectMany(volume => volume.HasInstantiatedProfile() ?
                new[] { volume.sharedProfile, volume.profile } : new[] { volume.sharedProfile })
            .Where(x => x != null).Distinct().ToArray();
        // Do not silently edit a global or another stage's shared settings.
        foreach (var profile in profiles.Where(EditorUtility.IsPersistent))
            if (!AssetDatabase.GetAssetPath(profile).StartsWith(request.folder.TrimEnd('/') + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Profile is shared outside this stage: " + AssetDatabase.GetAssetPath(profile));
        var results = new System.Collections.Generic.List<ProfileCleanup>();
        foreach (var profile in profiles)
        {
            var native = profile.components.Where(IsBuiltinFilter).ToArray();
            string before = HoFingerprint(profile);
            if (native.Length > 0)
            {
                Undo.RegisterCompleteObjectUndo(profile, "移除 URP 内置后处理");
                foreach (var component in native)
                {
                    profile.components.Remove(component);
                    Undo.DestroyObjectImmediate(component);
                }
                profile.isDirty = true;
                EditorUtility.SetDirty(profile);
                if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
            }
            string after = HoFingerprint(profile);
            if (before != after) throw new InvalidOperationException("Ho settings changed during native cleanup.");
            results.Add(new ProfileCleanup {
                profile = AssetDatabase.GetAssetPath(profile),
                removed = native.Select(x => x.GetType().Name).ToArray(),
                remainingNative = profile.components.Where(IsBuiltinFilter).Select(x => x.GetType().Name).ToArray(),
                hoSettingsUnchanged = before == after, hoFingerprint = after
            });
        }
        VolumeManager.instance.ResetMainStack();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        string output = Path.GetFullPath("Library/HoMmdVideo/Diagnostics/Removed-Native-Post-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(new CleanupReport {
            scene = scene.path, profiles = results.ToArray()
        }, true));
        return output;
    }

    static bool IsBuiltinFilter(VolumeComponent component) => component != null &&
        component.GetType().Namespace == "UnityEngine.Rendering.Universal" && component is IPostProcessComponent;

    static string HoFingerprint(VolumeProfile profile)
    {
        string json = string.Join("\n", profile.components.Where(x => x != null &&
            (x.GetType().Namespace ?? "").StartsWith("lilToon.URP.Extensions", StringComparison.Ordinal))
            .Select(x => x.GetType().FullName + ":" + EditorJsonUtility.ToJson(x)));
        using var hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).Replace("-", "");
    }

    [Serializable] sealed class CleanupReport { public string scene; public ProfileCleanup[] profiles; }
    [Serializable] sealed class ProfileCleanup
    {
        public string profile, hoFingerprint;
        public string[] removed, remainingNative;
        public bool hoSettingsUnchanged;
    }

    public static string ValidatePresets()
    {
        const string typeName = "Hollow.HoUnityTools.Editor.QuickCapture.HoQuickCaptureQualityPresets";
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(typeName)).First(x => x != null);
        var method = type.GetMethod("Resolve", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        int[] edges = { 360, 720, 1080, 2160 };
        Vector2Int[] ratios = { new Vector2Int(16, 9), new Vector2Int(9, 16), new Vector2Int(4, 3), new Vector2Int(1, 1) };
        for (int quality = 0; quality < edges.Length; ++quality)
            for (int aspect = 0; aspect < ratios.Length; ++aspect)
            {
                var size = (Vector2Int)method.Invoke(null, new object[] { quality, aspect });
                var ratio = ratios[aspect];
                var expected = new Vector2Int(edges[quality] * ratio.x / Mathf.Min(ratio.x, ratio.y),
                    edges[quality] * ratio.y / Mathf.Min(ratio.x, ratio.y));
                if (size != expected || size.x % 2 != 0 || size.y % 2 != 0)
                    throw new InvalidOperationException("Preset mismatch: " + size);
            }
        return "PASS: all 16 native quality/aspect presets return exact even dimensions.";
    }

    public static string Proof(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != request.scene) throw new InvalidOperationException("Proof only renders the currently loaded scene.");
        var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
        var camera = request.cameraName == "$scene-view" ? SceneView.lastActiveSceneView?.camera :
            string.IsNullOrEmpty(request.cameraName) ? cameras.Single(x => x.CompareTag("MainCamera")) :
            cameras.Single(x => x.name == request.cameraName);
        if (camera == null) throw new InvalidOperationException("No active camera for the requested proof.");
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        var mainData = Camera.main != null ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null;
        int mask = data != null ? data.volumeLayerMask.value :
            camera.cameraType == CameraType.SceneView && mainData != null ? mainData.volumeLayerMask.value : 1;
        Transform trigger = data != null && data.volumeTrigger != null ? data.volumeTrigger :
            camera.cameraType == CameraType.SceneView && mainData != null && mainData.volumeTrigger != null ?
                mainData.volumeTrigger : camera.transform;
        var previousTarget = camera.targetTexture;
        float previousAspect = camera.aspect;
        var previousAA = data != null ? data.antialiasing : AntialiasingMode.None;
        bool previousPost = data != null && data.renderPostProcessing;
        var view = camera.cameraType == CameraType.SceneView ? SceneView.lastActiveSceneView : null;
        bool previousFx = view != null && view.sceneViewState.fxEnabled;
        bool previousImageEffects = view != null && view.sceneViewState.showImageEffects;
        const string sceneAaKey = "Unity.Rendering.Universal.SceneViewAntialiasing";
        bool hadSceneAa = EditorPrefs.HasKey(sceneAaKey);
        int previousSceneAa = EditorPrefs.GetInt(sceneAaKey);
        string folder = string.IsNullOrEmpty(request.output) ?
            Path.GetFullPath("Library/HoMmdVideo/Diagnostics/PostProof-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) :
            Path.GetFullPath(request.output);
        Directory.CreateDirectory(folder);
        var target = new RenderTexture(request.width, request.height, 24, RenderTextureFormat.ARGBHalf) {
            name = "Ho post-process proof", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave
        };
        var copies = new System.Collections.Generic.List<LiveVolumeCopy>();
        var results = new System.Collections.Generic.List<ProofFrame>();
        try
        {
            foreach (var volume in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Volume>(true))
                         .Where(x => x.isActiveAndEnabled && x.sharedProfile != null))
                copies.Add(new LiveVolumeCopy(volume));
            target.Create();
            camera.targetTexture = target;
            camera.aspect = request.width / (float)request.height;
            // This diagnostic freezes the current authored pose. Use spatial AA
            // rather than pretending same-frame editor rerenders converge TAA.
            if (data != null && data.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (data != null && request.antialiasing == "fxaa")
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            if (view != null)
                EditorPrefs.SetInt(sceneAaKey, request.antialiasing == "fxaa" ? 2 : 3);
            bool glowProof = request.action == "proof-glow";
            bool hoProof = request.action == "proof-ho-only";
            foreach (string mode in hoProof ? new[] { "authored", "native-toggle-off", "native-stress",
                         "ho-layers-off-native-stress", "legacy-stress" } :
                         glowProof ? new[] { "authored", "glow-off", "glow-probe" } :
                         new[] { "authored", "image-off", "screen-off" })
            {
                foreach (var copy in copies) copy.SetMode(mode);
                if (data != null)
                    data.renderPostProcessing = mode == "native-toggle-off" ? false : previousPost;
                if (view != null)
                {
                    view.sceneViewState.fxEnabled = previousFx;
                    view.sceneViewState.showImageEffects = mode == "native-toggle-off" ? false : previousImageEffects;
                }
                VolumeManager.instance.Update(trigger, mask);
                for (int warmup = 0; warmup < 4; ++warmup)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var sp = ScreenProcessRuntimeDiagnostics.CurrentSnapshot;
                var ip = ImageProcessRuntimeDiagnostics.CurrentSnapshot;
                bool hasExecution = HoPostProcessingDiagnostics.TryGet(camera, out var execution);
                SavePng(target, Path.Combine(folder, mode + ".png"));
                results.Add(new ProofFrame { mode = mode, camera = camera.name,
                    cameraType = camera.cameraType.ToString(),
                    postProcessing = camera.cameraType == CameraType.SceneView ?
                        CoreUtils.ArePostProcessesEnabled(camera) : data != null && data.renderPostProcessing,
                    volumeMask = mask, effects = ReadStack(camera, mask),
                    screenReady = sp.CameraName == camera.name && sp.Ready,
                    imageReady = ip.CameraName == camera.name && ip.Ready,
                    screenLayers = sp.CameraName == camera.name ? sp.WrittenLayerCount : 0,
                    imageLayers = ip.CameraName == camera.name ? ip.WrittenLayerCount : 0,
                    screenReason = sp.CameraName == camera.name ? sp.Reason : "No camera diagnostic.",
                    imageReason = ip.CameraName == camera.name ? ip.Reason : "No camera diagnostic.",
                    hasExecution = hasExecution, execution = execution
                });
            }
            File.WriteAllText(Path.Combine(folder, "proof.json"), JsonUtility.ToJson(
                new ProofReport { frames = results.ToArray(), width = request.width, height = request.height }, true));
            return folder;
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.aspect = previousAspect;
            if (data != null) { data.antialiasing = previousAA; data.renderPostProcessing = previousPost; }
            if (view != null)
            {
                view.sceneViewState.fxEnabled = previousFx;
                view.sceneViewState.showImageEffects = previousImageEffects;
                if (hadSceneAa) EditorPrefs.SetInt(sceneAaKey, previousSceneAa);
                else EditorPrefs.DeleteKey(sceneAaKey);
                view.Repaint();
            }
            foreach (var copy in copies) copy.Dispose();
            target.Release(); Object.DestroyImmediate(target);
            VolumeManager.instance.ResetMainStack();
            VolumeManager.instance.Update(trigger, mask);
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    sealed class LiveVolumeCopy : IDisposable
    {
        readonly Volume volume;
        readonly VolumeProfile previous, source, copy;
        internal LiveVolumeCopy(Volume volume)
        {
            this.volume = volume;
            previous = volume.HasInstantiatedProfile() ? volume.profile : null;
            source = previous != null ? previous : volume.sharedProfile;
            copy = ScriptableObject.CreateInstance<VolumeProfile>();
            copy.hideFlags = HideFlags.HideAndDontSave;
            foreach (var component in source.components.Where(x => x != null))
            {
                var cloned = Object.Instantiate(component);
                cloned.hideFlags = HideFlags.HideAndDontSave;
                copy.components.Add(cloned);
            }
            volume.profile = copy;
        }
        internal void SetMode(string mode)
        {
            foreach (var component in copy.components.ToArray())
            {
                if (component.GetType().Namespace != "UnityEngine.Rendering.Universal") continue;
                var original = source.components.FirstOrDefault(x => x != null && x.GetType() == component.GetType());
                if (original != null) EditorUtility.CopySerialized(original, component);
                else { copy.components.Remove(component); Object.DestroyImmediate(component); }
            }
            if (copy.TryGet(out ImageProcessStackVolume image) && source.TryGet(out ImageProcessStackVolume originalImage))
            {
                image.Enable.value = originalImage.Enable.value;
                image.Enable.overrideState = originalImage.Enable.overrideState;
                image.ReplaceBuiltinPostProcessing.value = originalImage.ReplaceBuiltinPostProcessing.value;
                image.ReplaceBuiltinPostProcessing.overrideState = originalImage.ReplaceBuiltinPostProcessing.overrideState;
                if (mode == "legacy-stress") image.ReplaceBuiltinPostProcessing.Override(false);
                if (mode == "image-off") image.Enable.Override(false);
                if (mode == "ho-layers-off-native-stress") image.Enable.Override(false);
                for (int index = 0; index < image.layers.value.Count; ++index)
                {
                    var layer = image.layers.value[index];
                    var original = originalImage.layers.value[index];
                    if (layer == null || original == null || layer.effect != ImageProcessEffect.Glow) continue;
                    layer.enabled = original.enabled;
                    layer.parameters0 = original.parameters0;
                    if (mode == "glow-off") layer.enabled = false;
                    if (mode == "glow-probe")
                    {
                        // Change only the temporary clone. Keep gain, radius,
                        // color and later authored layers identical.
                        layer.parameters0.x = 0.65f;
                        layer.parameters0.y = 0.5f;
                    }
                }
            }
            if (copy.TryGet(out ScreenProcessStackVolume screen) && source.TryGet(out ScreenProcessStackVolume originalScreen))
            {
                screen.Enable.value = originalScreen.Enable.value;
                screen.Enable.overrideState = originalScreen.Enable.overrideState;
                if (mode == "screen-off") screen.Enable.Override(false);
                if (mode == "ho-layers-off-native-stress") screen.Enable.Override(false);
            }
            if (mode.EndsWith("stress", StringComparison.Ordinal)) ApplyNativeStress();
            copy.isDirty = true;
        }
        T Native<T>() where T : VolumeComponent
        {
            if (!copy.TryGet(out T component)) component = copy.Add<T>(true);
            component.active = true;
            return component;
        }
        void ApplyNativeStress()
        {
            var color = Native<ColorAdjustments>();
            color.postExposure.Override(7);
            color.contrast.Override(90);
            color.colorFilter.Override(Color.magenta);
            Native<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Native<Bloom>();
            bloom.intensity.Override(5);
            bloom.threshold.Override(0);
            Native<FilmGrain>().intensity.Override(1);
            Native<Vignette>().intensity.Override(1);
            Native<ChromaticAberration>().intensity.Override(1);
            Native<LensDistortion>().intensity.Override(0.7f);
            Native<PaniniProjection>().distance.Override(1);
        }
        public void Dispose()
        {
            volume.profile = previous;
            foreach (var component in copy.components) Object.DestroyImmediate(component);
            Object.DestroyImmediate(copy);
        }
    }

    static void SavePng(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        var readback = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        var png = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            bool encode = QualitySettings.activeColorSpace == ColorSpace.Linear && !target.sRGB;
            var colors = readback.GetPixels();
            png.SetPixels32(colors.Select(x => (Color32)(encode ? x.gamma : x)).ToArray());
            png.Apply(); File.WriteAllBytes(path, png.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); Object.DestroyImmediate(png); }
    }

    [Serializable] sealed class ProofReport { public int width, height; public ProofFrame[] frames; }
    [Serializable] sealed class ProofFrame
    {
        public string mode, camera, cameraType, screenReason, imageReason;
        public string[] effects;
        public bool postProcessing, screenReady, imageReady, hasExecution;
        public HoPostProcessingExecution execution;
        public int volumeMask, screenLayers, imageLayers;
    }

    static string[] ReadStack(Camera camera, int mask)
    {
        var manager = VolumeManager.instance;
        if (manager == null) return Array.Empty<string>();
        var stack = manager.CreateStack();
        try
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            manager.Update(stack, data != null && data.volumeTrigger != null ? data.volumeTrigger : camera.transform, mask);
            var screen = stack.GetComponent<ScreenProcessStackVolume>();
            var image = stack.GetComponent<ImageProcessStackVolume>();
            var color = stack.GetComponent<ColorAdjustments>();
            var bloom = stack.GetComponent<Bloom>();
            var tone = stack.GetComponent<Tonemapping>();
            return new[] {
                "ScreenProcess active=" + (screen != null && screen.IsActive()) + " layers=" +
                    (screen != null ? string.Join(", ", screen.layers.value.Where(x => x != null && x.IsActive).Select(x => x.name)) : ""),
                "ImageProcess active=" + (image != null && image.IsActive()) + " layers=" +
                    (image != null ? string.Join(", ", image.layers.value.Where(x => x != null && x.IsActive).Select(x => x.name)) : ""),
                "ColorAdjustments active=" + (color != null && color.IsActive()) +
                    " exposure=" + (color != null ? color.postExposure.value : 0),
                "Bloom active=" + (bloom != null && bloom.IsActive()) +
                    " intensity=" + (bloom != null ? bloom.intensity.value : 0),
                "Tonemapping=" + (tone != null && tone.active ? tone.mode.value.ToString() : "Off"),
                "ImageSceneView=" + (image != null && image.ShowInSceneView.value),
                "HoOnly=" + (image != null && image.ReplaceBuiltinPostProcessing.value) +
                    " (native Volume effects are ignored when true; AA remains enabled)"
            }.Concat(image == null ? Array.Empty<string>() : image.layers.value
                .Where(x => x != null && x.effect == ImageProcessEffect.Glow).Select(x =>
                    $"Glow enabled={x.enabled} intensity={x.intensity} threshold={x.parameters0.x} " +
                    $"knee={x.parameters0.y} radius={x.parameters0.z} gain={x.parameters1.x} opacity={x.parameters1.w}"))
                .ToArray();
        }
        finally { manager.DestroyStack(stack); }
    }

    [Serializable] sealed class Report
    {
        public string scene, pipeline, renderer;
        public string[] features;
        public CameraReport[] cameras;
        public VolumeReport[] volumes;
        public SceneViewReport[] sceneViews;
    }
    [Serializable] sealed class SceneViewReport
    {
        public string name, camera;
        public bool postProcessing, sceneLighting;
        public int volumeMask;
        public string[] effects;
    }
    [Serializable] sealed class CameraReport
    {
        public string name, tag;
        public bool enabled, active, automaticRendering, postProcessing;
        public int volumeMask, display;
        public string[] volumes, effects, enabledScripts;
    }
    [Serializable] sealed class VolumeReport
    {
        public string name, profile;
        public int layer;
        public bool active, global;
        public float weight, priority;
    }
}
