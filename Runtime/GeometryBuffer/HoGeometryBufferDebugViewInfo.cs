using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    public static class HoGeometryBufferDebugViewInfo
    {
        private const string FeatureName = "GeometryBuffer";
        private const string ShaderName = "Hidden/lilToon/URP/GeometryBuffer/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/Debug/HoGeometryBufferDebug.shader";
        private const string MissingFallback = "GeometryBuffer debug view is skipped when the feature-local debug shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("geometry.coverage", "Cover", HoGeometryBufferDebugMode.Coverage),
            View("geometry.linear-depth", "Depth", HoGeometryBufferDebugMode.LinearDepth),
            View("geometry.world-normal", "Normal", HoGeometryBufferDebugMode.WorldNormal),
            View("geometry.normal-validity", "NValid", HoGeometryBufferDebugMode.NormalValidity)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoGeometryBufferDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.GeometryBuffer, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
