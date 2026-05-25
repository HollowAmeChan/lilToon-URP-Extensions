Shader "Hidden/lilToon/URP/ImageProcess/Kuwahara"
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
            Name "ImageProcess Kuwahara"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragKuwahara

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            static const int MaxKuwaharaRadius = 10;

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x radius, y quality, z posterize levels, w edge threshold
            float4 _LayerParams1; // x edge strength, y noise strength

            float ImageProcessKuwaharaLuma(float3 color)
            {
                return dot(color, float3(0.299, 0.587, 0.114));
            }

            float ImageProcessKuwaharaHash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.11369));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float3 ImageProcessKuwaharaSample(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            void ImageProcessKuwaharaStats(float2 uv, int minX, int maxX, int minY, int maxY, out float3 mean, out float variance)
            {
                float2 texel = rcp(max(_ScreenParams.xy, 1.0));
                minX = clamp(minX, -MaxKuwaharaRadius, MaxKuwaharaRadius);
                maxX = clamp(maxX, -MaxKuwaharaRadius, MaxKuwaharaRadius);
                minY = clamp(minY, -MaxKuwaharaRadius, MaxKuwaharaRadius);
                maxY = clamp(maxY, -MaxKuwaharaRadius, MaxKuwaharaRadius);
                float3 sum = 0.0;
                float3 sumSq = 0.0;
                float count = 0.0;

                [loop]
                for (int y = minY; y <= maxY; y++)
                {
                    [loop]
                    for (int x = minX; x <= maxX; x++)
                    {
                        float3 color = ImageProcessKuwaharaSample(uv + float2(x, y) * texel);
                        sum += color;
                        sumSq += color * color;
                        count += 1.0;
                    }
                }

                mean = sum / max(count, 1.0);
                float3 sigma = max(sumSq / max(count, 1.0) - mean * mean, 0.0);
                variance = ImageProcessKuwaharaLuma(sigma);
            }

            void ImageProcessKuwaharaChoose(float2 uv, int minX, int maxX, int minY, int maxY, inout float3 bestColor, inout float bestVariance)
            {
                float3 mean;
                float variance;
                ImageProcessKuwaharaStats(uv, minX, maxX, minY, maxY, mean, variance);
                if (variance < bestVariance)
                {
                    bestVariance = variance;
                    bestColor = mean;
                }
            }

            void ImageProcessKuwaharaChooseWindow(float2 uv, int centerX, int centerY, int radius, inout float3 bestColor, inout float bestVariance)
            {
                ImageProcessKuwaharaChoose(uv, centerX - radius, centerX + radius, centerY - radius, centerY + radius, bestColor, bestVariance);
            }

            float3 ImageProcessKuwaharaFilter(float2 uv, int radius, int quality)
            {
                int r = clamp(radius, 1, MaxKuwaharaRadius);
                if (quality == 0)
                {
                    r = min(r, 3);
                }
                else if (quality == 1)
                {
                    r = min(r, 6);
                }

                float3 bestColor = ImageProcessKuwaharaSample(uv);
                float bestVariance = 1.0e20;

                ImageProcessKuwaharaChoose(uv, -r, 0, -r, 0, bestColor, bestVariance);
                ImageProcessKuwaharaChoose(uv, 0, r, -r, 0, bestColor, bestVariance);
                ImageProcessKuwaharaChoose(uv, -r, 0, 0, r, bestColor, bestVariance);
                ImageProcessKuwaharaChoose(uv, 0, r, 0, r, bestColor, bestVariance);

                if (quality >= 2)
                {
                    int windowRadius = max(1, r / 2);
                    int windowOffset = max(1, r - windowRadius);
                    ImageProcessKuwaharaChooseWindow(uv, 0, 0, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, -windowOffset, 0, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, windowOffset, 0, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, 0, -windowOffset, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, 0, windowOffset, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, -windowOffset, -windowOffset, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, windowOffset, -windowOffset, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, -windowOffset, windowOffset, windowRadius, bestColor, bestVariance);
                    ImageProcessKuwaharaChooseWindow(uv, windowOffset, windowOffset, windowRadius, bestColor, bestVariance);
                }

                return bestColor;
            }

            float ImageProcessKuwaharaSobel(float2 uv)
            {
                float2 texel = rcp(max(_ScreenParams.xy, 1.0));
                float l00 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(-1.0, -1.0)));
                float l10 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(0.0, -1.0)));
                float l20 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(1.0, -1.0)));
                float l01 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(-1.0, 0.0)));
                float l21 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(1.0, 0.0)));
                float l02 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(-1.0, 1.0)));
                float l12 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(0.0, 1.0)));
                float l22 = ImageProcessKuwaharaLuma(ImageProcessKuwaharaSample(uv + texel * float2(1.0, 1.0)));

                float gx = l00 + 2.0 * l01 + l02 - l20 - 2.0 * l21 - l22;
                float gy = l00 + 2.0 * l10 + l20 - l02 - 2.0 * l12 - l22;
                return sqrt(gx * gx + gy * gy);
            }

            float3 ImageProcessKuwaharaPosterize(float3 color, float levels)
            {
                if (levels < 2.0)
                {
                    return color;
                }

                float steps = max(round(levels), 2.0);
                float originalLuma = max(ImageProcessKuwaharaLuma(max(color, 0.0)), 0.0001);
                float quantizedLuma = floor(originalLuma * steps + 0.5) / steps;
                return max(color, 0.0) * (quantizedLuma / originalLuma);
            }

            half4 FragKuwahara(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
                    return source;
                }

                int radius = clamp((int)round(_LayerParams0.x), 1, MaxKuwaharaRadius);
                int quality = clamp((int)round(_LayerParams0.y), 0, 2);
                float3 color = ImageProcessKuwaharaFilter(input.texcoord, radius, quality);
                color = ImageProcessKuwaharaPosterize(color, _LayerParams0.z);

                float edgeStrength = saturate(_LayerParams1.x);
                if (edgeStrength > 0.0001)
                {
                    float threshold = saturate(_LayerParams0.w);
                    float edge = smoothstep(threshold, threshold + 0.2, ImageProcessKuwaharaSobel(input.texcoord));
                    color = lerp(color, _LayerColor.rgb, edge * edgeStrength);
                }

                float noiseStrength = saturate(_LayerParams1.y);
                if (noiseStrength > 0.0001)
                {
                    float noise = ImageProcessKuwaharaHash(floor(input.texcoord * _ScreenParams.xy));
                    color += (noise - 0.5) * noiseStrength;
                }

                return half4(lerp(source.rgb, color, amount), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
