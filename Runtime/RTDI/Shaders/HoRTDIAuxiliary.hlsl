// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// NVIDIA portions are governed by ../ThirdParty/RTXDI/LICENSE.txt.
// Application-side fresh analytic samples and primary radiance evaluation.
#ifndef HO_RTDI_AUXILIARY_INCLUDED
#define HO_RTDI_AUXILIARY_INCLUDED
int _HoRTDIFinalSamples;
StructuredBuffer<float> _HoRTDIRuntimeAuxiliaryCDF;
RWStructuredBuffer<uint2> _HoRTDIRuntimeAuxiliarySecondary;
bool HoDIUseAuxiliary(){return _HoRTDIFinalSamples>1&&_HoRTDIFrame.z>0;}
RTXDI_DIReservoir HoDIFreshAuxiliary(inout RTXDI_RandomSamplerState rng)
{
    uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;float u=RTXDI_GetNextRandom(rng);
    while(lo<hi){uint middle=(lo+hi)/2;if(_HoRTDIRuntimeAuxiliaryCDF[middle]<=u)lo=middle+1;else hi=middle;}
    uint index=min(lo,count-1);float pdf=_HoRTDIRuntimeAuxiliaryCDF[index]-(index>0?_HoRTDIRuntimeAuxiliaryCDF[index-1]:0);
    RTXDI_DIReservoir sample=RTXDI_EmptyDIReservoir();RTXDI_StreamSample(sample,index,float2(.5,.5),0,1,1/pdf);RTXDI_FinalizeResampling(sample,1,1);return sample;
}
uint2 HoDIPackRuntimeAuxiliary(RTXDI_DIReservoir sample,bool visible)
{
    if(!RTXDI_IsValidDIReservoir(sample))return uint2(0x7fffffffu,0);
    return uint2(RTXDI_GetDIReservoirLightIndex(sample)|(visible?0x80000000u:0),asuint(sample.weightSum));
}
RTXDI_DIReservoir HoDIUnpackRuntimeAuxiliary(uint2 payload)
{
    RTXDI_DIReservoir sample=RTXDI_EmptyDIReservoir();uint index=payload.x&0x7fffffffu;if(index==0x7fffffffu)return sample;
    sample.lightData=index|RTXDI_DIReservoir_LightValidBit;sample.uvData=0x7fff7fffu;sample.weightSum=asfloat(payload.y);sample.targetPdf=1;sample.M=1;
    float visibility=(payload.x&0x80000000u)!=0?1:0;RTXDI_StoreVisibilityInDIReservoir(sample,visibility.xxx,false);return sample;
}
float2 HoDICachedRuntimePrimaryLuminance(RTXDI_DIReservoir reservoir,RAB_Surface surface,bool previous)
{
    if(!RTXDI_IsValidDIReservoir(reservoir)||!RAB_IsSurfaceValid(surface))return 0;
    RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(reservoir),previous),surface,RTXDI_GetDIReservoirSampleUV(reservoir));
    float visibility=float(reservoir.packedVisibility&63)/63;float3 L;float distance;RAB_GetLightDirDistance(surface,sample,L,distance);if(dot(L,surface.geoNormal)<=0)return 0;
    float3 radiance=sample.radiance*visibility*reservoir.weightSum/max(sample.solidAnglePdf,1e-12);
    float3 d=Lambert(surface.normal,-L)*surface.material.diffuseAlbedo*radiance,p=0;
    if(surface.material.roughness>=.03)p=GGX_times_NdotL(surface.viewDir,L,surface.normal,max(.03,surface.material.roughness),surface.material.specularF0)*radiance;
    d=f16tof32(f32tof16(d));p=f16tof32(f32tof16(p));return f16tof32(f32tof16(float2(HoDILuminance(d),HoDILuminance(p))));
}
#endif
