namespace lilToon.URP.Extensions.GTAO
{
    /// <summary>
    /// 一次性填写 Volume 的分辨率、密度和去噪参数，不改变 Override、半径或厚度。
    /// 参考：LILTOON_HTRACE_GTAO_QUALITY_REFERENCE.md §8。
    /// </summary>
    public static class HoGTAOQualityPresets
    {
        public static void Apply(HoGTAOQuality quality, HoGTAOVolume volume)
        {
            switch (quality)
            {
                case HoGTAOQuality.Low:
                    volume.resolution.value = HoGTAOResolution.Quarter;
                    volume.sliceCount.value = 2;
                    volume.stepCount.value = 8;
                    volume.temporalFrameCount.value = 0;
                    volume.spatialFilter.value = HoGTAOSpatialFilter.Box;
                    volume.boxPassCount.value = 1;
                    break;

                case HoGTAOQuality.Medium:
                    volume.resolution.value = HoGTAOResolution.Half;
                    volume.sliceCount.value = 2;
                    volume.stepCount.value = 12;
                    volume.temporalFrameCount.value = 4;
                    volume.spatialFilter.value = HoGTAOSpatialFilter.Box;
                    volume.boxPassCount.value = 2;
                    break;

                default:
                    volume.resolution.value = HoGTAOResolution.Full;
                    volume.sliceCount.value = 4;
                    volume.stepCount.value = 32;
                    volume.temporalFrameCount.value = 12;
                    volume.spatialFilter.value = HoGTAOSpatialFilter.Box;
                    volume.boxPassCount.value = 3;
                    break;
            }
        }
    }
}
