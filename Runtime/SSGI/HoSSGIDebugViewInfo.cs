using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.SSGI
{
    public static class HoSSGIDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoSSGIDebugMode>(
            "SSGI", "Hidden/lilToon/URP/HoSSGI/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SSGI/Shaders/HoSSGIDebug.shader");
    }
}
