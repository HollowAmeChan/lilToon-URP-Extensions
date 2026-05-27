Shader "Hidden/lilToon/URP/ScreenProcess/SkyTyndall"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ScreenProcess Sky Tyndall"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessRuleMask.hlsl"

            static const int MaxSkyTyndallSamples = 32;

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x radius, y threshold, z soft knee, w exposure
            float4 _LayerParams1; // x center x, y center y, z decay, w quality
            float4 _LayerParams2; // x foreground amount, y normal amount, z sky gain, w occlusion power
            float4 _LayerParams3; // x opacity, y show rays only, z sky alpha power, w jitter
            float _HoGeometryBufferSkyTextureValid;

            TEXTURE2D_X(_HoGeometryBufferSkyTexture);
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);

            float Luma(float3 color)
            {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.11369));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float HighlightWeight(float3 color, float threshold, float softKnee, float exposure)
            {
                float luma = Luma(max(color * max(exposure, 0.0), 0.0));
                if (softKnee <= 0.0001)
                {
                    return luma >= threshold ? 1.0 : 0.0;
                }

                return smoothstep(max(threshold - softKnee, 0.0), threshold + softKnee, luma);
            }

            float3 ApplyBlend(float3 baseColor, float3 layerColor, float blendMode)
            {
                int mode = (int)round(blendMode);
                if (mode == 1)
                {
                    return max(baseColor + layerColor, 0.0);
                }

                if (mode == 2)
                {
                    float3 ldrBase = saturate(baseColor);
                    float3 ldrLayer = saturate(layerColor);
                    return 1.0 - (1.0 - ldrBase) * (1.0 - ldrLayer) + max(baseColor - 1.0, 0.0);
                }

                if (mode == 3)
                {
                    return baseColor * layerColor;
                }

                return layerColor;
            }

            float ResolveRuleAmount(float2 uv, float geometryCoverage)
            {
                if (_LayerRuleMaskEnabled <= 0.5)
                {
                    if (LilScreenProcessShouldOutputRuleDebug())
                    {
                        return LilScreenProcessResolveRequiredRuleMask(uv);
                    }

                    return 1.0;
                }

                float rule = LilScreenProcessResolveRequiredRuleMask(uv);
                return lerp(1.0, rule, geometryCoverage);
            }

            float ResolveNormalAmount(float2 uv, float geometryCoverage, float2 rayToCenter)
            {
                float normalAmount = saturate(_LayerParams2.y);
                if (normalAmount <= 0.0001 || geometryCoverage <= 0.0001)
                {
                    return 1.0;
                }

                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);
                if (dot(normalWS, normalWS) <= 0.0001)
                {
                    return 1.0;
                }

                float3 normalVS = normalize(TransformWorldToViewDir(normalWS, true));
                float2 dir2 = normalize(rayToCenter + 0.0001);
                float3 lightDirVS = normalize(float3(dir2 * 0.82, 0.58));
                float ndl = saturate(dot(normalVS, lightDirVS) * 0.5 + 0.5);
                return lerp(1.0, ndl, normalAmount);
            }

            float3 AccumulateSkyRays(float2 uv, float2 center)
            {
                float radius = saturate(_LayerParams0.x);
                float threshold = max(_LayerParams0.y, 0.0);
                float softKnee = max(_LayerParams0.z, 0.0);
                float exposure = max(_LayerParams0.w, 0.0);
                float decay = max(_LayerParams1.z, 0.0);
                float skyGain = max(_LayerParams2.z, 0.0);
                float alphaPower = max(_LayerParams3.z, 0.001);
                float jitterAmount = saturate(_LayerParams3.w);
                int quality = clamp((int)round(_LayerParams1.w), 0, 2);
                int sampleCount = quality == 0 ? 8 : (quality == 1 ? 16 : 32);

                float2 direction = uv - center;
                float jitter = (Hash(floor(uv * _ScreenParams.xy)) - 0.5) * jitterAmount / max(sampleCount, 1);
                float3 sum = 0.0;
                float totalWeight = 0.0;

                [loop]
                for (int i = 0; i < MaxSkyTyndallSamples; i++)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    float t = saturate(((float)i + 0.5 + jitter) / max(sampleCount, 1));
                    float2 sampleUV = uv - direction * radius * t;
                    half4 sky = SAMPLE_TEXTURE2D_X(_HoGeometryBufferSkyTexture, sampler_LinearClamp, sampleUV);
                    float contribution = pow(saturate(sky.a), alphaPower);
                    float highlight = HighlightWeight(sky.rgb, threshold, softKnee, exposure);
                    float distanceWeight = pow(saturate(1.0 - t), decay);
                    float weight = contribution * highlight * distanceWeight;
                    sum += sky.rgb * exposure * weight;
                    totalWeight += weight;
                }

                return totalWeight > 0.0001 ? (sum / totalWeight) * skyGain * _LayerColor.rgb : 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_HoGeometryBufferSkyTextureValid <= 0.5 || _Intensity <= 0.0001)
                {
                    if (LilScreenProcessShouldOutputRuleDebug())
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return source;
                }

                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float geometryCoverage = LilHoGeometryBufferCoverage(normalDepth);
                float ruleAmount = ResolveRuleAmount(uv, geometryCoverage);
                if (LilScreenProcessShouldOutputRuleDebug())
                {
                    return half4(ruleAmount, ruleAmount, ruleAmount, source.a);
                }

                float2 center = saturate(_LayerParams1.xy);
                float2 rayToCenter = center - uv;
                float foregroundAmount = saturate(_LayerParams2.x);
                float occlusionPower = max(_LayerParams2.w, 0.001);
                float foregroundMask = lerp(1.0, pow(saturate(geometryCoverage), occlusionPower), foregroundAmount);
                float normalMask = ResolveNormalAmount(uv, geometryCoverage, rayToCenter);
                float opacity = saturate(_LayerParams3.x);
                float amount = saturate(_Intensity * opacity * foregroundMask * normalMask * ruleAmount);

                float3 rays = AccumulateSkyRays(uv, center);
                if (_LayerParams3.y > 0.5)
                {
                    return half4(rays * amount, source.a);
                }

                float3 blended = ApplyBlend(source.rgb, rays, _LayerBlendMode);
                return half4(lerp(source.rgb, blended, amount), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
