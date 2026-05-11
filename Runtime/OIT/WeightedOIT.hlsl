#ifndef LILTOON_URP_WEIGHTED_OIT_INCLUDED
#define LILTOON_URP_WEIGHTED_OIT_INCLUDED

float _lilOITWeight;
float _lilOITAlphaClipThreshold;

struct lilWeightedOITOutput
{
    float4 accumulation : SV_Target0;
    float4 revealage : SV_Target1;
};

float lilWeightedOITCalculateWeight(float alpha, float linearDepth)
{
    float depthWeight = saturate(1.0 - linearDepth);
    depthWeight = max(0.01, depthWeight * depthWeight);
    return max(1.0e-3, alpha * _lilOITWeight * depthWeight);
}

lilWeightedOITOutput lilWeightedOITResolveOutput(float4 color, float linearDepth)
{
    clip(color.a - _lilOITAlphaClipThreshold);

    float alpha = saturate(color.a);
    float weight = lilWeightedOITCalculateWeight(alpha, linearDepth);

    lilWeightedOITOutput output;
    output.accumulation = float4(color.rgb * weight, alpha * weight);
    output.revealage = float4(alpha, 0.0, 0.0, 0.0);
    return output;
}

#endif
