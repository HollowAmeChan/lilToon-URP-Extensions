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
        private readonly TextureHandle surfaceDepth, writer, geometryDepth, geometry;
        private readonly Vector4 state;

        private HoSurfaceAssociationInputs(TextureHandle surfaceDepth, TextureHandle writer,
            TextureHandle geometryDepth, TextureHandle geometry, Vector4 state)
        {
            this.surfaceDepth = surfaceDepth; this.writer = writer;
            this.geometryDepth = geometryDepth; this.geometry = geometry; this.state = state;
        }

        internal static HoSurfaceAssociationInputs Read(RenderGraph graph, ContextContainer frame, IRasterRenderGraphBuilder builder)
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
            return new HoSurfaceAssociationInputs(sb.depthTexture, sb.emissionTexture, gb.depthTexture, gb.normalDepthTexture,
                new Vector4(surfacePresent ? 1 : 0, geometryPresent ? 1 : 0, sameDomain ? 1 : 0, 0));
        }

        internal void Bind(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalVector(StateId, state);
            if (state.x > .5f) { cmd.SetGlobalTexture(SurfaceDepthId, surfaceDepth); cmd.SetGlobalTexture(WriterId, writer); }
            if (state.y > .5f) { cmd.SetGlobalTexture(GeometryDepthId, geometryDepth); cmd.SetGlobalTexture(GeometryId, geometry); }
        }

        internal static void BindCompatibility(CommandBuffer cmd)
        {
            var s = HoSurfaceBufferPass.CompatibilityDepthTexture;
            var w = HoSurfaceBufferPass.CompatibilityWriterTexture;
            var gb = HoGeometryBufferPass.CompatibilityTargets;
            bool surfacePresent = HoSurfaceBufferPass.CompatibilityProduced && s?.rt != null && w?.rt != null;
            bool geometryPresent = gb?.DepthTexture?.rt != null && gb.NormalDepthTexture?.rt != null;
            bool sameDomain = surfacePresent && geometryPresent && gb.MsaaSamples == 1
                && s.rt.width == gb.DepthTexture.rt.width && s.rt.height == gb.DepthTexture.rt.height
                && s.rt.dimension == gb.DepthTexture.rt.dimension && s.rt.volumeDepth == gb.DepthTexture.rt.volumeDepth;
            cmd.SetGlobalVector(StateId, new Vector4(surfacePresent ? 1 : 0, geometryPresent ? 1 : 0, sameDomain ? 1 : 0, 0));
            if (surfacePresent) { cmd.SetGlobalTexture(SurfaceDepthId, s.nameID); cmd.SetGlobalTexture(WriterId, w.nameID); }
            if (geometryPresent) { cmd.SetGlobalTexture(GeometryDepthId, gb.DepthTexture.nameID); cmd.SetGlobalTexture(GeometryId, gb.NormalDepthTexture.nameID); }
        }
    }
}
