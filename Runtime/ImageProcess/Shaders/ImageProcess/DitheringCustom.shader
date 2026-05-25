Shader "Hidden/lilToon/URP/ImageProcess/DitheringCustom"
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
            Name "ImageProcess Dithering Custom"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDitheringCustom

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x mode, y resolution scale, z dithering texture index, w grid line
            float4 _LayerParams1; // x brightness steps, y red steps, z green steps, w blue steps
            float4 _LayerParams2; // x color multiply
            float4 _LayerParams3; // shadows
            float4 _LayerParams4; // midtones
            float4 _LayerParams5; // highlights
            float _LayerTextureEnabled;
            TEXTURE2D_X(_LayerTexture);

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float DecodeDither(float4 sampleValue)
            {
                return (sampleValue.r + sampleValue.g + sampleValue.b) > 0.0001 ? sampleValue.r : sampleValue.a;
            }

            float DitherThreshold(float2 pixel, float2 uv, float2 ditherSize)
            {
                float frame = floor(_Time.y * 30.0);
                float phaseIndex = fmod(frame, ditherSize.x * ditherSize.y);
                float2 phase = float2(fmod(phaseIndex, ditherSize.x), floor(phaseIndex / ditherSize.x)) / ditherSize;

                if (_LayerTextureEnabled > 0.5)
                {
                    return DecodeDither(SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_PointRepeat, uv + phase));
                }

                return Hash12(pixel + phaseIndex);
            }

            float Quantize(float value, float steps, float threshold)
            {
                steps = max(round(steps), 2.0);
                float scaled = saturate(value) * (steps - 1.0);
                float whole = floor(scaled);
                float fraction = frac(scaled);
                return saturate((whole + step(threshold, fraction)) / (steps - 1.0));
            }

            float3 ToneMapMonochrome(float value, float3 shadows, float3 midtones, float3 highlights)
            {
                float lowWeight = saturate(1.0 - value * 2.0);
                float highWeight = saturate(value * 2.0 - 1.0);
                float midWeight = 1.0 - lowWeight - highWeight;
                return shadows * lowWeight + midtones * midWeight + highlights * highWeight;
            }

            half4 FragDitheringCustom(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                }

                float mode = round(_LayerParams0.x);
                float resolutionScale = max(_LayerParams0.y, 0.01);
                float2 baseResolution = mode < 0.5 ? float2(144.0, 160.0) : float2(256.0, 224.0);
                float screenAspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float targetHeight = baseResolution.y * resolutionScale;
                float2 targetResolution = max(round(float2(targetHeight * screenAspect, targetHeight)), 16.0);
                float2 pixel = floor(input.texcoord * targetResolution);
                float2 pixelUv = (pixel + 0.5) / targetResolution;

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, pixelUv);
                float ditherIndex = clamp(round(_LayerParams0.z), 0.0, 2.0);
                float2 ditherSize = ditherIndex < 1.5 ? float2(2.0, 2.0) : float2(4.0, 4.0);
                float2 ditherUv = input.texcoord * targetResolution / ditherSize;
                float threshold = DitherThreshold(pixel, ditherUv, ditherSize);
                float3 result;

                if (mode < 0.5)
                {
                    float luma = dot(saturate(source.rgb), float3(0.2126729, 0.7151522, 0.0721750));
                    float tone = Quantize(luma, _LayerParams1.x, threshold);
                    result = ToneMapMonochrome(tone, saturate(_LayerParams3.rgb), saturate(_LayerParams4.rgb), saturate(_LayerParams5.rgb));
                }
                else
                {
                    float3 steps = max(round(_LayerParams1.yzw), 2.0);
                    float3 quantized = float3(
                        Quantize(source.r, steps.x, threshold),
                        Quantize(source.g, steps.y, threshold),
                        Quantize(source.b, steps.z, threshold));
                    result = lerp(source.rgb, quantized, saturate(_LayerParams2.x));
                }

                if (_LayerParams0.w > 0.5)
                {
                    float2 virtualPixel = input.texcoord * targetResolution;
                    float2 pixelFraction = frac(virtualPixel);
                    float2 boundaryDistance = min(pixelFraction, 1.0 - pixelFraction);
                    float2 screenPixelsPerCell = max(_ScreenParams.xy / targetResolution, 1.0);
                    float2 screenPixelDistance = boundaryDistance * screenPixelsPerCell;
                    float2 gridLine = 1.0 - smoothstep(0.65, 0.85, screenPixelDistance);
                    float lineMask = max(gridLine.x, gridLine.y);
                    result *= lerp(1.0, 0.55, saturate(lineMask));
                }

                float3 hdrResidual = max(source.rgb - saturate(source.rgb), 0.0);
                result = lerp(source.rgb, max(result, 0.0) + hdrResidual, amount);
                return half4(max(result, 0.0), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
