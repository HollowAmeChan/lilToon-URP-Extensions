using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        private const float SubjectOutlineMinHardThreshold = 0.003921569f;
        private const float SubjectOutlineMinLevelRange = 0.0001f;

        private static bool RequiresSubjectOutlineTextures(HoCharacterSpecializationSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (settings.subjectOutlineEnabled)
            {
                return true;
            }

            switch (settings.debugMode)
            {
                case HoCharacterSpecializationDebugMode.SubjectOutlineSourceMask:
                case HoCharacterSpecializationDebugMode.SubjectOutlineBlurMask:
                case HoCharacterSpecializationDebugMode.SubjectOutlineMask:
                case HoCharacterSpecializationDebugMode.SubjectOutlineNormal:
                    return true;
                default:
                    return false;
            }
        }

        private static void FillSubjectOutlineMaterialVectors(
            HoCharacterSpecializationSettings settings,
            bool texturesReady,
            out Vector4 subjectOutlineParams,
            out Vector4 subjectOutlineLevels,
            out Color subjectOutlineColor,
            out Color subjectOutlineFogColor,
            out Vector4 subjectOutlineFogParams,
            out Vector4 subjectOutlineHeightFadeParams,
            out Vector4 subjectOutlineOptions)
        {
            float levelBlack = Mathf.Clamp01(settings.subjectOutlineLevelBlack);
            float levelWhite = Mathf.Clamp01(settings.subjectOutlineLevelWhite);
            bool hardLevelThreshold = levelWhite <= levelBlack + SubjectOutlineMinLevelRange;
            if (hardLevelThreshold)
            {
                levelBlack = Mathf.Max(levelBlack, SubjectOutlineMinHardThreshold);
                levelWhite = levelBlack;
            }

            subjectOutlineParams = new Vector4(
                Mathf.Clamp01(settings.subjectOutlineStrength),
                Mathf.Max(0.0f, settings.subjectOutlineRadiusPixels),
                settings.subjectOutlineNormalRotationDegrees * Mathf.Deg2Rad,
                settings.subjectOutlineNormalFlowDegreesPerSecond * 50.0f * Mathf.Deg2Rad);
            subjectOutlineLevels = new Vector4(
                levelBlack,
                levelWhite,
                hardLevelThreshold ? 0.0f : 1.0f / Mathf.Max(SubjectOutlineMinLevelRange, levelWhite - levelBlack),
                hardLevelThreshold ? 1.0f : 0.0f);
            subjectOutlineColor = settings.subjectOutlineColor;
            subjectOutlineFogColor = settings.subjectOutlineFogColor;
            subjectOutlineFogParams = new Vector4(
                settings.subjectOutlineFogHueShiftDegrees / 360.0f,
                Mathf.Max(0.0f, settings.subjectOutlineFogSaturation),
                Mathf.Max(0.0f, settings.subjectOutlineFogValue),
                Mathf.Clamp(settings.subjectOutlineFogSoftness, 0.05f, 4.0f));
            float heightFadeStart = Mathf.Max(0.0f, settings.subjectOutlineHeightFadeStart);
            float heightFadeEnd = Mathf.Max(heightFadeStart + 0.0001f, settings.subjectOutlineHeightFadeEnd);
            subjectOutlineHeightFadeParams = new Vector4(
                (float)settings.subjectOutlineHeightFadeMode,
                settings.subjectOutlineHeightFadeGroundY,
                heightFadeStart,
                1.0f / (heightFadeEnd - heightFadeStart));
            subjectOutlineOptions = new Vector4(
                settings.subjectOutlineEnabled && texturesReady ? 1.0f : 0.0f,
                texturesReady ? 1.0f : 0.0f,
                (float)settings.subjectOutlineFillMode,
                Mathf.Clamp(settings.subjectOutlineHeightFadeHardness, 0.1f, 8.0f));
        }

        private static Vector4 CreateSubjectOutlineBlurParams(
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
                Mathf.Max(0.0f, settings.subjectOutlineRadiusPixels) * Mathf.Max(scale, 0.0001f) * Mathf.Max(radiusScale, 0.0001f),
                iterationIndex * 1.61803399f,
                SubjectOutlineBlurIterationCount,
                0.0f);
        }

        private static void AddSubjectOutlineBlurPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle source,
            TextureHandle destination,
            Vector4 blurParams)
        {
            using (var builder = renderGraph.AddRasterRenderPass<SubjectOutlineBlurPassData>(passName, out SubjectOutlineBlurPassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.destination = destination;
                passData.material = material;
                passData.blurParams = blurParams;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SubjectOutlineBlurPassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoCharacterSpecializationShaderConstants.SubjectOutlineBlurParamsId, data.blurParams);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 1);
                });
            }
        }

        private static TextureDesc CreateSubjectOutlineTextureDesc(
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
