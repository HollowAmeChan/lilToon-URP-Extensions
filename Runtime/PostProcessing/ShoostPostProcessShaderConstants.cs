using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessShaderConstants
    {
        public const string DefaultLayerShaderName = "Hidden/lilToon-Shoost/URP/Shoost/PostProcessLayerBlit";
        public const string TempTextureAName = "_lilShoostPostProcessTempA";
        public const string TempTextureBName = "_lilShoostPostProcessTempB";

        public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        public static readonly int LayerBlendModeId = Shader.PropertyToID("_LayerBlendMode");
        public static readonly int LayerColorId = Shader.PropertyToID("_LayerColor");
        public static readonly int LayerTextureId = Shader.PropertyToID("_LayerTexture");
        public static readonly int LayerParams0Id = Shader.PropertyToID("_LayerParams0");
        public static readonly int LayerParams1Id = Shader.PropertyToID("_LayerParams1");
        public static readonly int LayerParams2Id = Shader.PropertyToID("_LayerParams2");
        public static readonly int LayerParams3Id = Shader.PropertyToID("_LayerParams3");
        public static readonly int RadiusId = Shader.PropertyToID("_Radius");
        public static readonly int ScreenRatioId = Shader.PropertyToID("_ScreenRatio");
        public static readonly int OriginalTexId = Shader.PropertyToID("_OriginalTex");
        public static readonly int BlurredTexId = Shader.PropertyToID("_BlurredTex");
        public static readonly int CenterId = Shader.PropertyToID("_Center");
        public static readonly int CenterSizeId = Shader.PropertyToID("_CenterSize");
        public static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        public static readonly int BlurOffsetRId = Shader.PropertyToID("_BlurOffsetR");
        public static readonly int BlurOffsetGId = Shader.PropertyToID("_BlurOffsetG");
        public static readonly int BlurOffsetBId = Shader.PropertyToID("_BlurOffsetB");
        public static readonly int DistanceId = Shader.PropertyToID("_Distance");
        public static readonly int AngleId = Shader.PropertyToID("_Angle");
    }
}
