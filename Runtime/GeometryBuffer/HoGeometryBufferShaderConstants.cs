using UnityEngine;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal static class HoGeometryBufferShaderConstants
    {
        public const string FallbackShaderName = "Hidden/lilToon/URP/GeometryBuffer/Fallback";
        public const string DebugShaderName = "Hidden/lilToon/URP/GeometryBuffer/DebugView";
        public const string ShaderPassName = "HoGeometryBuffer";

        public static readonly int NormalDepthTextureId = Shader.PropertyToID("_lilHoAovNormalDepthTexture");
        public static readonly int DepthTextureId = Shader.PropertyToID("_lilHoAovDepthTexture");
        public static readonly int DebugModeId = Shader.PropertyToID("_HoGeometryBufferDebugMode");
        public static readonly int DebugDepthParamsId = Shader.PropertyToID("_HoGeometryBufferDebugDepthParams");
    }
}
