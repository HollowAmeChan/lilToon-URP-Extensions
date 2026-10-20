// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../../RTDI/ThirdParty/RTXDI/LICENSE.txt.
#ifndef HO_RTGI_APPLICATION_BRIDGE_INCLUDED
#define HO_RTGI_APPLICATION_BRIDGE_INCLUDED
// Reuse the already adapted primary surface, light sampling and visibility RAB.
// The DI and GI features bind independent resources to these shader-local names.
// GI only uses DI initial sampling at secondary hits, not DI light history.
#define HO_RTDI_LIGHT_HISTORY_ENABLED 0
// Keep the shared light helpers' established GI proposal; DI's new sampling
// policy and its neighbor-compatibility guard belong to the DI path.
#define HO_RTDI_RGB_IMPORTANCE_ENABLED 0
#define HO_RTDI_ENVIRONMENT_ENABLED 1
#define HO_RTDI_PRESAMPLING_ENABLED 1
#define _HoRTDIEnvironment _HoRTGIEnvironment
#define _HoRTDIEnvironmentPdf _HoRTGIEnvironmentPdf
#define _HoRTDIEnvironmentPdfOutput _HoRTGIEnvironmentPdfOutput
#define _HoRTDIEnvironmentBrdfSamples _HoRTGIEnvironmentBrdfSamples
#define _HoRTDIRISBuffer _HoRTGIEnvironmentRIS
#define _HoRTDIRISLayout _HoRTGIEnvironmentRISLayout
#define _HoRTDIFrame _HoRTGIFrame
#define _HoRTDISettings _HoRTGISettings
#define _HoRTDIFlags _HoRTGIFlags
#define _HoRTDIReservoirLayout _HoRTGIReservoirLayout
#define _HoRTDIFrameIndex _HoRTGIFrameIndex
#define _HoRTDIPreviousInverseVP _HoRTGIPreviousInverseVP
#define _HoRTDIPreviousVP _HoRTGIPreviousVP
#define _HoRTDIPreviousCameraPosition _HoRTGIPreviousCameraPosition
#define _HoRTDIPreviousTowardsEye _HoRTGIPreviousTowardsEye
#define _HoRTDIPreviousViewZ _HoRTGIPreviousViewZ
#define _HoRTDIPreviousClipParameters _HoRTGIPreviousClipParameters
#define _HoRTDILights _HoRTGILights
#define _HoRTDIHistory _HoRTGIHistory
#define _HoRTDINeighbors _HoRTGINeighbors
// Keep the shared DI implementation intact. GI owns its lattice/history lookup.
#define RAB_GetGBufferSurface HoGIUnusedSharedGBufferSurface
#include "../../RTDI/Shaders/HoRTDIApplicationBridge.hlsl"
#undef RAB_GetGBufferSurface
#include "HoRTGIGrid.hlsl"
float4 _HoRTGIInputGrid;

RAB_Surface HoGIFromPrimary(HoRTPrimarySurface p)
{
    RAB_Surface s=RAB_EmptySurface();
    s.worldPos=p.positionWS;s.viewDir=p.viewDirectionWS;s.viewDepth=p.eyeDepth;
    // Canonicalize both frames identically, including a producer's FP16 fallback.
    s.normal=HoDIUnpackNormal(HoDIPackNormal(p.shadingNormalWS));s.geoNormal=HoDIUnpackNormal(HoDIPackNormal(p.geometricNormalWS));
    // Explicit PBR reference response, not a change to the raw primary input or NPR reception.
    float3 base=saturate(p.baseColor);
    s.material.diffuseAlbedo=HoDIUnpack(HoDIPack(float4(base*(1-p.metallic),1))).rgb;
    float4 specular=HoDIUnpack(HoDIPack(float4(lerp(p.reflectance.xxx,base,p.metallic),p.perceptualRoughness)));
    s.material.specularF0=specular.rgb;s.material.roughness=specular.a;
    return s;
}
RAB_Surface RAB_GetGBufferSurface(int2 pixel, bool previousFrame)
{
    RAB_Surface s=RAB_EmptySurface();
    if(any(pixel<0)||any(pixel >= (int2)_HoRTGIFrame.xy))return s;
    if(previousFrame)
    {
        if(_HoRTGIFlags.x<.5)return s;
        HoRTDIPrimaryHistory h=_HoRTGIHistory[pixel.y*(uint)_HoRTGIFrame.x+pixel.x];
        if((h.diffuse>>24)==0)return s;
        float raw=asfloat(h.depth);float2 uv=HoGIGridPrimaryUV((uint2)pixel,_HoRTGIInputGrid);
        #if !UNITY_REVERSED_Z
            raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
        #endif
        s.worldPos=ComputeWorldSpacePosition(uv,raw,_HoRTGIPreviousInverseVP);
        s.normal=HoDIUnpackNormal(h.shadingNormal);s.geoNormal=HoDIUnpackNormal(h.geometricNormal);
        s.viewDepth=-dot(_HoRTGIPreviousViewZ,float4(s.worldPos,1));
        s.viewDir=_HoRTGIPreviousClipParameters.z>.5?normalize(_HoRTGIPreviousTowardsEye.xyz):normalize(_HoRTGIPreviousCameraPosition.xyz-s.worldPos);
        s.material.diffuseAlbedo=HoDIUnpack(h.diffuse).rgb;
        float4 specular=HoDIUnpack(h.specular);s.material.specularF0=specular.rgb;s.material.roughness=specular.a;
        return s;
    }
    HoRTPrimarySurface p;
    if(!HoRTTryLoadPrimarySurface(HoGIGridPrimaryPixel((uint2)pixel,_HoRTGIInputGrid),p))return s;
    return HoGIFromPrimary(p);
}
#include "../../RTDI/ThirdParty/RTXDI/GI/ReSTIRGIParameters.h"
#include "HoRTGISecondaryData.hlsl"
RWStructuredBuffer<RTXDI_PackedGIReservoir> _HoRTGIReservoirs;
#define RTXDI_GI_RESERVOIR_BUFFER _HoRTGIReservoirs
RWStructuredBuffer<HoRTGISecondaryData> _HoRTGISecondary;

float3 HoGIEvaluateDiffuse(RAB_Surface surface,float3 position)
{
    float3 delta=position-surface.worldPos;float d2=dot(delta,delta);
    if(d2<1e-12)return 0;
    float3 L=delta*rsqrt(d2);
    return dot(L,surface.geoNormal)>0?Lambert(surface.normal,-L)*surface.material.diffuseAlbedo:0;
}
float RAB_GetGISampleTargetPdfForSurface(float3 position,float3 radiance,RAB_Surface surface)
{ return max(0,HoDILuminance(HoGIEvaluateDiffuse(surface,position)*radiance)); }
bool RAB_ValidateGISampleWithJacobian(inout float jacobian)
{
    // Same rejection and clamp as reference RAB_SpatialHelpers.
    if(!isfinite(jacobian)||jacobian>10||jacobian<.1)return false;
    jacobian=clamp(jacobian,1.0/3.0,3.0);return true;
}
bool RAB_GetConservativeVisibility(RAB_Surface surface,float3 position)
{
    float3 delta=position-surface.worldPos;float distance=length(delta);float bias=_HoRTGISettings.w;
    if(distance<=bias*2)return false;
    return HoRTTraceScene(surface.worldPos,delta/distance,bias,distance-bias,255).status==0;
}
bool RAB_GetTemporalConservativeVisibility(RAB_Surface current,RAB_Surface previous,float3 position)
// History is only reused for unchanged geometry: the current AS also describes
// the previous world, so it can test visibility from the previous surface.
{ return RAB_GetConservativeVisibility(previous,position); }
#endif
