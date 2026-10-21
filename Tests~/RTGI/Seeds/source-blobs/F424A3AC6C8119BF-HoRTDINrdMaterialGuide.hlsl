// Borrowed Mono primary inputs; no additional material texture or object-ID routing.
#include "../HoRTDIPrimaryHistory.hlsl"
Texture2D<float4> _HoRTPrimaryColor, _HoRTPrimaryMaterial, _HoRTPrimaryReflection;
float4 _HoRTPrimarySize;
StructuredBuffer<HoRTDIPrimaryHistory> _HoRTDIHistory;
int _HoRTDINrdGuideHistoryValid;

float3 HoNrdSpecularGuide(int2 pixel)
{
    float3 color = saturate(_HoRTPrimaryColor.Load(int3(pixel, 0)).rgb);
    float metal = _HoRTPrimaryMaterial.Load(int3(pixel, 0)).g;
    float reflectance = _HoRTPrimaryReflection.Load(int3(pixel, 0)).r;
    // Match the DI bridge's quantized F0, with the same nonzero factor floor.
    return round(saturate(lerp(reflectance.xxx, color, metal)) * 255.0) / 255.0 + NRD_MATERIAL_FACTOR_MIN_SCALE;
}

float HoNrdSpecularMaterialWeight(float3 a, float3 b)
{
    // A zero channel cannot be reconstructed by dividing the old signal.
    if(any((a > NRD_MATERIAL_FACTOR_MIN_SCALE) != (b > NRD_MATERIAL_FACTOR_MIN_SCALE))) return 0;
    float3 lo = min(a, b), hi = max(a, b);
    return dot(lo, lo) / max(dot(hi, hi), 1e-12);
}

float HoNrdSpecularHistoryMatch(int2 pixel, float2 previousUV)
{
    if(_HoRTDINrdGuideHistoryValid == 0 || any(isnan(previousUV)) || any(isinf(previousUV)) || any(previousUV<0) || any(previousUV>1)) return 0;
    float2 p = previousUV * _HoRTPrimarySize.xy - .5;
    int2 origin = int2(floor(p)); float2 f = frac(p);
    float3 current = HoNrdSpecularGuide(pixel); float match = 1;
    for(int y=0;y<2;y++)for(int x=0;x<2;x++)
    {
        float w = (x==0?1-f.x:f.x)*(y==0?1-f.y:f.y); if(w <= 1e-6) continue;
        int2 q=origin+int2(x,y); if(any(q<0)||any(q>=(int2)_HoRTPrimarySize.xy)) return 0;
        HoRTDIPrimaryHistory h=_HoRTDIHistory[q.y*(uint)_HoRTPrimarySize.x+q.x]; if((h.diffuse>>24)==0) return 0;
        float3 old=float3(h.specular&255,(h.specular>>8)&255,(h.specular>>16)&255)/255.0 + NRD_MATERIAL_FACTOR_MIN_SCALE;
        match=min(match,HoNrdSpecularMaterialWeight(current,old));
    }
    return match;
}
