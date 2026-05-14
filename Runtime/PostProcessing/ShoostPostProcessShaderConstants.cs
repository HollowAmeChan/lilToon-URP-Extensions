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
    }
}
