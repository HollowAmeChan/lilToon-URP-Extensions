using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.Atmosphere
{
    public static class HoAtmosphereDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoAtmosphereDebugMode>(
            "HoAtmosphere", "Hidden/lilToon/URP/Atmosphere/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Atmosphere/Shaders/HoAtmosphereDebug.shader");
    }
}
