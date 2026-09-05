using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal static class HoCharacterSpecializationShaderConstants
    {
        public const string CompositeShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/Composite";
        public const string CaptureClearShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/CaptureClear";
        public const string FaceHairDiffuseShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/FaceHairDiffuse";
        public const string SubjectOutlineShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/SubjectOutline";
        public const string CapturePassName = "HoCharacterCapture";
        public const string EyeColorTextureName = "_lilHoCharacterEyeColorTexture";
        public const string EyeDataTextureName = "_lilHoCharacterEyeDataTexture";
        public const string CaptureDepthTextureName = "_lilHoCharacterCaptureDepthTexture";
        public const string TempTextureName = "_lilHoCharacterCompositeSource";
        public const string FaceHairDiffuseSourceColorTextureName = "_lilHoCharacterFaceHairDiffuseSourceColorTexture";
        public const string FaceHairDiffuseSourceDepthTextureName = "_lilHoCharacterFaceHairDiffuseSourceDepthTexture";
        public const string FaceHairDiffuseTempColorTextureName = "_lilHoCharacterFaceHairDiffuseTempColorTexture";
        public const string FaceHairDiffuseTempDepthTextureName = "_lilHoCharacterFaceHairDiffuseTempDepthTexture";
        public const string FaceHairDiffuseColorTextureName = "_lilHoCharacterFaceHairDiffuseColorTexture";
        public const string FaceHairDiffuseDepthTextureName = "_lilHoCharacterFaceHairDiffuseDepthTexture";
        public const string SubjectOutlineSourceTextureName = "_lilHoCharacterSubjectOutlineSourceTexture";
        public const string SubjectOutlineTempTextureName = "_lilHoCharacterSubjectOutlineTempTexture";
        public const string SubjectOutlineTextureName = "_lilHoCharacterSubjectOutlineTexture";
        public const string EnhancedOutlineSourceTextureName = "_lilHoCharacterEnhancedOutlineSourceTexture";
        public const string EnhancedOutlineTempTextureName = "_lilHoCharacterEnhancedOutlineTempTexture";
        public const string EnhancedOutlineTextureName = "_lilHoCharacterEnhancedOutlineTexture";
        public const string EyeAngleTextureName = "_lilHoCharacterEyeAngleTable";

        public static readonly ShaderTagId CaptureShaderTagId = new ShaderTagId(CapturePassName);
        public static readonly int CaptureModeId = Shader.PropertyToID("_HoCharacterCaptureMode");
        public static readonly int EyeColorTextureId = Shader.PropertyToID(EyeColorTextureName);
        public static readonly int EyeDataTextureId = Shader.PropertyToID(EyeDataTextureName);
        public static readonly int EyeRevealParamsId = Shader.PropertyToID("_HoCharacterEyeRevealParams");
        public static readonly int HairShadowParamsId = Shader.PropertyToID("_HoCharacterHairShadowParams");
        public static readonly int HairShadowParams1Id = Shader.PropertyToID("_HoCharacterHairShadowParams1");
        public static readonly int HairShadowParams2Id = Shader.PropertyToID("_HoCharacterHairShadowParams2");
        public static readonly int HairShadowColorId = Shader.PropertyToID("_HoCharacterHairShadowColor");
        public static readonly int FaceHairDiffuseSourceColorTextureId = Shader.PropertyToID(FaceHairDiffuseSourceColorTextureName);
        public static readonly int FaceHairDiffuseSourceDepthTextureId = Shader.PropertyToID(FaceHairDiffuseSourceDepthTextureName);
        public static readonly int FaceHairDiffuseColorTextureId = Shader.PropertyToID(FaceHairDiffuseColorTextureName);
        public static readonly int FaceHairDiffuseDepthTextureId = Shader.PropertyToID(FaceHairDiffuseDepthTextureName);
        public static readonly int FaceHairDiffuseParamsId = Shader.PropertyToID("_HoCharacterFaceHairDiffuseParams");
        public static readonly int FaceHairDiffuseLevelsId = Shader.PropertyToID("_HoCharacterFaceHairDiffuseLevels");
        public static readonly int FaceHairDiffuseTintColorId = Shader.PropertyToID("_HoCharacterFaceHairDiffuseTintColor");
        public static readonly int FaceHairDiffuseOptionsId = Shader.PropertyToID("_HoCharacterFaceHairDiffuseOptions");
        public static readonly int FaceHairDiffuseBlurParamsId = Shader.PropertyToID("_HoCharacterFaceHairDiffuseBlurParams");
        public static readonly int SubjectOutlineSourceTextureId = Shader.PropertyToID(SubjectOutlineSourceTextureName);
        public static readonly int SubjectOutlineTextureId = Shader.PropertyToID(SubjectOutlineTextureName);
        public static readonly int EnhancedOutlineSourceTextureId = Shader.PropertyToID(EnhancedOutlineSourceTextureName);
        public static readonly int EnhancedOutlineTextureId = Shader.PropertyToID(EnhancedOutlineTextureName);
        public static readonly int SubjectOutlineParamsId = Shader.PropertyToID("_HoCharacterSubjectOutlineParams");
        public static readonly int SubjectOutlineLevelsId = Shader.PropertyToID("_HoCharacterSubjectOutlineLevels");
        public static readonly int SubjectOutlineColorId = Shader.PropertyToID("_HoCharacterSubjectOutlineColor");
        public static readonly int SubjectOutlineFogColorId = Shader.PropertyToID("_HoCharacterSubjectOutlineFogColor");
        public static readonly int SubjectOutlineFogParamsId = Shader.PropertyToID("_HoCharacterSubjectOutlineFogParams");
        public static readonly int SubjectOutlineHeightFadeParamsId = Shader.PropertyToID("_HoCharacterSubjectOutlineHeightFadeParams");
        public static readonly int SubjectOutlineOptionsId = Shader.PropertyToID("_HoCharacterSubjectOutlineOptions");
        public static readonly int EnhancedOutlineParamsId = Shader.PropertyToID("_HoCharacterEnhancedOutlineParams");
        public static readonly int EnhancedOutlineFogColorId = Shader.PropertyToID("_HoCharacterEnhancedOutlineFogColor");
        public static readonly int EnhancedOutlineFogParamsId = Shader.PropertyToID("_HoCharacterEnhancedOutlineFogParams");
        public static readonly int EnhancedOutlineHeightFadeParamsId = Shader.PropertyToID("_HoCharacterEnhancedOutlineHeightFadeParams");
        public static readonly int EnhancedOutlineOptionsId = Shader.PropertyToID("_HoCharacterEnhancedOutlineOptions");
        public static readonly int SubjectOutlineSourceParamsId = Shader.PropertyToID("_HoCharacterSubjectOutlineSourceParams");
        public static readonly int SubjectOutlineBlurParamsId = Shader.PropertyToID("_HoCharacterSubjectOutlineBlurParams");
        public static readonly int OptionsId = Shader.PropertyToID("_HoCharacterOptions");
        public static readonly int EyeAngleTextureId = Shader.PropertyToID(EyeAngleTextureName);
        public static readonly int EyeAngleParamsId = Shader.PropertyToID("_HoCharacterEyeAngleParams");
    }
}
