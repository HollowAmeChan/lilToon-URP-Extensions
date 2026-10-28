#ifndef HO_RTGI_DENSE_PRIMARY_INCLUDED
#define HO_RTGI_DENSE_PRIMARY_INCLUDED
// Include the original declaration before mapping subsequent GI call sites.
#include "../../../Runtime/RT/Shaders/HoRTPrimarySurface.hlsl"
StructuredBuffer<uint2> _HoGIDensePixels;
bool HoGIDenseLoadPrimary(uint2 samplePixel,out HoRTPrimarySurface surface)
{
    HoRTPrimarySurface actual=(HoRTPrimarySurface)0;
    bool valid=HoRTTryLoadPrimarySurface(_HoGIDensePixels[samplePixel.y],actual);
    surface=actual;return valid;
}
#define HoRTTryLoadPrimarySurface HoGIDenseLoadPrimary
#endif
