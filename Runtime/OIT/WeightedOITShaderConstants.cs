using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.OIT
{
    internal static class WeightedOITShaderConstants
    {
        public const string AccumulationTextureName = "_lilOITAccumulationTexture";
        public const string RevealageTextureName = "_lilOITRevealageTexture";
        public const string CompositeSourceTextureName = "_lilOITCompositeSourceTexture";
        public const string CompositeShaderName = "Hidden/lilToon/URP/WeightedOITComposite";
        public const string ShaderPassName = "HoOIT";
        public const string LegacyShaderPassName = "lilToonOIT";

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly ShaderTagId LegacyShaderTagId = new ShaderTagId(LegacyShaderPassName);
        public static readonly int AccumulationTextureId = Shader.PropertyToID(AccumulationTextureName);
        public static readonly int RevealageTextureId = Shader.PropertyToID(RevealageTextureName);

        private static readonly int ActiveId = Shader.PropertyToID("_HoOITActive");
        private static readonly int WeightId = Shader.PropertyToID("_HoOITWeight");
        private static readonly int AlphaClipThresholdId = Shader.PropertyToID("_HoOITAlphaClipThreshold");
        private static readonly int QueueRangeId = Shader.PropertyToID("_HoOITQueueRange");
        private static readonly int LegacyActiveId = Shader.PropertyToID("_lilOITActive");
        private static readonly int LegacyWeightId = Shader.PropertyToID("_lilOITWeight");
        private static readonly int LegacyAlphaClipThresholdId = Shader.PropertyToID("_lilOITAlphaClipThreshold");

        public static void ResetGlobals()
        {
            Shader.SetGlobalFloat(LegacyActiveId, 0.0f);
            Shader.SetGlobalFloat(ActiveId, 0.0f);
            Shader.SetGlobalFloat(WeightId, 0.0f);
            Shader.SetGlobalFloat(AlphaClipThresholdId, 0.0f);
            Shader.SetGlobalVector(QueueRangeId, Vector4.zero);
        }

        public static void ResetGlobals(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalFloat(LegacyActiveId, 0.0f);
            cmd.SetGlobalFloat(ActiveId, 0.0f);
            cmd.SetGlobalFloat(WeightId, 0.0f);
            cmd.SetGlobalFloat(AlphaClipThresholdId, 0.0f);
            cmd.SetGlobalVector(QueueRangeId, Vector4.zero);
        }

        public static void SetAccumulationGlobals(RasterCommandBuffer cmd, bool canonicalActive,
            float weight, float alphaClipThreshold, Vector4 queueRange)
        {
            cmd.SetGlobalFloat(LegacyActiveId, 1.0f);
            cmd.SetGlobalFloat(LegacyWeightId, weight);
            cmd.SetGlobalFloat(LegacyAlphaClipThresholdId, alphaClipThreshold);
            cmd.SetGlobalFloat(ActiveId, canonicalActive ? 1.0f : 0.0f);
            cmd.SetGlobalFloat(WeightId, weight);
            cmd.SetGlobalFloat(AlphaClipThresholdId, alphaClipThreshold);
            cmd.SetGlobalVector(QueueRangeId, queueRange);
        }
    }
}
