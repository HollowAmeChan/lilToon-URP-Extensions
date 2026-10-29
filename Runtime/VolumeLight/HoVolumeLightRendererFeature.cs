#pragma warning disable CS0618, CS0672
using System;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace lilToon.URP.Extensions.VolumeLight
{
    [DisallowMultipleRendererFeature("Ho-VolumeLight")]
    public sealed class HoVolumeLightRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [InspectorName("启用")] public bool enabled = true;
            [Min(0), InspectorName("体积亮度倍率")] public float intensity = 1;
            [Min(.01f), InspectorName("最大照亮距离")] public float maxDistance = 100;
            [Range(8, 256), InspectorName("采样步数")] public int stepCount = 48;
            [InspectorName("抗锯齿")] public HoVolumeLightAntialiasing antialiasing = HoVolumeLightAntialiasing.Temporal;
            [InspectorName("覆盖区域方向性")] public bool overrideAnisotropy;
            [Range(-.9f, .9f), InspectorName("散射方向性覆盖")] public float anisotropy = .3f;
            [InspectorName("Shader")] public Shader shader;
        }
        [SerializeField] private Settings settings = new();
        private Material material;
        private SolarPass pass;
        private SolarDebugPass debugPass;
        private HoVolumeLightDustPass dustPass;
        private HoVolumeLightLocalPass localPass;
        private bool registered;
        private readonly HoVolumeLightReconstruction reconstruction = new();
        private Texture2D blueNoise;
        private RTHandle blueNoiseHandle;
        private bool reconstructionUnavailable;
        public Settings FeatureSettings => settings;
        public string Status { get; private set; } = "未生产";
        public bool HasMaterial => material != null;
        public int HistoryCameraCount => reconstruction.Count;
        public int DustClusterCount => dustPass != null ? dustPass.ParticleCount : 0;
        [System.Obsolete("Dust is drawn as texture clusters. Use DustClusterCount.")]
        public int DustParticleCount => DustClusterCount;
        public bool DustCapacityLimited => dustPass != null && dustPass.CapacityLimited;
        public bool HasDustMaterial => dustPass != null && dustPass.HasMaterial;
        public int LocalLightCount => localPass != null ? localPass.Count : 0;
        public void ResetHistory() => reconstruction.Dispose();
        public override void Create()
        {
            ResetHistory();
            dustPass?.Dispose(); dustPass = new HoVolumeLightDustPass();
            localPass?.Dispose(); localPass = new HoVolumeLightLocalPass();
            pass = new SolarPass(this);
            debugPass?.ReleaseDebugResources();
            debugPass = new SolarDebugPass(pass);
            if (!registered)
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                SceneManager.sceneUnloaded += SceneUnloaded;
                #if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += SceneUnloaded;
                #endif
                registered = true;
            }
        }
        private void SceneUnloaded(Scene scene) => ResetHistory();
        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            localPass?.SetContext(context);
            if (!isActive) ReleaseResources();
        }
        private void ReleaseResources()
        {
            debugPass?.ReleaseDebugResources();
            ResetHistory(); blueNoise = null;
            dustPass?.Dispose();
            localPass?.Dispose();
            blueNoiseHandle?.Release(); blueNoiseHandle = null;
            CoreUtils.Destroy(material); material = null;
        }
        private void ReleaseCameraResources(Camera camera)
        {
            reconstruction.Remove(camera);
            if (reconstruction.Count == 0) ReleaseResources();
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var volume = VolumeManager.instance.stack.GetComponent<HoVolumeLightVolume>();
            bool enabled = volume != null && volume.enable.overrideState ? volume.enable.value : settings.enabled;
            if (data.cameraData.xrRendering || data.cameraData.renderType != CameraRenderType.Base ||
                (data.cameraData.cameraType != CameraType.Game && data.cameraData.cameraType != CameraType.SceneView))
            {
                Status = "本相机未启用";
                reconstruction.Remove(data.cameraData.camera);
                return;
            }
            if (!enabled)
            {
                Status = "本相机未启用";
                if (volume != null && volume.enable.overrideState) ReleaseCameraResources(data.cameraData.camera);
                else ReleaseResources();
                return;
            }
            float localIntensity = volume != null && volume.intensity.overrideState ? volume.intensity.value : settings.intensity;
            float localDistance = volume != null && volume.maxDistance.overrideState ? volume.maxDistance.value : settings.maxDistance;
            float localG = volume != null && volume.anisotropy.overrideState ? Mathf.Clamp(HoAtmosphereRegion.Finite(volume.anisotropy.value), -.9f, .9f) :
                settings.overrideAnisotropy ? Mathf.Clamp(HoAtmosphereRegion.Finite(settings.anisotropy), -.9f, .9f) : 2;
            localPass.Setup(Mathf.Max(0, HoAtmosphereRegion.Finite(localIntensity)), localG, Mathf.Max(.01f, HoAtmosphereRegion.Finite(localDistance, 100)));
            renderer.EnqueuePass(localPass);
            int main = data.lightData.mainLightIndex;
            if (main < 0 || data.lightData.visibleLights[main].lightType != LightType.Directional)
            {
                Status = "局部体积光；无主方向光";
                reconstruction.Remove(data.cameraData.camera); dustPass.Dispose(); debugPass.ReleaseDebugResources();
                return;
            }
            Shader shader = settings.shader != null ? settings.shader : Resources.Load<Shader>("HoVolumeLightSolarHD");
            if (shader == null) { Status = "Shader 缺失"; ReleaseResources(); return; }
            if (material == null || material.shader != shader)
            {
                ResetHistory();
                CoreUtils.Destroy(material); material = CoreUtils.CreateEngineMaterial(shader);
            }
            float intensity = volume != null && volume.intensity.overrideState ? volume.intensity.value : settings.intensity;
            float distance = volume != null && volume.maxDistance.overrideState ? volume.maxDistance.value : settings.maxDistance;
            int steps = volume != null && volume.stepCount.overrideState ? volume.stepCount.value : settings.stepCount;
            var aa = volume != null && volume.antialiasing.overrideState ? volume.antialiasing.value : settings.antialiasing;
            reconstructionUnavailable = aa != HoVolumeLightAntialiasing.Off && material.passCount < 6;
            if (reconstructionUnavailable) aa = HoVolumeLightAntialiasing.Off;
            if (aa != HoVolumeLightAntialiasing.Off && blueNoise == null) blueNoise = Resources.Load<Texture2D>("HoVolumeLightBlueNoise");
            if (aa != HoVolumeLightAntialiasing.Off && blueNoise == null) { Status = "采样噪声资源缺失"; return; }
            if (aa != HoVolumeLightAntialiasing.Off && (blueNoiseHandle == null || blueNoiseHandle.externalTexture == null))
            {
                blueNoiseHandle?.Release(); blueNoiseHandle = RTHandles.Alloc(blueNoise);
            }
            if (aa != HoVolumeLightAntialiasing.Temporal) reconstruction.Remove(data.cameraData.camera);
            float g = volume != null && volume.anisotropy.overrideState ? Mathf.Clamp(HoAtmosphereRegion.Finite(volume.anisotropy.value), -.9f, .9f) :
                settings.overrideAnisotropy ? Mathf.Clamp(HoAtmosphereRegion.Finite(settings.anisotropy), -.9f, .9f) : 2;
            int debug = volume != null && (data.cameraData.cameraType == CameraType.SceneView ? volume.debugInSceneView.value : volume.debugInGameView.value) ? (int)volume.debugMode.value : 0;
            int sunSignature = HoAtmosphereCache.Hash(main, data.lightData.visibleLights[main].finalColor.GetHashCode());
            var light = data.lightData.visibleLights[main].light;
            sunSignature = HoAtmosphereCache.Hash(sunSignature, light != null ? light.GetInstanceID() : 0);
            sunSignature = HoAtmosphereCache.Hash(sunSignature, data.lightData.visibleLights[main].localToWorldMatrix.GetHashCode());
            sunSignature = HoAtmosphereCache.Hash(sunSignature, light != null ? (int)light.shadows : 0);
            if (light != null)
            {
                sunSignature = HoAtmosphereCache.Hash(sunSignature, new Vector4(light.shadowStrength, light.shadowBias, light.shadowNormalBias, light.shadowNearPlane).GetHashCode());
                if (light.TryGetComponent<UniversalAdditionalLightData>(out var additional))
                    sunSignature = HoAtmosphereCache.Hash(sunSignature, (int)additional.softShadowQuality);
            }
            pass.Setup(new Vector4(Mathf.Max(0, HoAtmosphereRegion.Finite(intensity)), Mathf.Max(.01f, HoAtmosphereRegion.Finite(distance, 100)), Mathf.Clamp(steps, 8, 256), g), material, debug, aa, sunSignature);
            renderer.EnqueuePass(pass);
            dustPass.Setup(Mathf.Max(0, HoAtmosphereRegion.Finite(intensity)), g); renderer.EnqueuePass(dustPass);
            if (debug != 0) debugPass.Enqueue(renderer);
            else debugPass.ReleaseDebugResources();
            Status = "已安排太阳 HD";
        }
        protected override void Dispose(bool disposing)
        {
            if (registered)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                SceneManager.sceneUnloaded -= SceneUnloaded;
                #if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.sceneClosed -= SceneUnloaded;
                #endif
            }
            registered = false;
            ReleaseResources(); debugPass = null; pass = null; dustPass = null; localPass = null;
        }

        private sealed class SolarDebugPass : HoDebugRenderPass
        {
            private readonly SolarPass source;
            internal SolarDebugPass(SolarPass source) { this.source = source; renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) => source.Record(graph, frameData, CreateDebugTarget(graph, frameData), true);
        }

        private sealed class SolarPass : ScriptableRenderPass
        {
            private readonly HoVolumeLightRendererFeature owner;
            private Material material;
            private Vector4 parameters;
            private int debug;
            private HoVolumeLightAntialiasing antialiasing;
            private int sunSignature;
            private TextureHandle resolved;
            private static readonly int ParamsId = Shader.PropertyToID("_HoVolumeLightParams");
            private static readonly int ShadowId = Shader.PropertyToID("_HoVolumeLightShadow");
            private static readonly int DebugId = Shader.PropertyToID("_HoVolumeLightDebug");
            private static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");
            internal SolarPass(HoVolumeLightRendererFeature owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            internal void Setup(Vector4 parameters, Material material, int debug, HoVolumeLightAntialiasing antialiasing, int sunSignature)
            {
                this.parameters = parameters; this.material = material; this.debug = debug; this.antialiasing = antialiasing; this.sunSignature = sunSignature;
                resolved = TextureHandle.nullHandle;
            }
            private sealed class Data
            {
                internal Material material;
                internal Vector4 parameters, shadows;
                internal int debug;
                internal TextureHandle depth, shadowTexture;
                internal HoAtmosphereMediumResources medium;
                internal float time;
                internal int shaderPass;
                internal Vector4 sampling;
                internal TextureHandle blueNoise;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) => Record(graph, frameData, TextureHandle.nullHandle, false);
            internal void Record(RenderGraph graph, ContextContainer frameData, TextureHandle target, bool debugOutput)
            {
                var medium = frameData.GetOrCreate<HoAtmosphereMediumResources>();
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>().camera;
                if (!medium.IsValid) { owner.reconstruction.Remove(camera); owner.Status = medium.published ? medium.snapshot.status : "缺少 HoAtmosphere 介质发布"; return; }
                if (medium.snapshot.densityAndWeight.x <= 0 || medium.snapshot.densityAndWeight.y <= 0 || parameters.x <= 0)
                {
                    owner.Status = "体积贡献为零";
                    owner.reconstruction.Remove(camera);
                    return;
                }
                if (!resources.cameraDepthTexture.IsValid()) { owner.reconstruction.Remove(camera); owner.Status = "相机深度缺失"; return; }
                if (debugOutput && debug == 1 && resolved.IsValid())
                {
                    HoVolumeLightReconstruction.Present(graph, material, resolved, target, true); return;
                }
                bool reconstruct = antialiasing != HoVolumeLightAntialiasing.Off && !debugOutput;
                var descriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
                Vector4 sampling = new Vector4(reconstruct ? 1 : 0, antialiasing == HoVolumeLightAntialiasing.Temporal ? owner.reconstruction.Sequence(camera) : 0, descriptor.width, descriptor.height);
                TextureHandle raw = reconstruct ? graph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
                    { name = "Ho-VolumeLight Samples", colorFormat = GraphicsFormat.R16G16B16A16_SFloat, clearBuffer = false }) : TextureHandle.nullHandle;
                var shadows = frameData.Get<UniversalShadowData>();
                using (var builder = graph.AddRasterRenderPass<Data>(debugOutput ? "Ho-VolumeLight Debug" : "Ho-VolumeLight Solar HD", out var data))
                {
                    data.material = material; data.parameters = parameters; data.debug = debugOutput ? debug : 0; data.medium = medium; data.time = medium.snapshot.sampleTime; data.shaderPass = debugOutput ? 1 : 0;
                    data.sampling = sampling;
                    if (reconstruct) { data.shaderPass = 2; data.blueNoise = graph.ImportTexture(owner.blueNoiseHandle); builder.UseTexture(data.blueNoise, AccessFlags.Read); }
                    data.depth = resources.cameraDepthTexture; data.shadowTexture = resources.mainShadowsTexture;
                    data.shadows = new Vector4(shadows.mainLightShadowCascadesCount > 1 ? 1 : 0, shadows.supportsMainLightShadows && data.shadowTexture.IsValid() ? 1 : 0, 0, 0);
                    builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                    if (data.shadowTexture.IsValid()) builder.UseTexture(data.shadowTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(reconstruct ? raw : debugOutput ? target : resources.activeColorTexture, 0, reconstruct || debugOutput ? AccessFlags.WriteAll : AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.time);
                        context.cmd.SetGlobalTexture(DepthId, d.depth);
                        if (d.shadowTexture.IsValid()) context.cmd.SetGlobalTexture(Shader.PropertyToID("_MainLightShadowmapTexture"), d.shadowTexture);
                        context.cmd.SetGlobalVector(ParamsId, d.parameters); context.cmd.SetGlobalVector(ShadowId, d.shadows);
                        context.cmd.SetGlobalInt(DebugId, d.debug);
                        context.cmd.SetGlobalVector("_HoVolumeLightSampling", d.sampling);
                        if (d.blueNoise.IsValid()) context.cmd.SetGlobalTexture("_HoVolumeLightBlueNoise", d.blueNoise);
                        Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), d.material, d.shaderPass);
                    });
                }
                if (reconstruct)
                {
                    int signature = HoAtmosphereCache.Hash(sunSignature, HoAtmosphereCache.Hash(parameters.GetHashCode(), (int)medium.snapshot.revision));
                    signature = HoAtmosphereCache.Hash(signature, frameData.Get<UniversalCameraData>().maxShadowDistance.GetHashCode());
                    signature = HoAtmosphereCache.Hash(signature, shadows.mainLightShadowCascadesCount);
                    signature = HoAtmosphereCache.Hash(signature, shadows.mainLightShadowCascadesSplit.GetHashCode());
                    signature = HoAtmosphereCache.Hash(signature, shadows.mainLightShadowCascadeBorder.GetHashCode());
                    signature = HoAtmosphereCache.Hash(signature, HoAtmosphereCache.Hash(shadows.mainLightShadowmapWidth, shadows.mainLightShadowmapHeight));
                    signature = HoAtmosphereCache.Hash(signature, shadows.supportsMainLightShadows ? 1 : 0);
                    signature = HoAtmosphereCache.Hash(signature, shadows.supportsSoftShadows ? 1 : 0);
                    resolved = owner.reconstruction.Record(graph, frameData, raw, material, parameters, sampling, medium, antialiasing, signature);
                    HoVolumeLightReconstruction.Present(graph, material, resolved, resources.activeColorTexture, false);
                }
                owner.Status = owner.reconstructionUnavailable ? "太阳 HD 可用；自定义 Shader 缺少抗锯齿 Pass" : "太阳 HD 可用";
            }
        }
    }
}
