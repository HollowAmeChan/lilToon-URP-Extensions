using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.SSGI
{
    public static class HoSSGIDebugViewInfo
    {
        public static readonly HoDebugPerformanceInfo Performance = HoDebugPerformanceRegistry.Register(
            "SSGI", "SSGI", "Ho-SSGI Source Reprojection", "Ho-SSGI Raw Trace", "Ho-SSGI Temporal",
            "Ho-SSGI Temporal History", "Ho-SSGI Depth History", "Ho-SSGI Temporal Metadata",
            "Ho-SSGI Source History", "Ho-SSGI Firefly", "Ho-SSGI Spatial Resampling",
            "Ho-SSGI Spatial Validation", "Ho-SSGI Temporal Accumulation", "Ho-SSGI Denoised History",
            "Ho-SSGI Spatial Filter 1", "Ho-SSGI Spatial Filter 2", "Ho-SSGI Depth Pyramid 0",
            "Ho-SSGI Depth Pyramid 1", "Ho-SSGI Depth Pyramid 2", "Ho-SSGI Depth Pyramid 3",
            "Ho-SSGI Composite", "Ho-SSGI Debug");

        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoSSGIDebugMode>(
            "SSGI", "Hidden/lilToon/URP/HoSSGI/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SSGI/Shaders/HoSSGIDebug.shader",
            Performance);
    }
}
