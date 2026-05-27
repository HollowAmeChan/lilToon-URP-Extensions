using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.PlanarReflection
{
    public static class HoPlanarReflectionDebugViewInfo
    {
        private const string FeatureName = "Planar Reflection";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/PlanarReflection/HoPlanarReflectionComposite.shader";
        private const string MissingFallback = "PlanarReflection debug view is skipped when the composite shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("planar-reflection.inputs", "Inputs", HoPlanarReflectionDebugMode.InputStatus),
            View("planar-reflection.surface-mask", "Mask", HoPlanarReflectionDebugMode.SurfaceMask),
            View("planar-reflection.smoothness", "Smooth", HoPlanarReflectionDebugMode.Smoothness),
            View("planar-reflection.wetness", "Wet", HoPlanarReflectionDebugMode.Wetness),
            View("planar-reflection.normal-strength", "NormS", HoPlanarReflectionDebugMode.NormalStrength),
            View("planar-reflection.reflection-strength", "ReflS", HoPlanarReflectionDebugMode.ReflectionStrength),
            View("planar-reflection.world-normal", "Normal", HoPlanarReflectionDebugMode.WorldNormal),
            View("planar-reflection.linear-depth", "Depth", HoPlanarReflectionDebugMode.LinearDepth),
            View("planar-reflection.distortion", "Dist", HoPlanarReflectionDebugMode.Distortion),
            View("planar-reflection.distorted-uv", "UV", HoPlanarReflectionDebugMode.DistortedUv),
            View("planar-reflection.reflection-color", "Refl", HoPlanarReflectionDebugMode.ReflectionColor),
            View("planar-reflection.composite-weight", "Weight", HoPlanarReflectionDebugMode.CompositeWeight),
            View("planar-reflection.depth-gate", "DGate", HoPlanarReflectionDebugMode.DepthGate),
            View("planar-reflection.custom0", "Custom0", HoPlanarReflectionDebugMode.Custom0),
            View("planar-reflection.edge-extend", "Extend", HoPlanarReflectionDebugMode.EdgeExtend)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoPlanarReflectionDebugMode mode)
        {
            return new HoDebugViewInfo(
                FeatureName,
                viewId,
                shortName,
                (int)mode,
                HoDebugViewRenderKind.PlanarReflection,
                HoPlanarReflectionShaderConstants.CompositeShaderName,
                ShaderAssetPath,
                true,
                MissingFallback);
        }
    }
}
