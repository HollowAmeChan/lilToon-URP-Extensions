using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ScreenProcessShaderConstants
    {
        public const string DefaultLayerShaderName = "Hidden/lilToon/URP/ScreenProcess/LayerBlit";
        public const string EdgeLightShaderName = "Hidden/lilToon/URP/ScreenProcess/EdgeLight";
        public const string OutlineShaderName = "Hidden/lilToon/URP/ScreenProcess/Outline";
        public const string DropShadowShaderName = "Hidden/lilToon/URP/ScreenProcess/DropShadow";
        public const string DepthOfFieldShaderName = "Hidden/lilToon/URP/ScreenProcess/DepthOfField";
        public const string PostLightingShaderName = "Hidden/lilToon/URP/ScreenProcess/PostLighting";
        public const string SkyTyndallShaderName = "Hidden/lilToon/URP/ScreenProcess/SkyTyndall";
        public const string SubjectMaskShaderName = "Hidden/lilToon/URP/ScreenProcess/SubjectMask";
        public const string TempTextureAName = "_lilScreenProcessTempA";
        public const string TempTextureBName = "_lilScreenProcessTempB";
        public const string SubjectMaskTextureName = "_lilScreenProcessSubjectMaskTexture";

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
        public static readonly int LayerRuleMaskEnabledId = Shader.PropertyToID("_LayerRuleMaskEnabled");
        public static readonly int LayerRuleSourceId = Shader.PropertyToID("_LayerRuleSource");
        public static readonly int LayerRuleModeId = Shader.PropertyToID("_LayerRuleMode");
        public static readonly int LayerRuleParamsId = Shader.PropertyToID("_LayerRuleParams");
        public static readonly int LayerRuleMatchColorId = Shader.PropertyToID("_LayerRuleMatchColor");
        public static readonly int LayerRuleDebugOutputId = Shader.PropertyToID("_LayerRuleDebugOutput");
        public static readonly int LayerRuleMaskCountId = Shader.PropertyToID("_LayerRuleMaskCount");
        public static readonly int LayerRuleMaskData0Id = Shader.PropertyToID("_LayerRuleMaskData0");
        public static readonly int LayerRuleMaskData1Id = Shader.PropertyToID("_LayerRuleMaskData1");
        public static readonly int LayerRuleMaskData2Id = Shader.PropertyToID("_LayerRuleMaskData2");
        public static readonly int LayerRuleMaskColorId = Shader.PropertyToID("_LayerRuleMaskColor");
        public static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        public static readonly int CameraNormalsTextureId = Shader.PropertyToID("_CameraNormalsTexture");
        public static readonly int SubjectMaskTextureId = Shader.PropertyToID(SubjectMaskTextureName);
        public static readonly int SubjectMaskValidId = Shader.PropertyToID("_SubjectMaskValid");
    }
}
