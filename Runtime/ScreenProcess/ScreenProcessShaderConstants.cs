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
        public const string DepthFogShaderName = "Hidden/lilToon/URP/ScreenProcess/DepthFog";
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
        public static readonly int LayerMaskEnabledId = Shader.PropertyToID("_LayerMaskEnabled");
        public static readonly int LayerMaskInvertId = Shader.PropertyToID("_LayerMaskInvert");
        public static readonly int LayerMaskDebugOutputId = Shader.PropertyToID("_LayerMaskDebugOutput");
        public static readonly int SubjectMaskTextureId = Shader.PropertyToID(SubjectMaskTextureName);
        public static readonly int SubjectMaskValidId = Shader.PropertyToID("_SubjectMaskValid");
    }
}
