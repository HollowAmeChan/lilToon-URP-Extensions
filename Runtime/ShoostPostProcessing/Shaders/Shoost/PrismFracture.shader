Shader "Hidden/lilToon-Shoost/URP/Shoost/PrismFracture"
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
            Name "Shoost Prism Fracture"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrismFracture

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x center x, y center y, z radius, w softness
            float4 _LayerParams1; // x fracture, y dispersion, z shard count, w rotation degrees
            float4 _LayerParams2; // x prism highlight, y seed, z unused, w unused

            float ShoostPrismHash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453123);
            }

            float2 ShoostPrismHash21(float value)
            {
                return frac(sin(float2(value * 127.1, value * 311.7)) * 43758.5453123);
            }

            float2 ShoostPrismRotate(float2 value, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(value.x * c - value.y * s, value.x * s + value.y * c);
            }

            half3 ShoostPrismSample(float2 uv, float2 offset, float dispersion)
            {
                float2 ca = offset * dispersion;
                half3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 plus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv + ca)).rgb;
                half3 minus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv - ca)).rgb;
                return half3(plus.r, center.g, minus.b);
            }

            struct ShoostPrismLayerResult
            {
                half3 color;
                float weight;
            };

            ShoostPrismLayerResult ShoostPrismLayer(
                float2 uv,
                float2 local,
                float angle,
                float radial,
                float aspect,
                float mask,
                float fracture,
                float dispersion,
                float highlightAmount,
                float seed,
                float shardCount,
                float layerIndex,
                float ringScale,
                float shardScale,
                float offsetScale,
                float displacementScale,
                float dispersionScale,
                float edgeScale,
                float layerWeight)
            {
                ShoostPrismLayerResult result;

                float layerSeed = seed + layerIndex * 23.17;
                float ringCount = max(2.0 + layerIndex, floor(lerp(2.0 + layerIndex, max(3.0 + layerIndex, shardCount * ringScale), fracture)));
                float radialCurve = lerp(1.24 - layerIndex * 0.04, 0.80 + layerIndex * 0.03, fracture);
                float curvedRadial = pow(radial, radialCurve);
                float radialPhase = (ShoostPrismHash11(layerSeed + 9.1) - 0.5) * fracture * layerIndex * 0.34;
                float ringCoord = curvedRadial * ringCount + radialPhase;
                float ring = min(max(floor(ringCoord), 0.0), ringCount - 1.0);
                float radialCell = frac(ringCoord);
                float ringSeed = ring + layerSeed * 13.0;
                float ringRnd = ShoostPrismHash11(ringSeed + 2.1);
                float shardVariation = lerp(0.04, 0.48, fracture) * (0.75 + layerIndex * 0.18);
                float ringShardCount = max(3.0, floor(shardCount * shardScale * lerp(0.60, 1.18, radial) * lerp(1.0 - shardVariation, 1.0 + shardVariation, ringRnd)));
                float ringOffset = (ShoostPrismHash11(ringSeed + 7.7) - 0.5) * fracture * offsetScale / ringShardCount;
                float angleCoord = frac(angle / 6.2831853 + ringOffset);
                float wedgeCoord = angleCoord * ringShardCount;
                float wedge = floor(wedgeCoord);
                float angularCell = frac(wedgeCoord);
                float shardId = wedge + ring * 73.0 + layerSeed * 19.0;

                float2 rnd = ShoostPrismHash21(shardId) * 2.0 - 1.0;
                float rndAngle = ShoostPrismHash11(shardId + 5.3) * 6.2831853;
                float2 shardDirection = normalize(rnd + float2(cos(rndAngle), sin(rndAngle)) * 0.75 + 0.0001);

                float radialBreak = min(radialCell, 1.0 - radialCell);
                float angularBreak = min(angularCell, 1.0 - angularCell);
                float chosenEdge = min(radialBreak, angularBreak);
                float edgeWidth = lerp(0.052, 0.018, fracture) * edgeScale;
                float edgeLine = 1.0 - smoothstep(0.0, edgeWidth, chosenEdge);

                float displacementPixels = lerp(3.0, 70.0, fracture) * displacementScale * (0.34 + ShoostPrismHash11(shardId + 11.0) * 0.90);
                float microRadialCount = floor(lerp(1.0 + layerIndex, 3.0 + layerIndex * 2.0, fracture));
                float microAngularCount = floor(lerp(1.0 + layerIndex, 4.0 + layerIndex * 2.0, fracture));
                float microShardId = shardId + floor(radialCell * microRadialCount) * 211.0 + floor(angularCell * microAngularCount) * 37.0;
                float2 microRnd = ShoostPrismHash21(microShardId + 23.0) * 2.0 - 1.0;
                float shardInterior = smoothstep(0.08, 0.28, radialBreak) * smoothstep(0.08, 0.28, angularBreak);
                float microPixels = shardInterior * fracture * fracture * displacementScale * lerp(1.0, 12.0 + layerIndex * 4.0, ShoostPrismHash11(microShardId + 5.0));
                float2 displacement = (shardDirection * displacementPixels + normalize(microRnd + 0.0001) * microPixels) / _ScreenParams.xy;
                displacement.x /= max(aspect, 0.0001);

                float localHighlightSeed = ShoostPrismHash11(shardId + 37.0);
                float edgeFacing = saturate(dot(normalize(local + 0.0001), normalize(shardDirection + 0.0001)) * 0.5 + 0.5);
                float highlightGate = smoothstep(0.56, 0.93, localHighlightSeed) * smoothstep(0.12, 0.72, radial) * (1.0 - smoothstep(0.58, 1.0, radial));
                float highlightMask = edgeLine * edgeFacing * highlightGate;
                float prismDispersion = dispersion * dispersionScale * lerp(0.60, 2.45, fracture) * (0.50 + highlightMask * 1.05);

                float2 sampleUv = saturate(uv + displacement * mask);
                half3 prism = ShoostPrismSample(sampleUv, displacement * (1.0 + highlightMask * 1.25), prismDispersion);
                float hue = frac(angle / 6.2831853 + localHighlightSeed * 0.37 + layerIndex * 0.11);
                float3 spectral = 0.5 + 0.5 * cos(6.2831853 * (hue + float3(0.0, 0.33, 0.67)));
                float glint = highlightAmount * highlightMask * (0.85 + layerIndex * 0.18);
                result.color = lerp(prism, prism + spectral * 0.34, glint);
                result.weight = saturate(layerWeight);
                return result;
            }

            half4 FragPrismFracture(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 center = _LayerParams0.xy;
                float radius = max(_LayerParams0.z, 0.0001);
                float softness = max(_LayerParams0.w, 0.0001);
                float fracture = saturate(_LayerParams1.x);
                float dispersion = saturate(_LayerParams1.y);
                float shardCount = max(_LayerParams1.z, 3.0);
                float rotation = radians(_LayerParams1.w);
                float highlightAmount = saturate(_LayerParams2.x);
                float seed = _LayerParams2.y;

                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 local = input.texcoord - center;
                local.x *= aspect;
                float dist = length(local);
                float mask = 1.0 - smoothstep(radius, radius + softness, dist);
                if (mask <= 0.0001)
                {
                    return source;
                }

                float2 rotated = ShoostPrismRotate(local, rotation);
                float angle = atan2(rotated.y, rotated.x);
                angle = angle < 0.0 ? angle + 6.2831853 : angle;
                float radial = saturate(dist / radius);

                ShoostPrismLayerResult largeLayer = ShoostPrismLayer(input.texcoord, local, angle, radial, aspect, mask, fracture, dispersion, highlightAmount, seed, shardCount, 0.0, 0.42, 0.74, 1.35, 0.92, 0.92, 1.00, 1.00);
                ShoostPrismLayerResult middleLayer = ShoostPrismLayer(input.texcoord, local, angle, radial, aspect, mask, fracture, dispersion, highlightAmount, seed, shardCount, 1.0, 0.68, 1.06, 2.15, 0.76, 1.22, 0.82, 0.18 + fracture * 0.30);
                ShoostPrismLayerResult fineLayer = ShoostPrismLayer(input.texcoord, local, angle, radial, aspect, mask, fracture, dispersion, highlightAmount, seed, shardCount, 2.0, 0.96, 1.46, 3.20, 0.42, 1.62, 0.58, fracture * 0.22);
                half3 prism = largeLayer.color;
                prism = lerp(prism, middleLayer.color, middleLayer.weight);
                prism = lerp(prism, fineLayer.color, fineLayer.weight);

                float blend = mask * intensity;
                source.rgb = lerp(source.rgb, prism, blend);
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
