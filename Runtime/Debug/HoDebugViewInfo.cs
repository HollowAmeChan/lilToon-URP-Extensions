namespace lilToon.URP.Extensions.Debugging
{
    public readonly struct HoDebugViewInfo
    {
        public HoDebugViewInfo(
            string featureName,
            string viewId,
            string shortName,
            int modeValue,
            string shaderName,
            string shaderAssetPath,
            bool requiresShaderCollection,
            string missingFallback)
        {
            FeatureName = featureName;
            ViewId = viewId;
            ShortName = shortName;
            ModeValue = modeValue;
            ShaderName = shaderName;
            ShaderAssetPath = shaderAssetPath;
            RequiresShaderCollection = requiresShaderCollection;
            MissingFallback = missingFallback;
        }

        public readonly string FeatureName;
        public readonly string ViewId;
        public readonly string ShortName;
        public readonly int ModeValue;
        public readonly string ShaderName;
        public readonly string ShaderAssetPath;
        public readonly bool RequiresShaderCollection;
        public readonly string MissingFallback;

        public bool HasShader => !string.IsNullOrEmpty(ShaderName);
    }
}
