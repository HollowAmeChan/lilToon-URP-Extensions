using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class HoPostProcessShaderConstants
    {
        public const string DefaultLayerShaderName = "Hidden/lilToon-HoPost/URP/HoPost/LayerBlit";
        public const string TempTextureAName = "_lilHoPostProcessTempA";
        public const string TempTextureBName = "_lilHoPostProcessTempB";

        public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        public static readonly int LayerBlendModeId = Shader.PropertyToID("_LayerBlendMode");
        public static readonly int LayerColorId = Shader.PropertyToID("_LayerColor");
        public static readonly int LayerTextureId = Shader.PropertyToID("_LayerTexture");
        public static readonly int LayerTextureEnabledId = Shader.PropertyToID("_LayerTextureEnabled");
        public static readonly int LayerParams0Id = Shader.PropertyToID("_LayerParams0");
        public static readonly int LayerParams1Id = Shader.PropertyToID("_LayerParams1");
        public static readonly int LayerParams2Id = Shader.PropertyToID("_LayerParams2");
        public static readonly int LayerParams3Id = Shader.PropertyToID("_LayerParams3");
        public static readonly int LayerParams4Id = Shader.PropertyToID("_LayerParams4");
        public static readonly int LayerParams5Id = Shader.PropertyToID("_LayerParams5");
    }
}
