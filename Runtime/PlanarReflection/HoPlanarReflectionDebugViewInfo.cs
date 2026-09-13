using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.PlanarReflection
{
    public static class HoPlanarReflectionDebugViewInfo
    {
        private const string FeatureName = "PLR";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/PlanarReflection/HoPlanarReflectionComposite.shader";
        private const string MissingFallback = "PlanarReflection debug view is skipped when the composite shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("planar-reflection.inputs", "Inputs", HoPlanarReflectionDebugMode.InputStatus),
            View("planar-reflection.surface-mask", "Mask", HoPlanarReflectionDebugMode.SurfaceMask),
            View("planar-reflection.perceptual-roughness", "PRough", HoPlanarReflectionDebugMode.PerceptualRoughness),
            View("planar-reflection.metallic", "Metal", HoPlanarReflectionDebugMode.Metallic),
            View("planar-reflection.reflectance", "F0", HoPlanarReflectionDebugMode.Reflectance),
            View("planar-reflection.reflection-strength", "ReflS", HoPlanarReflectionDebugMode.ReflectionStrength),
            View("planar-reflection.world-normal", "Normal", HoPlanarReflectionDebugMode.WorldNormal),
            View("planar-reflection.linear-depth", "Depth", HoPlanarReflectionDebugMode.LinearDepth),
            View("planar-reflection.distortion", "Dist", HoPlanarReflectionDebugMode.Distortion),
            View("planar-reflection.distorted-uv", "UV", HoPlanarReflectionDebugMode.DistortedUv),
            View("planar-reflection.reflection-color", "Refl", HoPlanarReflectionDebugMode.ReflectionColor),
            View("planar-reflection.composite-weight", "Weight", HoPlanarReflectionDebugMode.CompositeWeight),
            View("planar-reflection.depth-gate", "DGate", HoPlanarReflectionDebugMode.DepthGate),
            View("planar-reflection.reflection-material", "ReflMat", HoPlanarReflectionDebugMode.ReflectionMaterial),
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
