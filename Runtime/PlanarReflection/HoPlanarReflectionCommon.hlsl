#ifndef HO_PLANAR_REFLECTION_COMMON_INCLUDED
#define HO_PLANAR_REFLECTION_COMMON_INCLUDED

float HoPlanarReflectionSmoothnessWeight(float smoothness, float minimumSmoothness)
{
    smoothness = saturate(smoothness);
    minimumSmoothness = saturate(minimumSmoothness);
    // At the upper endpoint, retain a perfect mirror rather than evaluating 0/epsilon.
    if (minimumSmoothness >= 1.0) return step(1.0, smoothness);
    return saturate((smoothness - minimumSmoothness) / max(1.0 - minimumSmoothness, 0.0001));
}

#endif
