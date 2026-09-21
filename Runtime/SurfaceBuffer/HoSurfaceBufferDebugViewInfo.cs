using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    public static class HoSurfaceBufferDebugViewInfo
    {
        private const string FeatureName = "SurfaceBuffer";
        private const string ShaderName = "Hidden/lilToon/URP/SurfaceBuffer/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SurfaceBuffer/Shaders/Debug/HoSurfaceBufferDebug.shader";
        private const string MissingFallback = "SurfaceBuffer debug view is skipped when the feature-local debug shader is missing.";

        /// <summary>
        /// SB 的视图登记（SB 架构 §4.13：五张数值图 + owner 对齐 + **每个 surface semantic lane** 都要有视图）。
        /// mode 值与 `HoSurfaceBufferDebugMode` 一一对应，DebugTile 的平铺视图与 Volume 的整屏调试**共用同一套 mode**。
        /// </summary>
        public static readonly HoDebugViewInfo[] Views =
        {
            View("surface.color", "Color", HoSurfaceBufferDebugMode.Color),
            View("surface.normal", "Nrm", HoSurfaceBufferDebugMode.Normal),
            View("surface.material", "Mat", HoSurfaceBufferDebugMode.Material),
            View("surface.reflection", "Refl", HoSurfaceBufferDebugMode.Reflection),
            View("surface.classification", "Class", HoSurfaceBufferDebugMode.Classification),
            View("surface.owner", "Owner", HoSurfaceBufferDebugMode.Owner),
            View("surface.class-id", "ClassId", HoSurfaceBufferDebugMode.ClassId),
            View("surface.semantic-owner", "SemOwner", HoSurfaceBufferDebugMode.SemanticOwner),
            View("surface.semantic-lanes", "SemLanes", HoSurfaceBufferDebugMode.SemanticLanes)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoSurfaceBufferDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.SurfaceBuffer, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
