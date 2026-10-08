// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../../RTDI/ThirdParty/RTXDI/LICENSE.txt.
#ifndef HO_RTGI_APPLICATION_BRIDGE_INCLUDED
#define HO_RTGI_APPLICATION_BRIDGE_INCLUDED
// Reuse the already adapted primary surface, light sampling and visibility RAB.
// The DI and GI features bind independent resources to these shader-local names.
// GI only uses DI initial sampling at secondary hits, not DI light history.
#define HO_RTDI_LIGHT_HISTORY_ENABLED 0
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
#include "../../RTDI/Shaders/HoRTDIApplicationBridge.hlsl"
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
