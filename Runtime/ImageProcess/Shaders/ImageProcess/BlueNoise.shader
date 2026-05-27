Shader "Hidden/lilToon/URP/ImageProcess/BlueNoise"
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
            Name "ImageProcess Blue Noise Mosaic"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlueNoiseMosaic

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x mode, y blend amount, z cell size px, w point jitter
            float4 _LayerParams1; // x color averaging, y edge width px, z edge opacity, w poster steps

            float2 ImageProcessBlueNoiseHash22(float2 value)
            {
                float3 p3 = frac(float3(value.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float2 ImageProcessBlueNoiseSeed(float2 cell, float jitter)
            {
                float2 randomOffset = ImageProcessBlueNoiseHash22(cell) - 0.5;
                return cell + 0.5 + randomOffset * saturate(jitter);
            }

            void ImageProcessBlueNoiseNearest(
                float2 mosaicUv,
                float jitter,
                out float2 nearestSeed,
                out float nearestDistance,
                out float secondDistance)
            {
                float2 baseCell = floor(mosaicUv);
                nearestSeed = 0.0;
                nearestDistance = 1.0e20;
                secondDistance = 1.0e20;

                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 cell = baseCell + float2(x, y);
                        float2 seed = ImageProcessBlueNoiseSeed(cell, jitter);
                        float distanceSquared = dot(mosaicUv - seed, mosaicUv - seed);
                        if (distanceSquared < nearestDistance)
                        {
                            secondDistance = nearestDistance;
                            nearestDistance = distanceSquared;
                            nearestSeed = seed;
                        }
                        else if (distanceSquared < secondDistance)
                        {
                            secondDistance = distanceSquared;
                        }
                    }
                }

                nearestDistance = sqrt(nearestDistance);
                secondDistance = sqrt(secondDistance);
            }

            float3 ImageProcessBlueNoiseSampleCellColor(float2 seedUv, float2 texel, float averageRadius)
            {
                float2 radius = texel * averageRadius;
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, seedUv).rgb * 0.36;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, seedUv + radius * float2(1.7, 0.2)).rgb * 0.16;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, seedUv + radius * float2(-1.1, 1.3)).rgb * 0.16;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, seedUv + radius * float2(0.4, -1.6)).rgb * 0.16;
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, seedUv + radius * float2(-1.6, -0.7)).rgb * 0.16;
                return color;
            }

            float3 ImageProcessBlueNoisePosterize(float3 color, float steps)
            {
                steps = max(round(steps), 2.0);
                return floor(saturate(color) * steps) / max(steps - 1.0, 1.0);
            }

            half4 FragBlueNoiseMosaic(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float p0Default = 1.0 - step(0.0001, dot(abs(_LayerParams0), float4(1.0, 1.0, 1.0, 1.0)));
                float p1Default = 1.0 - step(0.0001, dot(abs(_LayerParams1), float4(1.0, 1.0, 1.0, 1.0)));

                float mode = round(_LayerParams0.x);
                float blendAmount = saturate(lerp(_LayerParams0.y, 1.0, p0Default)) * saturate(_Intensity);
                float cellSize = max(lerp(_LayerParams0.z, 18.0, p0Default), 2.0);
                float jitter = saturate(lerp(_LayerParams0.w, 0.78, p0Default));
                float averageRadius = max(lerp(_LayerParams1.x, 0.35, p1Default), 0.0) * cellSize;
                float edgeWidth = max(lerp(_LayerParams1.y, 0.75, p1Default), 0.0);
                float edgeOpacity = saturate(lerp(_LayerParams1.z, 0.18, p1Default));
                float posterSteps = max(lerp(_LayerParams1.w, 12.0, p1Default), 2.0);

                if (blendAmount <= 0.0001)
                {
                    return source;
                }

                float2 pixel = input.texcoord * _ScreenParams.xy;
                float2 mosaicUv = pixel / cellSize;
                float2 seed;
                float nearestDistance;
                float secondDistance;
                ImageProcessBlueNoiseNearest(mosaicUv, jitter, seed, nearestDistance, secondDistance);

                float2 seedUv = saturate(seed * cellSize / max(_ScreenParams.xy, 1.0));
                float2 texel = 1.0 / max(_ScreenParams.xy, 1.0);
                float3 cellColor = ImageProcessBlueNoiseSampleCellColor(seedUv, texel, averageRadius);
                float edgeWidthInCells = edgeWidth / cellSize;
                float rawEdge = 1.0 - smoothstep(edgeWidthInCells, edgeWidthInCells + 0.035, secondDistance - nearestDistance);
                float edge = edgeWidth > 0.0001 ? rawEdge : 0.0;
                float3 result = cellColor;

                if (mode < 0.5)
                {
                    result = cellColor;
                }
                else if (mode < 1.5)
                {
                    float2 centerOffset = (seed - floor(seed)) - 0.5;
                    float shade = 1.0 - dot(centerOffset, centerOffset) * 0.22;
                    result = cellColor * shade;
                }
                else if (mode < 2.5)
                {
                    float3 glassColor = lerp(cellColor, saturate(cellColor * 1.08 + 0.04), 0.45);
                    result = lerp(glassColor, saturate(_LayerColor.rgb), edge * max(edgeOpacity, 0.35));
                }
                else
                {
                    float3 poster = ImageProcessBlueNoisePosterize(cellColor, posterSteps);
                    result = lerp(poster, saturate(_LayerColor.rgb), edge * edgeOpacity);
                }

                if (mode < 2.5)
                {
                    result = lerp(result, saturate(_LayerColor.rgb), edge * edgeOpacity);
                }

                return half4(lerp(source.rgb, max(result, 0.0), blendAmount), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
