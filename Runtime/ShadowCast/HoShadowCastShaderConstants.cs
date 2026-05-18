using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal static class HoShadowCastShaderConstants
    {
        public const int MaxDirectionalLights = 4;
        public const int MaxSpotLights = 4;
        public const int MaxPointLights = 4;
        public const int MaxLights = MaxDirectionalLights + MaxSpotLights + MaxPointLights;
        public const int MaxShadowSlices = MaxDirectionalLights + MaxSpotLights + MaxPointLights * 6;

        public const string AtlasTextureName = "_HoShadowCastAtlas";
        public const string CastingPunctualKeywordName = "_CASTING_PUNCTUAL_LIGHT_SHADOW";

        public static readonly int AtlasTextureId = Shader.PropertyToID(AtlasTextureName);
        public static readonly int ActiveId = Shader.PropertyToID("_HoShadowCastActive");
        public static readonly int LightCountId = Shader.PropertyToID("_HoShadowCastLightCount");
        public static readonly int SliceCountId = Shader.PropertyToID("_HoShadowCastSliceCount");
        public static readonly int AtlasSizeId = Shader.PropertyToID("_HoShadowCastAtlasSize");
        public static readonly int WorldToShadowId = Shader.PropertyToID("_HoShadowCastWorldToShadow");
        public static readonly int LightData0Id = Shader.PropertyToID("_HoShadowCastLightData0");
        public static readonly int LightData1Id = Shader.PropertyToID("_HoShadowCastLightData1");
        public static readonly int LightData2Id = Shader.PropertyToID("_HoShadowCastLightData2");
        public static readonly int LightColorId = Shader.PropertyToID("_HoShadowCastLightColor");
        public static readonly int SliceDataId = Shader.PropertyToID("_HoShadowCastSliceData");

        public static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        public static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        public static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");
        public static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
        public static readonly int WorldToCameraMatrixId = Shader.PropertyToID("unity_WorldToCamera");
        public static readonly int CameraToWorldMatrixId = Shader.PropertyToID("unity_CameraToWorld");

        public static readonly GlobalKeyword CastingPunctualLightShadowKeyword = GlobalKeyword.Create(CastingPunctualKeywordName);
    }
}
