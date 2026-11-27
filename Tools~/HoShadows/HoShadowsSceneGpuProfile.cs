using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.Shadows;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Run the isolated cutover helper first. Start Unity batch without -quit; this entry exits after the profiler frames arrive.
public static class HoShadowsSceneGpuProfile
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string Preview = "_HO_SHADOWS_SCREEN_PREVIEW";
    const string CalibrationName = "Ho Shadows GPU Timing Calibration";
    const int Warmup = 40;
    const int Width = 1280;
    const int Height = 720;

    static readonly string[] DefaultScenes = {
        "Assets/00动画/角色/羽啾/羽啾.unity",
        "Assets/00动画/MMD场景/H2Cu阿相/圣诞万花镜/圣诞万花镜.unity"
    };
    static readonly string[] RegistryNames = {
        "Ho-ShadowCast", "Ho-CharacterShadow", "Ho-GeometryBuffer Output", "Ho-ObjectBuffer Output",
        "Ho-SurfaceBuffer", "Ho-Surface Semantic Association", "Ho-CharacterSpecialization", "Ho-RTShadow",
        "Ho-WeightedOIT Accumulation", "Ho-WeightedOIT Composite"
    };
    static readonly string[] MarkerNames = {
        "Draw Main Light Shadowmap", "Draw Additional Lights Shadowmap", "Draw Depth Only", "DrawDepthNormalPrepass",
        "DrawOpaqueObjects", "DrawTransparentObjects",
        "Ho Native Main Shadow Resolve", "Ho Character Screen Shadow Resolve", "Ho Additional Screen Shadow Visibility",
        "Ho Bind Opaque Screen Shadows", "Ho OIT Shadow Receiver Layer 1", "Ho OIT Shadow Receiver Layer 2",
        "Ho Eye Shadow Receiver Layer 1", "Ho Screen Shadow Layer 1", "Ho Screen Shadow Layer 2",
        "Ho Transparent Additional Shadow Visibility", "Ho Bind OIT Screen Shadows", "Ho Bind Eye Screen Shadows",
        "Ho Capture Bind Additional Shadows", "Ho Ray Traced Screen Shadow Resolve"
    };
    static readonly HashSet<string> PreviewShaders = new() {
        "Ho/Skin/URP/Stylized", "Ho/Skin/URP/Eye", "Ho/Cloth/URP/Cloth", "Ho/Environment/URP/Lit"
    };

    [Serializable] sealed class Sample { public int profilerFrame, blocks; public double milliseconds; }
    [Serializable] sealed class PassResult {
        public string name, source, status;
        public int frames, blocks;
        public double[] p50MeanP95Milliseconds = Array.Empty<double>();
        public List<Sample> samples = new();
    }
    [Serializable] sealed class Segment {
        public string scene, camera, mode;
        public int eligibleProfilerFrames, submittedFrames, savedCameraRenders, otherCameraRenders, previewMaterials;
        public List<PassResult> passes = new();
        public List<double> frameTimingGpuMilliseconds = new();
    }
    [Serializable] sealed class Report {
        public string unity, api, gpu, requestMode, error;
        public bool recorderSupported, calibrationMeasured, completed, frameTimingFeature;
        public int width, height, warmup, requestedSamples, candidateBudget;
        public List<Segment> segments = new();
    }

    sealed class Watch
    {
        public PassResult result;
        public ProfilingSampler sampler;
        public Recorder recorder;

        public void Read(int frame)
        {
            int count = sampler != null ? sampler.gpuSampleCount : recorder?.gpuSampleBlockCount ?? 0;
            double milliseconds = sampler != null ? sampler.gpuElapsedTime : (recorder?.gpuElapsedNanoseconds ?? 0) / 1_000_000.0;
            if (count <= 0 || milliseconds <= 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
            result.samples.Add(new Sample { profilerFrame = frame, milliseconds = milliseconds, blocks = count });
        }

        public void Finish()
        {
            if (sampler != null) sampler.enableRecording = false;
            if (recorder != null) recorder.enabled = false;
            result.frames = result.samples.Count;
            result.blocks = result.samples.Sum(sample => sample.blocks);
            if (result.frames == 0)
            {
                result.status = sampler == null && recorder == null ? "marker_unavailable" : "no_positive_gpu_samples";
                return;
            }
            result.status = "sampled";
            double[] sorted = result.samples.Select(sample => sample.milliseconds).OrderBy(value => value).ToArray();
            result.p50MeanP95Milliseconds = new[] { Percentile(sorted, .5), sorted.Average(), Percentile(sorted, .95) };
        }
    }

    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly FieldInfo UrpGraph = typeof(UniversalRenderPipeline).GetField("s_RenderGraph", StaticHidden);
    static readonly FieldInfo GraphSamplers = typeof(RenderGraph).GetField("m_DefaultProfilingSamplers", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly ProfilingSampler Calibration = new(CalibrationName);
    static readonly FrameTiming[] FrameTimings = new FrameTiming[1];
    static readonly List<Watch> Watches = new();
    static readonly Dictionary<Renderer, Material[]> Originals = new();
    static readonly Dictionary<Material, Material> MaterialCopies = new();
    static UniversalRendererData renderer;
    static HoShadowsRendererFeature managed;
    static HoCharacterSpecializationRendererFeature capture;
    static ScriptableRendererFeature[] legacy;
    static bool[] originalLegacy;
    static bool originalCapture, originalManaged, previousProfiler, previousProfileEditor, previousGpuArea;
    static RenderTexture target, calibrationTarget;
    static Camera camera;
    static UniversalRenderPipeline.SingleCameraRequest request;
    static RenderPipeline.StandardRequest stackRequest;
    static bool authoredStack;
    static Report report;
    static Segment current;
    static string reportPath;
    static string[] scenes;
    static int segmentIndex, tick, lastProfilerFrame;
    static ulong lastTimingTimestamp;

    public static void Run()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string expected = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_GPU_PROJECT");
        string output = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_GPU_OUTPUT");
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(output) ||
            !Application.isBatchMode || !SamePath(project, expected) || !IsTemporary(project) || !IsTemporary(output) ||
            !Directory.Exists(output) || !PhysicalPath(Application.dataPath) || !PhysicalPath(output))
            throw new InvalidOperationException("Use an explicitly selected physical D:/Temp project and output in Windows batch mode.");

        reportPath = Path.Combine(output, "scene-gpu-profile.json");
        if (File.Exists(reportPath)) throw new InvalidOperationException("Select a new GPU profile output directory.");
        string scene = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_GPU_SCENE");
        scenes = string.IsNullOrWhiteSpace(scene) ? DefaultScenes : new[] { scene };
        foreach (string path in scenes)
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || !path.EndsWith(".unity", StringComparison.Ordinal) || !File.Exists(path))
                throw new InvalidOperationException("Missing saved scene: " + path);
        int samples = int.TryParse(Environment.GetEnvironmentVariable("HO_SHADOWS_P5_GPU_SAMPLES"), out int count) ? count : 90;
        authoredStack = Environment.GetEnvironmentVariable("HO_SHADOWS_P5_GPU_AUTHORED_STACK") == "1";
        report = new Report {
            unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
            recorderSupported = SystemInfo.supportsGpuRecorder, frameTimingFeature = FrameTimingManager.IsFeatureEnabled(),
            requestMode = authoredStack ? "authored_camera_stack" : "single_camera",
            width = Width, height = Height, warmup = Warmup, requestedSamples = Math.Max(12, samples), candidateBudget = 4
        };
        renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null) throw new InvalidOperationException("Missing isolated PC_Renderer.");
        managed = renderer.rendererFeatures.OfType<HoShadowsRendererFeature>().Single();
        capture = renderer.rendererFeatures.OfType<HoCharacterSpecializationRendererFeature>().Single();
        legacy = new ScriptableRendererFeature[] {
            renderer.rendererFeatures.OfType<HoShadowCastRendererFeature>().Single(),
            renderer.rendererFeatures.OfType<HoCharacterShadowRendererFeature>().Single(),
            renderer.rendererFeatures.OfType<HoRTShadowRendererFeature>().Single()
        };
        originalLegacy = legacy.Select(feature => feature.isActive).ToArray();
        originalManaged = managed.isActive;
        originalCapture = capture.CaptureShadowInputsEnabled;
        if (!originalManaged || originalLegacy.Any(active => active) || managed.RayTracedShadowsEnabled ||
            new SerializedObject(managed).FindProperty("additionalLightBudget").intValue != report.candidateBudget)
            throw new InvalidOperationException("Run the physical cutover helper first, with budget 4 and managed RT disabled.");

        previousProfiler = ProfilerDriver.enabled;
        previousProfileEditor = ProfilerDriver.profileEditor;
        previousGpuArea = ProfilerDriver.IsAreaEnabled(ProfilerArea.GPU);
        Profiler.enabled = true;
        ProfilerDriver.profileEditor = true;
        ProfilerDriver.enabled = true;
        ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
        target = MakeTarget("Ho Shadows profile", Width, Height, 24);
        calibrationTarget = MakeTarget("Ho Shadows calibration", Width, Height, 0);
        RenderPipelineManager.endCameraRendering += OnCameraEnd;
        StartSegment();
        EditorApplication.update += Tick;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    static void StartSegment()
    {
        tick = 0;
        lastProfilerFrame = -1;
        lastTimingTimestamp = 0;
        current = new Segment { mode = segmentIndex == 0 ? "calibration" : segmentIndex % 2 == 1 ? "legacy" : "managed-budget4" };
        report.segments.Add(current);
        if (segmentIndex == 0)
        {
            var watch = NewWatch(CalibrationName);
            watch.sampler = Calibration;
            watch.result.source = "explicit_command_buffer";
            Calibration.enableRecording = true;
        }
        else
        {
            bool candidate = segmentIndex % 2 == 0;
            current.scene = scenes[(segmentIndex - 1) / 2];
            var scene = EditorSceneManager.OpenScene(current.scene, OpenSceneMode.Single);
            camera = Camera.main;
            if (camera == null || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base)
                throw new InvalidOperationException("Missing saved Base Main Camera for " + current.scene);
            if (target == null) target = MakeTarget("Ho Shadows profile", Width, Height, 24);
            if (!target.IsCreated() && !target.Create()) throw new InvalidOperationException("Camera target was lost.");
            if (authoredStack) stackRequest = new RenderPipeline.StandardRequest { destination = target };
            else request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            current.camera = camera.name;
            managed.SetActive(candidate);
            capture.CaptureShadowInputsEnabled = candidate && originalCapture;
            foreach (var feature in legacy) feature.SetActive(!candidate);
            if (candidate)
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var mesh in root.GetComponentsInChildren<Renderer>(true))
                        CopyPreviewMaterials(mesh);
                current.previewMaterials = MaterialCopies.Count;
            }
            foreach (string name in RegistryNames.Concat(MarkerNames)) NewWatch(name);
            NewWatch($"UniversalRenderPipeline.RenderSingleCameraInternal: {camera.name}");
        }
        Debug.Log($"HO_SHADOWS_GPU_SEGMENT {current.mode} {current.scene}");
    }

    static void Tick()
    {
        try
        {
            int profilerFrame = ProfilerDriver.lastFrameIndex;
            if (tick < Warmup)
                AttachWatches();
            else if (profilerFrame >= 0 && profilerFrame != lastProfilerFrame)
            {
                current.eligibleProfilerFrames++;
                foreach (var watch in Watches) watch.Read(profilerFrame);
                if (FrameTimingManager.GetLatestTimings(1, FrameTimings) > 0)
                {
                    var timing = FrameTimings[0];
                    if (timing.frameStartTimestamp > 0 && timing.frameStartTimestamp != lastTimingTimestamp && timing.gpuFrameTime > 0)
                        current.frameTimingGpuMilliseconds.Add(timing.gpuFrameTime);
                    lastTimingTimestamp = timing.frameStartTimestamp;
                }
            }
            lastProfilerFrame = profilerFrame;

            if (tick >= Warmup + report.requestedSamples)
            {
                FinishSegment();
                if (segmentIndex != 0 && (current.savedCameraRenders == 0 || !current.passes.Any(pass => pass.frames > 0)))
                    throw new InvalidOperationException("No saved-camera GPU samples for " + current.scene + " " + current.mode);
                if (++segmentIndex > scenes.Length * 2) { End(null); return; }
                StartSegment();
            }

            if (camera == null) SubmitCalibration();
            else
            {
                if (target == null) throw new InvalidOperationException("The saved camera has no destination texture.");
                // The shared registry expires recording requests when its HUD is not active.
                HoDebugPerformanceRegistry.RequestRecording();
                if (authoredStack) RenderPipeline.SubmitRenderRequest(camera, stackRequest);
                else RenderPipeline.SubmitRenderRequest(camera, request);
            }
            if (report.frameTimingFeature) FrameTimingManager.CaptureFrameTimings();
            current.submittedFrames++;
            tick++;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception exception) { End(exception); }
    }

    static Watch NewWatch(string name)
    {
        var watch = new Watch { result = new PassResult { name = name } };
        Watches.Add(watch);
        current.passes.Add(watch.result);
        return watch;
    }

    static void AttachWatches()
    {
        var graph = UrpGraph?.GetValue(null);
        var defaults = graph == null ? null : GraphSamplers?.GetValue(graph) as Dictionary<int, ProfilingSampler>;
        foreach (var watch in Watches)
        {
            if (watch.sampler != null || watch.recorder != null) continue;
            string name = watch.result.name;
            if (Array.IndexOf(RegistryNames, name) >= 0)
            {
                watch.sampler = HoDebugPerformanceRegistry.GetSampler(name);
                watch.result.source = "ho_registry";
            }
            else if (defaults != null && defaults.TryGetValue(name.GetHashCode(), out var sampler) && sampler.name == name)
            {
                watch.sampler = sampler;
                watch.result.source = "render_graph_default";
            }
            else
            {
                var recorder = Recorder.Get(name);
                if (recorder == null || !recorder.isValid) continue;
                watch.recorder = recorder;
                watch.result.source = "named_gpu_recorder";
                recorder.enabled = true;
            }
            if (watch.sampler != null) watch.sampler.enableRecording = true;
        }
    }

    static void SubmitCalibration()
    {
        var commands = CommandBufferPool.Get();
        using (new ProfilingScope(commands, Calibration))
        {
            for (int i = 0; i < 16; i++)
            {
                commands.Blit(Texture2D.whiteTexture, calibrationTarget);
                commands.Blit(calibrationTarget, target);
            }
        }
        Graphics.ExecuteCommandBuffer(commands);
        CommandBufferPool.Release(commands);
    }

    static void CopyPreviewMaterials(Renderer mesh)
    {
        Material[] original = mesh.sharedMaterials;
        Material[] replaced = null;
        for (int i = 0; i < original.Length; i++)
        {
            Material material = original[i];
            if (!material || !material.shader || !PreviewShaders.Contains(material.shader.name)) continue;
            if (material.renderQueue > 2500 && material.shader.name == "Ho/Environment/URP/Lit") continue;
            if (material.renderQueue > 2500 && material.shader.name == "Ho/Cloth/URP/Cloth" &&
                (!material.HasProperty("_UseHoOIT") || material.GetFloat("_UseHoOIT") < .5f)) continue;
            if (!MaterialCopies.TryGetValue(material, out var clone))
            {
                clone = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                clone.EnableKeyword(Preview);
                MaterialCopies.Add(material, clone);
            }
            replaced ??= (Material[])original.Clone();
            replaced[i] = clone;
        }
        if (replaced == null) return;
        Originals.Add(mesh, original);
        mesh.sharedMaterials = replaced;
    }

    static void FinishSegment()
    {
        foreach (var watch in Watches) watch.Finish();
        Watches.Clear();
        if (segmentIndex == 0) report.calibrationMeasured = current.passes[0].frames > 0;
        foreach (var entry in Originals) if (entry.Key) entry.Key.sharedMaterials = entry.Value;
        Originals.Clear();
        foreach (var material in MaterialCopies.Values) Object.DestroyImmediate(material);
        MaterialCopies.Clear();
        camera = null;
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }

    static void End(Exception exception)
    {
        EditorApplication.update -= Tick;
        RenderPipelineManager.endCameraRendering -= OnCameraEnd;
        if (Watches.Count > 0) FinishSegment();
        managed.SetActive(originalManaged);
        capture.CaptureShadowInputsEnabled = originalCapture;
        for (int i = 0; i < legacy.Length; i++) legacy[i].SetActive(originalLegacy[i]);
        ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, previousGpuArea);
        ProfilerDriver.profileEditor = previousProfileEditor;
        ProfilerDriver.enabled = previousProfiler;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(calibrationTarget);
        report.completed = exception == null;
        report.error = exception?.ToString();
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        if (exception != null) Debug.LogException(exception);
        Debug.Log($"HO_SHADOWS_GPU_REPORT calibration={report.calibrationMeasured} {reportPath}");
        EditorApplication.Exit(exception == null && report.calibrationMeasured ? 0 : 1);
    }

    static void OnCameraEnd(ScriptableRenderContext _, Camera rendered)
    {
        if (camera == null) return;
        if (camera == rendered) current.savedCameraRenders++;
        else current.otherCameraRenders++;
    }

    static RenderTexture MakeTarget(string name, int width, int height, int depth)
    {
        var value = new RenderTexture(width, height, depth, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave
        };
        if (!value.Create()) throw new InvalidOperationException("Could not create " + name);
        return value;
    }

    static double Percentile(double[] values, double fraction) => values[Math.Max(0, (int)Math.Ceiling(values.Length * fraction) - 1)];

    static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'),
        Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    static bool IsTemporary(string path) => Path.GetFullPath(path).StartsWith("D:\\Temp\\", StringComparison.OrdinalIgnoreCase);

    static bool PhysicalPath(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
