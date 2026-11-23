using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.GTAO
{
    public static class HoGTAODebugViewInfo
    {
        public static readonly HoDebugPerformanceInfo Performance = HoDebugPerformanceRegistry.Register(
            "GTAO", "GTAO", "Ho-GTAO", "Ho-GTAO Motion Mask", "Ho-GTAO Motion Delta", "Ho-GTAO Camera Motion", "Ho-GTAO Debug Output", "Ho-GTAO Debug Capture");

        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoGTAODebugMode>(
            "GTAO", "Hidden/lilToon/URP/HoGTAO/DebugView",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GTAO/Shaders/Debug/HoGTAODebug.shader",
            Performance);
    }
}
