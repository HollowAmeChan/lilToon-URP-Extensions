namespace lilToon.URP.Extensions.Debugging
{
    public static class HoAdaptiveProbeVolumeDebugViewInfo
    {
        private const string FeatureName = "AdaptiveProbeVolume";
        private const string ShaderName = "Hidden/lilToon/URP/Debug/DebugTile";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugTile.shader";
        private const string MissingFallback = "APV debug view is skipped when the GeometryBuffer input is unavailable.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("apv.validity", "APV Valid", HoAdaptiveProbeVolumeDebugMode.Validity),
            View("apv.irradiance", "APV GI", HoAdaptiveProbeVolumeDebugMode.Irradiance),
            View("apv.toon-indirect", "APV Toon", HoAdaptiveProbeVolumeDebugMode.ToonIndirect)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoAdaptiveProbeVolumeDebugMode mode)
        {
            return new HoDebugViewInfo(
                FeatureName,
                viewId,
                shortName,
                (int)mode,
                HoDebugViewRenderKind.AdaptiveProbeVolume,
                ShaderName,
                ShaderAssetPath,
                true,
                MissingFallback);
        }
    }

    public enum HoAdaptiveProbeVolumeDebugMode
    {
        Validity = 1,
        Irradiance = 2,
        ToonIndirect = 3
    }
}
