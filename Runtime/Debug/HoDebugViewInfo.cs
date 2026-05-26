namespace lilToon.URP.Extensions.Debugging
{
    public enum HoDebugViewRenderKind
    {
        None = 0,
        MetadataBuffer = 1,
        GeometryBuffer = 2
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
