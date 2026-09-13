using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.MetadataBuffer;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        // The semantic mask anti-aliasing is a shared service of this feature: the MetadataBuffer
        // semantic bits are single-sample 0/1 fields, so every effect that uses one as a silhouette
        // gets a hard edge. Blurring the two objectCustom textures whole (they are RGBA(bits), one
        // semantic per channel, and a box filter works per channel) produces one anti-aliased copy
        // that the checked effects read, with a single user-facing width. There is no master switch:
        // the copy is produced exactly when at least one effect asked for it.
        private static bool RequiresSemanticMaskBlurTextures(HoCharacterSpecializationSettings settings)
        {
            return settings != null
                && (settings.semanticMaskBlurHairShadow
                    || settings.semanticMaskBlurFaceHairDiffuse
                    || settings.semanticMaskBlurEyeReveal
                    || settings.semanticMaskBlurSubjectOutline
                    || settings.semanticMaskBlurEnhancedOutline);
        }

        private static Vector4 CreateSemanticMaskBlurParams(HoCharacterSpecializationSettings settings)
        {
            // The shader floors the width at 1px: below one texel a single-sample 0/1 field cannot
            // gain any anti-aliasing, so the user value only ever widens the ramp from there.
            float radiusPixels = settings != null ? Mathf.Max(0.0f, settings.semanticMaskBlurRadiusPixels) : 1.0f;
            return new Vector4(
                radiusPixels,
                HoCharacterSpecializationShaderConstants.SemanticMaskBlurMaxTapsPerAxis,
                0.0f,
                0.0f);
        }

        // x = the anti-aliased copy exists, y/z/w = which effect reads it. Each effect's switch is a
        // user-facing option, so the decision lives in one vector instead of hidden per-effect code.
        private static Vector4 CreateSemanticMaskOptions(HoCharacterSpecializationSettings settings, bool ready)
        {
            if (settings == null || !ready)
            {
                return Vector4.zero;
            }

            return new Vector4(
                1.0f,
                settings.semanticMaskBlurHairShadow ? 1.0f : 0.0f,
                settings.semanticMaskBlurFaceHairDiffuse ? 1.0f : 0.0f,
                settings.semanticMaskBlurEyeReveal ? 1.0f : 0.0f);
        }

        private static void AddSemanticMaskBlurPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle metadataObjectCustom0Texture,
            TextureHandle metadataObjectCustom1Texture,
            TextureHandle destinationLowTexture,
            TextureHandle destinationHighTexture,
            Vector4 blurParams)
        {
            using (var builder = renderGraph.AddRasterRenderPass<SemanticMaskBlurPassData>(passName, out SemanticMaskBlurPassData passData, ProfilingSampler))
            {
                passData.metadataObjectCustom0Texture = metadataObjectCustom0Texture;
                passData.metadataObjectCustom1Texture = metadataObjectCustom1Texture;
                passData.destinationLowTexture = destinationLowTexture;
                passData.destinationHighTexture = destinationHighTexture;
                passData.material = material;
                passData.blurParams = blurParams;

                builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                builder.SetRenderAttachment(destinationLowTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(destinationHighTexture, 1, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SemanticMaskBlurPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.metadataObjectCustom0Texture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.metadataObjectCustom1Texture);
                    context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SemanticMaskBlurParamsId, data.blurParams);
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                    Blitter.BlitTexture(context.cmd, data.metadataObjectCustom0Texture, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }
}
