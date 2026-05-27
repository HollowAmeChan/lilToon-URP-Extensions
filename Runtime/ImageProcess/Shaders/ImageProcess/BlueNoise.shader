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
            Name "ImageProcess Blue Noise"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlueNoise

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float _LayerTextureEnabled;
            float4 _LayerParams0; // x mode, y local amount, z noise scale, w animation speed
            float4 _LayerParams1; // x contrast, y mode value, z mode value, w mode value
            TEXTURE2D_X(_LayerTexture);

            static const float3 ImageProcessBlueNoiseLuma = float3(0.2126729, 0.7151522, 0.0721750);

            float ImageProcessBlueNoiseHash12(float2 value)
            {
                float3 p3 = frac(float3(value.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ImageProcessBlueNoiseDecode(float4 sampleValue)
            {
                float rgbSum = sampleValue.r + sampleValue.g + sampleValue.b;
                return rgbSum > 0.0001 ? dot(sampleValue.rgb, ImageProcessBlueNoiseLuma) : sampleValue.a;
            }

            float ImageProcessBlueNoiseProcedural(float2 cell)
            {
                float center = ImageProcessBlueNoiseHash12(cell);
                float axial =
                    ImageProcessBlueNoiseHash12(cell + float2(1.0, 0.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(-1.0, 0.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(0.0, 1.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(0.0, -1.0));
                float diagonal =
                    ImageProcessBlueNoiseHash12(cell + float2(1.0, 1.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(-1.0, 1.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(1.0, -1.0)) +
                    ImageProcessBlueNoiseHash12(cell + float2(-1.0, -1.0));
                float localAverage = axial * 0.18 + diagonal * 0.07;
                return saturate((center - localAverage) * 1.65 + 0.5);
            }

            float ImageProcessBlueNoiseValue(float2 uv, float2 pixel, float scale, float contrast, float speed, float channelOffset)
            {
                float frame = floor(_Time.y * max(speed, 0.0));
                float2 phase = frame * float2(17.0, 43.0) + channelOffset;
                float2 cell = floor(pixel / max(scale, 0.5)) + phase;
                float noise;

                if (_LayerTextureEnabled > 0.5)
                {
                    float2 tileUv = uv * _ScreenParams.xy / (128.0 * max(scale, 0.5));
                    tileUv += frac(phase * 0.011);
                    noise = ImageProcessBlueNoiseDecode(SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_PointRepeat, tileUv));
                }
                else
                {
                    noise = ImageProcessBlueNoiseProcedural(cell);
                }

                return saturate((noise - 0.5) * max(contrast, 0.01) + 0.5);
            }

            float3 ImageProcessBlueNoise3(float2 uv, float2 pixel, float scale, float contrast, float speed)
            {
                return float3(
                    ImageProcessBlueNoiseValue(uv, pixel, scale, contrast, speed, 0.0),
                    ImageProcessBlueNoiseValue(uv, pixel, scale, contrast, speed, 19.19),
                    ImageProcessBlueNoiseValue(uv, pixel, scale, contrast, speed, 47.47));
            }

            float ImageProcessBlueNoiseQuantize(float value, float steps, float threshold)
            {
                steps = max(round(steps), 2.0);
                float scaled = saturate(value) * (steps - 1.0);
                float whole = floor(scaled);
                float fraction = frac(scaled);
                return saturate((whole + step(threshold, fraction)) / (steps - 1.0));
            }

            float ImageProcessBlueNoiseEdge(float2 uv)
            {
                float2 texel = 1.0 / max(_ScreenParams.xy, 1.0);
                float center = dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb, ImageProcessBlueNoiseLuma);
                float left = dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x, 0.0)).rgb, ImageProcessBlueNoiseLuma);
                float right = dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb, ImageProcessBlueNoiseLuma);
                float up = dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb, ImageProcessBlueNoiseLuma);
                float down = dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y)).rgb, ImageProcessBlueNoiseLuma);
                float cross = abs(left - right) + abs(up - down);
                float local = abs(center - (left + right + up + down) * 0.25);
                return saturate(cross + local * 2.0);
            }

            half4 FragBlueNoise(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float p0Default = 1.0 - step(0.0001, dot(abs(_LayerParams0), float4(1.0, 1.0, 1.0, 1.0)));
                float p1Default = 1.0 - step(0.0001, dot(abs(_LayerParams1), float4(1.0, 1.0, 1.0, 1.0)));

                float mode = round(_LayerParams0.x);
                float localAmount = lerp(_LayerParams0.y, 0.45, p0Default);
                float scale = lerp(_LayerParams0.z, 1.0, p0Default);
                float speed = lerp(_LayerParams0.w, 0.0, p0Default);
                float contrast = lerp(_LayerParams1.x, 1.0, p1Default);
                float defaultModeValue0 = mode < 0.5 ? 0.85 : mode < 1.5 ? 8.0 : mode < 2.5 ? 1.0 : 5.5;
                float defaultModeValue1 = mode < 0.5 ? 0.25 : mode < 1.5 ? 1.0 : mode < 2.5 ? 0.78 : 0.45;
                float defaultModeValue2 = mode < 0.5 ? 0.75 : mode < 1.5 ? 0.8 : mode < 2.5 ? 0.35 : 0.72;
                float modeValue0 = lerp(_LayerParams1.y, defaultModeValue0, p1Default);
                float modeValue1 = lerp(_LayerParams1.z, defaultModeValue1, p1Default);
                float modeValue2 = lerp(_LayerParams1.w, defaultModeValue2, p1Default);
                float amount = saturate(_Intensity) * saturate(localAmount);

                if (amount <= 0.0001)
                {
                    return source;
                }

                float2 pixel = input.texcoord * _ScreenParams.xy;
                float noise = ImageProcessBlueNoiseValue(input.texcoord, pixel, scale, contrast, speed, 0.0);
                float3 noise3 = ImageProcessBlueNoise3(input.texcoord, pixel, scale, contrast, speed);
                float luma = dot(saturate(source.rgb), ImageProcessBlueNoiseLuma);
                float3 result = source.rgb;

                if (mode < 0.5)
                {
                    float3 signedNoise = noise3 * 2.0 - 1.0;
                    float colored = saturate(modeValue1);
                    float highlightProtect = saturate(modeValue2);
                    float lumaMask = lerp(1.0, 1.0 - sqrt(saturate(luma)), highlightProtect);
                    result = source.rgb + lerp(signedNoise.rrr, signedNoise, colored) * modeValue0 * 0.08 * lumaMask;
                }
                else if (mode < 1.5)
                {
                    float steps = max(modeValue0, 2.0);
                    float colorAmount = saturate(modeValue1);
                    float3 quantized = float3(
                        ImageProcessBlueNoiseQuantize(source.r, steps, noise3.r),
                        ImageProcessBlueNoiseQuantize(source.g, steps, noise3.g),
                        ImageProcessBlueNoiseQuantize(source.b, steps, noise3.b));
                    float mono = ImageProcessBlueNoiseQuantize(luma, steps, noise);
                    result = lerp(float3(mono, mono, mono), quantized, colorAmount);
                }
                else if (mode < 2.5)
                {
                    float densityScale = max(modeValue0, 0.05);
                    float inkOpacity = saturate(modeValue1);
                    float paperPreserve = saturate(modeValue2);
                    float density = saturate(pow(saturate(1.0 - luma), 1.0 / max(contrast, 0.01)) * densityScale);
                    float dotMask = step(noise, density);
                    float3 paper = lerp(float3(1.0, 1.0, 1.0), source.rgb, paperPreserve);
                    result = lerp(paper, saturate(_LayerColor.rgb), dotMask * inkOpacity);
                }
                else
                {
                    float edgeStrength = max(modeValue0, 0.0);
                    float edgeTint = saturate(modeValue1);
                    float colorPreserve = saturate(modeValue2);
                    float edge = saturate(ImageProcessBlueNoiseEdge(input.texcoord) * edgeStrength);
                    float edgeMask = step(noise, edge);
                    float3 rough = source.rgb + (noise3 * 2.0 - 1.0) * edge * 0.35;
                    float3 tinted = lerp(rough, saturate(_LayerColor.rgb), edgeMask * edgeTint * (1.0 - colorPreserve));
                    result = lerp(source.rgb, tinted, edge);
                }

                float hdrPreserve = mode >= 0.5 && mode < 1.5 ? saturate(modeValue2) : 0.0;
                float3 hdrResidual = max(source.rgb - saturate(source.rgb), 0.0) * hdrPreserve;
                result = lerp(source.rgb, max(result, 0.0) + hdrResidual, amount);
                return half4(max(result, 0.0), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
