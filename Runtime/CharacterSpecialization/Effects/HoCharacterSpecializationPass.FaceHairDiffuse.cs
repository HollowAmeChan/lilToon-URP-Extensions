using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        private static bool RequiresFaceHairDiffuseTextures(HoCharacterSpecializationSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (settings.faceHairDiffuseEnabled)
            {
                return true;
            }

            switch (settings.debugMode)
            {
                case HoCharacterSpecializationDebugMode.FaceHairDiffuseSourceMask:
                case HoCharacterSpecializationDebugMode.FaceHairDiffuseBlurMask:
                case HoCharacterSpecializationDebugMode.FaceHairDiffuseBlurColor:
                case HoCharacterSpecializationDebugMode.FaceHairDiffuseMask:
                    return true;
                default:
                    return false;
            }
        }

        private static void FillFaceHairDiffuseMaterialVectors(
            HoCharacterSpecializationSettings settings,
            bool texturesReady,
            out Vector4 faceHairDiffuseParams,
            out Vector4 faceHairDiffuseLevels,
            out Color faceHairDiffuseTintColor,
            out Vector4 faceHairDiffuseOptions)
        {
            float levelBlack = Mathf.Clamp01(settings.faceHairDiffuseLevelBlack);
            float levelWhite = Mathf.Clamp01(settings.faceHairDiffuseLevelWhite);
            if (levelWhite < levelBlack + 0.0001f)
            {
                levelWhite = levelBlack + 0.0001f;
            }

            faceHairDiffuseParams = new Vector4(
                Mathf.Clamp01(settings.faceHairDiffuseStrength),
                Mathf.Max(0.0f, settings.faceHairDiffuseRadiusPixels),
                Mathf.Max(0.0f, settings.faceHairDiffuseDepthTolerance),
                (float)settings.faceHairDiffuseBlendMode);
            faceHairDiffuseLevels = new Vector4(
                levelBlack,
                levelWhite,
                1.0f / Mathf.Max(0.0001f, levelWhite - levelBlack),
                0.0f);
            faceHairDiffuseTintColor = settings.faceHairDiffuseTintColor;
            faceHairDiffuseOptions = new Vector4(
                settings.faceHairDiffuseEnabled && texturesReady ? 1.0f : 0.0f,
                texturesReady ? 1.0f : 0.0f,
                0.0f,
                0.0f);
        }

        private static Vector4 CreateFaceHairDiffuseBlurParams(
            HoCharacterSpecializationSettings settings,
            RenderTextureDescriptor cameraTextureDescriptor,
            TextureDesc blurTextureDesc,
            float radiusScale,
            int iterationIndex)
        {
            float scale = blurTextureDesc.width > 0
                ? blurTextureDesc.width / (float)Mathf.Max(1, cameraTextureDescriptor.width)
                : 1.0f;
            return new Vector4(
                Mathf.Max(0.0f, settings.faceHairDiffuseRadiusPixels) * Mathf.Max(scale, 0.0001f) * Mathf.Max(radiusScale, 0.0001f),
                iterationIndex * 1.61803399f,
                FaceHairDiffuseBlurIterationCount,
                0.0f);
        }

        private static void AddFaceHairDiffuseBlurPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle sourceColor,
            TextureHandle sourceDepth,
            TextureHandle destinationColor,
            TextureHandle destinationDepth,
            Vector4 blurParams)
        {
            using (var builder = renderGraph.AddRasterRenderPass<FaceHairDiffuseBlurPassData>(passName, out FaceHairDiffuseBlurPassData passData, ProfilingSampler))
            {
                passData.sourceColor = sourceColor;
                passData.sourceDepth = sourceDepth;
                passData.destinationColor = destinationColor;
                passData.destinationDepth = destinationDepth;
                passData.material = material;
                passData.blurParams = blurParams;

                builder.UseTexture(passData.sourceColor, AccessFlags.Read);
                builder.UseTexture(passData.sourceDepth, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destinationColor, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(passData.destinationDepth, 1, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (FaceHairDiffuseBlurPassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoCharacterSpecializationShaderConstants.FaceHairDiffuseBlurParamsId, data.blurParams);
                    context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.FaceHairDiffuseDepthTextureId, data.sourceDepth);
                    Blitter.BlitTexture(context.cmd, data.sourceColor, new Vector4(1, 1, 0, 0), data.material, 1);
                });
            }
        }

        private static TextureDesc CreateFaceHairDiffuseTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoCharacterSpecializationSettings settings,
            GraphicsFormat format,
            string name)
        {
            TextureDesc descriptor = CreateTextureDesc(cameraTextureDescriptor, settings, format, name);
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            return descriptor;
        }
    }
}
