#ifndef HO_RTGI_SECONDARY_SURFACE_INCLUDED
#define HO_RTGI_SECONDARY_SURFACE_INCLUDED

// The same contract is used by the GI ray generation and material closest-hit
// shaders. Miss = 0; evaluated opaque lilToon = 1; opaque unsupported material = 2.
// Unsupported hits still occlude and never turn into environment samples.
// Private input-only marker for native visibility rays. Closest-hit returns
// ordinary status/distance; it does not change this output struct's ABI.
static const uint HO_RTGI_VISIBILITY_QUERY=0xffffffffu;
struct HoRTGISecondarySurface
{
    float distance;
    uint status;
    float3 normalWS;
    float3 geometricNormalWS;
    float3 baseColor;
    float perceptualRoughness;
    float metallic;
    float reflectance;
    float3 emission;
};
#endif
