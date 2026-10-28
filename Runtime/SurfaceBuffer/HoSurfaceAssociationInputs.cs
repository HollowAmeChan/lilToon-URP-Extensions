#pragma warning disable CS0618
using lilToon.URP.Extensions.GeometryBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    // A borrowed input bundle, not another texture producer. This checks depth agreement,
    // not primitive identity; coplanar overlap and unsupported GB geometry need separate policy.
    internal readonly struct HoSurfaceAssociationInputs
    {
        private static readonly int StateId = Shader.PropertyToID("_HoSurfaceAssociationState");
        private static readonly int SurfaceDepthId = Shader.PropertyToID("_HoSurfaceAssociationSurfaceDepth");
        private static readonly int WriterId = Shader.PropertyToID("_HoSurfaceAssociationWriter");
        private static readonly int GeometryDepthId = Shader.PropertyToID("_HoSurfaceAssociationGeometryDepth");
        private static readonly int GeometryId = Shader.PropertyToID("_HoSurfaceAssociationGeometry");
        private readonly TextureHandle surfaceDepth, writer, geometryDepth, geometry, fallback;
        private readonly Vector4 state;

        private HoSurfaceAssociationInputs(TextureHandle surfaceDepth, TextureHandle writer,
            TextureHandle geometryDepth, TextureHandle geometry, TextureHandle fallback, Vector4 state)
        {
            this.surfaceDepth = surfaceDepth; this.writer = writer;
            this.geometryDepth = geometryDepth; this.geometry = geometry; this.fallback = fallback; this.state = state;
        }

        internal static HoSurfaceAssociationInputs Read(RenderGraph graph, ContextContainer frame, IBaseRenderGraphBuilder builder)
        {
            var sb = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            var gb = frame.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            bool surfacePresent = sb.depthTexture.IsValid() && sb.emissionTexture.IsValid();
            bool geometryPresent = gb.depthTexture.IsValid() && gb.normalDepthTexture.IsValid();
            bool sameDomain = false;
            if (surfacePresent && geometryPresent)
            {
                var s = graph.GetTextureDesc(sb.depthTexture);
                var g = graph.GetTextureDesc(gb.depthTexture);
                sameDomain = gb.rasterSamples == 1 && s.width == g.width && s.height == g.height
                    && s.dimension == g.dimension && s.slices == g.slices;
            }
            if (surfacePresent) { builder.UseTexture(sb.depthTexture); builder.UseTexture(sb.emissionTexture); }
            if (geometryPresent) { builder.UseTexture(gb.depthTexture); builder.UseTexture(gb.normalDepthTexture); }
            if (!surfacePresent || !geometryPresent) builder.UseTexture(graph.defaultResources.blackTexture);
            return new HoSurfaceAssociationInputs(sb.depthTexture, sb.emissionTexture, gb.depthTexture, gb.normalDepthTexture, graph.defaultResources.blackTexture,
                new Vector4(surfacePresent ? 1 : 0, geometryPresent ? 1 : 0, sameDomain ? 1 : 0, 0));
        }

        internal void Bind(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalVector(StateId, state);
            cmd.SetGlobalTexture(SurfaceDepthId, state.x > .5f ? surfaceDepth : fallback);
            cmd.SetGlobalTexture(WriterId, state.x > .5f ? writer : fallback);
            cmd.SetGlobalTexture(GeometryDepthId, state.y > .5f ? geometryDepth : fallback);
            cmd.SetGlobalTexture(GeometryId, state.y > .5f ? geometry : fallback);
        }

        internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeVectorParam(shader, StateId, state);
            // Compute kernels require all declared texture slots, even when an early-out makes them unused.
            cmd.SetComputeTextureParam(shader, kernel, SurfaceDepthId, state.x > .5f ? surfaceDepth : fallback);
            cmd.SetComputeTextureParam(shader, kernel, WriterId, state.x > .5f ? writer : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GeometryDepthId, state.y > .5f ? geometryDepth : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GeometryId, state.y > .5f ? geometry : fallback);
        }

        internal static void BindCompatibility(CommandBuffer cmd)
        {
            Vector4 state = CompatibilityState(out var s, out var w, out var gb);
            cmd.SetGlobalVector(StateId, state);
            var fallback = new RenderTargetIdentifier(Texture2D.blackTexture);
            cmd.SetGlobalTexture(SurfaceDepthId, state.x > .5f ? s.nameID : fallback);
            cmd.SetGlobalTexture(WriterId, state.x > .5f ? w.nameID : fallback);
            cmd.SetGlobalTexture(GeometryDepthId, state.y > .5f ? gb.DepthTexture.nameID : fallback);
            cmd.SetGlobalTexture(GeometryId, state.y > .5f ? gb.NormalDepthTexture.nameID : fallback);
        }

        internal static void BindCompatibilityCompute(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            Vector4 state = CompatibilityState(out var s, out var w, out var gb);
            cmd.SetComputeVectorParam(shader, StateId, state);
            var fallback = new RenderTargetIdentifier(Texture2D.blackTexture);
            cmd.SetComputeTextureParam(shader, kernel, SurfaceDepthId, state.x > .5f ? s.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, WriterId, state.x > .5f ? w.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GeometryDepthId, state.y > .5f ? gb.DepthTexture.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GeometryId, state.y > .5f ? gb.NormalDepthTexture.nameID : fallback);
        }

        private static Vector4 CompatibilityState(out RTHandle s, out RTHandle w, out HoGeometryBufferRenderTargets gb)
        {
            s = HoSurfaceBufferPass.CompatibilityDepthTexture;
            w = HoSurfaceBufferPass.CompatibilityWriterTexture;
            gb = HoGeometryBufferPass.CompatibilityTargets;
            bool surfacePresent = HoSurfaceBufferPass.CompatibilityProduced && s?.rt != null && w?.rt != null;
            bool geometryPresent = gb?.DepthTexture?.rt != null && gb.NormalDepthTexture?.rt != null;
            bool sameDomain = surfacePresent && geometryPresent && gb.MsaaSamples == 1
                && s.rt.width == gb.DepthTexture.rt.width && s.rt.height == gb.DepthTexture.rt.height
                && s.rt.dimension == gb.DepthTexture.rt.dimension && s.rt.volumeDepth == gb.DepthTexture.rt.volumeDepth;
            return new Vector4(surfacePresent ? 1 : 0, geometryPresent ? 1 : 0, sameDomain ? 1 : 0, 0);
        }
    }
}
