#ifndef LIL_HO_GEOMETRY_BUFFER_SAMPLING_INCLUDED
#define LIL_HO_GEOMETRY_BUFFER_SAMPLING_INCLUDED

#define LIL_HO_GEOMETRY_BUFFER_DEPTH_EPSILON 0.0001
#define LIL_HO_GEOMETRY_BUFFER_NORMAL_EPSILON 0.0001

half LilHoGeometryBufferCoverage(half4 normalDepth)
{
    return step(LIL_HO_GEOMETRY_BUFFER_DEPTH_EPSILON, normalDepth.a);
}

half LilHoGeometryBufferNormalValid(half4 normalDepth)
{
    return LilHoGeometryBufferCoverage(normalDepth) * step(LIL_HO_GEOMETRY_BUFFER_NORMAL_EPSILON, dot(normalDepth.rgb, normalDepth.rgb));
}

float LilHoGeometryBufferLinearDepthOrFar(half4 normalDepth, float farDepth)
{
    return lerp(farDepth, (float)normalDepth.a, LilHoGeometryBufferCoverage(normalDepth));
}

half3 LilHoGeometryBufferEncodedNormalOrBlack(half4 normalDepth)
{
    return normalDepth.rgb * LilHoGeometryBufferNormalValid(normalDepth);
}

float3 LilHoGeometryBufferWorldNormalOrZero(half4 normalDepth)
{
    half validNormal = LilHoGeometryBufferNormalValid(normalDepth);
    if (validNormal < 0.5)
    {
        return 0.0;
    }

    return normalize((float3)normalDepth.rgb * 2.0 - 1.0);
}

#endif
