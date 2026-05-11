using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.OIT
{
    internal static class WeightedOITShaderConstants
    {
        public const string AccumulationTextureName = "_lilOITAccumulationTexture";
        public const string RevealageTextureName = "_lilOITRevealageTexture";
        public const string CompositeShaderName = "Hidden/lilToon/URP/WeightedOITComposite";
        public const string ShaderPassName = "lilToonOIT";

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly int AccumulationTextureId = Shader.PropertyToID(AccumulationTextureName);
        public static readonly int RevealageTextureId = Shader.PropertyToID(RevealageTextureName);
    }
}
