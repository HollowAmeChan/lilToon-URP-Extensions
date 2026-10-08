#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RayTracing/Shaders/HoRTPrimarySurface.hlsl"
int _TestQuery;
float4 HoRTValidationPixel(uint2 outputPixel)
{
    uint band = outputPixel.y / 64;
    uint2 pixel = uint2(outputPixel.x, outputPixel.y % 64);
    HoRTPrimarySurface s;
    bool valid;
    if (_TestQuery == 1) valid = HoRTTryLoadPrimarySurfaceAtUV(float2(-.1, .5), s);
    else if (_TestQuery == 2) valid = HoRTTryLoadPrimarySurface(uint2(10000, 10000), s);
    else valid = HoRTTryLoadPrimarySurface(pixel, s);
    if (band == 0) return float4(s.positionWS, s.status);
    if (band == 1) return float4(s.geometricNormalWS, s.eyeDepth);
    if (band == 2) return float4(s.shadingNormalWS, s.metallic);
    if (band == 3) return float4(s.viewDirectionWS, s.perceptualRoughness);
    if (band == 4) return float4(s.baseColor, s.reflectance);
    if (band == 5) return float4(s.emission, s.owner);
    if (band == 6) return float4(valid ? 1 : 0, s.ownerAvailable, 0, s.status);
    HoRTPrimarySurface atUV;
    HoRTTryLoadPrimarySurfaceAtUV((float2(pixel) + .1) * _HoRTPrimarySize.zw, atUV);
    return float4(atUV.positionWS - s.positionWS, atUV.status);
}
