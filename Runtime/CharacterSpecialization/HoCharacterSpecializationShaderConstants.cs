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

        public static readonly ShaderTagId CaptureShaderTagId = new ShaderTagId(CapturePassName);
        public static readonly int CaptureModeId = Shader.PropertyToID("_HoCharacterCaptureMode");
        public static readonly int EyeColorTextureId = Shader.PropertyToID(EyeColorTextureName);
        public static readonly int EyeDataTextureId = Shader.PropertyToID(EyeDataTextureName);
        public static readonly int EyeRevealParamsId = Shader.PropertyToID("_HoCharacterEyeRevealParams");
        public static readonly int HairShadowParamsId = Shader.PropertyToID("_HoCharacterHairShadowParams");
        public static readonly int HairShadowParams1Id = Shader.PropertyToID("_HoCharacterHairShadowParams1");
        public static readonly int HairShadowColorId = Shader.PropertyToID("_HoCharacterHairShadowColor");
        public static readonly int OptionsId = Shader.PropertyToID("_HoCharacterOptions");
    }
}
