using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    internal sealed class HoVolumeLightDustPass : ScriptableRenderPass
    {
        private Material material;
        private float intensity, anisotropy;
        internal bool HasMaterial => material != null;
        internal int ParticleCount { get; private set; }
        internal bool CapacityLimited { get; private set; }
        internal HoVolumeLightDustPass() { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; ConfigureInput(ScriptableRenderPassInput.Depth); }
        internal void Setup(float intensity, float anisotropy) { this.intensity = intensity; this.anisotropy = anisotropy; }
        internal void Dispose() { CoreUtils.Destroy(material); material = null; ParticleCount = 0; CapacityLimited = false; }
        private sealed class Data
        {
            internal Material material;
            internal HoAtmosphereMediumResources medium;
            internal BufferHandle seeds;
            internal TextureHandle depth, shadow;
            internal Vector4 shape, lighting, shadows;
            internal int count;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            ParticleCount = 0; CapacityLimited = false;
            var medium = frame.GetOrCreate<HoAtmosphereMediumResources>();
            var resources = frame.Get<UniversalResourceData>();
            var s = medium.snapshot;
            if (!medium.IsValid || s.owner == null || !s.dust.enabled || s.dust.numberDensity <= 0 || s.densityAndWeight.x <= 0 || s.densityAndWeight.y <= 0 || intensity <= 0)
            {
                if (s.owner != null && !s.dust.enabled) s.owner.ReleaseDust();
                Dispose(); return;
            }
            if (!resources.cameraDepthTexture.IsValid()) { Dispose(); return; }
            var field = s.owner.GetDustField(); field.Prepare(s);
            if (!field.HasBuffer || field.Count == 0) { Dispose(); return; }
            if (material == null)
            {
                var shader = Resources.Load<Shader>("HoVolumeLightDust");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            ParticleCount = field.Count; CapacityLimited = field.CapacityLimited;
            var shadows = frame.Get<UniversalShadowData>();
            using (var builder = graph.AddRasterRenderPass<Data>("Ho-VolumeLight Shared Dust", out var data))
            {
                data.material = material; data.medium = medium; data.count = field.Count;
                data.seeds = graph.ImportBuffer(field.buffer); data.depth = resources.cameraDepthTexture; data.shadow = resources.mainShadowsTexture;
                data.shape = new Vector4(s.dust.size.x, s.dust.size.y, s.dust.flutter, s.dust.maxDistance);
                data.lighting = new Vector4(s.dust.brightness, s.sampleTime, anisotropy, intensity);
                data.shadows = new Vector4(shadows.mainLightShadowCascadesCount > 1 ? 1 : 0, shadows.supportsMainLightShadows && data.shadow.IsValid() ? 1 : 0, 0, 0);
                builder.UseBuffer(data.seeds, AccessFlags.Read); builder.UseTexture(data.depth, AccessFlags.Read); builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                if (data.shadow.IsValid()) builder.UseTexture(data.shadow, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite); builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                {
                    HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.medium.snapshot.sampleTime);
                    context.cmd.SetGlobalTexture("_CameraDepthTexture", d.depth);
                    if (d.shadow.IsValid()) context.cmd.SetGlobalTexture("_MainLightShadowmapTexture", d.shadow);
                    context.cmd.SetGlobalVector("_HoVolumeLightShadow", d.shadows); context.cmd.SetGlobalInt("_HoVolumeLightDebug", 0);
                    context.cmd.SetGlobalVector("_HoAtmosphereDustShape", d.shape); context.cmd.SetGlobalVector("_HoAtmosphereDustLighting", d.lighting);
                    context.cmd.SetGlobalVector("_HoVolumeLightParams", new Vector4(0, d.shape.w, 0, d.lighting.z));
                    context.cmd.SetGlobalBuffer("_HoAtmosphereDustSeeds", d.seeds);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 6, d.count);
                });
            }
        }
    }
}
