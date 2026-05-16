#ifndef LIL_HOAOV_SAMPLING_INCLUDED
#define LIL_HOAOV_SAMPLING_INCLUDED

#define LIL_HOAOV_DEPTH_EPSILON 0.0001
#define LIL_HOAOV_NORMAL_EPSILON 0.0001

half LilHoAovCoverage(half4 normalDepth)
{
    return step(LIL_HOAOV_DEPTH_EPSILON, normalDepth.a);
}

half LilHoAovNormalValid(half4 normalDepth)
{
    return LilHoAovCoverage(normalDepth) * step(LIL_HOAOV_NORMAL_EPSILON, dot(normalDepth.rgb, normalDepth.rgb));
}

float LilHoAovLinearDepthOrFar(half4 normalDepth, float farDepth)
{
    return lerp(farDepth, (float)normalDepth.a, LilHoAovCoverage(normalDepth));
}

half3 LilHoAovEncodedNormalOrBlack(half4 normalDepth)
{
    return normalDepth.rgb * LilHoAovNormalValid(normalDepth);
}

float3 LilHoAovWorldNormalOrZero(half4 normalDepth)
{
    half validNormal = LilHoAovNormalValid(normalDepth);
    if (validNormal < 0.5)
    {
        return 0.0;
    }

    return normalize((float3)normalDepth.rgb * 2.0 - 1.0);
}

#endif
