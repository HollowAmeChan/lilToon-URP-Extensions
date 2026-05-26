using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    public static class HoSubsurfaceScatteringDebugViewInfo
    {
        private const string FeatureName = "Subsurface Scattering";
        private const string ShaderName = "Hidden/lilToon/URP/HoSubsurfaceScattering/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SubsurfaceScattering/Shaders/Debug/HoSubsurfaceScatteringDebug.shader";
        private const string MissingFallback = "SSS debug view is skipped when the feature-local debug shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("sss.mask", "Mask", HoSubsurfaceScatteringDebugMode.Mask),
            View("sss.source", "Input", HoSubsurfaceScatteringDebugMode.Source),
            View("sss.diffusion", "Diff", HoSubsurfaceScatteringDebugMode.Diffusion),
            View("sss.transmission", "Trans", HoSubsurfaceScatteringDebugMode.Transmission),
            View("sss.transmission-gate", "Gate", HoSubsurfaceScatteringDebugMode.TransmissionGate),
            View("sss.composite-weight", "Comp", HoSubsurfaceScatteringDebugMode.CompositeWeight),
            View("sss.profile-id", "Profile", HoSubsurfaceScatteringDebugMode.ProfileId),
            View("sss.thickness", "Thick", HoSubsurfaceScatteringDebugMode.Thickness),
            View("sss.profile-radius", "Radius", HoSubsurfaceScatteringDebugMode.ProfileRadius),
            View("sss.transmission-direction", "Dir", HoSubsurfaceScatteringDebugMode.TransmissionDirection),
            View("sss.transmission-rim", "Rim", HoSubsurfaceScatteringDebugMode.TransmissionRim)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoSubsurfaceScatteringDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.SubsurfaceScattering, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
