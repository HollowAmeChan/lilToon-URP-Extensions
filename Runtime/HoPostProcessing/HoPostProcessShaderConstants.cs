using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class HoPostProcessShaderConstants
    {
        public const string DefaultLayerShaderName = "Hidden/lilToon-HoPost/URP/HoPost/LayerBlit";
        public const string EdgeLightShaderName = "Hidden/lilToon-HoPost/URP/HoPost/EdgeLight";
        public const string OutlineShaderName = "Hidden/lilToon-HoPost/URP/HoPost/Outline";
        public const string DropShadowShaderName = "Hidden/lilToon-HoPost/URP/HoPost/DropShadow";
        public const string DepthOfFieldShaderName = "Hidden/lilToon-HoPost/URP/HoPost/DepthOfField";
        public const string PostLightingShaderName = "Hidden/lilToon-HoPost/URP/HoPost/PostLighting";
        public const string SubjectMaskShaderName = "Hidden/lilToon-HoPost/URP/HoPost/SubjectMask";
        public const string TempTextureAName = "_lilHoPostProcessTempA";
        public const string TempTextureBName = "_lilHoPostProcessTempB";
        public const string SubjectMaskTextureName = "_lilHoPostSubjectMaskTexture";

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
        public static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        public static readonly int CameraNormalsTextureId = Shader.PropertyToID("_CameraNormalsTexture");
        public static readonly int SubjectMaskTextureId = Shader.PropertyToID(SubjectMaskTextureName);
        public static readonly int SubjectMaskValidId = Shader.PropertyToID("_SubjectMaskValid");
    }
}
