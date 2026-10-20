// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../../RTDI/ThirdParty/RTXDI/LICENSE.txt.
// Same pinned SDK resampling and application callbacks for both backends.
#ifndef HO_RTGI_RESAMPLING_INCLUDED
#define HO_RTGI_RESAMPLING_INCLUDED
#include "HoRTGIShadingCommon.hlsl"
#include "../../RTDI/ThirdParty/RTXDI/GI/TemporalResampling.hlsl"
#include "../../RTDI/ThirdParty/RTXDI/GI/SpatialResampling.hlsl"
RTXDI_RuntimeParameters HoGIRuntime()
{RTXDI_RuntimeParameters p=(RTXDI_RuntimeParameters)0;p.neighborOffsetMask=255;p.frameIndex=(uint)_HoRTGIFrameIndex;return p;}

void HoGITemporal(uint2 pixel)
{
    if(!HoGIInside(pixel))return;
    RTXDI_GIReservoir reservoir=RTXDI_LoadGIReservoir(HoGILayout(),pixel,1);
    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);
    if(_HoRTGIFlags.x>.5&&RAB_IsSurfaceValid(surface))
    {
        float4 clip=mul(_HoRTGIPreviousVP,float4(surface.worldPos,1));
        float previousDepth=-dot(_HoRTGIPreviousViewZ,float4(surface.worldPos,1));
        if(all(isfinite(clip))&&clip.w>1e-6&&previousDepth>0)
        {
            float2 uv=clip.xy/clip.w*float2(.5,-.5)+.5;
            float3 motion=float3(uv*_HoRTGIFrame.xy-.5-float2(pixel),previousDepth-surface.viewDepth);
            RTXDI_GITemporalResamplingParameters p=(RTXDI_GITemporalResamplingParameters)0;
            p.depthThreshold=.1;p.normalThreshold=.9;p.maxHistoryLength=(uint)_HoRTGIFlags.z;p.maxReservoirAge=(uint)_HoRTGIFlags.z;
            p.biasCorrectionMode=RTXDI_BIAS_CORRECTION_RAY_TRACED;
            RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel,(uint)_HoRTGIFrameIndex,RTXDI_GI_TEMPORAL_RESAMPLING_RANDOM_SEED);
            reservoir=RTXDI_GITemporalResampling(pixel,surface,motion,0,reservoir,rng,HoGIRuntime(),HoGILayout(),p);
        }
    }
    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,2);
}

void HoGISpatial(uint2 pixel)
{
    if(!HoGIInside(pixel))return;
    RTXDI_GIReservoir reservoir=RTXDI_LoadGIReservoir(HoGILayout(),pixel,2);
    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);
    if(_HoRTGIFlags.y>.5&&RAB_IsSurfaceValid(surface))
    {
        RTXDI_GISpatialResamplingParameters p=(RTXDI_GISpatialResamplingParameters)0;
        p.depthThreshold=.1;p.normalThreshold=.9;p.numSamples=(uint)_HoRTGISettings.y;p.samplingRadius=_HoRTGISettings.z;
        p.biasCorrectionMode=RTXDI_BIAS_CORRECTION_RAY_TRACED;
        RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel,(uint)_HoRTGIFrameIndex,RTXDI_GI_SPATIAL_RESAMPLING_RANDOM_SEED);
        reservoir=RTXDI_GISpatialResampling(pixel,surface,2,reservoir,rng,HoGIRuntime(),HoGILayout(),p);
    }
    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,1);
}
#endif
