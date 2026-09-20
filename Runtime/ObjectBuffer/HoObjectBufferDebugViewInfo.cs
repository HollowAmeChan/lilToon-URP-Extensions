using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    public static class HoObjectBufferDebugViewInfo
    {
        private const string FeatureName = "ObjectBuffer";
        private const string ShaderName = "Hidden/lilToon/URP/ObjectBuffer/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/Debug/HoObjectBufferDebug.shader";
        private const string MissingFallback = "ObjectBuffer debug view is skipped when the feature-local debug shader is missing.";

        /// <summary>
        /// 每个通道都要有视图（规划 §5.9①）：没有视图的成本与错误都只能在 Frame Debugger 里猜。
        /// ID 视图按 palette 的显示色上色，所以"未注册"会直接显示成洋红的 unknown 行。
        /// </summary>
        public static readonly HoDebugViewInfo[] Views =
        {
            View("object.id0", "ID0", HoObjectBufferDebugMode.Id0),
            View("object.id1", "ID1", HoObjectBufferDebugMode.Id1),
            View("object.id2", "ID2", HoObjectBufferDebugMode.Id2),
            View("object.id3", "ID3", HoObjectBufferDebugMode.Id3),
            View("object.coverage-total", "Cov", HoObjectBufferDebugMode.CoverageTotal),
            View("object.coverage-layers", "CovL", HoObjectBufferDebugMode.CoverageLayers),
            View("object.selection", "Sel", HoObjectBufferDebugMode.Selection),
            View("object.palette-row", "Pal", HoObjectBufferDebugMode.PaletteRow),
            View("object.valid", "Valid", HoObjectBufferDebugMode.Valid)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoObjectBufferDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.ObjectBuffer, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
