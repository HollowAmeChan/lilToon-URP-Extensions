namespace lilToon.URP.Extensions.Debugging
{
    public enum HoDebugViewRenderKind
    {
        None = 0,
        GeometryBuffer = 1,
        ShadowCast = 2,
        SubsurfaceScattering = 3,
        PlanarReflection = 4,
        AdaptiveProbeVolume = 5,
        ObjectBuffer = 6,
        /// <summary>SB 的表面数值 + 语义 lane（DebugTile 里的平铺视图；与 §4.13 的"每个 lane 都要有视图"对应）。</summary>
        SurfaceBuffer = 7
    }

    public readonly struct HoDebugViewInfo
    {
        public HoDebugViewInfo(
            string featureName,
            string viewId,
            string shortName,
            int modeValue,
            HoDebugViewRenderKind renderKind,
            string shaderName,
            string shaderAssetPath,
            bool requiresShaderCollection,
            string missingFallback)
        {
            FeatureName = featureName;
            ViewId = viewId;
            ShortName = shortName;
            ModeValue = modeValue;
            RenderKind = renderKind;
            ShaderName = shaderName;
            ShaderAssetPath = shaderAssetPath;
            RequiresShaderCollection = requiresShaderCollection;
            MissingFallback = missingFallback;
        }

        public readonly string FeatureName;
        public readonly string ViewId;
        public readonly string ShortName;
        public readonly int ModeValue;
        public readonly HoDebugViewRenderKind RenderKind;
        public readonly string ShaderName;
        public readonly string ShaderAssetPath;
        public readonly bool RequiresShaderCollection;
        public readonly string MissingFallback;

        public bool HasShader => !string.IsNullOrEmpty(ShaderName);
        public bool SupportsAutomaticTile => RenderKind != HoDebugViewRenderKind.None;
    }
}
