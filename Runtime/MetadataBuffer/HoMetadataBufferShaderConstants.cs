using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal static class HoMetadataBufferShaderConstants
    {
        public const string ShaderPassName = "HoMetadataBuffer";
        public const string SurfaceColorShaderPassName = "HoMetadataBufferSurfaceColor";

        public const string ActiveName = "_HoMetadataBufferActive";
        public const string SystemChannelMaskName = "_HoMetadataBufferSystemChannelMask";
        public const string MaskIdTextureName = "_HoMetadataBufferMaskIdTexture";
        public const string SurfaceDataTextureName = "_HoMetadataBufferSurfaceDataTexture";
        public const string Custom0TextureName = "_HoMetadataBufferMaterialCustom0_3Texture";
        public const string ObjectCustom0TextureName = "_HoMetadataBufferObjectCustom0_3Texture";
        public const string ObjectCustom1TextureName = "_HoMetadataBufferObjectCustom4_7Texture";
        public const string SurfaceColorTextureName = "_HoMetadataBufferSurfaceColorTexture";
        public const string DepthTextureName = "_HoMetadataBufferDepthTexture";
        public const string MBufferDepthTextureName = "_HoMetadataBufferMBufferDepthTexture";

        public const string MaskWeightName = "_HoMetadataBufferMaskWeight";
        public const string SystemWriteMaskName = "_HoMetadataBufferSystemWriteMask";
        public const string CustomWriteMaskName = "_HoMetadataBufferCustomWriteMask";
        public const string GroupIdName = "_HoMetadataBufferGroupId";
        public const string ObjectIdName = "_HoMetadataBufferObjectId";
        public const string MaterialClassName = "_HoMetadataBufferMaterialClass";
        public const string FlagsName = "_HoMetadataBufferFlags";
        public const string ThicknessName = "_HoMetadataBufferThickness";
        public const string CurvatureName = "_HoMetadataBufferCurvature";
        public const string TransmittanceHintName = "_HoMetadataBufferTransmittanceHint";
        public const string DebugColorName = "_HoMetadataBufferDebugColor";
        public const string CustomValues0Name = "_HoMetadataBufferCustomValues0";
        public const string ObjectCustomMaskName = "_HoMetadataBufferObjectCustomMask";

        public const string ClearShaderName = "Hidden/lilToon/URP/MetadataBuffer/Clear";
        public const string FallbackShaderName = "Hidden/lilToon/URP/MetadataBuffer/Fallback";
        public const string DebugShaderName = "Hidden/lilToon/URP/MetadataBuffer/DebugView";

        public static readonly ShaderTagId ShaderTagId = new ShaderTagId(ShaderPassName);
        public static readonly ShaderTagId SurfaceColorShaderTagId = new ShaderTagId(SurfaceColorShaderPassName);

        public static readonly int ActiveId = Shader.PropertyToID(ActiveName);
        public static readonly int SystemChannelMaskId = Shader.PropertyToID(SystemChannelMaskName);
        public static readonly int MaskIdTextureId = Shader.PropertyToID(MaskIdTextureName);
        public static readonly int SurfaceDataTextureId = Shader.PropertyToID(SurfaceDataTextureName);
        public static readonly int Custom0TextureId = Shader.PropertyToID(Custom0TextureName);
        public static readonly int ObjectCustom0TextureId = Shader.PropertyToID(ObjectCustom0TextureName);
        public static readonly int ObjectCustom1TextureId = Shader.PropertyToID(ObjectCustom1TextureName);
        public static readonly int SurfaceColorTextureId = Shader.PropertyToID(SurfaceColorTextureName);
        public static readonly int MBufferDepthTextureId = Shader.PropertyToID(MBufferDepthTextureName);
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
    }
}
