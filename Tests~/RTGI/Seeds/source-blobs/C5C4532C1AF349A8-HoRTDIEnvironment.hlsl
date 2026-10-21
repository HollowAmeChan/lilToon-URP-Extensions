#ifndef HO_RTDI_ENVIRONMENT_INCLUDED
#define HO_RTDI_ENVIRONMENT_INCLUDED
#include "../../Atmosphere/Shaders/HoAtmosphereSampling.hlsl"
Texture2D _HoRTDIEnvironmentPdf;
RWTexture2D<float> _HoRTDIEnvironmentPdfOutput;
float4 _HoRTDIEnvironment; // PDF size, candidates, active, RIS segment offset
int _HoRTDIEnvironmentBrdfSamples;
static const uint HO_RTDI_ENVIRONMENT_LIGHT_INDEX=0x7ffffffe;
float3 HoDIEnvironmentDirection(float2 uv)
{
    float y=1-2*uv.y,phi=2*M_PI*uv.x,r=sqrt(max(0,1-y*y));
    return float3(r*cos(phi),y,r*sin(phi));
}
float2 HoDIEnvironmentUV(float3 direction)
{
    direction=normalize(direction);return float2(frac(atan2(direction.z,direction.x)/(2*M_PI)),saturate((1-direction.y)*.5));
}
float HoDIEnvironmentTexelPdf(float2 uv)
{
    uint size=(uint)_HoRTDIEnvironment.x;float average=_HoRTDIEnvironmentPdf.Load(int3(0,0,(int)log2(size))).r;
    return average>0?_HoRTDIEnvironmentPdf.Load(int3(min((uint2)(uv*size),size-1),0)).r/(average*size*size):0;
}
#endif
