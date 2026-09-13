#ifndef LILTOON_HO_SHADOW_CAST_SHADER_CONTRACT_INCLUDED
#define LILTOON_HO_SHADOW_CAST_SHADER_CONTRACT_INCLUDED

// ShadowCast shader/C# numeric contract.
//
// The C# mirror lives in Runtime/ShadowCast/HoShadowCastShaderContract.cs.
// Editor/ShadowCast/HoShadowCastShaderContractValidator.cs parses this file on editor load and
// reports any drift, so both sides must be edited together.

// ---------------------------------------------------------------------------------------------
// Fixed global array sizes.
//
// Unity caches the length of a global array slot the first time it is set in an editor session and
// only allows smaller uploads afterwards:
//     "Property (_HoShadowCastLightData0) exceeds previous array size (48 vs 12). Cap to previous
//      size. Restart Unity to recreate the arrays."
// Therefore these sizes must NOT depend on the capacity tier: HoShadowCastPublisher always uploads
// arrays of exactly this length, and the tier only bounds what is collected and sampled. Keeping the
// layout fixed is what makes switching tiers at runtime safe (no restart, no silently capped data).
//
// Operational note: INCREASING these values needs one Unity editor restart, because the already
// allocated slots cannot grow. Lowering them is fine, but the shader arrays and the C# upload length
// must always stay equal (HoShadowCastShaderContract.cs / the validator enforce that).
// ---------------------------------------------------------------------------------------------
#define HO_SHADOW_CAST_ARRAY_LIGHTS 48
#define HO_SHADOW_CAST_ARRAY_SLICES 128

// Capacity tiers: how many additional lights may be sampled at once. This is the per-pixel cost knob;
// the sampling loops are bounded by the resolved value.
#define HO_SHADOW_CAST_CAPACITY_LOW_LIGHTS 12
#define HO_SHADOW_CAST_CAPACITY_MEDIUM_LIGHTS 24
#define HO_SHADOW_CAST_CAPACITY_HIGH_LIGHTS 48

#if defined(HO_SHADOW_CAST_CAPACITY_HIGH)
    #define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_HIGH_LIGHTS
#elif defined(HO_SHADOW_CAST_CAPACITY_MEDIUM)
    #define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_MEDIUM_LIGHTS
#else
    #define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_LOW_LIGHTS
#endif

// The slice count is NOT tiered. How many slices are accepted is decided by the atlas geometry
// (atlas size against the configured spot/point resolutions) and stays within the fixed
// HO_SHADOW_CAST_ARRAY_SLICES ceiling: lowering a resolution buys more concurrent lights, raising it
// buys fewer, without touching the light tier.

// Second directional light: dedicated atlas with a fixed capacity, not part of the tier selection.
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS 4
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES 4
#define HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES 16

// PCSS sample ceilings. HoShadowCastShaderContract.GetPcssSampleCounts must stay within them.
#define HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES 32
#define HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES 64

// Light type ids carried in _HoShadowCastLightData0.x by HoShadowCastFrameCollector.GetLightTypeId.
#define HO_SHADOW_CAST_LIGHT_DIRECTIONAL 0.0
#define HO_SHADOW_CAST_LIGHT_SPOT 1.0
#define HO_SHADOW_CAST_LIGHT_POINT 2.0

#endif
