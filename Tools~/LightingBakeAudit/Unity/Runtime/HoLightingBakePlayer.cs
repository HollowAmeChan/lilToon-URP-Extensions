using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Hollow.HoUnityTools.MmdMotion;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.ShadowCast;

public sealed class HoLightingBakePlayer : MonoBehaviour
{
    [Serializable] sealed class Result
    {
        public string stage, mode, scene, gpu, api, pipeline;
        public string probeSystem;
        public bool frameTimingEnabled, shadowCastActive, rtgiFeatureActive, rtgiVolumeActive;
        public ulong timerHz;
        public int frames, warmupFrames, requestedSamples, validSamples, lightmaps, legacyProbes, apvVolumes;
        public int shadowMarkerFrames, rtgiMarkerFrames;
        public double meanGpuMs, medianGpuMs, p95GpuMs, minGpuMs, maxGpuMs, wallSeconds;
        public double shadowMarkerMeanMs, rtgiMarkerMeanMs;
        public double[] gpuSamples;
        public ulong[] frameIds;
        public string screenshot60, screenshot180;
    }
    static string requestedStage, requestedMode, output;
    static int frame;
    static bool ready;
    static double started;
    static readonly FrameTiming[] timings = new FrameTiming[1];
    static readonly List<double> samples = new();
    static readonly List<ulong> ids = new();
    static ProfilingSampler shadowMarker, rtgiMarker;
    static long shadowLastCount, rtgiLastCount;
    static double shadowLastElapsed, rtgiLastElapsed;
    static readonly List<double> shadowSamples = new(), rtgiSamples = new();
    static string SceneName(string stage, string mode) => "Assets/LightingBakeAudit/" + stage + "/" +
        mode + "/" + stage + "-" + mode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Read(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        requestedStage = Read("-hoStage");
        requestedMode = Read("-hoMode");
        output = Read("-hoOutput");
        if (string.IsNullOrEmpty(requestedStage) || string.IsNullOrEmpty(requestedMode) || string.IsNullOrEmpty(output))
        { Debug.LogError("Lighting benchmark needs -hoStage, -hoMode and -hoOutput."); Application.Quit(2); return; }
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed, 60);
        var owner = new GameObject("Ho Lighting Bake GPU Benchmark");
        DontDestroyOnLoad(owner);
        owner.AddComponent<HoLightingBakePlayer>();
        string scenePath = SceneName(requestedStage, requestedMode) + ".unity";
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
        if (sceneIndex < 0)
        {
            Debug.LogError("Lighting benchmark scene is missing from the player build: " + scenePath);
            Application.Quit(2);
            return;
        }
        SceneManager.LoadScene(sceneIndex, LoadSceneMode.Single);
    }

    void Start()
    {
        try
        {
            requestedProbeSystem = (requestedMode == "C" || requestedMode == "E") ? 0 : 1;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 90;
            QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var data = field?.GetValue(asset) as ScriptableRendererData[];
            if (data == null || data.Length == 0) throw new InvalidOperationException("Cannot resolve renderer data.");
            SetLightProbeSystem(asset, requestedProbeSystem);
            foreach (var feature in data.SelectMany(x => x.rendererFeatures).Where(x => x != null))
            {
                if (feature is HoShadowCastRendererFeature shadow) shadow.SetActive(requestedMode == "A");
                if (feature is HoRTGIRendererFeature gi) gi.SetActive("ABCD".Contains(requestedMode));
            }
            var shadowType = typeof(HoShadowCastRendererFeature).Assembly.GetType(
                "lilToon.URP.Extensions.ShadowCast.HoShadowCastPass");
            shadowMarker = (ProfilingSampler)shadowType?.GetField("ProfilingSampler",
                BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (shadowMarker != null) shadowMarker.enableRecording = true;
            var giType = typeof(HoRTGIRendererFeature).GetNestedType("HoRTGIPass", BindingFlags.NonPublic);
            rtgiMarker = (ProfilingSampler)giType?.GetField("LightingBakeAuditSampler",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
            if (rtgiMarker != null) rtgiMarker.enableRecording = true;
            shadowLastCount = shadowMarker != null ? shadowMarker.gpuSampleCount : 0;
            shadowLastElapsed = shadowMarker != null ? shadowMarker.gpuElapsedTime : 0;
            rtgiLastCount = rtgiMarker != null ? rtgiMarker.gpuSampleCount : 0;
            rtgiLastElapsed = rtgiMarker != null ? rtgiMarker.gpuElapsedTime : 0;
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var camera in cameras) camera.enabled = camera == Camera.main;
            if (Camera.main == null) throw new InvalidOperationException("Benchmark scene has no MainCamera.");
            var cameraTarget = Camera.main;
            cameraTarget.GetUniversalAdditionalCameraData().resetHistory = true;
            foreach (var player in FindObjectsByType<HoVmdMotionPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            { player.Playing = false; player.RebuildBindings(); player.ApplyAtFrame(240); }
            foreach (var player in FindObjectsByType<HoVmdCameraPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            { player.Playing = false; player.RebuildTrack(); if (player.TargetCamera == cameraTarget) player.ApplyAtFrame(240); }
            foreach (var player in FindObjectsByType<HoVmdSequencePlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                player.Playing = false;
            samples.Clear(); ids.Clear(); frame = 0; started = Time.realtimeSinceStartupAsDouble; ready = true;
        }
        catch (Exception error) { Debug.LogException(error); Application.Quit(3); }
    }

    static void SetLightProbeSystem(UniversalRenderPipelineAsset asset, int value)
    {
        var property = typeof(UniversalRenderPipelineAsset).GetProperty("lightProbeSystem",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite) throw new MissingMemberException("URP lightProbeSystem is not writable.");
        property.SetValue(asset, Enum.ToObject(property.PropertyType, value));
    }

    void Update()
    {
        if (!ready) return;
        frame++;
        SampleMarker(shadowMarker, shadowSamples, ref shadowLastCount, ref shadowLastElapsed);
        SampleMarker(rtgiMarker, rtgiSamples, ref rtgiLastCount, ref rtgiLastElapsed);
        FrameTimingManager.CaptureFrameTimings();
        if (frame == 60) ScreenCapture.CaptureScreenshot(output + ".frame060.png");
        if (frame == 180) ScreenCapture.CaptureScreenshot(output + ".frame180.png");
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
        {
            var timing = timings[0];
            if (timing.frameStartTimestamp > 0 && (ids.Count == 0 || ids[ids.Count - 1] != timing.frameStartTimestamp)
                && frame > 45 && timing.gpuFrameTime > 0 && !double.IsNaN(timing.gpuFrameTime)
                && !double.IsInfinity(timing.gpuFrameTime))
            { ids.Add(timing.frameStartTimestamp); samples.Add(timing.gpuFrameTime); }
        }
        if (frame >= 245) Write();
    }

    void Write()
    {
        ready = false;
        double[] sorted = samples.OrderBy(x => x).ToArray();
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var rendererData = pipeline == null ? null :
            typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pipeline) as ScriptableRendererData[];
        var features = rendererData == null ? Array.Empty<ScriptableRendererFeature>() :
            rendererData.Where(x => x != null)
                .SelectMany(x => x.rendererFeatures ?? new List<ScriptableRendererFeature>())
                .Where(x => x != null).ToArray();
        var volumeStack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        var giVolume = volumeStack != null ? volumeStack.GetComponent<HoRTGIVolume>() : null;
        var report = new Result {
            stage = requestedStage, mode = requestedMode, scene = SceneManager.GetActiveScene().path,
            gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
            pipeline = pipeline != null ? pipeline.name : null,
            probeSystem = GetProbeSystemName(pipeline),
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), timerHz = FrameTimingManager.GetGpuTimerFrequency(),
            frames = frame, warmupFrames = 45, requestedSamples = 200, validSamples = sorted.Length,
            shadowMarkerFrames = shadowSamples.Count, rtgiMarkerFrames = rtgiSamples.Count,
            meanGpuMs = sorted.Length > 0 ? sorted.Average() : -1,
            medianGpuMs = Percentile(sorted, .5), p95GpuMs = Percentile(sorted, .95),
            minGpuMs = sorted.Length > 0 ? sorted[0] : -1, maxGpuMs = sorted.Length > 0 ? sorted[sorted.Length - 1] : -1,
            gpuSamples = sorted, frameIds = ids.ToArray(), wallSeconds = Time.realtimeSinceStartupAsDouble - started,
            shadowMarkerMeanMs = shadowSamples.Count > 0 ? shadowSamples.Average() : -1,
            rtgiMarkerMeanMs = rtgiSamples.Count > 0 ? rtgiSamples.Average() : -1,
            shadowCastActive = features != null && features.OfType<HoShadowCastRendererFeature>().Any(x => x.isActive),
            rtgiFeatureActive = features != null && features.OfType<HoRTGIRendererFeature>().Any(x => x.isActive && x.Settings.enabled),
            rtgiVolumeActive = giVolume != null && giVolume.active && giVolume.enable.value,
            lightmaps = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0,
            legacyProbes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0,
            apvVolumes = FindObjectsByType<ProbeVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length,
            screenshot60 = output + ".frame060.png", screenshot180 = output + ".frame180.png"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output + ".json", JsonUtility.ToJson(report, true));
        Debug.Log("HO_LIGHTING_BAKE_PLAYER=" + output + ".json samples=" + sorted.Length + " gpuMs=" + report.meanGpuMs);
        Application.Quit(sorted.Length >= 120 ? 0 : 4);
    }
    static void SampleMarker(ProfilingSampler sampler, List<double> samples, ref long previousCount, ref double previousElapsed)
    {
        if (sampler == null) return;
        long count = sampler.gpuSampleCount; double elapsed = sampler.gpuElapsedTime;
        long deltaCount = count - previousCount; double deltaElapsed = elapsed - previousElapsed;
        if (deltaCount > 0 && deltaElapsed > 0) samples.Add(deltaElapsed / deltaCount);
        previousCount = count; previousElapsed = elapsed;
    }
    static double Percentile(double[] sorted, double percentile) =>
        sorted.Length > 0 ? sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * (float)percentile) - 1, 0, sorted.Length - 1)] : -1;
    static int requestedProbeSystem;
    static string GetProbeSystemName(UniversalRenderPipelineAsset asset)
    {
        var property = typeof(UniversalRenderPipelineAsset).GetProperty("lightProbeSystem",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return asset != null && property != null ? property.GetValue(asset).ToString() : "unavailable";
    }
}
