using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal static class HoCharacterSpecializationShaderConstants
    {
        public const string CompositeShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/Composite";
        public const string CaptureClearShaderName = "Hidden/lilToon-HoCharacterSpecialization/URP/CaptureClear";
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
        public static readonly int OptionsId = Shader.PropertyToID("_HoCharacterOptions");
    }
}
