using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed partial class HoCharacterSpecializationPass
    {
        private static void ApplyMaterialProperties(Material material, HoCharacterSpecializationSettings settings)
        {
            FillMaterialVectors(
                settings,
                false,
                false,
                out Vector4 eyeRevealParams,
                out Vector4 hairShadowParams,
                out Vector4 hairShadowParams1,
                out Vector4 hairShadowParams2,
                out Color hairShadowColor,
                out Vector4 faceHairDiffuseParams,
                out Vector4 faceHairDiffuseLevels,
                out Color faceHairDiffuseTintColor,
                out Vector4 faceHairDiffuseOptions,
                out Vector4 subjectOutlineParams,
                out Vector4 subjectOutlineLevels,
                out Color subjectOutlineColor,
                out Vector4 subjectOutlineOptions,
                out Vector4 options);
            ApplyMaterialProperties(
                material,
                eyeRevealParams,
                hairShadowParams,
                hairShadowParams1,
                hairShadowParams2,
                hairShadowColor,
                faceHairDiffuseParams,
                faceHairDiffuseLevels,
                faceHairDiffuseTintColor,
                faceHairDiffuseOptions,
                subjectOutlineParams,
                subjectOutlineLevels,
                subjectOutlineColor,
                subjectOutlineOptions,
                options);
        }

        private static void ApplyMaterialProperties(
            Material material,
            Vector4 eyeRevealParams,
            Vector4 hairShadowParams,
            Vector4 hairShadowParams1,
            Vector4 hairShadowParams2,
            Color hairShadowColor,
            Vector4 faceHairDiffuseParams,
            Vector4 faceHairDiffuseLevels,
            Color faceHairDiffuseTintColor,
            Vector4 faceHairDiffuseOptions,
            Vector4 subjectOutlineParams,
            Vector4 subjectOutlineLevels,
            Color subjectOutlineColor,
            Vector4 subjectOutlineOptions,
            Vector4 options)
        {
            material.SetVector(HoCharacterSpecializationShaderConstants.EyeRevealParamsId, eyeRevealParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParamsId, hairShadowParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParams1Id, hairShadowParams1);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParams2Id, hairShadowParams2);
            material.SetColor(HoCharacterSpecializationShaderConstants.HairShadowColorId, hairShadowColor);
            material.SetVector(HoCharacterSpecializationShaderConstants.FaceHairDiffuseParamsId, faceHairDiffuseParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.FaceHairDiffuseLevelsId, faceHairDiffuseLevels);
            material.SetColor(HoCharacterSpecializationShaderConstants.FaceHairDiffuseTintColorId, faceHairDiffuseTintColor);
            material.SetVector(HoCharacterSpecializationShaderConstants.FaceHairDiffuseOptionsId, faceHairDiffuseOptions);
            material.SetVector(HoCharacterSpecializationShaderConstants.SubjectOutlineParamsId, subjectOutlineParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.SubjectOutlineLevelsId, subjectOutlineLevels);
            material.SetColor(HoCharacterSpecializationShaderConstants.SubjectOutlineColorId, subjectOutlineColor);
            material.SetVector(HoCharacterSpecializationShaderConstants.SubjectOutlineOptionsId, subjectOutlineOptions);
            material.SetVector(HoCharacterSpecializationShaderConstants.OptionsId, options);
        }

        private static void FillMaterialVectors(
            HoCharacterSpecializationSettings settings,
            bool faceHairDiffuseTexturesReady,
            bool subjectOutlineTexturesReady,
            out Vector4 eyeRevealParams,
            out Vector4 hairShadowParams,
            out Vector4 hairShadowParams1,
            out Vector4 hairShadowParams2,
            out Color hairShadowColor,
            out Vector4 faceHairDiffuseParams,
            out Vector4 faceHairDiffuseLevels,
            out Color faceHairDiffuseTintColor,
            out Vector4 faceHairDiffuseOptions,
            out Vector4 subjectOutlineParams,
            out Vector4 subjectOutlineLevels,
            out Color subjectOutlineColor,
            out Vector4 subjectOutlineOptions,
            out Vector4 options)
        {
            if (settings == null)
            {
                eyeRevealParams = Vector4.zero;
                hairShadowParams = Vector4.zero;
                hairShadowParams1 = Vector4.zero;
                hairShadowParams2 = Vector4.zero;
                hairShadowColor = Color.white;
                faceHairDiffuseParams = Vector4.zero;
                faceHairDiffuseLevels = Vector4.zero;
                faceHairDiffuseTintColor = Color.white;
                faceHairDiffuseOptions = Vector4.zero;
                subjectOutlineParams = Vector4.zero;
                subjectOutlineLevels = Vector4.zero;
                subjectOutlineColor = Color.white;
                subjectOutlineOptions = Vector4.zero;
                options = Vector4.zero;
                return;
            }

            eyeRevealParams = new Vector4(
                Mathf.Clamp01(settings.eyeRevealStrength),
                Mathf.Max(0.0f, settings.eyeRevealFeatherPixels),
                Mathf.Max(0.0f, settings.eyeRevealDilationPixels),
                Mathf.Max(0.0f, settings.eyeRevealDepthBias));
            hairShadowParams = new Vector4(
                Mathf.Clamp01(settings.hairShadowOpacity),
                Mathf.Max(0.0f, settings.hairShadowDistancePixels),
                settings.hairShadowAngleDegrees,
                Mathf.Max(0.0f, settings.hairShadowSoftnessPixels));
            hairShadowParams1 = new Vector4(
                Mathf.Max(0.0f, settings.hairShadowSpreadPixels),
                Mathf.Clamp01(settings.hairShadowKeepOffHair),
                (float)settings.hairShadowBlendMode,
                settings.useEyeRevealArea ? 1.0f : 0.0f);
            hairShadowParams2 = new Vector4(
                Mathf.Clamp01(settings.hairShadowDistancePerspectiveStrength),
                Mathf.Max(0.0f, settings.hairShadowDistanceReferenceDepth),
                Mathf.Clamp01(settings.hairShadowDistanceMinScale),
                0.0f);
            hairShadowColor = settings.hairShadowColor;

            FillFaceHairDiffuseMaterialVectors(
                settings,
                faceHairDiffuseTexturesReady,
                out faceHairDiffuseParams,
                out faceHairDiffuseLevels,
                out faceHairDiffuseTintColor,
                out faceHairDiffuseOptions);
            FillSubjectOutlineMaterialVectors(
                settings,
                subjectOutlineTexturesReady,
                out subjectOutlineParams,
                out subjectOutlineLevels,
                out subjectOutlineColor,
                out subjectOutlineOptions);

            options = new Vector4(
                settings.eyeRevealEnabled ? 1.0f : 0.0f,
                settings.hairDropShadowEnabled ? 1.0f : 0.0f,
                settings.sameCharacterOnly ? 1.0f : 0.0f,
                (float)settings.debugMode);
        }
    }
}
