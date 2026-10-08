// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../ThirdParty/RTXDI/LICENSE.txt.
// Application adaptation of UnityPathTracing's RAB_Surface / RAB_LightInfo /
// RAB_VisibilityTest and ShadingHelpers. SDK resampling remains in ThirdParty/RTXDI.
#ifndef HO_RTDI_APPLICATION_BRIDGE_INCLUDED
#define HO_RTDI_APPLICATION_BRIDGE_INCLUDED
#include "../../RayTracing/Shaders/HoRTPrimarySurface.hlsl"
#include "../../RayTracing/Shaders/HoRTSceneQuery.hlsl"
#include "../ThirdParty/donut/brdf.hlsli"
#ifndef HO_RTDI_PRESAMPLING_ENABLED
#define HO_RTDI_PRESAMPLING_ENABLED 0
#endif
#define RTXDI_ENABLE_PRESAMPLING HO_RTDI_PRESAMPLING_ENABLED
#define RTXDI_REGIR_MODE 0
#define RTXDI_ALLOWED_BIAS_CORRECTION 3
#include "../ThirdParty/RTXDI/RtxdiParameters.h"
#include "../ThirdParty/RTXDI/Utils/RandomSamplerState.hlsl"
#if RTXDI_ENABLE_PRESAMPLING
#include "../ThirdParty/RTXDI/LightSampling/RISBufferSegmentParameters.h"
RWStructuredBuffer<uint2> _HoRTDIRISBuffer;
#define RTXDI_RIS_BUFFER _HoRTDIRISBuffer
float4 _HoRTDIRISLayout; // tile size, tile count, power enabled, reserved
#endif

float4 _HoRTDIFrame; // width, height, total light count, DI local region count (unused by GI)
float4 _HoRTDISettings; // initial samples, spatial samples, spatial radius, ray bias
float4 _HoRTDIFlags; // temporal history valid, spatial enabled, max history, reserved
float4 _HoRTDIReservoirLayout;
int _HoRTDIFrameIndex;
float4x4 _HoRTDIPreviousInverseVP, _HoRTDIPreviousVP;
float4 _HoRTDIPreviousCameraPosition, _HoRTDIPreviousTowardsEye, _HoRTDIPreviousViewZ;
float4 _HoRTDIPreviousClipParameters;
struct HoRTDILight { float4 positionRange, radianceType, directionOuter, innerShadow; };
StructuredBuffer<HoRTDILight> _HoRTDILights;
#ifndef HO_RTDI_LIGHT_HISTORY_ENABLED
#define HO_RTDI_LIGHT_HISTORY_ENABLED 1
#endif
#if HO_RTDI_LIGHT_HISTORY_ENABLED
StructuredBuffer<HoRTDILight> _HoRTDIPreviousLights;
StructuredBuffer<int> _HoRTDICurrentToPrevious, _HoRTDIPreviousToCurrent;
int _HoRTDIPreviousLightCount;
#endif
RWStructuredBuffer<uint4> _HoRTDIHistory;
StructuredBuffer<float2> _HoRTDINeighbors;
RWStructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDIReservoirs;
#define RTXDI_LIGHT_RESERVOIR_BUFFER _HoRTDIReservoirs
#define RTXDI_NEIGHBOR_OFFSETS_BUFFER _HoRTDINeighbors

struct RAB_Material { float3 diffuseAlbedo, specularF0; float roughness; };
struct RAB_Surface { float3 worldPos, normal, geoNormal, viewDir; float viewDepth; RAB_Material material; };
struct RAB_LightInfo { HoRTDILight data; };
struct RAB_LightSample { float3 position, radiance; float solidAnglePdf; };
RAB_Surface RAB_EmptySurface() { return (RAB_Surface)0; }
RAB_LightInfo RAB_EmptyLightInfo() { return (RAB_LightInfo)0; }
RAB_LightSample RAB_EmptyLightSample() { return (RAB_LightSample)0; }
bool RAB_IsSurfaceValid(RAB_Surface s) { return s.viewDepth > 0; }
float3 RAB_GetSurfaceWorldPos(RAB_Surface s) { return s.worldPos; }
float3 RAB_GetSurfaceNormal(RAB_Surface s) { return s.normal; }
float RAB_GetSurfaceLinearDepth(RAB_Surface s) { return s.viewDepth; }
RAB_Material RAB_GetMaterial(RAB_Surface s) { return s.material; }
uint HoDIPack(float4 v) { uint4 b=(uint4)round(saturate(v)*255);return b.x|(b.y<<8)|(b.z<<16)|(b.w<<24); }
float4 HoDIUnpack(uint v) { return float4(v&255,(v>>8)&255,(v>>16)&255,v>>24)/255.0; }
float HoDILuminance(float3 v) { return dot(v,float3(.2126,.7152,.0722)); }
RAB_Surface RAB_GetGBufferSurface(int2 pixel, bool previousFrame)
{
    RAB_Surface s=RAB_EmptySurface();
    if(any(pixel<0)||any(pixel >= (int2)_HoRTDIFrame.xy))return s;
    if(previousFrame)
    {
        if(_HoRTDIFlags.x<.5)return s;
        uint4 h=_HoRTDIHistory[pixel.y*(uint)_HoRTDIFrame.x+pixel.x];
        if((h.z>>24)==0)return s;
        float raw=asfloat(h.x);float2 uv=(float2(pixel)+.5)/_HoRTDIFrame.xy;
        #if !UNITY_REVERSED_Z
            raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
        #endif
        s.worldPos=ComputeWorldSpacePosition(uv,raw,_HoRTDIPreviousInverseVP);
        float4 normals=HoDIUnpack(h.y);
        s.normal=HoSurfaceOctDecode(normals.xy);s.geoNormal=HoSurfaceOctDecode(normals.zw);
        s.viewDepth=-dot(_HoRTDIPreviousViewZ,float4(s.worldPos,1));
        s.viewDir=_HoRTDIPreviousClipParameters.z>.5?normalize(_HoRTDIPreviousTowardsEye.xyz):normalize(_HoRTDIPreviousCameraPosition.xyz-s.worldPos);
        s.material.diffuseAlbedo=HoDIUnpack(h.z).rgb;
        float4 specular=HoDIUnpack(h.w);s.material.specularF0=specular.rgb;s.material.roughness=specular.a;
        return s;
    }
    HoRTPrimarySurface p;
    if(!HoRTTryLoadPrimarySurface((uint2)pixel,p))return s;
    s.worldPos=p.positionWS;s.normal=p.shadingNormalWS;s.geoNormal=p.geometricNormalWS;s.viewDir=p.viewDirectionWS;s.viewDepth=p.eyeDepth;
    // Explicit PBR reference response, not a change to the raw primary input or NPR reception.
    float3 base=saturate(p.baseColor);
    s.material.diffuseAlbedo=HoDIUnpack(HoDIPack(float4(base*(1-p.metallic),1))).rgb;
    float4 specular=HoDIUnpack(HoDIPack(float4(lerp(p.reflectance.xxx,base,p.metallic),p.perceptualRoughness)));
    s.material.specularF0=specular.rgb;s.material.roughness=specular.a;
    return s;
}
bool RAB_AreMaterialsSimilar(RAB_Material a,RAB_Material b)
{
    return RTXDI_CompareRelativeDifference(a.roughness,b.roughness,.5)
        && abs(HoDILuminance(a.specularF0)-HoDILuminance(b.specularF0))<.25
        && abs(HoDILuminance(a.diffuseAlbedo)-HoDILuminance(b.diffuseAlbedo))<.25;
}
RAB_LightInfo RAB_LoadLightInfo(uint index,bool previousFrame)
{
    RAB_LightInfo result=RAB_EmptyLightInfo();
    #if HO_RTDI_LIGHT_HISTORY_ENABLED
    if(previousFrame)
    {if(index<(uint)_HoRTDIPreviousLightCount)result.data=_HoRTDIPreviousLights[index];return result;}
    #endif
    if(index<(uint)_HoRTDIFrame.z)result.data=_HoRTDILights[index];return result;
}
RAB_LightInfo RAB_LoadCompactLightInfo(uint index) { return RAB_EmptyLightInfo(); }
// Compact-light copies are optional in the SDK. Keep the canonical light
// buffer as the single source; RIS entries store an index and inverse PDF.
bool RAB_StoreCompactLightInfo(uint index,RAB_LightInfo light) { return false; }
bool RAB_IsAnalyticLightSample(RAB_LightSample sample) { return true; }
float RAB_LightSampleSolidAnglePdf(RAB_LightSample sample) { return sample.solidAnglePdf; }
void RAB_GetLightDirDistance(RAB_Surface surface,RAB_LightSample sample,out float3 direction,out float distance)
{float3 delta=sample.position-surface.worldPos;distance=length(delta);direction=delta/max(distance,1e-12);}
RAB_LightSample RAB_SamplePolymorphicLight(RAB_LightInfo info,RAB_Surface surface,float2 uv)
{
    RAB_LightSample sample=RAB_EmptyLightSample();HoRTDILight light=info.data;
    if(light.radianceType.w>1.5)
    {sample.position=surface.worldPos-light.directionOuter.xyz*100000;sample.radiance=light.radianceType.rgb;sample.solidAnglePdf=1;return sample;}
    float3 vector=light.positionRange.xyz-surface.worldPos;float d2=dot(vector,vector);
    if(d2<1e-12||d2>=light.positionRange.w*light.positionRange.w)return sample;
    sample.position=light.positionRange.xyz;
    // Reference PolymorphicLight::PointLight's delta-light limit, with URP range/spot adaptation.
    sample.radiance=light.radianceType.rgb/d2;sample.solidAnglePdf=1;
    float fade=saturate(1-square(d2/max(1e-12,light.positionRange.w*light.positionRange.w)));
    sample.radiance*=fade*fade;
    if(light.radianceType.w>.5)
    {
        float angle=dot(-normalize(vector),light.directionOuter.xyz);
        float cone=saturate((angle-light.directionOuter.w)/max(1e-5,light.innerShadow.x-light.directionOuter.w));
        sample.radiance*=cone*cone;
    }
    return sample;
}
float3 RAB_SurfaceEvaluateBrdfTimesNoL(RAB_Surface s,float3 L)
{
    if(dot(L,s.geoNormal)<=0)return 0;
    float d=Lambert(s.normal,-L);float3 specular=0;
    if(s.material.roughness>=.03)specular=GGX_times_NdotL(s.viewDir,L,s.normal,max(s.material.roughness,.03),s.material.specularF0);
    return d*s.material.diffuseAlbedo+specular;
}
float RAB_GetLightSampleTargetPdfForSurface(RAB_LightSample sample,RAB_Surface surface)
{float3 direction;float distance;RAB_GetLightDirDistance(surface,sample,direction,distance);return max(0,HoDILuminance(RAB_SurfaceEvaluateBrdfTimesNoL(surface,direction)*sample.radiance))/max(sample.solidAnglePdf,1e-12);}
bool RAB_GetConservativeVisibility(RAB_Surface surface,RAB_LightSample sample)
{
    float3 direction;float distance;RAB_GetLightDirDistance(surface,sample,direction,distance);
    float bias=_HoRTDISettings.w;if(distance<=bias*2)return false;
    HoRTSceneHit hit=HoRTTraceScene(surface.worldPos,direction,bias,distance-bias,255);
    return hit.status==0;
}
bool RAB_GetTemporalConservativeVisibility(RAB_Surface current,RAB_Surface previous,RAB_LightSample sample)
// Geometry changes reject history, so current AS also represents the previous
// world. The sample carries the old light, and must be tested from the old surface.
{return RAB_GetConservativeVisibility(previous,sample);}
int RAB_TranslateLightIndex(uint index,bool currentToPrevious)
{
    #if HO_RTDI_LIGHT_HISTORY_ENABLED
    if(currentToPrevious)return index<(uint)_HoRTDIFrame.z?_HoRTDICurrentToPrevious[index]:-1;
    return index<(uint)_HoRTDIPreviousLightCount?_HoRTDIPreviousToCurrent[index]:-1;
    #else
    return index<(uint)_HoRTDIFrame.z?(int)index:-1;
    #endif
}
int2 RAB_ClampSamplePositionIntoView(int2 pixel,bool previous)
{
    int2 size=(int2)_HoRTDIFrame.xy;
    if(pixel.x<0)pixel.x=-pixel.x;if(pixel.y<0)pixel.y=-pixel.y;
    if(pixel.x>=size.x)pixel.x=2*size.x-pixel.x-1;if(pixel.y>=size.y)pixel.y=2*size.y-pixel.y-1;
    return pixel;
}
// Disabled by initial-sampling parameters: no environment, mesh-light or BRDF light candidates in this slice.
float RAB_EvaluateLocalLightSourcePdf(uint index) {return rcp(max(1,_HoRTDIFrame.z));}
float RAB_EvaluateEnvironmentMapSamplingPdf(float3 direction) {return 0;}
float2 RAB_GetEnvironmentMapRandXYFromDir(float3 direction) {return 0;}
float RAB_SurfaceEvaluateBrdfPdf(RAB_Surface surface,float3 direction) {return max(0,dot(surface.normal,direction))/M_PI;}
bool RAB_SurfaceImportanceSampleBrdf(RAB_Surface surface,inout RTXDI_RandomSamplerState rng,out float3 direction) {direction=0;return false;}
bool RAB_TraceRayForLocalLight(float3 origin,float3 direction,float tMin,float tMax,out uint lightIndex,out float2 uv) {lightIndex=RTXDI_InvalidLightIndex;uv=0;return false;}
#endif
