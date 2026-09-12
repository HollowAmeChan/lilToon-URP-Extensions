using UnityEngine;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal static class HoGeometryBufferShaderConstants
    {
        public const string FallbackShaderName = "Hidden/lilToon/URP/GeometryBuffer/Fallback";
        public const string SkyCaptureShaderName = "Hidden/lilToon/URP/GeometryBuffer/SkyCapture";
        public const string DebugShaderName = "Hidden/lilToon/URP/GeometryBuffer/DebugView";
        public const string ResolveShaderName = "Hidden/lilToon/URP/GeometryBuffer/Resolve";
        public const string ShaderPassName = "HoGeometryBuffer";
        public const string OutlineNormalDepthShaderPassName = "HoGeometryBufferOutlineNormalDepth";

        public const string NormalDepthTextureName = "_HoGeometryBufferNormalDepthTexture";
        public const string DepthTextureName = "_HoGeometryBufferDepthTexture";
        public const string OutlineNormalDepthTextureName = "_HoGeometryBufferOutlineNormalDepthTexture";
        public const string CoverageTextureName = "_HoGeometryBufferCoverageTexture";
        public const string OutlineCoverageTextureName = "_HoGeometryBufferOutlineCoverageTexture";
        public const string ResolveNormalDepthTextureMsName = "_HoGeometryBufferResolveNormalDepthTextureMS";
        public const string ResolveDepthTextureMsName = "_HoGeometryBufferResolveDepthTextureMS";
        public const string SkyTextureName = "_HoGeometryBufferSkyTexture";

        public static readonly int NormalDepthTextureId = Shader.PropertyToID(NormalDepthTextureName);
        public static readonly int DepthTextureId = Shader.PropertyToID(DepthTextureName);
        public static readonly int OutlineNormalDepthTextureId = Shader.PropertyToID(OutlineNormalDepthTextureName);
        public static readonly int CoverageTextureId = Shader.PropertyToID(CoverageTextureName);
        public static readonly int OutlineCoverageTextureId = Shader.PropertyToID(OutlineCoverageTextureName);
        public static readonly int ResolveNormalDepthTextureMsId = Shader.PropertyToID(ResolveNormalDepthTextureMsName);
        public static readonly int ResolveDepthTextureMsId = Shader.PropertyToID(ResolveDepthTextureMsName);
        public static readonly int SkyTextureId = Shader.PropertyToID(SkyTextureName);
        public static readonly int ValidId = Shader.PropertyToID("_HoGeometryBufferValid");
        public static readonly int CoverageTextureValidId = Shader.PropertyToID("_HoGeometryBufferCoverageTextureValid");
        public static readonly int OutlineCoverageTextureValidId = Shader.PropertyToID("_HoGeometryBufferOutlineCoverageTextureValid");
        public static readonly int SkyTextureValidId = Shader.PropertyToID("_HoGeometryBufferSkyTextureValid");
        public static readonly int DebugModeId = Shader.PropertyToID("_HoGeometryBufferDebugMode");
        public static readonly int DebugDepthParamsId = Shader.PropertyToID("_HoGeometryBufferDebugDepthParams");
    }
}
