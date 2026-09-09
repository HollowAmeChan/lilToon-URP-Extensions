namespace lilToon.URP.Extensions.GTAO
{
    internal static class HoGTAOShaderConstants
    {
        public const string FeatureName = "Ho-GTAO";
        public const string ShaderName = "Hidden/lilToon/URP/HoGTAOv4";
        public const string MotionShaderName = "Hidden/lilToon/URP/HoGTAOMotion";
        public const string DebugShaderName = "Hidden/lilToon/URP/HoGTAO/DebugView";

        public const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GTAO/Shaders/HoGTAO.shader";
        public const string DebugShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GTAO/Shaders/Debug/HoGTAODebug.shader";

        // 公共 AO 通道（契约 v1 的 ao 通道运行时载体；lilToon 侧直接采样本纹理）
        public const string AOTextureName = "_HoAOTexture";

        public static readonly int AOTextureId = UnityEngine.Shader.PropertyToID(AOTextureName);
        public static readonly int DebugParamsId = UnityEngine.Shader.PropertyToID("_HoGTAODebugParams");
        public static readonly int PrevViewProjId = UnityEngine.Shader.PropertyToID("_HoGTAOPrevViewProj");
        public static readonly int GeometryNormalDepthId = UnityEngine.Shader.PropertyToID("_HoGeometryBufferNormalDepthTexture");
        public static readonly int AoInputTexId = UnityEngine.Shader.PropertyToID("_HoGTAOAoInputTex");
        public static readonly int AoFilteredTexId = UnityEngine.Shader.PropertyToID("_HoGTAOAoFilteredTex");
        public static readonly int HistoryPrevTexId = UnityEngine.Shader.PropertyToID("_HoGTAOHistoryPrevTex");
        public static readonly int DebugIntensityId = UnityEngine.Shader.PropertyToID("_HoGTAODebugIntensity");
        public static readonly int DebugInvertId = UnityEngine.Shader.PropertyToID("_HoGTAODebugInvert");
        public static readonly int DebugViewModeId = UnityEngine.Shader.PropertyToID("_HoGTAODebugViewMode");
        public static readonly int MotionMaskId = UnityEngine.Shader.PropertyToID("_HoGTAOMotionMask");
        public static readonly int MotionDeltaId = UnityEngine.Shader.PropertyToID("_HoGTAOMotionDelta");
    }
}
