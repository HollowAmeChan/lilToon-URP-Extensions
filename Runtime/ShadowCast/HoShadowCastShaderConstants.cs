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
        public const int MaxSecondDirectionalCascades = 4;
        public const int MaxSecondDirectionalSlices = MaxDirectionalLights * MaxSecondDirectionalCascades;

        public const string AtlasTextureName = "_HoShadowCastAtlas";
        public const string SecondDirectionalAtlasTextureName = "_HoShadowCastSecondDirectionalAtlas";
        public const string DebugShaderName = "Hidden/lilToon-HoShadowCast/URP/DebugView";
        public const string CastingPunctualKeywordName = "_CASTING_PUNCTUAL_LIGHT_SHADOW";

        public static readonly int AtlasTextureId = Shader.PropertyToID(AtlasTextureName);
        public static readonly int SecondDirectionalAtlasTextureId = Shader.PropertyToID(SecondDirectionalAtlasTextureName);
        public static readonly int ActiveId = Shader.PropertyToID("_HoShadowCastActive");
        public static readonly int LightCountId = Shader.PropertyToID("_HoShadowCastLightCount");
        public static readonly int SliceCountId = Shader.PropertyToID("_HoShadowCastSliceCount");
        public static readonly int AtlasSizeId = Shader.PropertyToID("_HoShadowCastAtlasSize");
        public static readonly int WorldToShadowRow0Id = Shader.PropertyToID("_HoShadowCastWorldToShadowRow0");
        public static readonly int WorldToShadowRow1Id = Shader.PropertyToID("_HoShadowCastWorldToShadowRow1");
        public static readonly int WorldToShadowRow2Id = Shader.PropertyToID("_HoShadowCastWorldToShadowRow2");
        public static readonly int WorldToShadowRow3Id = Shader.PropertyToID("_HoShadowCastWorldToShadowRow3");
        public static readonly int LightData0Id = Shader.PropertyToID("_HoShadowCastLightData0");
        public static readonly int LightData1Id = Shader.PropertyToID("_HoShadowCastLightData1");
        public static readonly int LightData2Id = Shader.PropertyToID("_HoShadowCastLightData2");
        public static readonly int LightAttenuationId = Shader.PropertyToID("_HoShadowCastLightAttenuation");
        public static readonly int LightColorId = Shader.PropertyToID("_HoShadowCastLightColor");
        public static readonly int SliceDataId = Shader.PropertyToID("_HoShadowCastSliceData");
        public static readonly int SecondDirectionalParamsId = Shader.PropertyToID("_HoShadowCastSecondDirectionalParams");
        public static readonly int SecondDirectionalCameraPositionId = Shader.PropertyToID("_HoShadowCastSecondDirectionalCameraPosition");
        public static readonly int SecondDirectionalAtlasSizeId = Shader.PropertyToID("_HoShadowCastSecondDirectionalAtlasSize");
        public static readonly int SecondDirectionalWorldToShadowRow0Id = Shader.PropertyToID("_HoShadowCastSecondDirectionalWorldToShadowRow0");
        public static readonly int SecondDirectionalWorldToShadowRow1Id = Shader.PropertyToID("_HoShadowCastSecondDirectionalWorldToShadowRow1");
        public static readonly int SecondDirectionalWorldToShadowRow2Id = Shader.PropertyToID("_HoShadowCastSecondDirectionalWorldToShadowRow2");
        public static readonly int SecondDirectionalWorldToShadowRow3Id = Shader.PropertyToID("_HoShadowCastSecondDirectionalWorldToShadowRow3");
        public static readonly int SecondDirectionalLightDataId = Shader.PropertyToID("_HoShadowCastSecondDirectionalLightData");
        public static readonly int SecondDirectionalSliceDataId = Shader.PropertyToID("_HoShadowCastSecondDirectionalSliceData");
        public static readonly int DebugModeId = Shader.PropertyToID("_HoShadowCastDebugMode");

        public static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        public static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        public static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");
        public static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
        public static readonly int WorldToCameraMatrixId = Shader.PropertyToID("unity_WorldToCamera");
        public static readonly int CameraToWorldMatrixId = Shader.PropertyToID("unity_CameraToWorld");

        public static readonly GlobalKeyword CastingPunctualLightShadowKeyword = GlobalKeyword.Create(CastingPunctualKeywordName);
    }
}
