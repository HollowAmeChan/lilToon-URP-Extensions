using UnityEngine;

namespace lilToon.URP.Extensions.SSGI
{
    internal static class HoSSGIShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/HoSSGI";
        public const string DebugShaderName = "Hidden/lilToon/URP/HoSSGI/Debug";
        public const string GITextureName = "_HoGITexture";
        public const string RawGITextureName = "_HoSSGIRawGI";
        public const string ReservoirColorName = "_HoSSGIReservoirColor";
        public const string ReservoirAuxName = "_HoSSGIReservoirAux";
        public const string ReservoirRayName = "_HoSSGIReservoirRay";
        public const string ReservoirHistoryColorName = "_HoSSGIReservoirHistoryColor";
        public const string ReservoirHistoryAuxName = "_HoSSGIReservoirHistoryAux";
        public const string ReservoirHistoryRayName = "_HoSSGIReservoirHistoryRay";
        public const string ReservoirOutputColorName = "_HoSSGIReservoirOutputColor";
        public const string ReservoirOutputAuxName = "_HoSSGIReservoirOutputAux";
        public const string ReservoirOutputRayName = "_HoSSGIReservoirOutputRay";
        public static readonly int GITextureId = Shader.PropertyToID(GITextureName);
        public static readonly int RawGIId = Shader.PropertyToID(RawGITextureName);
        public static readonly int ReservoirColorId = Shader.PropertyToID(ReservoirColorName);
        public static readonly int ReservoirAuxId = Shader.PropertyToID(ReservoirAuxName);
        public static readonly int ReservoirRayId = Shader.PropertyToID(ReservoirRayName);
        public static readonly int ReservoirHistoryColorId = Shader.PropertyToID(ReservoirHistoryColorName);
        public static readonly int ReservoirHistoryAuxId = Shader.PropertyToID(ReservoirHistoryAuxName);
        public static readonly int ReservoirHistoryRayId = Shader.PropertyToID(ReservoirHistoryRayName);
        public static readonly int ReservoirOutputColorId = Shader.PropertyToID(ReservoirOutputColorName);
        public static readonly int ReservoirOutputAuxId = Shader.PropertyToID(ReservoirOutputAuxName);
        public static readonly int ReservoirOutputRayId = Shader.PropertyToID(ReservoirOutputRayName);
        public static readonly int GeometryId = Shader.PropertyToID("_HoSSGIGeometry");
        public static readonly int SourceId = Shader.PropertyToID("_HoSSGISource");
        public static readonly int SourceHistoryId = Shader.PropertyToID("_HoSSGISourceHistory");
        public static readonly int RayCountId = Shader.PropertyToID("_HoSSGIRayCount");
        public static readonly int StepCountId = Shader.PropertyToID("_HoSSGIStepCount");
        public static readonly int RayLengthId = Shader.PropertyToID("_HoSSGIRayLength");
        public static readonly int ThicknessId = Shader.PropertyToID("_HoSSGIThickness");
        public static readonly int IntensityId = Shader.PropertyToID("_HoSSGIIntensity");
        public static readonly int SourceSaturationId = Shader.PropertyToID("_HoSSGISourceSaturation");
        public static readonly int TemporalBlendId = Shader.PropertyToID("_HoSSGITemporalBlend");
        public static readonly int SpatialRadiusId = Shader.PropertyToID("_HoSSGISpatialRadius");
        public static readonly int HistoryValidId = Shader.PropertyToID("_HoSSGIHistoryValid");
        public static readonly int HistoryTextureId = Shader.PropertyToID("_HoSSGIHistory");
        public static readonly int HistoryDepthId = Shader.PropertyToID("_HoSSGIHistoryDepth");
        public static readonly int DenoisedHistoryId = Shader.PropertyToID("_HoSSGIDenoisedHistory");
        public static readonly int MotionVectorId = Shader.PropertyToID("_HoSSGIMotionVectors");
        public static readonly int MotionValidId = Shader.PropertyToID("_HoSSGIUseMotion");
        public static readonly int ReservoirReuseId = Shader.PropertyToID("_HoSSGIReservoirReuse");
        public static readonly int ReservoirValidationId = Shader.PropertyToID("_HoSSGIReservoirValidation");
        public static readonly int FireflyEnabledId = Shader.PropertyToID("_HoSSGIFireflyEnabled");
        public static readonly int RawGIInputId = Shader.PropertyToID("_HoSSGIRawGIInput");
        public static readonly int DebugModeId = Shader.PropertyToID("_HoSSGIDebugMode");
    }
}
