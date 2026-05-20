using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessShaderConstants
    {
        public const string DefaultLayerShaderName = "Hidden/lilToon-Shoost/URP/Shoost/PostProcessLayerBlit";
        public const string AovCompositeShaderName = "Hidden/lilToon-Shoost/URP/Shoost/AOVComposite";
        public const string TempTextureAName = "_lilShoostPostProcessTempA";
        public const string TempTextureBName = "_lilShoostPostProcessTempB";
        public const string TempTextureCName = "_lilShoostPostProcessTempC";

        public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        public static readonly int LayerBlendModeId = Shader.PropertyToID("_LayerBlendMode");
        public static readonly int LayerColorId = Shader.PropertyToID("_LayerColor");
        public static readonly int LayerTextureId = Shader.PropertyToID("_LayerTexture");
        public static readonly int LayerTextureEnabledId = Shader.PropertyToID("_LayerTextureEnabled");
        public static readonly int[] LogoTextureIds =
        {
            Shader.PropertyToID("_LogoTexture0"),
            Shader.PropertyToID("_LogoTexture1"),
            Shader.PropertyToID("_LogoTexture2"),
            Shader.PropertyToID("_LogoTexture3"),
            Shader.PropertyToID("_LogoTexture4"),
            Shader.PropertyToID("_LogoTexture5"),
            Shader.PropertyToID("_LogoTexture6"),
            Shader.PropertyToID("_LogoTexture7")
        };
        public static readonly int LogoTextureEnabled0Id = Shader.PropertyToID("_LogoTextureEnabled0");
        public static readonly int LogoTextureEnabled1Id = Shader.PropertyToID("_LogoTextureEnabled1");
        public static readonly int LayerParams0Id = Shader.PropertyToID("_LayerParams0");
        public static readonly int LayerParams1Id = Shader.PropertyToID("_LayerParams1");
        public static readonly int LayerParams2Id = Shader.PropertyToID("_LayerParams2");
        public static readonly int LayerParams3Id = Shader.PropertyToID("_LayerParams3");
        public static readonly int LayerParams4Id = Shader.PropertyToID("_LayerParams4");
        public static readonly int LayerParams5Id = Shader.PropertyToID("_LayerParams5");
        public static readonly int LayerParams6Id = Shader.PropertyToID("_LayerParams6");
        public static readonly int LayerParams7Id = Shader.PropertyToID("_LayerParams7");
        public static readonly int LayerParams8Id = Shader.PropertyToID("_LayerParams8");
        public static readonly int LayerParams9Id = Shader.PropertyToID("_LayerParams9");
        public static readonly int LayerParams10Id = Shader.PropertyToID("_LayerParams10");
        public static readonly int LayerParams11Id = Shader.PropertyToID("_LayerParams11");
        public static readonly int LayerParams12Id = Shader.PropertyToID("_LayerParams12");
        public static readonly int LayerAovMaskEnabledId = Shader.PropertyToID("_LayerAovMaskEnabled");
        public static readonly int LayerAovSourceId = Shader.PropertyToID("_LayerAovSource");
        public static readonly int LayerAovModeId = Shader.PropertyToID("_LayerAovMode");
        public static readonly int LayerAovParamsId = Shader.PropertyToID("_LayerAovParams");
        public static readonly int LayerAovMatchColorId = Shader.PropertyToID("_LayerAovMatchColor");
        public static readonly int LayerAovDebugOutputId = Shader.PropertyToID("_LayerAovDebugOutput");
        public static readonly int LayerAovRuleCountId = Shader.PropertyToID("_LayerAovRuleCount");
        public static readonly int LayerAovRuleData0Id = Shader.PropertyToID("_LayerAovRuleData0");
        public static readonly int LayerAovRuleData1Id = Shader.PropertyToID("_LayerAovRuleData1");
        public static readonly int LayerAovRuleData2Id = Shader.PropertyToID("_LayerAovRuleData2");
        public static readonly int LayerAovRuleColorId = Shader.PropertyToID("_LayerAovRuleColor");
        public static readonly int ModeId = Shader.PropertyToID("_Mode");
        public static readonly int RadiusId = Shader.PropertyToID("_Radius");
        public static readonly int SharpnessId = Shader.PropertyToID("_Sharpness");
        public static readonly int ScreenRatioId = Shader.PropertyToID("_ScreenRatio");
        public static readonly int OriginalTexId = Shader.PropertyToID("_OriginalTex");
        public static readonly int BlurredTexId = Shader.PropertyToID("_BlurredTex");
        public static readonly int FrozenFrameTexId = Shader.PropertyToID("_FrozenFrameTex");
        public static readonly int CenterId = Shader.PropertyToID("_Center");
        public static readonly int CenterSizeId = Shader.PropertyToID("_CenterSize");
        public static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        public static readonly int BlurOffsetRId = Shader.PropertyToID("_BlurOffsetR");
        public static readonly int BlurOffsetGId = Shader.PropertyToID("_BlurOffsetG");
        public static readonly int BlurOffsetBId = Shader.PropertyToID("_BlurOffsetB");
        public static readonly int DistanceId = Shader.PropertyToID("_Distance");
        public static readonly int AngleId = Shader.PropertyToID("_Angle");
        public static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        public static readonly int LayerResultTextureId = Shader.PropertyToID("_LayerResultTexture");
    }
}
