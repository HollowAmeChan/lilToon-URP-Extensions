// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../../RTDI/ThirdParty/RTXDI/LICENSE.txt.
// Shared original secondary estimator; compute and native raygen differ only
// in the scene-query implementation selected before the RAB bridge is included.
#ifndef HO_RTGI_SHADING_COMMON_INCLUDED
#define HO_RTGI_SHADING_COMMON_INCLUDED
#if defined(HO_RTGI_NATIVE_SHADING)
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGINativeSceneQuery.hlsl"
#endif
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGIApplicationBridge.hlsl"
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/RTXDI/DI/InitialSampling.hlsl"
#include "LineageReservoir.hlsl"
RTXDI_ReservoirBufferParameters HoGILayout()
{RTXDI_ReservoirBufferParameters p=(RTXDI_ReservoirBufferParameters)0;p.reservoirBlockRowPitch=(uint)_HoRTGIReservoirLayout.x;p.reservoirArrayPitch=(uint)_HoRTGIReservoirLayout.y;return p;}
bool HoGIInside(uint2 pixel) {return all(pixel<(uint2)_HoRTGIFrame.xy);}
RTXDI_RISBufferSegmentParameters HoGIEnvironmentRIS()
{RTXDI_RISBufferSegmentParameters p=(RTXDI_RISBufferSegmentParameters)0;p.tileSize=(uint)_HoRTGIEnvironmentRISLayout.x;p.tileCount=(uint)_HoRTGIEnvironmentRISLayout.y;return p;}

void HoGIShadeSecondary(uint2 pixel)
{
    if(!HoGIInside(pixel))return;
    uint index=pixel.y*(uint)_HoRTGIFrame.x+pixel.x;
    HoRTGISecondaryData data=_HoRTGISecondary[index];
    RTXDI_GIReservoir reservoir=RTXDI_EmptyGIReservoir();
    uint4 hoMeta=uint4(0,data.flags,0,0);float3 hoExactView=0;
    if(data.flags!=0)
    {
        float3 radiance=data.emission;
        if(data.flags==1&&(_HoRTGIFrame.z>0||_HoRTGIEnvironment.z>.5))
        {
            RAB_Surface surface=RAB_EmptySurface();
            surface.worldPos=data.positionWS;surface.viewDepth=1;
            surface.normal=HoGIUnpackNormal(data.normal);surface.geoNormal=HoGIUnpackNormal(data.geometricNormal);
            surface.material.diffuseAlbedo=HoDIUnpack(data.diffuseAlbedo).rgb;
            float4 specular=HoDIUnpack(data.specularRoughness);surface.material.specularF0=specular.rgb;surface.material.roughness=specular.a;
            RAB_Surface primary=RAB_GetGBufferSurface(pixel,false);
            surface.viewDir=normalize(primary.worldPos-surface.worldPos);hoExactView=surface.viewDir;
            RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel,(uint)_HoRTGIFrameIndex,RTXDI_SECONDARY_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED);
            RTXDI_RandomSamplerState tileRng=RTXDI_InitRandomSampler(pixel/16,(uint)_HoRTGIFrameIndex,RTXDI_SECONDARY_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED);
            RTXDI_DIInitialSamplingParameters p=(RTXDI_DIInitialSamplingParameters)0;
            p.numLocalLightSamples=_HoRTGIFrame.z>0?(uint)_HoRTGISettings.x:0;
            p.numEnvironmentSamples=_HoRTGIEnvironment.z>.5?(uint)_HoRTGIEnvironment.y:0;
            p.enableInitialVisibility=0;p.brdfRayMinT=_HoRTGISettings.w;
            RTXDI_LightBufferParameters lights=(RTXDI_LightBufferParameters)0;lights.localLightBufferRegion.numLights=(uint)_HoRTGIFrame.z;
            lights.environmentLightParams.lightPresent=_HoRTGIEnvironment.z>.5?1:0;lights.environmentLightParams.lightIndex=HO_RTDI_ENVIRONMENT_LIGHT_INDEX;
            RAB_LightSample sample=RAB_EmptyLightSample();
            RTXDI_DIReservoir lightReservoir=RTXDI_SampleLightsForSurface(rng,tileRng,surface,p,lights,HoGIEnvironmentRIS(),HoGIEnvironmentRIS(),sample);
            if(RTXDI_IsValidDIReservoir(lightReservoir))
            {
                hoMeta=uint4(lightReservoir.lightData,lightReservoir.uvData,asuint(RTXDI_GetDIReservoirInvPdf(lightReservoir)),RTXDI_EncodeNormalizedVectorToSnorm2x16(surface.viewDir));
                // Reconstruct the stored choice before visibility; the SDK's out
                // sample can diverge under Unity DXC with multiple candidates.
                sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(lightReservoir),false),surface,RTXDI_GetDIReservoirSampleUV(lightReservoir));
                if(RAB_GetConservativeVisibility(surface,sample))
                {
                    float3 L;float distance;RAB_GetLightDirDistance(surface,sample,L,distance);
                    radiance+=RAB_SurfaceEvaluateBrdfTimesNoL(surface,L)*sample.radiance*RTXDI_GetDIReservoirInvPdf(lightReservoir)/max(sample.solidAnglePdf,1e-12);
                }
            }
        }
        // Keep HDR energy; the reference's optional firefly clamp is disabled.
        reservoir=RTXDI_MakeGIReservoir(data.positionWS,HoGIUnpackNormal(data.normal),radiance,data.pdf);
    }
    uint hoPointer=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,1);
    HoLMeta(hoPointer)=hoMeta;
    HoLOrigin(hoPointer)=data.flags!=0?uint4((uint)_HoRTGIFrameIndex,index,data.flags,((uint)_HoRTGIFrameIndex+1)*(uint)(_HoRTGIFrame.x*_HoRTGIFrame.y)+index+1):0;
    _HoLRecords[hoPointer*6+2]=uint4(asuint(data.positionWS),data.normal);
    _HoLRecords[hoPointer*6+3]=uint4(data.diffuseAlbedo,data.specularRoughness,asuint(data.emission.xy));
    _HoLRecords[hoPointer*6+4]=uint4(asuint(data.emission.z),asuint(data.pdf),data.geometricNormal,data.flags);
    _HoLRecords[hoPointer*6+5]=asuint(float4(hoExactView,0));
    reservoir.hoSourcePointer=hoPointer;
    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,1);
}

struct HoReplayEvaluation {float3 radiance,sampleRadiance,direction,brdf;float pdf,distance;uint visible;};
HoReplayEvaluation HoReplayEvaluate(HoRTGISecondaryData data,uint4 meta,float3 view)
{
    HoReplayEvaluation result=(HoReplayEvaluation)0;result.radiance=data.emission;
    if(data.flags==1&&(meta.x&0x80000000u)!=0){
        RAB_Surface surface=RAB_EmptySurface();surface.worldPos=data.positionWS;surface.viewDepth=1;
        surface.normal=HoGIUnpackNormal(data.normal);surface.geoNormal=HoGIUnpackNormal(data.geometricNormal);surface.viewDir=view;
        surface.material.diffuseAlbedo=HoDIUnpack(data.diffuseAlbedo).rgb;float4 specular=HoDIUnpack(data.specularRoughness);surface.material.specularF0=specular.rgb;surface.material.roughness=specular.a;
        RAB_LightSample chosen=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(meta.x&0x7fffffffu,false),surface,float2(meta.y&65535,meta.y>>16)/65535.0);
        result.sampleRadiance=chosen.radiance;result.pdf=chosen.solidAnglePdf;RAB_GetLightDirDistance(surface,chosen,result.direction,result.distance);
        result.brdf=RAB_SurfaceEvaluateBrdfTimesNoL(surface,result.direction);result.visible=RAB_GetConservativeVisibility(surface,chosen)?1:0;
        if(result.visible!=0)result.radiance+=result.brdf*chosen.radiance*asfloat(meta.z)/max(chosen.solidAnglePdf,1e-12);
    }return result;
}

#endif
