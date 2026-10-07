using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.GTAO
{
    public static class HoGTAODebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoGTAODebugMode>(
            "GTAO", "Hidden/lilToon/URP/HoGTAO/DebugView",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GTAO/Shaders/Debug/HoGTAODebug.shader");
    }
}
