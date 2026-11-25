#ifndef HO_RTGI_SECONDARY_TENSION_INCLUDED
#define HO_RTGI_SECONDARY_TENSION_INCLUDED
// Same GeometryData ABI and response as Cloth/Skin. Ray hits interpolate the
// original per-vertex data before applying the nonlinear material response.
#if defined(_HO_RT_TENSION)
struct HoGITensionEntry { uint offset,count,valid,reserved; };
StructuredBuffer<HoGITensionEntry> _HoGDTensionEntries;
StructuredBuffer<float4> _HoGDTensionValues;
uint _HoGDTensionEntryCount;
float _HoGDTensionAvailable;
float4 HoGIReadTension(uint3 tri,float3 weights)
{
    if(_HoRTTensionEnabled<.5||_HoGDTensionAvailable<.5)return 0;
    uint slot=(uint)round(_HoRTInstanceUserValue.y);
    if(slot==0||slot>=_HoGDTensionEntryCount)return 0;
    HoGITensionEntry entry=_HoGDTensionEntries[slot];
    if(entry.valid==0)return 0;
    float4 a=tri.x<entry.count?_HoGDTensionValues[entry.offset+tri.x]:0;
    float4 b=tri.y<entry.count?_HoGDTensionValues[entry.offset+tri.y]:0;
    float4 c=tri.z<entry.count?_HoGDTensionValues[entry.offset+tri.z]:0;
    return a*weights.x+b*weights.y+c*weights.z;
}
float HoGITensionResponse(float value,float2 range)
{
    float start=max(0,range.x),end=max(start,range.y);
    return end-start<1e-6?(value>start?1:0):smoothstep(start,end,value);
}
float2 HoGITensionWeights(float4 data,float2 maps)
{
    if(data.w<.999||!all(isfinite(data)))return 0;
    float2 response=float2(HoGITensionResponse(data.x,_HoTesionStretchRange.xy),HoGITensionResponse(data.y,_HoTesionCompressionRange.xy));
    float2 weights=saturate(response*_HoTesionStrength*float2(_HoTesionStretchStrength,_HoTesionCompressionStrength))*saturate(maps);
    return weights/max(1,weights.x+weights.y);
}
float3 HoGITensionAlbedo(float3 baseColor,float2 uv,float4 data)
{
    float2 weights=HoGITensionWeights(data,_HoTesionMaps.xy);
    float3 color=baseColor*(1-weights.x-weights.y);
    if(weights.x>0)
    {
        float3 sample=_HoRTMaterialFamily==5?_HoTesionStretchBaseColor.SampleLevel(sampler_HoTesionStretchBaseColor,HoGITransformUV(uv,_HoTesionStretchBaseColor_ST),0).rgb
            :_HoTesionStretchBaseColor.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_HoTesionStretchBaseColor_ST),0).rgb;
        color+=weights.x*_HoTesionStretchColor.rgb*sample;
    }
    if(weights.y>0)
    {
        float3 sample=_HoRTMaterialFamily==5?_HoTesionCompressionBaseColor.SampleLevel(sampler_HoTesionCompressionBaseColor,HoGITransformUV(uv,_HoTesionCompressionBaseColor_ST),0).rgb
            :_HoTesionCompressionBaseColor.SampleLevel(sampler_MainTex,HoGITransformUV(uv,_HoTesionCompressionBaseColor_ST),0).rgb;
        color+=weights.y*_HoTesionCompressionColor.rgb*sample;
    }
    return color;
}
float3 HoGITensionNormal(float3 normal,float2 uv,float4 data)
{
    float2 weights=HoGITensionWeights(data,_HoTesionMaps.zw);
    float3 result=normal*(1-weights.x-weights.y);
    if(weights.x>0)
    {
        float4 sample=_HoRTMaterialFamily==5?_HoTesionStretchNormal.SampleLevel(sampler_HoTesionStretchNormal,HoGITransformUV(uv,_HoTesionStretchBaseColor_ST),0)
            :_HoTesionStretchNormal.SampleLevel(sampler_BumpMap,HoGITransformUV(uv,_HoTesionStretchBaseColor_ST),0);
        result+=weights.x*UnpackNormalScale(sample,_HoTesionStretchNormalScale);
    }
    if(weights.y>0)
    {
        float4 sample=_HoRTMaterialFamily==5?_HoTesionCompressionNormal.SampleLevel(sampler_HoTesionCompressionNormal,HoGITransformUV(uv,_HoTesionCompressionBaseColor_ST),0)
            :_HoTesionCompressionNormal.SampleLevel(sampler_BumpMap,HoGITransformUV(uv,_HoTesionCompressionBaseColor_ST),0);
        result+=weights.y*UnpackNormalScale(sample,_HoTesionCompressionNormalScale);
    }
    return HoGISafeNormal(result,normal);
}
#else
float4 HoGIReadTension(uint3 tri,float3 weights){return 0;}
float3 HoGITensionAlbedo(float3 color,float2 uv,float4 data){return color;}
float3 HoGITensionNormal(float3 normal,float2 uv,float4 data){return normal;}
#endif
#endif
