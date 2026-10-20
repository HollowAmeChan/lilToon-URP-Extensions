#ifndef HO_RTGI_NRD_SIGNAL_INCLUDED
#define HO_RTGI_NRD_SIGNAL_INCLUDED
#include "../../RTDI/ThirdParty/NRD/NRD.hlsli"
RWTexture2D<float4> _HoRTGINrdSignal,_HoRTGINrdNormal;
RWTexture2D<float> _HoRTGINrdViewZ;
Texture2D<float4> _HoRTGINrdOutput;
RWTexture2D<float4> _HoRTGIFiltered;
// Fixed units conversion, identical for input/history/output; not camera exposure.
// Preserves weak HDR channels while leaving headroom for FP16 second moments.
float _HoRTGINrdSignalScale;
#endif
