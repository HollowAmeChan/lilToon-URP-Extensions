using UnityEngine;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    internal static class HoSubsurfaceScatteringShaderConstants
    {
        public const string ShaderName = "Hidden/lilToon/URP/HoSubsurfaceScattering";
        public const string DebugShaderName = "Hidden/lilToon/URP/HoSubsurfaceScattering/DebugView";
        public const string SourceTextureName = "_lilHoSSSSourceTexture";
        public const string DiffusionTextureName = "_lilHoSSSDiffusedTexture";
        public const string TransmissionTextureName = "_lilHoSSSTransmissionTexture";
        public const string TransmissionTempTextureName = "_lilHoSSSTransmissionTempTexture";
        public const string CompositeSourceTextureName = "_lilHoSSSCompositeSourceTexture";

        public static readonly int SourceTextureId = Shader.PropertyToID(SourceTextureName);
        public static readonly int DiffusionTextureId = Shader.PropertyToID(DiffusionTextureName);
        public static readonly int TransmissionTextureId = Shader.PropertyToID(TransmissionTextureName);
        public static readonly int TransmissionTempTextureId = Shader.PropertyToID(TransmissionTempTextureName);
        public static readonly int CompositeSourceTextureId = Shader.PropertyToID(CompositeSourceTextureName);
        public static readonly int ParamsId = Shader.PropertyToID("_lilHoSSSParams");
        public static readonly int GateParamsId = Shader.PropertyToID("_lilHoSSSGateParams");
        public static readonly int ColorId = Shader.PropertyToID("_lilHoSSSColor");
        public static readonly int TransmissionParamsId = Shader.PropertyToID("_lilHoSSSTransmissionParams");
        public static readonly int TransmissionColorId = Shader.PropertyToID("_lilHoSSSTransmissionColor");
        public static readonly int TransmissionShapeParamsId = Shader.PropertyToID("_lilHoSSSTransmissionShapeParams");
        public static readonly int CompositeParamsId = Shader.PropertyToID("_lilHoSSSCompositeParams");
        public static readonly int DebugParamsId = Shader.PropertyToID("_lilHoSSSDebugParams");
        public static readonly int ProfileIdsId = Shader.PropertyToID("_lilHoSSSProfileIds");
        public static readonly int ProfileDiffusionParamsId = Shader.PropertyToID("_lilHoSSSProfileDiffusionParams");
        public static readonly int ProfileTransmissionParamsId = Shader.PropertyToID("_lilHoSSSProfileTransmissionParams");
        public static readonly int ProfileShapeParamsId = Shader.PropertyToID("_lilHoSSSProfileShapeParams");
        public static readonly int DirectionId = Shader.PropertyToID("_lilHoSSSDirection");
        public static readonly int SourceTexelSizeId = Shader.PropertyToID("_lilHoSSSSourceTexelSize");
    }
}
