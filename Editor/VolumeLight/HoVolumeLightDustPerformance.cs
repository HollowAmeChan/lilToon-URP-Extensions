using System;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    public static partial class HoVolumeLightValidation
    {
        private static int dustTimingStage, dustTimingFrame, dustTimingCount;
        private static double dustTimingSum;
        private static double dustTimingStarted;
        private static int dustTimingLastFrame;
        private static bool dustProfilerStarted, previousProfilerEnabled, previousProfileEditor, previousGpuArea;
        private static readonly System.Collections.Generic.List<float> dustTimingSamples = new();
        private static readonly System.Collections.Generic.List<int> dustTimingFrameIds = new();
        public static void RunDustDepthTiming() { dustTimingStage = 3; RunDust(); }
        private static void BeginDustTiming()
        {
            // Same 1080p camera, medium, phase and sun visibility for all draws.
            target = Keep(new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
            camera.aspect = 1920f / 1080; camera.farClipPlane = 1000;
            region.transform.position = Vector3.zero; region.size = Vector3.one * 500;
            region.density = .02f; region.anisotropy = .3f; region.wind = Vector3.zero;
            region.dust = new HoAtmosphereDustSettings { enabled = true };
            sun.intensity = 2; sun.transform.rotation = Quaternion.LookRotation(Vector3.back); sun.shadows = LightShadows.Hard;
            var window = Quad("Benchmark cutout", new Vector3(0, 1, 0), new Vector3(100, 100, 1));
            window.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var cutout = window.GetComponent<Renderer>().sharedMaterial;
            var alpha = Keep(new Texture2D(8, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point });
            var pixels = new Color[8]; for (int i = 0; i < pixels.Length; ++i) pixels[i] = new Color(0, 0, 0, i % 2);
            alpha.SetPixels(pixels); alpha.Apply(); cutout.SetTexture("_BaseMap", alpha); cutout.SetFloat("_AlphaClip", 1); cutout.SetFloat("_Cutoff", .5f); cutout.EnableKeyword("_ALPHATEST_ON");
            lightFeature.FeatureSettings.antialiasing = HoVolumeLightAntialiasing.Off; lightFeature.FeatureSettings.stepCount = 8; lightFeature.FeatureSettings.intensity = 1;
            HoAtmosphereMediumClock.TimeOverride = 0;
            previousProfilerEnabled = UnityEditorInternal.ProfilerDriver.enabled;
            previousProfileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
            previousGpuArea = UnityEditorInternal.ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU);
            dustProfilerStarted = true;
            UnityEditorInternal.ProfilerDriver.profileEditor = true; UnityEditorInternal.ProfilerDriver.enabled = true;
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, true);
            HoVolumeLightDustPass.Profiling.enableRecording = true; HoVolumeLightDustReferenceFeature.Profiling.enableRecording = true;
            HoVolumeLightDustPass.ProfilingCameraId = camera.GetInstanceID(); dustReference.observer = camera;
            dustReport.gpu = SystemInfo.graphicsDeviceName;
            PrepareDustTimingStage(); EditorApplication.update += TickDustTiming;
        }
        private static void RestoreDustProfiling()
        {
            if (!dustProfilerStarted) return;
            UnityEditorInternal.ProfilerDriver.enabled = previousProfilerEnabled;
            UnityEditorInternal.ProfilerDriver.profileEditor = previousProfileEditor;
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, previousGpuArea);
            dustProfilerStarted = false;
        }
        private static void PrepareDustTimingStage()
        {
            dustTimingFrame = dustTimingCount = 0; dustTimingSum = 0; dustTimingSamples.Clear(); dustTimingFrameIds.Clear(); dustTimingLastFrame = -1;
            if (dustTimingStage == 3)
            {
                region.dust = new HoAtmosphereDustSettings { enabled = true, maxDistance = 200, maxClusters = 128, thickness = 3, brightness = 25 };
                pipeline.shadowDistance = 250;
            }
            region.dust.enabled = false; dustReference.SetActive(false);
            var baseline = Render();
            if (dustTimingStage == 0 || dustTimingStage == 3) region.dust.enabled = true;
            else { dustReference.Build(dustTimingStage == 1 ? 500 : 60); dustReference.SetActive(true); }
            var with = Render(); var contribution = new Color[with.Length]; int changed = 0;
            for (int i = 0; i < with.Length; ++i) { contribution[i] = with[i] - baseline[i]; if (contribution[i].r > .001f) ++changed; }
            Save(contribution, "dust-benchmark-" + dustTimingStage);
            if (dustTimingStage == 0) { dustReport.benchmarkClusterPixels = changed; dustReport.benchmarkClusterMean = Mean(contribution); }
            if (dustTimingStage == 1) { dustReport.benchmarkGlobalPixels = changed; dustReport.benchmarkGlobalMean = Mean(contribution); }
            if (dustTimingStage == 2) { dustReport.benchmarkLocalPixels = changed; dustReport.benchmarkLocalMean = Mean(contribution); }
            Debug.Log($"[HoDustPerformance] stage={dustTimingStage} instances={(dustTimingStage == 0 || dustTimingStage == 3 ? lightFeature.DustClusterCount : 1000000)} pixels={changed} mean={Mean(contribution)}");
            dustTimingStarted = EditorApplication.timeSinceStartup;
        }
        private static void TickDustTiming()
        {
            try
            {
                var sampler = dustTimingStage == 0 || dustTimingStage == 3 ? HoVolumeLightDustPass.Profiling : HoVolumeLightDustReferenceFeature.Profiling;
                int frameId = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
                if (frameId > dustTimingLastFrame && dustTimingFrame >= 32 && EditorApplication.timeSinceStartup - dustTimingStarted >= 2 && sampler.gpuSampleCount > 0 && sampler.gpuElapsedTime > 0)
                {
                    float sample = sampler.gpuElapsedTime / sampler.gpuSampleCount;
                    dustTimingSum += sample; dustTimingCount++; dustTimingSamples.Add(sample);
                    dustTimingFrameIds.Add(frameId); dustTimingLastFrame = frameId;
                }
                VolumeManager.instance.Update(camera.transform, -1);
                renderError = null; Application.logMessageReceived += CaptureError;
                try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target }); }
                finally { Application.logMessageReceived -= CaptureError; }
                Require(renderError == null, "Timing render error: " + renderError);
                if (++dustTimingFrame < 3600 && dustTimingCount < 32) return;
                var samples = dustTimingSamples.ToArray(); var ordered = (float[])samples.Clone(); Array.Sort(ordered);
                // A constant frozen counter is inconclusive, not 32 independent timings.
                float ms = dustTimingCount >= 32 && ordered[ordered.Length - 1] - ordered[0] > .000001f ? (float)(dustTimingSum / dustTimingCount) : -1;
                var timing = new DustTimingReport { stage = dustTimingStage, mean = ms, samples = samples, frameIds = dustTimingFrameIds.ToArray(),
                    median = ms < 0 ? -1 : ordered[ordered.Length / 2], p95 = ms < 0 ? -1 : ordered[Mathf.CeilToInt((ordered.Length - 1) * .95f)],
                    minimum = ms < 0 ? -1 : ordered[0], maximum = ms < 0 ? -1 : ordered[ordered.Length - 1] };
                dustReport.timings[dustTimingStage] = timing;
                if (dustTimingStage == 0) dustReport.clustersGpuMs = ms;
                if (dustTimingStage == 1) dustReport.millionGlobalGpuMs = ms;
                if (dustTimingStage == 2) dustReport.millionLocalGpuMs = ms;
                if (dustTimingStage == 3) dustReport.largeSceneGpuMs = ms;
                Debug.Log($"[HoDustPerformance] stage={dustTimingStage} frames={dustTimingFrame} received={dustTimingCount} gpuMs={ms} median={timing.median} p95={timing.p95} max={timing.maximum}; unavailable=-1");
                if (++dustTimingStage < 4) { PrepareDustTimingStage(); return; }
                // Million local is a stronger baseline, but equal coverage is not assumed.
                if (dustReport.benchmarkClusterPixels > 0) Require(dustReport.benchmarkClusterPixels > dustReport.benchmarkGlobalPixels * 10, "cluster coverage did not improve the large-region failure case");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
    }

    // Editor-only reference to the previous one-billboard-per-point representation.
    // Production never allocates these populations or loads its shader.
    [SupportedOnRenderer()]
    internal sealed class HoVolumeLightDustReferenceFeature : ScriptableRendererFeature
    {
        internal static readonly ProfilingSampler Profiling = new("Ho-Dust Million Points Reference");
        internal Camera observer;
        private GraphicsBuffer seeds;
        private Material material;
        private ReferencePass pass;
        public override void Create() => pass = new ReferencePass(this);
        internal void Build(float boxSize)
        {
            seeds?.Dispose(); const int count = 1000000;
            var values = new Vector4[count]; var random = new System.Random(17);
            for (int i = 0; i < count; ++i) values[i] = new Vector4(((float)random.NextDouble() - .5f) * boxSize,
                ((float)random.NextDouble() - .5f) * boxSize, ((float)random.NextDouble() - .5f) * boxSize, (float)random.NextDouble());
            seeds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16); seeds.SetData(values);
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/lilToon/URP/VolumeLight/DustPointReference"));
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { if (seeds != null && data.cameraData.camera == observer) renderer.EnqueuePass(pass); }
        protected override void Dispose(bool disposing) { seeds?.Dispose(); seeds = null; CoreUtils.Destroy(material); material = null; }
        private sealed class ReferencePass : ScriptableRenderPass
        {
            private readonly HoVolumeLightDustReferenceFeature owner;
            internal ReferencePass(HoVolumeLightDustReferenceFeature owner) { this.owner = owner; renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; ConfigureInput(ScriptableRenderPassInput.Depth); }
            private sealed class Data
            {
                internal Material material;
                internal HoAtmosphereMediumResources medium;
                internal BufferHandle seeds;
                internal TextureHandle depth, shadow;
                internal Vector4 shadowParams;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>(); var shadows = frame.Get<UniversalShadowData>();
                var medium = frame.GetOrCreate<HoAtmosphereMediumResources>();
                using (var builder = graph.AddRasterRenderPass<Data>("Ho-Dust Million Points Reference", out var data, Profiling))
                {
                    data.material = owner.material; data.medium = medium; data.seeds = graph.ImportBuffer(owner.seeds);
                    data.depth = resources.cameraDepthTexture; data.shadow = resources.mainShadowsTexture;
                    data.shadowParams = new Vector4(shadows.mainLightShadowCascadesCount > 1 ? 1 : 0, shadows.supportsMainLightShadows && data.shadow.IsValid() ? 1 : 0, 0, 0);
                    builder.UseBuffer(data.seeds, AccessFlags.Read); builder.UseTexture(data.depth, AccessFlags.Read); builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                    if (data.shadow.IsValid()) builder.UseTexture(data.shadow, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite); builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        HoAtmosphereMediumBindings.Bind(ctx.cmd, d.medium, d.medium.snapshot.sampleTime);
                        ctx.cmd.SetGlobalTexture("_CameraDepthTexture", d.depth);
                        if (d.shadow.IsValid()) ctx.cmd.SetGlobalTexture("_MainLightShadowmapTexture", d.shadow);
                        ctx.cmd.SetGlobalVector("_HoVolumeLightShadow", d.shadowParams); ctx.cmd.SetGlobalInt("_HoVolumeLightDebug", 0);
                        ctx.cmd.SetGlobalVector("_HoVolumeLightParams", new Vector4(0, 30, 0, 2));
                        ctx.cmd.SetGlobalVector("_HoAtmosphereDustShape", new Vector4(.008f, .025f, .12f, 30));
                        ctx.cmd.SetGlobalVector("_HoAtmosphereDustLighting", new Vector4(20, 0, 2, 1));
                        ctx.cmd.SetGlobalBuffer("_HoAtmosphereDustSeeds", d.seeds);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 6, 1000000);
                    });
                }
            }
        }
    }
}
