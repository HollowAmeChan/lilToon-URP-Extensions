using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.ShadowCast
{
    public static class HoShadowCastDebugViewInfo
    {
        private const string FeatureName = "ShadowCast";
        private const string ShaderName = "Hidden/lilToon-HoShadowCast/URP/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ShadowCast/Shaders/Debug/HoShadowCastDebug.shader";
        private const string MissingFallback = "ShadowCast debug overlay is skipped when the feature-local debug shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("shadow-cast.atlas", "Atlas", HoShadowCastDebugMode.Atlas),
            View("shadow-cast.second-directional-atlas", "2ndDir", HoShadowCastDebugMode.SecondDirectionalAtlas)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoShadowCastDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.ShadowCast, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
