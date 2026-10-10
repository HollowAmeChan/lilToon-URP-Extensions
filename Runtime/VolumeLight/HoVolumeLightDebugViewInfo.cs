using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.VolumeLight
{
    public static class HoVolumeLightDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoVolumeLightDebugMode>(
            "Ho-VolumeLight", "Hidden/lilToon/URP/VolumeLight/SolarHD",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/VolumeLight/Resources/HoVolumeLightSolarHD.shader");
    }
}
