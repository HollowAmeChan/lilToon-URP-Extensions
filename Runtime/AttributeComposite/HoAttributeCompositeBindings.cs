using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using lilToon.URP.Extensions.ObjectBuffer;

namespace lilToon.URP.Extensions.AttributeComposite
{
    internal static class HoAttributeCompositeBindings
    {
        public static void ReadQuery(IRasterRenderGraphBuilder builder, HoAttributeCompositeRenderGraphResources r,
            HoACQueryDescriptor query, bool intrinsicCoverage = false)
        {
            bool physical = query.Domain == HoACMaskDomain.Geometry && r.outlineInheritance;
            if (query.NeedsIdentity || intrinsicCoverage || query.NeedsSelection)
            {
                Read(builder, physical ? r.rawIdentityCoverageTexture : r.identityCoverageTexture);
                if (query.Kind == HoACQueryKind.Group || query.Kind == HoACQueryKind.Identity)
                {
                    Read(builder, physical ? r.rawIdentityId0Texture : r.identityId0Texture);
                    Read(builder, physical ? r.rawIdentityId1Texture : r.identityId1Texture);
                }
            }
            if (query.NeedsSelection)
                foreach (TextureHandle texture in physical ? r.rawSelectionTextures : r.selectionTextures) Read(builder, texture);
            if (query.NeedsGeometry)
                Read(builder, r.geometryCoverageTexture.IsValid() ? r.geometryCoverageTexture : r.geometryNormalDepthTexture);
            if (query.NeedsOutline)
                Read(builder, r.outlineCoverageTexture.IsValid() ? r.outlineCoverageTexture : r.outlineNormalDepthTexture);
        }

        public static void Read(IRasterRenderGraphBuilder builder, TextureHandle texture)
        {
            if (texture.IsValid()) builder.UseTexture(texture, AccessFlags.Read);
        }

        // Only bind textures actually declared by this query. Flags and lane count are per-camera snapshots.
        public static void BindQuery(RasterCommandBuffer cmd, HoAttributeCompositeRenderGraphResources r,
            HoACQueryDescriptor query, bool intrinsicCoverage = false)
        {
            bool physical = query.Domain == HoACMaskDomain.Geometry && r.outlineInheritance;
            cmd.SetGlobalVector(HoAttributeCompositeShaderConstants.InputFlagsId, r.InputFlags);
            cmd.SetGlobalVector(HoAttributeCompositeShaderConstants.GeometryFlagsId, r.GeometryFlags);
            cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.InputsPublishedId, r.published ? 1 : 0);
            cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.LaneCountId, r.laneCount);
            if (query.NeedsIdentity || intrinsicCoverage || query.NeedsSelection)
            {
                Bind(cmd, HoObjectBufferShaderConstants.CoverageTextureId, physical ? r.rawIdentityCoverageTexture : r.identityCoverageTexture);
                if (query.Kind == HoACQueryKind.Group || query.Kind == HoACQueryKind.Identity)
                {
                    Bind(cmd, HoObjectBufferShaderConstants.Id0TextureId, physical ? r.rawIdentityId0Texture : r.identityId0Texture);
                    Bind(cmd, HoObjectBufferShaderConstants.Id1TextureId, physical ? r.rawIdentityId1Texture : r.identityId1Texture);
                }
            }
            if (query.NeedsSelection)
                for (int i = 0; i < r.selectionTextures.Length; i++)
                    Bind(cmd, HoAttributeCompositeShaderConstants.SelectionTextureIds[i], physical ? r.rawSelectionTextures[i] : r.selectionTextures[i]);
            if (query.NeedsGeometry)
                Bind(cmd, r.geometryCoverageTexture.IsValid() ? HoAttributeCompositeShaderConstants.GeometryCoverageId :
                    HoAttributeCompositeShaderConstants.GeometryNormalDepthId,
                    r.geometryCoverageTexture.IsValid() ? r.geometryCoverageTexture : r.geometryNormalDepthTexture);
            if (query.NeedsOutline)
                Bind(cmd, r.outlineCoverageTexture.IsValid() ? HoAttributeCompositeShaderConstants.OutlineCoverageId :
                    HoAttributeCompositeShaderConstants.OutlineNormalDepthId,
                    r.outlineCoverageTexture.IsValid() ? r.outlineCoverageTexture : r.outlineNormalDepthTexture);
        }

        private static void Bind(RasterCommandBuffer cmd, int id, TextureHandle texture)
        {
            if (texture.IsValid()) cmd.SetGlobalTexture(id, texture);
        }
    }
}
