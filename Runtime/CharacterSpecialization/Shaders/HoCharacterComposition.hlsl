#ifndef HO_CHARACTER_COMPOSITION_INCLUDED
#define HO_CHARACTER_COMPOSITION_INCLUDED

// IDs match HoCharacterCompositionEffect. y in Last marks a bound, normalized order.
float4 _HoCharacterCompositionOrderFirst;
float4 _HoCharacterCompositionOrderLast;

half3 BlendFaceHairDiffuse(half3 source, half3 tint, float amount)
{
    float blendMode = round(_HoCharacterFaceHairDiffuseParams.w);
    half3 result = tint;
    if (blendMode > 0.5 && blendMode < 1.5) result = source + tint;
    else if (blendMode >= 1.5)
    {
        half3 source01 = saturate(source);
        half3 tint01 = saturate(tint);
        result = 1.0 - (1.0 - source01) * (1.0 - tint01);
    }
    return lerp(source, max(result, 0.0), saturate(amount));
}

half3 BlendOutlineFog(half3 baseColor, half3 fogColor, float amount)
{
    float alpha = saturate(amount);
    half3 base01 = saturate(baseColor);
    half3 fog01 = saturate(fogColor);
    half3 screen = 1.0 - (1.0 - base01) * (1.0 - fog01);
    half3 hdrLift = max(fogColor - fog01, 0.0);
    half3 target = max(baseColor, screen + hdrLift);
    return lerp(baseColor, target, alpha);
}

// Capture, masks and blur fields keep their own dependencies. Only color layering is ordered.
struct HoCharacterCompositionInputs
{
    half3 source;
    half3 eyeColor;
    float eyeAmount;
    float shadowAmount;
    half3 diffuseColor;
    float diffuseAmount;
    half3 enhancedColor;
    float enhancedAmount;
    half3 subjectColor;
    float subjectAmount;
    float subjectIsFog;
};

half3 HoCharacterCompose(HoCharacterCompositionInputs inputs)
{
    half3 color = inputs.source;
    [loop] for (int stage = 0; stage < 5; stage++)
    {
        // Unbound diagnostic callers also get the default, with EyeReveal last.
        int effect = stage;
        if (_HoCharacterCompositionOrderLast.y > 0.5)
            effect = (int)round(stage < 4 ? _HoCharacterCompositionOrderFirst[min(stage, 3)] : _HoCharacterCompositionOrderLast.x);
        if (effect == 0 && inputs.shadowAmount > 0.0001)
        {
            half3 shadowColor = (half3)_HoCharacterHairShadowColor.rgb;
            if (round(_HoCharacterHairShadowParams1.y) < 0.5)
                color *= lerp(half3(1.0, 1.0, 1.0), shadowColor, inputs.shadowAmount);
            else color = lerp(color, shadowColor, inputs.shadowAmount);
        }
        else if (effect == 1 && inputs.diffuseAmount > 0.0001)
            color = BlendFaceHairDiffuse(color, inputs.diffuseColor, inputs.diffuseAmount);
        else if (effect == 2 && inputs.enhancedAmount > 0.0001)
            color = BlendOutlineFog(color, inputs.enhancedColor, inputs.enhancedAmount);
        else if (effect == 3 && inputs.subjectAmount > 0.0001)
            color = inputs.subjectIsFog > 0.5 ? BlendOutlineFog(color, inputs.subjectColor, inputs.subjectAmount)
                : lerp(color, inputs.subjectColor, inputs.subjectAmount);
        else if (effect == 4)
            color = lerp(color, inputs.eyeColor, inputs.eyeAmount);
    }
    return color;
}
#endif
