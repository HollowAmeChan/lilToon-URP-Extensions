using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal static class HoMetadataBufferShaderConstants
    {
        public const string ShaderPassName = "HoAOV";
        public const string SssShaderPassName = "HoAOVSSS";

        public const string ActiveName = "_lilHoAovActive";
        public const string SystemChannelMaskName = "_lilHoAovSystemChannelMask";
        public const string MaskIdTextureName = "_lilHoAovMaskIdTexture";
        public const string NormalDepthTextureName = "_lilHoAovNormalDepthTexture";
        public const string SurfaceDataTextureName = "_lilHoAovSurfaceDataTexture";
        public const string Custom0TextureName = "_lilHoAovCustom0_3Texture";
        public const string ObjectCustom0TextureName = "_lilHoAovObjectCustom0_3Texture";
        public const string ObjectCustom1TextureName = "_lilHoAovObjectCustom4_7Texture";
        public const string SssTextureName = "_lilHoAovSssTexture";
        public const string DepthTextureName = "_lilHoAovDepthTexture";

        public const string MaskWeightName = "_HoAovMaskWeight";
        public const string SystemWriteMaskName = "_HoAovSystemWriteMask";
        public const string CustomWriteMaskName = "_HoAovCustomWriteMask";
        public const string GroupIdName = "_HoMetadataBufferGroupId";
        public const string ObjectIdName = "_HoAovObjectId";
        public const string MaterialClassName = "_HoAovMaterialClass";
        public const string FlagsName = "_HoAovFlags";
        public const string ThicknessName = "_HoAovThickness";
        public const string CurvatureName = "_HoAovCurvature";
        public const string TransmittanceHintName = "_HoAovTransmittanceHint";
        public const string DebugColorName = "_HoAovDebugColor";
        public const string CustomValues0Name = "_HoAovCustomValues0";
        public const string ObjectCustomMaskName = "_HoAovObjectCustomMask";

        public const string ClearShaderName = "Hidden/lilToon/URP/MetadataBuffer/Clear";
        public const string FallbackShaderName = "Hidden/lilToon/URP/MetadataBuffer/Fallback";
        public const string DebugShaderName = "Hidden/lilToon/URP/MetadataBuffer/DebugView";

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly ShaderTagId SssShaderTagId = new ShaderTagId(SssShaderPassName);

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int SystemChannelMaskId = Shader.PropertyToID(SystemChannelMaskName);
        public static readonly int MaskIdTextureId = Shader.PropertyToID(MaskIdTextureName);
        public static readonly int NormalDepthTextureId = Shader.PropertyToID(NormalDepthTextureName);
        public static readonly int SurfaceDataTextureId = Shader.PropertyToID(SurfaceDataTextureName);
        public static readonly int Custom0TextureId = Shader.PropertyToID(Custom0TextureName);
        public static readonly int ObjectCustom0TextureId = Shader.PropertyToID(ObjectCustom0TextureName);
        public static readonly int ObjectCustom1TextureId = Shader.PropertyToID(ObjectCustom1TextureName);
        public static readonly int SssTextureId = Shader.PropertyToID(SssTextureName);
        public static readonly int MaskWeightId = Shader.PropertyToID(MaskWeightName);
        public static readonly int SystemWriteMaskId = Shader.PropertyToID(SystemWriteMaskName);
        public static readonly int CustomWriteMaskId = Shader.PropertyToID(CustomWriteMaskName);
        public static readonly int GroupIdId = Shader.PropertyToID(GroupIdName);
        public static readonly int ObjectIdId = Shader.PropertyToID(ObjectIdName);
        public static readonly int MaterialClassId = Shader.PropertyToID(MaterialClassName);
        public static readonly int FlagsId = Shader.PropertyToID(FlagsName);
        public static readonly int ThicknessId = Shader.PropertyToID(ThicknessName);
        public static readonly int CurvatureId = Shader.PropertyToID(CurvatureName);
        public static readonly int TransmittanceHintId = Shader.PropertyToID(TransmittanceHintName);
        public static readonly int DebugColorId = Shader.PropertyToID(DebugColorName);
        public static readonly int CustomValues0Id = Shader.PropertyToID(CustomValues0Name);
        public static readonly int ObjectCustomMaskId = Shader.PropertyToID(ObjectCustomMaskName);

        public static readonly int DebugModeId = Shader.PropertyToID("_HoMetadataBufferDebugMode");
        public static readonly int DebugDepthParamsId = Shader.PropertyToID("_HoMetadataBufferDebugDepthParams");
    }
}
