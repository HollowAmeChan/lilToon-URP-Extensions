#pragma warning disable CS0618, CS0672
using System;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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
            [Range(0, .9f), InspectorName("散射方向性")] public float anisotropy = .3f;
            [InspectorName("Shader")] public Shader shader;
        }
        [SerializeField] private Settings settings = new();
        private Material material;
        private SolarPass pass;
        private SolarDebugPass debugPass;
        private bool registered;
        public Settings FeatureSettings => settings;
        public string Status { get; private set; } = "未生产";
        public bool HasMaterial => material != null;
        public override void Create()
        {
            pass = new SolarPass(this);
            debugPass?.ReleaseDebugResources();
            debugPass = new SolarDebugPass(pass);
            if (!registered)
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                registered = true;
            }
        }
        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!isActive) ReleaseResources();
        }
        private void ReleaseResources()
        {
            debugPass?.ReleaseDebugResources();
            CoreUtils.Destroy(material); material = null;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var volume = VolumeManager.instance.stack.GetComponent<HoVolumeLightVolume>();
            bool enabled = volume != null && volume.enable.overrideState ? volume.enable.value : settings.enabled;
            if (!enabled || data.cameraData.renderType != CameraRenderType.Base ||
                (data.cameraData.cameraType != CameraType.Game && data.cameraData.cameraType != CameraType.SceneView))
            {
                Status = "本相机未启用";
                ReleaseResources();
                return;
            }
            int main = data.lightData.mainLightIndex;
            if (main < 0 || data.lightData.visibleLights[main].lightType != LightType.Directional)
            {
                Status = "缺少主方向光";
                ReleaseResources();
                return;
            }
            Shader shader = settings.shader != null ? settings.shader : Resources.Load<Shader>("HoVolumeLightSolarHD");
            if (shader == null) { Status = "Shader 缺失"; ReleaseResources(); return; }
            if (material == null || material.shader != shader)
            {
                CoreUtils.Destroy(material); material = CoreUtils.CreateEngineMaterial(shader);
            }
            float intensity = volume != null && volume.intensity.overrideState ? volume.intensity.value : settings.intensity;
            float distance = volume != null && volume.maxDistance.overrideState ? volume.maxDistance.value : settings.maxDistance;
            int steps = volume != null && volume.stepCount.overrideState ? volume.stepCount.value : settings.stepCount;
            float g = volume != null && volume.anisotropy.overrideState ? volume.anisotropy.value : settings.anisotropy;
            int debug = volume != null && (data.cameraData.cameraType == CameraType.SceneView ? volume.debugInSceneView.value : volume.debugInGameView.value) ? (int)volume.debugMode.value : 0;
            pass.Setup(new Vector4(Mathf.Max(0, HoAtmosphereRegion.Finite(intensity)), Mathf.Max(.01f, HoAtmosphereRegion.Finite(distance, 100)), Mathf.Clamp(steps, 8, 256), Mathf.Clamp(HoAtmosphereRegion.Finite(g), 0, .9f)), material, debug);
            renderer.EnqueuePass(pass);
            if (debug != 0) debugPass.Enqueue(renderer);
            else debugPass.ReleaseDebugResources();
            Status = "已安排太阳 HD";
        }
        protected override void Dispose(bool disposing)
        {
            if (registered) RenderPipelineManager.beginCameraRendering -= BeginCamera;
            registered = false;
            ReleaseResources(); debugPass = null; pass = null;
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
            internal void Setup(Vector4 parameters, Material material, int debug) { this.parameters = parameters; this.material = material; this.debug = debug; }
            private sealed class Data
            {
                internal Material material;
                internal Vector4 parameters, shadows;
                internal int debug;
                internal TextureHandle depth, shadowTexture;
                internal HoAtmosphereMediumResources medium;
                internal float time;
                internal int shaderPass;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) => Record(graph, frameData, TextureHandle.nullHandle, false);
            internal void Record(RenderGraph graph, ContextContainer frameData, TextureHandle target, bool debugOutput)
            {
                var medium = frameData.GetOrCreate<HoAtmosphereMediumResources>();
                var resources = frameData.Get<UniversalResourceData>();
                if (!medium.IsValid) { owner.Status = medium.published ? medium.snapshot.status : "缺少 HoAtmosphere 介质发布"; return; }
                if (medium.snapshot.densityAndWeight.x <= 0 || medium.snapshot.densityAndWeight.y <= 0 || parameters.x <= 0)
                {
                    owner.Status = "体积贡献为零";
                    return;
                }
                if (!resources.cameraDepthTexture.IsValid()) { owner.Status = "相机深度缺失"; return; }
                var shadows = frameData.Get<UniversalShadowData>();
                using (var builder = graph.AddRasterRenderPass<Data>(debugOutput ? "Ho-VolumeLight Debug" : "Ho-VolumeLight Solar HD", out var data))
                {
                    data.material = material; data.parameters = parameters; data.debug = debugOutput ? debug : 0; data.medium = medium; data.time = medium.snapshot.sampleTime; data.shaderPass = debugOutput ? 1 : 0;
                    data.depth = resources.cameraDepthTexture; data.shadowTexture = resources.mainShadowsTexture;
                    data.shadows = new Vector4(shadows.mainLightShadowCascadesCount > 1 ? 1 : 0, shadows.supportsMainLightShadows && data.shadowTexture.IsValid() ? 1 : 0, 0, 0);
                    builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                    if (data.shadowTexture.IsValid()) builder.UseTexture(data.shadowTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(debugOutput ? target : resources.activeColorTexture, 0, debugOutput ? AccessFlags.Write : AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.time);
                        context.cmd.SetGlobalTexture(DepthId, d.depth);
                        if (d.shadowTexture.IsValid()) context.cmd.SetGlobalTexture(Shader.PropertyToID("_MainLightShadowmapTexture"), d.shadowTexture);
                        context.cmd.SetGlobalVector(ParamsId, d.parameters); context.cmd.SetGlobalVector(ShadowId, d.shadows);
                        context.cmd.SetGlobalInt(DebugId, d.debug);
                        Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), d.material, d.shaderPass);
                    });
                }
                owner.Status = "太阳 HD 可用";
            }
        }
    }
}
