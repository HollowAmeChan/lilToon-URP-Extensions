using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    public static class HoCharacterBufferDebugViewInfo
    {
        private const string FeatureName = "CharacterBuffer";
        private const string ShaderName = "Hidden/lilToon/URP/CharacterBuffer/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterBuffer/Shaders/Debug/HoCharacterBufferDebug.shader";
        private const string MissingFallback = "CharacterBuffer debug view is skipped when the feature-local debug shader is missing.";

        /// <summary>
        /// 每个通道都要有视图（规划 §5.9①）：没有视图的成本与错误都只能在 Frame Debugger 里猜。
        /// ID 视图按 palette 的显示色上色，所以"未注册"会直接显示成洋红的 unknown 行。
        /// </summary>
        public static readonly HoDebugViewInfo[] Views =
        {
            View("character.id0", "ID0", HoCharacterBufferDebugMode.Id0),
            View("character.id1", "ID1", HoCharacterBufferDebugMode.Id1),
            View("character.id2", "ID2", HoCharacterBufferDebugMode.Id2),
            View("character.id3", "ID3", HoCharacterBufferDebugMode.Id3),
            View("character.coverage-total", "Cov", HoCharacterBufferDebugMode.CoverageTotal),
            View("character.coverage-layers", "CovL", HoCharacterBufferDebugMode.CoverageLayers),
            View("character.selection", "Sel", HoCharacterBufferDebugMode.Selection),
            View("character.palette-row", "Pal", HoCharacterBufferDebugMode.PaletteRow),
            View("character.valid", "Valid", HoCharacterBufferDebugMode.Valid)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoCharacterBufferDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.CharacterBuffer, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
