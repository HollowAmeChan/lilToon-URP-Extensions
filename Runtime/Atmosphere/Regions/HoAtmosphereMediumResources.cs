#pragma warning disable CS0618, CS0672
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Atmosphere
{
    public sealed class HoAtmosphereMediumResources : ContextItem
    {
        public HoAtmosphereMediumSnapshot snapshot;
        public TextureHandle densityTexture = TextureHandle.nullHandle;
        public bool published;
        public bool IsValid => published && snapshot.valid && densityTexture.IsValid();
        public override void Reset() { snapshot = default; densityTexture = TextureHandle.nullHandle; published = false; }
    }

    public static class HoAtmosphereMediumBindings
    {
        public static readonly int WorldToRegionId = Shader.PropertyToID("_HoAtmosphereWorldToRegion");
        public static readonly int RegionToWorldId = Shader.PropertyToID("_HoAtmosphereRegionToWorld");
        public static readonly int HalfSizeId = Shader.PropertyToID("_HoAtmosphereMediumHalfSize");
        public static readonly int DensityId = Shader.PropertyToID("_HoAtmosphereMediumDensity");
        public static readonly int TintId = Shader.PropertyToID("_HoAtmosphereMediumTint");
        public static readonly int NoiseId = Shader.PropertyToID("_HoAtmosphereMediumNoise");
        public static readonly int WindId = Shader.PropertyToID("_HoAtmosphereMediumWind");
        public static readonly int TextureId = Shader.PropertyToID("_HoAtmosphereMediumTexture");
        public static readonly int TimeId = Shader.PropertyToID("_HoAtmosphereMediumTime");
        internal static void ResetGlobals() => Shader.SetGlobalVector(DensityId, Vector4.zero);
        public static void Bind(RasterCommandBuffer cmd, HoAtmosphereMediumResources input, float time)
        {
            var s = input.snapshot;
            cmd.SetGlobalMatrix(WorldToRegionId, s.worldToRegion);
            cmd.SetGlobalMatrix(RegionToWorldId, s.valid ? s.worldToRegion.inverse : Matrix4x4.identity);
            cmd.SetGlobalVector(HalfSizeId, s.halfSizeAndBlend);
            cmd.SetGlobalVector(DensityId, input.IsValid ? s.densityAndWeight : Vector4.zero);
            cmd.SetGlobalVector(TintId, s.tint); cmd.SetGlobalVector(NoiseId, s.noise); cmd.SetGlobalVector(WindId, s.wind);
            cmd.SetGlobalFloat(TimeId, time);
            if (input.densityTexture.IsValid()) cmd.SetGlobalTexture(TextureId, input.densityTexture);
        }
    }

    internal sealed class HoAtmosphereMediumPass : ScriptableRenderPass
    {
        private HoAtmosphereMediumSnapshot snapshot;
        private Texture3D wrappedTexture, fallback;
        private RTHandle texture;
        internal HoAtmosphereMediumPass() { renderPassEvent = RenderPassEvent.AfterRenderingPrePasses; }
        internal void Setup(HoAtmosphereMediumSnapshot value) => snapshot = value;
        private sealed class Data { internal HoAtmosphereMediumResources input; internal float time; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            EnsureTexture();
            var input = frameData.GetOrCreate<HoAtmosphereMediumResources>();
            input.snapshot = snapshot; input.densityTexture = graph.ImportTexture(texture); input.published = true;
            using (var builder = graph.AddRasterRenderPass<Data>("HoAtmosphere Medium Publish", out var data))
            {
                data.input = input; data.time = snapshot.sampleTime;
                builder.UseTexture(input.densityTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(input.densityTexture, HoAtmosphereMediumBindings.TextureId);
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) => HoAtmosphereMediumBindings.Bind(context.cmd, d.input, d.time));
            }
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            EnsureTexture();
            var cmd = CommandBufferPool.Get("HoAtmosphere Medium Publish");
            cmd.SetGlobalMatrix(HoAtmosphereMediumBindings.WorldToRegionId, snapshot.worldToRegion);
            cmd.SetGlobalMatrix(HoAtmosphereMediumBindings.RegionToWorldId, snapshot.valid ? snapshot.worldToRegion.inverse : Matrix4x4.identity);
            cmd.SetGlobalVector(HoAtmosphereMediumBindings.HalfSizeId, snapshot.halfSizeAndBlend);
            cmd.SetGlobalVector(HoAtmosphereMediumBindings.DensityId, snapshot.valid ? snapshot.densityAndWeight : Vector4.zero);
            cmd.SetGlobalVector(HoAtmosphereMediumBindings.TintId, snapshot.tint);
            cmd.SetGlobalVector(HoAtmosphereMediumBindings.NoiseId, snapshot.noise);
            cmd.SetGlobalVector(HoAtmosphereMediumBindings.WindId, snapshot.wind);
            cmd.SetGlobalFloat(HoAtmosphereMediumBindings.TimeId, snapshot.sampleTime);
            cmd.SetGlobalTexture(HoAtmosphereMediumBindings.TextureId, texture.nameID);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
        private void EnsureTexture()
        {
            Texture3D source = snapshot.densityTexture;
            if (source == null)
            {
                if (fallback == null)
                {
                    fallback = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { name = "HoAtmosphere Uniform Density", hideFlags = HideFlags.HideAndDontSave };
                    fallback.SetPixel(0, 0, 0, Color.white); fallback.Apply();
                }
                source = fallback;
            }
            if (wrappedTexture == source && texture != null) return;
            texture?.Release(); texture = RTHandles.Alloc(source); wrappedTexture = source;
        }
        internal void Dispose()
        {
            texture?.Release(); texture = null; wrappedTexture = null;
            CoreUtils.Destroy(fallback); fallback = null;
            HoAtmosphereMediumBindings.ResetGlobals();
        }
    }
}
