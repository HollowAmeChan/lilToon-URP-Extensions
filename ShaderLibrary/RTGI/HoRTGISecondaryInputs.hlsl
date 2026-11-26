#ifndef HO_RTGI_SECONDARY_INPUTS_INCLUDED
#define HO_RTGI_SECONDARY_INPUTS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "UnityRayTracingMeshUtils.cginc"
#include "HoRTGISecondarySurface.hlsl"

Texture2D<float4> _MainTex,_AlphaMask,_BumpMap,_PBRMap,_MetallicGlossMap,_ReflectanceTex,_SmoothnessTex;
Texture2D<float4> _EmissionMap,_EmissionBlendMask,_EmissionGradTex;
Texture2D<float4> _HoTesionStretchBaseColor,_HoTesionCompressionBaseColor,_HoTesionStretchNormal,_HoTesionCompressionNormal;
SamplerState sampler_MainTex,sampler_AlphaMask,sampler_BumpMap,sampler_EmissionMap;
SamplerState sampler_PBRMap,sampler_MetallicGlossMap,sampler_ReflectanceTex,sampler_SmoothnessTex;
SamplerState sampler_HoTesionStretchBaseColor,sampler_HoTesionCompressionBaseColor,sampler_HoTesionStretchNormal,sampler_HoTesionCompressionNormal;
float4 _MainTex_ST,_AlphaMask_ST,_BumpMap_ST,_PBRMap_ST,_MetallicGlossMap_ST,_ReflectanceTex_ST,_SmoothnessTex_ST;
float4 _Color,_EmissionColor,_HoRTColorAdjustment,_HoRTPBRFlags,_HoRTInstanceUserValue;
float4 _EmissionMap_ST,_EmissionBlendMask_ST,_EmissionMap_ScrollRotate,_EmissionBlendMask_ScrollRotate,_EmissionBlink;
float _HoRTGISecondarySupported,_HoRTGICutout,_HoRTMaterialFamily,_HoRTAlphaDomain,_HoRTPBRMode;
float _HoRTMetallicMapScale,_HoRTSmoothnessMapScale,_HoRTNormalEnabled,_HoRTNormalPolicy,_HoRTEmissionMode,_HoRTTensionEnabled;
float _AlphaMaskMode,_AlphaMaskScale,_AlphaMaskValue,_Cutoff,_Metallic,_Reflectance,_Smoothness,_BumpScale,_FlipNormal;
float _EmissionMap_UVMode,_EmissionBlend,_EmissionUseGrad,_EmissionGradSpeed,_EmissionMainStrength,_EmissionParallaxDepth;
float _HoTesionStrength,_HoTesionStretchStrength,_HoTesionCompressionStrength,_HoTesionStretchNormalScale,_HoTesionCompressionNormalScale;
float4 _HoTesionMaps,_HoTesionStretchColor,_HoTesionCompressionColor,_HoTesionStretchRange,_HoTesionCompressionRange;
float4 _HoTesionStretchBaseColor_ST,_HoTesionCompressionBaseColor_ST;
struct HoGIAttributes { float2 barycentrics; };
float2 HoGIUV(uint3 tri,float3 weights,uint attribute)
{
    return UnityRayTracingFetchVertexAttribute2(tri.x,attribute)*weights.x
         + UnityRayTracingFetchVertexAttribute2(tri.y,attribute)*weights.y
         + UnityRayTracingFetchVertexAttribute2(tri.z,attribute)*weights.z;
}
float2 HoGITransformUV(float2 uv,float4 st){return uv*st.xy+st.zw;}
float3 HoGISafeNormal(float3 value,float3 fallback)
{
    float lengthSquared=dot(value,value);
    return lengthSquared>1e-12&&lengthSquared<1e30?value*rsqrt(lengthSquared):fallback;
}
#endif
