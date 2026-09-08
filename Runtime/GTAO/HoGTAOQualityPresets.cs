namespace lilToon.URP.Extensions.GTAO
{
    /// <summary>
    /// Quality 档位预设（三种）——只调整"密度/去噪"项；几何参数（Radius/Thickness 等）由 Volume/Settings 单独维护。
    /// 参考：LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md §8。
    /// </summary>
    public static class HoGTAOQualityPresets
    {
        public static void Apply(HoGTAOQuality quality, HoGTAOSettings settings)
        {
            settings.quality = quality;
            switch (quality)
            {
                case HoGTAOQuality.Low:
                    settings.resolution = HoGTAOResolution.Quarter;
                    settings.sliceCount = 2;
                    settings.stepCount = 8;
                    settings.temporalFrameCount = 0;
                    settings.spatialFilter = HoGTAOSpatialFilter.Box;
                    settings.boxPassCount = 1;
                    break;

                case HoGTAOQuality.Medium:
                    settings.resolution = HoGTAOResolution.Half;
                    settings.sliceCount = 2;
                    settings.stepCount = 12;
                    settings.temporalFrameCount = 4;
                    settings.spatialFilter = HoGTAOSpatialFilter.Box;
                    settings.boxPassCount = 2;
                    break;

                default:
                    settings.resolution = HoGTAOResolution.Full;
                    settings.sliceCount = 4;
                    settings.stepCount = 32;
                    settings.temporalFrameCount = 12;
                    // HTrace's default high profile is SpatioTemporal + Box x2.
                    settings.spatialFilter = HoGTAOSpatialFilter.Box;
                    settings.boxPassCount = 3;
                    break;
            }
        }
    }
}
