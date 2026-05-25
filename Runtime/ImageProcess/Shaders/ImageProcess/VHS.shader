Shader "Hidden/lilToon/URP/ImageProcess/VHS"
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
            Name "ImageProcess VHS"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVHS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x type, y noise intensity, z sharpen, w scanline
            float4 _LayerParams1; // x scanline size
            float _LayerTextureEnabled;
            TEXTURE2D_X(_LayerTexture);

            float ImageProcessHash(float2 p)
            {
                return frac(sin(dot(p, float2(89.44, 19.36))) * 22189.220703);
            }

            float ImageProcessValueNoise(float2 p)
            {
                float2 grid = p * 4.0;
                float2 cell = floor(grid) * 0.25;
                float2 weight = frac(grid);
                weight = weight * weight * (3.0 - 2.0 * weight);

                float a = ImageProcessHash(cell);
                float b = ImageProcessHash(cell + float2(0.25, 0.0));
                float c = ImageProcessHash(cell + float2(0.0, 0.25));
                float d = ImageProcessHash(cell + float2(0.25, 0.25));
                return lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 Hash32(float2 p)
            {
                return float3(Hash12(p), Hash12(p + 17.17), Hash12(p + 43.43));
            }

            float3 ProfileNoise2(float type)
            {
                if (type < 0.5)
                {
                    return float3(0.50, 0.20, 0.02);
                }

                if (type < 1.5)
                {
                    return float3(0.80, 0.30, 0.02);
                }

                return float3(1.00, 0.50, 0.05);
            }

            float2 ProfileEdgeNoise(float type)
            {
                if (type < 0.5)
                {
                    return float2(0.15, 0.50);
                }

                if (type < 1.5)
                {
                    return float2(0.25, 0.50);
                }

                return float2(0.50, 1.00);
            }

            float2 GetNoise2UV(float2 uv, float waveAmount, float tapeIntensity, out float tapeLine)
            {
                float time = _Time.y;
                float firstWave = ImageProcessValueNoise(float2(uv.y + 1.0, time + 1.0));
                float secondWave = ImageProcessValueNoise(float2(uv.y * 100.0 + 1.0, time * 10.0 + 1.0));
                float offset = (firstWave * 0.5 - 0.5) * 0.005 * waveAmount;
                offset += (secondWave * 0.5 - 0.5) * 0.010 * waveAmount;

                float tapePattern = sin(uv.y * 8.0 - time * 3.769912);
                float tapeNoise = ImageProcessValueNoise(float2(uv.y * 100.0 + 1.0, time * 5.0 + 1.0));
                tapeLine = min(max((tapePattern - 0.92) * tapeNoise * 0.5, 0.0), 0.01) * 10.0;

                float tapeJitter = max(ImageProcessValueNoise(float2(uv.y * 100.0 + 13.0, time * 5.0 + 7.0)) - 0.5, 0.0);
                return uv + float2(offset - tapeJitter * tapeLine * tapeIntensity, 0.0);
            }

            float3 GetNoise2Color(float2 uv, float3 warpedColor, float type)
            {
                float frame = floor(_Time.y * 30.0);
                float3 colorNoise = Hash32(uv * _ScreenParams.xy + frame);
                float monoNoise = dot(colorNoise, float3(0.299, 0.587, 0.114));
                colorNoise = lerp(monoNoise.xxx, colorNoise, step(1.5, type));

                float lineNoise = ImageProcessValueNoise(float2(uv.y * 100.0 + 1.0, _Time.y * 5.0 + 1.0));
                float pixelNoise = Hash12(uv * _ScreenParams.xy + frame * 13.37);
                float flickerMask = smoothstep(0.86 - type * 0.04, 1.0, lineNoise * 0.65 + pixelNoise * 0.35);
                float noiseOpacity = lerp(0.035, 0.085, saturate(type * 0.5)) * flickerMask;
                return lerp(warpedColor, colorNoise, noiseOpacity);
            }

            float3 ApplyEdgeNoise(float3 color, float2 uv, float2 warpedUV, float type, float gain)
            {
                float2 edgeProfile = ProfileEdgeNoise(type);
                float height = max(edgeProfile.x - 0.099, 0.01);
                float bottomRegion = saturate(floor(height / max(uv.y, 0.0001)));
                if (bottomRegion <= 0.0001)
                {
                    return color;
                }

                float2 noiseUV = frac(float2(warpedUV.x, warpedUV.y + _Time.y * 0.20));
                float3 edgeNoise;
                if (_LayerTextureEnabled > 0.5)
                {
                    edgeNoise = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearRepeat, noiseUV).rgb;
                }
                else
                {
                    edgeNoise = Hash32(noiseUV * 256.0);
                }

                float intensity = edgeProfile.y * gain;
                float distance = 1.0 - uv.y / height;
                float3 edge = color + 0.5 * (distance * edgeNoise * intensity) * (edgeNoise * intensity - color);
                return lerp(color, edge, bottomRegion);
            }

            half4 FragVHS(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float amount = saturate(_Intensity);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (amount <= 0.0001)
                {
                    return original;
                }

                float type = clamp(round(_LayerParams0.x), 0.0, 2.0);
                float userNoise = saturate(_LayerParams0.y);
                float sharpen = saturate(_LayerParams0.z);
                float scanlineEnabled = step(0.5, _LayerParams0.w);
                float gain = 1.0 + userNoise;

                float3 noise2Profile = ProfileNoise2(type);
                float noise2Fade = saturate(noise2Profile.x * amount);
                float waveAmount = noise2Profile.y * gain;
                float tapeIntensity = noise2Profile.z * gain;

                float tapeLine;
                float2 warpedUV = GetNoise2UV(uv, waveAmount, tapeIntensity, tapeLine);
                float2 texel = 1.0 / _ScreenParams.xy;
                float rgbBlur = lerp(1.5, 4.0, type * 0.5) * texel.x * 1.5;

                half4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV);
                float3 rgbOffset;
                rgbOffset.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV + float2(rgbBlur, 0.0)).r;
                rgbOffset.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV + float2(rgbBlur * 0.1, 0.0)).g;
                rgbOffset.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV - float2(rgbBlur, 0.0)).b;

                float3 warpedColor = lerp(center.rgb, rgbOffset, saturate(0.45 + type * 0.175));
                float3 noise2Color = GetNoise2Color(uv, warpedColor, type);
                noise2Color *= 1.0 - tapeLine * tapeIntensity;
                float3 color = lerp(original.rgb, noise2Color, noise2Fade);

                float sharpenAmount = sharpen * 1.75;
                if (sharpenAmount > 0.0001)
                {
                    float3 north = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV + float2(0.0, texel.y)).rgb;
                    float3 south = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV - float2(0.0, texel.y)).rgb;
                    float3 east = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV + float2(texel.x, 0.0)).rgb;
                    float3 west = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV - float2(texel.x, 0.0)).rgb;
                    color += (center.rgb * 4.0 - north - south - east - west) * sharpenAmount * 0.5;
                }

                color = ApplyEdgeNoise(color, uv, warpedUV, type, gain * amount);

                if (scanlineEnabled > 0.5)
                {
                    float scanlineSize = max(_LayerParams1.x, 0.01);
                    float scanlineFrequency = lerp(900.0, 120.0, scanlineSize);
                    float scan = sin(uv.y * scanlineFrequency * 6.2831853);
                    float mask = lerp(0.68, 1.14, smoothstep(-0.25, 0.85, scan));
                    color *= mask;
                }

                float3 hdrResidual = max(original.rgb - saturate(original.rgb), 0.0);
                return half4(max(color, 0.0) + hdrResidual, original.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
