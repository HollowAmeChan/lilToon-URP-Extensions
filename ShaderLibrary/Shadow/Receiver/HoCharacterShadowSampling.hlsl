#ifndef HO_CHARACTER_SHADOW_SAMPLING_INCLUDED
#define HO_CHARACTER_SHADOW_SAMPLING_INCLUDED

#include "HoRendererIdentity.hlsl"
#include "HoRTShadowSampling.hlsl"
#include "../../../Runtime/Shadows/Resources/HoCharacterShadowFixedPcf.hlsl"

float _HoCSSoftnessRadius;

float HoCSResolveMainCast(float3 positionWS, float sceneCast)
{
    float3 positionDx = ddx(positionWS);
    float3 positionDy = ddy(positionWS);
    uint identity = HoRendererIdentity();
    float rtCast;
    if (HoRTShadowTryMainCast(positionWS, identity, rtCast))
        return rtCast;

    uint group = HoRendererIdentityGroup(identity);
    if (_HoCSActive < 0.5 || group == 0u)
        return sceneCast;
    int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
    if (slice < 0 || slice >= min(_HoCSCount, HO_CS_FIXED_MAX_SLICES))
        return sceneCast;

    float radiusTexels = _HoCSSoftnessRadius / max(_HoCSParameters[slice].w, 1e-6);
    HoCharacterShadowLocalSample result = HoCSFixedEvaluate(positionWS, positionDx, positionDy,
        identity, true, radiusTexels);
    return lerp(sceneCast, result.localVisibility, result.blend);
}

#endif
