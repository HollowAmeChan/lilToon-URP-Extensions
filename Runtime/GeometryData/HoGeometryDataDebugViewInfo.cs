using lilToon.URP.Extensions.Debugging;
namespace lilToon.URP.Extensions.GeometryData
{
    public static class HoGeometryDataDebugViewInfo
    {
        public static readonly HoDebugPerformanceInfo Performance = HoDebugPerformanceRegistry.Register(
            "GeometryData", "GeometryData", "Ho-GD Outline Publish", "Ho-GD Tension Produce", "Ho-GD Tension Publish");

        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoGeometryDataDebugMode>(
            "GeometryData", "Hidden/Ho-GeometryData/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryData/Resources/HoGeometryDataDebug.shader",
            Performance);
    }
}
