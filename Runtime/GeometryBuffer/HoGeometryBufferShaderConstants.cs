using UnityEngine;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal static class HoGeometryBufferShaderConstants
    {
        public const string FallbackShaderName = "Hidden/lilToon/URP/GeometryBuffer/Fallback";
        public const string ShaderPassName = "HoGeometryBuffer";

        public static readonly int NormalDepthTextureId = Shader.PropertyToID("_lilHoAovNormalDepthTexture");
        public static readonly int DepthTextureId = Shader.PropertyToID("_lilHoAovDepthTexture");
    }
}
