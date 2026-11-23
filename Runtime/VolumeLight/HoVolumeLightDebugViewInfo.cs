using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.VolumeLight
{
    public static class HoVolumeLightDebugViewInfo
    {
        public static readonly HoDebugPerformanceInfo Performance = HoDebugPerformanceRegistry.Register(
            "Ho-VolumeLight", "VolumeLight", "Ho-VolumeLight Dust Clusters", "Ho-VolumeLight Dust Other Camera",
            "Ho-VolumeLight Solar HD", "Ho-VolumeLight Reconstruct", "Ho-VolumeLight Composite",
            "Ho-VolumeLight Local Shadow", "Ho-VolumeLight Local Draw", "Ho-VolumeLight Debug");

        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoVolumeLightDebugMode>(
            "Ho-VolumeLight", "Hidden/lilToon/URP/VolumeLight/SolarHD",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/VolumeLight/Resources/HoVolumeLightSolarHD.shader",
            Performance);
    }
}
