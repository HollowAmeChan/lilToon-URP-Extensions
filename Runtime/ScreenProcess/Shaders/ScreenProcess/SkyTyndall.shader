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

            static const int MaxSkyTyndallSamples = 48;

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x radius, y threshold, z soft knee, w exposure
            float4 _LayerParams1; // x center x, y center y, z decay, w quality
            float4 _LayerParams2; // x foreground suppress, y normal amount, z sky gain, w occlusion power
            float4 _LayerParams3; // x opacity, y show rays only, z sky alpha power, w jitter
            float4 _LayerParams4; // x fixed direction x, y fixed direction y, z direction angle degrees, w fixed direction enabled
            float4 _LayerParams5; // x sample weight, y source blur px, z dither mode, w dither amount
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

            float InterleavedGradientNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            int ResolveDitherMode()
            {
                return min(max((int)round(_LayerParams5.z), 0), 2);
            }

            float ResolveDitherAmount()
            {
                return _LayerParams5.w > 0.0001 ? saturate(_LayerParams5.w) : 0.65;
            }

            float ResolveBlueNoiseLikeValue(float2 pixel)
            {
                float tile = Hash(floor(pixel * 0.125) + 17.0);
                return frac(InterleavedGradientNoise(pixel) + tile * 0.61803398875);
            }

            float ResolveDitherValue(float2 pixel)
            {
                int mode = ResolveDitherMode();
                if (mode == 2)
                {
                    return 0.5;
                }

                return ResolveBlueNoiseLikeValue(pixel);
            }

            float ResolveJitterStrength()
            {
                int mode = ResolveDitherMode();
                if (mode == 2)
                {
                    return 0.0;
                }

                float amount = ResolveDitherAmount();
                return mode == 1 ? lerp(0.15, 0.65, amount) : lerp(0.25, 0.85, amount);
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

            float ResolvePositiveDefault(float value, float fallback)
            {
                return value > 0.0001 ? value : fallback;
            }

            float ResolveDecay(float value)
            {
                if (value <= 0.0001)
                {
                    return 0.94;
                }

                return value <= 1.0 ? saturate(value) : saturate(exp(-value * 0.03));
            }

            bool HasFixedDirection()
            {
                return _LayerParams4.w > 0.5;
            }

            float2 ResolveCenter()
            {
                return saturate(_LayerParams1.xy);
            }

            float ViewportGate(float2 uv)
            {
                float2 lower = step(0.0, uv);
                float2 upper = step(uv, 1.0);
                return lower.x * lower.y * upper.x * upper.y;
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

            float3 ApplyDitherStyle(float2 uv, float3 rays)
            {
                if (ResolveDitherMode() != 1)
                {
                    return rays;
                }

                float amount = ResolveDitherAmount();
                if (amount <= 0.0001)
                {
                    return rays;
                }

                float luma = Luma(max(rays, 0.0));
                if (luma <= 0.0001)
                {
                    return rays;
                }

                float tone = saturate((luma / (1.0 + luma)) * 1.35);
                float cellSize = lerp(9.0, 4.5, amount);
                float angleSin = 0.38268343;
                float angleCos = 0.92387953;
                float2 pixel = uv * _ScreenParams.xy;
                float2 rotatedPixel = float2(
                    pixel.x * angleCos - pixel.y * angleSin,
                    pixel.x * angleSin + pixel.y * angleCos);
                float2 cell = frac(rotatedPixel / cellSize) - 0.5;
                float radius = sqrt(max(tone, 0.0001)) * 0.58;
                float distanceToCenter = length(cell);
                float aa = max(fwidth(distanceToCenter), 0.015);
                float dotMask = smoothstep(radius + aa, radius - aa, distanceToCenter);
                float halftoneGain = lerp(0.35, 1.25, dotMask);
                return rays * lerp(1.0, halftoneGain, amount);
            }

            float SampleSkySignal(float2 uv, float threshold, float softKnee, float exposure, float alphaPower)
            {
                float viewportGate = ViewportGate(uv);
                if (viewportGate <= 0.0001)
                {
                    return 0.0;
                }

                half4 sky = SAMPLE_TEXTURE2D_X(_HoGeometryBufferSkyTexture, sampler_LinearClamp, uv);
                float contribution = pow(saturate(sky.a), alphaPower);
                float litLuma = Luma(max(sky.rgb * max(exposure, 0.0), 0.0));
                float highlight = HighlightWeight(sky.rgb, threshold, softKnee, exposure);
                float compressedLuma = litLuma / (1.0 + litLuma * 0.045);
                float floorSignal = compressedLuma * 0.04;
                float brightSignal = compressedLuma * highlight;
                return contribution * max(brightSignal, floorSignal) * viewportGate;
            }

            float SampleFilteredSkySignal(
                float2 uv,
                float2 rayDir,
                float threshold,
                float softKnee,
                float exposure,
                float alphaPower,
                float blurPixels)
            {
                float centerSignal = SampleSkySignal(uv, threshold, softKnee, exposure, alphaPower);
                if (blurPixels <= 0.001)
                {
                    return centerSignal;
                }

                float2 safeRayDir = normalize(rayDir + 0.0001);
                float2 tangentDir = float2(-safeRayDir.y, safeRayDir.x);
                float2 texel = rcp(_ScreenParams.xy);
                float2 rayOffset = safeRayDir * texel * blurPixels;
                float2 tangentOffset = tangentDir * texel * blurPixels;

                float filtered = centerSignal * 0.40;
                filtered += SampleSkySignal(uv + rayOffset, threshold, softKnee, exposure, alphaPower) * 0.15;
                filtered += SampleSkySignal(uv - rayOffset, threshold, softKnee, exposure, alphaPower) * 0.15;
                filtered += SampleSkySignal(uv + tangentOffset, threshold, softKnee, exposure, alphaPower) * 0.15;
                filtered += SampleSkySignal(uv - tangentOffset, threshold, softKnee, exposure, alphaPower) * 0.15;
                return filtered;
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

            float2 ResolveRayDirection(float2 uv, float2 center)
            {
                if (HasFixedDirection())
                {
                    float2 directionToLight = _LayerParams4.xy;
                    float lenSq = dot(directionToLight, directionToLight);
                    if (lenSq <= 0.000001)
                    {
                        return float2(0.0, -1.0);
                    }

                    return -directionToLight * rsqrt(lenSq);
                }

                float2 ray = uv - center;
                float lenSq = dot(ray, ray);
                if (lenSq <= 0.000001)
                {
                    return float2(0.0, 1.0);
                }

                return ray * rsqrt(lenSq);
            }

            float2 ResolveRayStep(float2 uv, float2 center, float2 rayDirection, float radius, int sampleCount)
            {
                if (HasFixedDirection())
                {
                    return rayDirection * radius / max(sampleCount, 1);
                }

                return (uv - center) * radius / max(sampleCount, 1);
            }

            float ResolveCenterCoreFade(float2 uv, float2 center)
            {
                if (HasFixedDirection())
                {
                    return 1.0;
                }

                float centerDistance = length(uv - center);
                return smoothstep(0.015, 0.065, centerDistance);
            }

            float3 AccumulateSkyRays(float2 uv, float2 center)
            {
                float radius = saturate(_LayerParams0.x);
                float threshold = max(_LayerParams0.y, 0.0);
                float softKnee = max(_LayerParams0.z, 0.0);
                float exposure = max(_LayerParams0.w, 0.0);
                float decay = ResolveDecay(_LayerParams1.z);
                float skyGain = max(_LayerParams2.z, 0.0);
                float alphaPower = max(_LayerParams3.z, 0.001);
                float jitterAmount = saturate(_LayerParams3.w);
                bool missingFilterDefaults = abs(_LayerParams5.x) <= 0.0001 && abs(_LayerParams5.y) <= 0.0001;
                float sampleWeight = ResolvePositiveDefault(_LayerParams5.x, 0.055);
                float blurPixels = missingFilterDefaults ? 1.25 : max(_LayerParams5.y, 0.0);
                int quality = clamp((int)round(_LayerParams1.w), 0, 2);
                int sampleCount = quality == 0 ? 12 : (quality == 1 ? 24 : 40);

                float2 direction = ResolveRayDirection(uv, center);
                float2 pixel = floor(uv * _ScreenParams.xy);
                float jitter = (ResolveDitherValue(pixel) - 0.5) * jitterAmount * ResolveJitterStrength();
                float2 sampleUV = uv;
                float2 deltaUV = ResolveRayStep(uv, center, direction, radius, sampleCount);
                float illuminationDecay = 1.0;
                float sum = 0.0;
                float centerCoreFade = ResolveCenterCoreFade(uv, center);

                [loop]
                for (int i = 0; i < MaxSkyTyndallSamples; i++)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    sampleUV -= deltaUV * (i == 0 ? 0.5 : 1.0);
                    float2 jitterUV = sampleUV - deltaUV * jitter;
                    float signal = SampleFilteredSkySignal(jitterUV, direction, threshold, softKnee, exposure, alphaPower, blurPixels);
                    sum += signal * illuminationDecay * sampleWeight;
                    illuminationDecay *= decay;
                }

                return sum * skyGain * centerCoreFade * _LayerColor.rgb;
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

                float2 center = ResolveCenter();
                float2 rayToCenter = HasFixedDirection() ? _LayerParams4.xy : center - uv;
                float foregroundSuppress = saturate(_LayerParams2.x);
                float occlusionPower = max(_LayerParams2.w, 0.001);
                float foregroundMask = lerp(1.0, pow(saturate(1.0 - geometryCoverage), occlusionPower), foregroundSuppress);
                float normalMask = ResolveNormalAmount(uv, geometryCoverage, rayToCenter);
                float opacity = saturate(_LayerParams3.x);
                float amount = saturate(_Intensity * opacity * foregroundMask * normalMask * ruleAmount);

                float3 rays = ApplyDitherStyle(uv, AccumulateSkyRays(uv, center));
                if (_LayerParams3.y > 0.5)
                {
                    return half4(rays * _Intensity, source.a);
                }

                float3 blended = ApplyBlend(source.rgb, rays, _LayerBlendMode);
                return half4(lerp(source.rgb, blended, amount), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
