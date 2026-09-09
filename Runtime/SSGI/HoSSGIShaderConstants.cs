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
        public const string OcclusionAuxName = "_HoSSGIOcclusionAux";
        public const string OcclusionRayName = "_HoSSGIOcclusionRay";
        public const string OcclusionHistoryAuxName = "_HoSSGIOcclusionHistoryAux";
        public const string OcclusionHistoryRayName = "_HoSSGIOcclusionHistoryRay";
        public const string ReservoirHistoryColorName = "_HoSSGIReservoirHistoryColor";
        public const string ReservoirHistoryAuxName = "_HoSSGIReservoirHistoryAux";
        public const string ReservoirHistoryRayName = "_HoSSGIReservoirHistoryRay";
        public const string ReservoirOutputColorName = "_HoSSGIReservoirOutputColor";
        public const string ReservoirOutputAuxName = "_HoSSGIReservoirOutputAux";
        public const string ReservoirOutputRayName = "_HoSSGIReservoirOutputRay";
        public const string CameraSourceName = "_HoSSGICameraSource";
        public const string SampleCountHistoryName = "_HoSSGISampleCountHistory";
        public const string InvalidityHistoryName = "_HoSSGIInvalidityHistory";
        public const string DepthPyramidMip0Name = "_HoSSGIDepthPyramidMip0";
        public const string DepthPyramidMip1Name = "_HoSSGIDepthPyramidMip1";
        public const string DepthPyramidMip2Name = "_HoSSGIDepthPyramidMip2";
        public const string DepthPyramidMip3Name = "_HoSSGIDepthPyramidMip3";
        public const string DepthPyramidMip4Name = "_HoSSGIDepthPyramidMip4";
        public static readonly int GITextureId = Shader.PropertyToID(GITextureName);
        public static readonly int RawGIId = Shader.PropertyToID(RawGITextureName);
        public static readonly int ReservoirColorId = Shader.PropertyToID(ReservoirColorName);
        public static readonly int ReservoirAuxId = Shader.PropertyToID(ReservoirAuxName);
        public static readonly int ReservoirRayId = Shader.PropertyToID(ReservoirRayName);
        public static readonly int OcclusionAuxId = Shader.PropertyToID(OcclusionAuxName);
        public static readonly int OcclusionRayId = Shader.PropertyToID(OcclusionRayName);
        public static readonly int OcclusionHistoryAuxId = Shader.PropertyToID(OcclusionHistoryAuxName);
        public static readonly int OcclusionHistoryRayId = Shader.PropertyToID(OcclusionHistoryRayName);
        public static readonly int ReservoirHistoryColorId = Shader.PropertyToID(ReservoirHistoryColorName);
        public static readonly int ReservoirHistoryAuxId = Shader.PropertyToID(ReservoirHistoryAuxName);
        public static readonly int ReservoirHistoryRayId = Shader.PropertyToID(ReservoirHistoryRayName);
        public static readonly int ReservoirOutputColorId = Shader.PropertyToID(ReservoirOutputColorName);
        public static readonly int ReservoirOutputAuxId = Shader.PropertyToID(ReservoirOutputAuxName);
        public static readonly int ReservoirOutputRayId = Shader.PropertyToID(ReservoirOutputRayName);
        public static readonly int CameraSourceId = Shader.PropertyToID(CameraSourceName);
        public static readonly int SampleCountHistoryId = Shader.PropertyToID(SampleCountHistoryName);
        public static readonly int InvalidityHistoryId = Shader.PropertyToID(InvalidityHistoryName);
        public static readonly int CurrentInvalidityId = Shader.PropertyToID("_HoSSGICurrentInvalidity");
        public static readonly int AOTextureId = Shader.PropertyToID("_HoAOTexture");
        public static readonly int UseAOId = Shader.PropertyToID("_HoSSGIUseAO");
        public static readonly int DepthPyramidMip0Id = Shader.PropertyToID(DepthPyramidMip0Name);
        public static readonly int DepthPyramidMip1Id = Shader.PropertyToID(DepthPyramidMip1Name);
        public static readonly int DepthPyramidMip2Id = Shader.PropertyToID(DepthPyramidMip2Name);
        public static readonly int DepthPyramidMip3Id = Shader.PropertyToID(DepthPyramidMip3Name);
        public static readonly int DepthPyramidMip4Id = Shader.PropertyToID(DepthPyramidMip4Name);
        public static readonly int DepthPyramidTexelSizeId = Shader.PropertyToID("_HoSSGIDepthPyramidTexelSize");
        public static readonly int[] DepthPyramidIds =
        {
            DepthPyramidMip0Id,
            DepthPyramidMip1Id,
            DepthPyramidMip2Id,
            DepthPyramidMip3Id,
            DepthPyramidMip4Id
        };
        public static readonly int GeometryId = Shader.PropertyToID("_HoSSGIGeometry");
        public static readonly int SourceId = Shader.PropertyToID("_HoSSGISource");
        public static readonly int SourceHistoryId = Shader.PropertyToID("_HoSSGISourceHistory");
        public static readonly int RayCountId = Shader.PropertyToID("_HoSSGIRayCount");
        public static readonly int StepCountId = Shader.PropertyToID("_HoSSGIStepCount");
        public static readonly int RayLengthId = Shader.PropertyToID("_HoSSGIRayLength");
        public static readonly int ThicknessId = Shader.PropertyToID("_HoSSGIThickness");
        public static readonly int IntensityId = Shader.PropertyToID("_HoSSGIIntensity");
        public static readonly int SourceSaturationId = Shader.PropertyToID("_HoSSGISourceSaturation");
        public static readonly int FrameIndexId = Shader.PropertyToID("_HoSSGIFrameIndex");
        public static readonly int PreviousInverseViewProjectionId = Shader.PropertyToID("_HoSSGIPreviousInverseViewProjection");
        public static readonly int PreviousMatrixValidId = Shader.PropertyToID("_HoSSGIPreviousMatrixValid");
        public static readonly int TemporalBlendId = Shader.PropertyToID("_HoSSGITemporalBlend");
        public static readonly int SpatialRadiusId = Shader.PropertyToID("_HoSSGISpatialRadius");
        public static readonly int SpatialGuidanceId = Shader.PropertyToID("_HoSSGISpatialGuidance");
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
