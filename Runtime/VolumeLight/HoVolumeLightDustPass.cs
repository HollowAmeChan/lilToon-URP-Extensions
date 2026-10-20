using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    internal sealed class HoVolumeLightDustPass : ScriptableRenderPass
    {
        internal static readonly ProfilingSampler Profiling = new("Ho-VolumeLight Dust Clusters");
        internal static int ProfilingCameraId;
        private static readonly ProfilingSampler OtherCameraProfiling = new("Ho-VolumeLight Dust Other Camera");
        private Material material;
        private RTHandle pattern;
        private float intensity, anisotropy;
        internal bool HasMaterial => material != null;
        internal int ParticleCount { get; private set; }
        internal bool CapacityLimited { get; private set; }
        internal HoVolumeLightDustPass() { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; ConfigureInput(ScriptableRenderPassInput.Depth); }
        internal void Setup(float intensity, float anisotropy) { this.intensity = intensity; this.anisotropy = anisotropy; }
        internal void Dispose() { CoreUtils.Destroy(material); material = null; pattern?.Release(); pattern = null; ParticleCount = 0; CapacityLimited = false; }
        private sealed class Data
        {
            internal Material material;
            internal HoAtmosphereMediumResources medium;
            internal BufferHandle seeds;
            internal TextureHandle depth, shadow, pattern;
            internal Vector4 shape, lighting, shadows, textureParams;
            internal int count;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            ParticleCount = 0; CapacityLimited = false;
            var medium = frame.GetOrCreate<HoAtmosphereMediumResources>();
            var resources = frame.Get<UniversalResourceData>();
            var s = medium.snapshot;
            if (!medium.IsValid || s.owner == null || !s.dust.enabled || s.dust.grainDensity <= 0 || s.densityAndWeight.x <= 0 || s.densityAndWeight.y <= 0 || intensity <= 0)
            {
                if (s.owner != null && !s.dust.enabled) s.owner.ReleaseDust();
                Dispose(); return;
            }
            if (!resources.cameraDepthTexture.IsValid()) { Dispose(); return; }
            var field = s.owner.GetDustField(); var view = field.Prepare(s, frame.Get<UniversalCameraData>().camera);
            if (view == null || view.count == 0) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("HoVolumeLightDust");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            var texture = s.dust.texture != null ? s.dust.texture : Resources.Load<Texture2D>("HoAtmosphereDustClusters");
            if (texture == null) return;
            if (pattern == null || pattern.externalTexture != texture) { pattern?.Release(); pattern = RTHandles.Alloc(texture); }
            ParticleCount = view.count; CapacityLimited = view.limited;
            var shadows = frame.Get<UniversalShadowData>();
            var sampler = ProfilingCameraId == 0 || frame.Get<UniversalCameraData>().camera.GetInstanceID() == ProfilingCameraId ? Profiling : OtherCameraProfiling;
            using (var builder = graph.AddRasterRenderPass<Data>("Ho-VolumeLight Dust Clusters", out var data, sampler))
            {
                data.material = material; data.medium = medium; data.count = view.count;
                data.seeds = graph.ImportBuffer(view.buffer); data.depth = resources.cameraDepthTexture; data.shadow = resources.mainShadowsTexture;
                data.pattern = graph.ImportTexture(pattern);
                data.shape = new Vector4(view.cardSize, s.dust.textureWorldSize, s.dust.flutter, view.distance);
                data.textureParams = new Vector4(s.dust.grainDensity, s.dust.thickness, HoAtmosphereDustField.MaximumLevel(s.dust), 0);
                data.lighting = new Vector4(s.dust.brightness, s.sampleTime, anisotropy, intensity);
                data.shadows = new Vector4(shadows.mainLightShadowCascadesCount > 1 ? 1 : 0, shadows.supportsMainLightShadows && data.shadow.IsValid() ? 1 : 0, 0, 0);
                builder.UseBuffer(data.seeds, AccessFlags.Read); builder.UseTexture(data.depth, AccessFlags.Read); builder.UseTexture(medium.densityTexture, AccessFlags.Read); builder.UseTexture(data.pattern, AccessFlags.Read);
                if (data.shadow.IsValid()) builder.UseTexture(data.shadow, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite); builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                {
                    HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.medium.snapshot.sampleTime);
                    context.cmd.SetGlobalTexture("_CameraDepthTexture", d.depth);
                    if (d.shadow.IsValid()) context.cmd.SetGlobalTexture("_MainLightShadowmapTexture", d.shadow);
                    context.cmd.SetGlobalVector("_HoVolumeLightShadow", d.shadows); context.cmd.SetGlobalInt("_HoVolumeLightDebug", 0);
                    context.cmd.SetGlobalVector("_HoAtmosphereDustShape", d.shape); context.cmd.SetGlobalVector("_HoAtmosphereDustLighting", d.lighting);
                    context.cmd.SetGlobalTexture("_HoAtmosphereDustPattern", d.pattern); context.cmd.SetGlobalVector("_HoAtmosphereDustTexture", d.textureParams);
                    context.cmd.SetGlobalVector("_HoVolumeLightParams", new Vector4(0, d.shape.w, 0, d.lighting.z));
                    context.cmd.SetGlobalBuffer("_HoAtmosphereDustSeeds", d.seeds);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 6, d.count);
                });
            }
        }
    }
}
