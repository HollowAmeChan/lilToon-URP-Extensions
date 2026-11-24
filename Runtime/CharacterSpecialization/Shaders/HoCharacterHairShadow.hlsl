#ifndef HO_CHARACTER_HAIR_SHADOW_INCLUDED
#define HO_CHARACTER_HAIR_SHADOW_INCLUDED

float2 HoCharacterResolveHairShadowDirection(
    HoGDObjectFrame frame,
    float4 surfaceToLight,
    float4x4 worldToClip,
    float2 screenSize,
    float manualAngleRadians,
    float influence,
    out float appliedInfluence)
{
    float2 manual = float2(cos(manualAngleRadians), sin(manualAngleRadians));
    appliedInfluence = 0.0;
    if (frame.originValid.w <= 0.5 || surfaceToLight.w <= 0.5 || influence <= 0.0001)
        return manual;

    float normalLength = length(frame.forward.xyz);
    float lightLength = length(surfaceToLight.xyz);
    if (normalLength < 0.0001 || lightLength < 0.0001) return manual;
    float3 normal = frame.forward.xyz / normalLength;
    float3 light = surfaceToLight.xyz / lightLength;
    float frontLight = dot(normal, light);
    float3 shadowOnFace = -light + normal * frontLight;
    // Project a tangent at the face origin, including off-axis perspective, camera roll and XR view.
    float4 originClip = mul(worldToClip, float4(frame.originValid.xyz, 1.0));
    float4 tangentClip = mul(worldToClip, float4(shadowOnFace, 0.0));
    if (originClip.w < 0.0001) return manual;
    float2 projected = (tangentClip.xy * originClip.w - originClip.xy * tangentClip.w)
        / originClip.w;
#if UNITY_UV_STARTS_AT_TOP
    projected.y = -projected.y;
#endif
    projected *= screenSize / max(max(screenSize.x, screenSize.y), 1.0);
    float projectedLength = length(projected);
    if (projectedLength <= 0.0001)
        return manual;

    float2 automatic = projected / projectedLength;
    appliedInfluence = saturate(influence) * smoothstep(0.02, 0.25, frontLight)
        * smoothstep(0.001, 0.03, projectedLength);
    // Blend offsets without renormalizing: opposing directions meet at zero instead of flipping.
    return lerp(manual, automatic, appliedInfluence);
}

#endif
