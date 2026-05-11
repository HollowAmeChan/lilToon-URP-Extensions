using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.OIT
{
    internal static class WeightedOITShaderConstants
    {
        public const string AccumulationTextureName = "_lilOITAccumulationTexture";
        public const string RevealageTextureName = "_lilOITRevealageTexture";
        public const string OpaqueTextureName = "_lilOITOpaqueTexture";
        public const string CompositeSourceTextureName = "_lilOITCompositeSourceTexture";
        public const string CompositeShaderName = "Hidden/lilToon/URP/WeightedOITComposite";
        public const string ShaderPassName = "lilToonOIT";
        public const string OITActiveName = "_lilOITActive";

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly int AccumulationTextureId = Shader.PropertyToID(AccumulationTextureName);
        public static readonly int RevealageTextureId = Shader.PropertyToID(RevealageTextureName);
        public static readonly int OpaqueTextureId = Shader.PropertyToID(OpaqueTextureName);
        public static readonly int CameraOpaqueTextureId = Shader.PropertyToID("_CameraOpaqueTexture");
        public static readonly int CameraOpaqueTextureTexelSizeId = Shader.PropertyToID("_CameraOpaqueTexture_TexelSize");
        public static readonly int OITActiveId = Shader.PropertyToID(OITActiveName);
    }
}
