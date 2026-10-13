// Copyright (c) 2020-2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.
// Adapted from the pinned UnityPathTracing RTXDI FullSample ComputeGradients,
// FilterGradientsPass and ConfidencePass. See ../ThirdParty/RTXDI/LICENSE.txt.
// Static opaque world, exact camera reprojection, no checkerboard or motion geometry.
RWTexture2D<float2> _HoRTDIPreviousLuminance;
RWTexture2D<float4> _HoRTDIGradientA,_HoRTDIGradientB;
RWTexture2D<float> _HoRTDIDiffConfidence,_HoRTDISpecConfidence;
int _HoRTDIGradientPass,_HoRTDINrdHistoryReady;
float2 HoDIEvaluateSample(RTXDI_DIReservoir r,RAB_Surface s,RAB_LightInfo info)
{
    if(!RTXDI_IsValidDIReservoir(r)||!RAB_IsSurfaceValid(s))return 0;
    RAB_LightSample sample=RAB_SamplePolymorphicLight(info,s,RTXDI_GetDIReservoirSampleUV(r));
    if(!RAB_GetConservativeVisibility(s,sample))return 0;
    float3 L;float distance;RAB_GetLightDirDistance(s,sample,L,distance);if(dot(L,s.geoNormal)<=0)return 0;
    float3 radiance=sample.radiance*RTXDI_GetDIReservoirInvPdf(r)/max(sample.solidAnglePdf,1e-12);
    float3 d=Lambert(s.normal,-L)*s.material.diffuseAlbedo*radiance;
    float3 p=0;if(s.material.roughness>=.03)p=GGX_times_NdotL(s.viewDir,L,s.normal,max(.03,s.material.roughness),s.material.specularF0)*radiance;
    d=f16tof32(f32tof16(d));p=f16tof32(f32tof16(p));
    return f16tof32(f32tof16(float2(HoDILuminance(d),HoDILuminance(p))));
}
[numthreads(8,8,1)]void Gradients(uint3 id:SV_DispatchThreadID)
{
    uint2 size=((uint2)_HoRTDIFrame.xy+2)/3;if(any(id.xy>=size))return;
    float2 selected=0;int2 pixel=-1,previousPixel=-1;bool usePrevious=false;
    if(_HoRTDINrdHistoryReady!=0)
    for(int y=0;y<3;y++)for(int x=0;x<3;x++)
    {
        int2 p=int2(id.xy)*3+int2(x,y);if(any(p>=(int2)_HoRTDIFrame.xy))continue;
        RAB_Surface s=RAB_GetGBufferSurface(p,false);if(!RAB_IsSurfaceValid(s))continue;
        float4 clip=mul(_HoRTDIPreviousVP,float4(s.worldPos,1));if(abs(clip.w)<1e-6)continue;
        float2 uv=clip.xy/clip.w*.5+.5;
        #if UNITY_UV_STARTS_AT_TOP
        uv.y=1-uv.y;
        #endif
        int2 old=int2(floor(uv*_HoRTDIFrame.xy));if(any(old<0)||any(old>=(int2)_HoRTDIFrame.xy))continue;
        RAB_Surface oldSurface=RAB_GetGBufferSurface(old,true);
        if(!RAB_IsSurfaceValid(oldSurface)||dot(s.normal,oldSurface.normal)<.9||distance(s.worldPos,oldSurface.worldPos)>.02*max(1,s.viewDepth))continue;
        float2 current=float2(HoDILuminance(_HoRTDIDiffuse[p].rgb),HoDILuminance(_HoRTDISpecular[p].rgb));
        current=f16tof32(f32tof16(current));float2 prev=_HoRTDIPreviousLuminance[old];
        if(HoDIUseAuxiliary())
        {
            current=HoDICachedRuntimePrimaryLuminance(RTXDI_LoadDIReservoir(HoDILayout(),p,1),s,false);
            prev=HoDICachedRuntimePrimaryLuminance(RTXDI_LoadDIReservoir(HoDILayout(),old,0),oldSurface,true);
        }
        float a=max(current.x,current.y),b=max(prev.x,prev.y),best=max(selected.x,selected.y);
        if(a>best&&a>b){selected=current;pixel=p;previousPixel=old;usePrevious=false;}
        else if(b>best){selected=prev;pixel=p;previousPixel=old;usePrevious=true;}
    }
    float4 gradient=0;
    if(pixel.x>=0)
    {
        RTXDI_DIReservoir r=RTXDI_LoadDIReservoir(HoDILayout(),usePrevious?previousPixel:pixel,usePrevious?0:1);
        int mapped=RAB_TranslateLightIndex(RTXDI_GetDIReservoirLightIndex(r),!usePrevious);
        float2 other=0;
        if(mapped>=0)
        {
            RAB_LightInfo source=RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(r),usePrevious);
            RAB_LightInfo destination=RAB_LoadLightInfo(mapped,!usePrevious);
            // Within the accepted static-world revision, identical analytic
            // light parameters at the same selected surface give identical
            // shading. Avoid a redundant visibility ray and history precision noise.
            bool identical=all(source.data.positionRange==destination.data.positionRange)&&all(source.data.radianceType==destination.data.radianceType)
                &&all(source.data.directionOuter==destination.data.directionOuter)&&all(source.data.innerShadow==destination.data.innerShadow);
            if(identical)other=selected;
            else
            {
                RAB_Surface s=RAB_GetGBufferSurface(usePrevious?previousPixel:pixel,usePrevious);
                other=HoDIEvaluateSample(r,s,destination);
            }
        }
        gradient=float4(abs(selected-other),max(selected,other));
    }
    _HoRTDIGradientA[id.xy]=min(gradient,65504);
}
[numthreads(8,8,1)]void FilterGradients(uint3 id:SV_DispatchThreadID)
{
    int2 size=((int2)_HoRTDIFrame.xy+2)/3;if(any(id.xy>=size))return;
    int stride=1<<_HoRTDIGradientPass;float4 value=0;float sum=0;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        int2 p=int2(id.xy)+int2(x,y)*stride;if(any(p<0)||any(p>=size))continue;
        float w=(x==0?1:.5)*(y==0?1:.5);sum+=w;
        value+=w*((_HoRTDIGradientPass&1)==0?_HoRTDIGradientA[p]:_HoRTDIGradientB[p]);
    }
    if((_HoRTDIGradientPass&1)==0)_HoRTDIGradientB[id.xy]=value/max(sum,1e-12);
    else _HoRTDIGradientA[id.xy]=value/max(sum,1e-12);
}
[numthreads(8,8,1)]void Confidence(uint3 id:SV_DispatchThreadID)
{
    uint2 size=((uint2)_HoRTDIFrame.xy+2)/3;if(any(id.xy>=size))return;
    float4 g=max(_HoRTDIGradientA[id.xy],0);float2 c=pow(saturate(1-g.xy/(g.zw+exp2(-12.0))),8.0);
    _HoRTDIDiffConfidence[id.xy]=c.x;_HoRTDISpecConfidence[id.xy]=c.y;
}
