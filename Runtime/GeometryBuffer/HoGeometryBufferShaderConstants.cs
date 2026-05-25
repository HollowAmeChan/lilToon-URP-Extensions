using UnityEngine;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal static class HoGeometryBufferShaderConstants
    {
        public const string FallbackShaderName = "Hidden/lilToon/URP/GeometryBuffer/Fallback";
        public const string DebugShaderName = "Hidden/lilToon/URP/GeometryBuffer/DebugView";
        public const string ShaderPassName = "HoGeometryBuffer";

        public const string NormalDepthTextureName = "_HoGeometryBufferNormalDepthTexture";
        public const string DepthTextureName = "_HoGeometryBufferDepthTexture";

        public static readonly int NormalDepthTextureId = Shader.PropertyToID(NormalDepthTextureName);
        public static readonly int DepthTextureId = Shader.PropertyToID(DepthTextureName);
        public static readonly int DebugModeId = Shader.PropertyToID("_HoGeometryBufferDebugMode");
        public static readonly int DebugDepthParamsId = Shader.PropertyToID("_HoGeometryBufferDebugDepthParams");
    }
}
