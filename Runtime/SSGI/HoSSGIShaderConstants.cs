using UnityEngine;

namespace lilToon.URP.Extensions.SSGI
{
    internal static class HoSSGIShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/HoSSGI";
        public const string DebugShaderName = "Hidden/lilToon/URP/HoSSGI/Debug";
        public const string GITextureName = "_HoGITexture";
        public static readonly int GITextureId = Shader.PropertyToID(GITextureName);
        public static readonly int GeometryId = Shader.PropertyToID("_HoSSGIGeometry");
        public static readonly int SourceId = Shader.PropertyToID("_HoSSGISource");
        public static readonly int SurfaceColorId = Shader.PropertyToID("_HoSSGISurfaceColor");
        public static readonly int RayCountId = Shader.PropertyToID("_HoSSGIRayCount");
        public static readonly int StepCountId = Shader.PropertyToID("_HoSSGIStepCount");
        public static readonly int RayLengthId = Shader.PropertyToID("_HoSSGIRayLength");
        public static readonly int ThicknessId = Shader.PropertyToID("_HoSSGIThickness");
        public static readonly int IntensityId = Shader.PropertyToID("_HoSSGIIntensity");
        public static readonly int SourceSaturationId = Shader.PropertyToID("_HoSSGISourceSaturation");
        public static readonly int DebugModeId = Shader.PropertyToID("_HoSSGIDebugMode");
    }
}
