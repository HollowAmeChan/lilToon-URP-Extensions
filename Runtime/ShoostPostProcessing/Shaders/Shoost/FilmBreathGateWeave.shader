Shader "Hidden/lilToon-Shoost/URP/Shoost/FilmBreathGateWeave"
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
            Name "Shoost Film"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFilm

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerTextureEnabled;
            float4 _LayerParams0; // x mode, y LUT/filter type, z LUT strength, w sharpen
            float4 _LayerParams1; // x grain intensity, y grain size, z gate weave amount
            TEXTURE2D_X(_LayerTexture);

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

            float ProfileValue(float mode, float v60, float v70, float v80, float v90)
            {
                if (mode < 0.5)
                {
                    return v60;
                }

                if (mode < 1.5)
                {
                    return v70;
                }

                if (mode < 2.5)
                {
                    return v80;
                }

                return v90;
            }

            float3 LinearToSrgb(float3 color)
            {
                color = saturate(color);
                float3 low = color * 12.92;
                float3 high = 1.055 * pow(max(color, 0.0001), 1.0 / 2.4) - 0.055;
                return lerp(high, low, step(color, float3(0.0031308, 0.0031308, 0.0031308)));
            }

            float3 SrgbToLinear(float3 color)
            {
                color = saturate(color);
                float3 low = color * 0.077399;
                float3 high = pow(max((color + 0.055) * 0.947867, 0.0001), 2.4);
                return lerp(high, low, step(color, float3(0.04045, 0.04045, 0.04045)));
            }

            float3 SampleScene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float3 SampleStripLut(float3 color)
            {
                float3 lutCoord = saturate(LinearToSrgb(color));
                const float lutSize = 32.0;
                const float lutSizeMinusOne = 31.0;
                float blue = lutCoord.b * lutSizeMinusOne;
                float blue0 = floor(blue);
                float blue1 = min(blue0 + 1.0, lutSizeMinusOne);
                float2 uv0 = float2((lutCoord.r * lutSizeMinusOne + blue0 * lutSize + 0.5) / (lutSize * lutSize), (lutCoord.g * lutSizeMinusOne + 0.5) / lutSize);
                float2 uv1 = float2((lutCoord.r * lutSizeMinusOne + blue1 * lutSize + 0.5) / (lutSize * lutSize), uv0.y);
                float3 sample0 = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearClamp, uv0).rgb;
                float3 sample1 = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearClamp, uv1).rgb;
                return SrgbToLinear(lerp(sample0, sample1, blue - blue0));
            }

            float2 ApplyGateWeave(float2 uv, float mode, float amount)
            {
                float time = _Time.y;
                float posAmp = ProfileValue(mode, 0.020, 0.020, 0.010, 0.010);
                float posFreq = ProfileValue(mode, 20.0, 20.0, 20.0, 20.0);
                float rotAmp = ProfileValue(mode, 0.050, 0.050, 0.020, 0.012);
                float rotFreq = 15.0;
                float scaleAmp = 0.005;
                float scaleFreq = 5.0;

                float motion = amount * 0.055;
                float2 positionNoise = float2(sin(time * posFreq), sin(time * posFreq * 1.2 + 1.5708));
                float rotationNoise = sin(time * rotFreq);
                float scaleNoise = sin(time * scaleFreq);

                float2 centered = uv - 0.5;
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                centered.x *= aspect;

                float rotation = rotationNoise * rotAmp * motion;
                float s = sin(rotation);
                float c = cos(rotation);
                centered = float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);
                centered /= max(1.0 + scaleNoise * scaleAmp * motion, 0.0001);
                centered.x /= aspect;

                return centered + 0.5 + positionNoise * posAmp * motion;
            }

            float3 ApplyRgbBlur(float2 uv, float3 color, float mode)
            {
                float2 texel = _BlitTexture_TexelSize.xy;
                float radiusR = ProfileValue(mode, 2.0, 3.0, 2.0, 0.7);
                float radiusG = ProfileValue(mode, 0.8, 0.5, 0.25, 0.1);
                float radiusB = ProfileValue(mode, 2.0, 3.0, 2.0, 0.7);
                float2 vectorR = float2(ProfileValue(mode, 0.0, 0.02, 0.02, 0.02), 0.0);
                float2 vectorG = float2(0.0, 0.0);
                float2 vectorB = float2(ProfileValue(mode, 0.0, -0.05, -0.04, -0.05), 0.03);

                float3 blurred = color;
                blurred.r = SampleScene(uv + vectorR * texel * radiusR).r;
                blurred.g = SampleScene(uv + vectorG * texel * radiusG).g;
                blurred.b = SampleScene(uv + vectorB * texel * radiusB).b;
                return lerp(color, blurred, 0.75);
            }

            float3 ApplyFilmBreath(float3 color, float mode, float amount)
            {
                float time = _Time.y;
                float motion = amount * 0.35;
                float exposureAmp = ProfileValue(mode, 0.20, 0.20, 0.07, 0.07);
                float exposureFreq = ProfileValue(mode, 15.0, 15.0, 10.0, 9.3);
                float contrastAmp = ProfileValue(mode, 0.10, 0.30, 0.10, 0.10);
                float contrastFreq = ProfileValue(mode, 12.0, 20.0, 15.0, 15.0);

                float exposure = 1.0 + sin(time * exposureFreq) * exposureAmp * motion;
                float contrast = 1.0 + sin(time * contrastFreq) * contrastAmp * motion;
                color = color * exposure;
                return (color - 0.5) * contrast + 0.5;
            }

            float3 ApplyProfileGrade(float3 color, float mode, float filterType)
            {
                float luma = dot(color, float3(0.2126729, 0.7151522, 0.0721750));
                if (mode < 0.5)
                {
                    float monoLift = filterType < 0.5 ? 0.00 : filterType < 1.5 ? 0.035 : -0.025;
                    float monoContrast = filterType < 0.5 ? 0.90 : filterType < 1.5 ? 0.80 : 1.06;
                    return saturate((luma.xxx - 0.5) * monoContrast + 0.5 + monoLift);
                }

                float3 tint70 = float3(1.05, 0.98, 0.86);
                float3 tint80 = filterType < 0.5 ? float3(1.08, 1.01, 0.90) : float3(1.02, 1.03, 0.92);
                float3 tint90 = filterType < 0.5 ? float3(1.06, 1.00, 0.92) : float3(0.98, 1.04, 0.96);
                float3 tint = mode < 1.5 ? tint70 : mode < 2.5 ? tint80 : tint90;
                float contrast = ProfileValue(mode, 1.0, 0.99, 0.99, 0.98);
                float brightness = ProfileValue(mode, 0.0, 0.0, 0.05, -0.05);
                return saturate((color * tint - 0.5) * contrast + 0.5 + brightness);
            }

            float3 ApplyOldFilmTone(float3 color, float mode)
            {
                float fade = ProfileValue(mode, 0.70, 0.70, 0.70, 0.20);
                float noiseGain = ProfileValue(mode, 0.50, 0.40, 0.30, 0.50);
                float saturation = ProfileValue(mode, 5.0, -10.0, 5.0, 5.0) * 0.01;
                float brightness = ProfileValue(mode, -10.0, 0.0, 5.0, 5.0) * 0.01;
                float contrast = ProfileValue(mode, -2.0, -1.0, -1.0, -2.0) * 0.01;

                float luma = dot(color, float3(0.2126729, 0.7151522, 0.0721750));
                float3 toned = lerp(luma.xxx, color, 1.0 + saturation);
                toned = (toned - 0.5) * (1.0 + contrast) + 0.5 + brightness;
                float frame = floor(_Time.y * 24.0);
                toned += (Hash12(float2(frame, frame) + mode) - 0.5) * noiseGain * 0.025;
                return lerp(color, toned, fade);
            }

            float3 ApplyGrain(float2 uv, float3 color, float intensity, float size)
            {
                intensity = saturate(intensity);
                if (intensity <= 0.0001)
                {
                    return color;
                }

                float grainSize = max(size, 0.3);
                float2 tiling = max(_ScreenParams.xy / (128.0 * grainSize), 1.0);
                float frame = floor(_Time.y * 60.0);
                float2 grainCell = floor((uv * tiling + frame * float2(0.06711056, 0.00583715)) * 128.0);
                float3 noise = Hash32(grainCell);
                float luma = sqrt(dot(saturate(color), float3(0.2126729, 0.7151522, 0.0721750)));
                float weight = intensity * (1.0 - 0.9 * luma);
                return color + (noise - 0.5) * weight;
            }

            float3 ProcessFilm(float2 baseUV, float mode, float filterType, float lutStrength, float grainIntensity, float grainSize, float weaveAmount)
            {
                float2 uv = ApplyGateWeave(baseUV, mode, weaveAmount);
                float3 color = SampleScene(uv);
                color = ApplyFilmBreath(color, mode, weaveAmount);
                color = ApplyRgbBlur(uv, color, mode);

                float3 graded = _LayerTextureEnabled > 0.5 ? SampleStripLut(color) : ApplyProfileGrade(color, mode, filterType);
                graded = ApplyProfileGrade(graded, mode, filterType);
                color = lerp(color, graded, lutStrength);
                color = ApplyOldFilmTone(color, mode);
                color = ApplyGrain(baseUV, color, grainIntensity, grainSize);
                return color;
            }

            half4 FragFilm(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float amount = saturate(_Intensity);
                float2 baseUV = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV);
                if (amount <= 0.0001)
                {
                    return original;
                }

                float mode = clamp(round(_LayerParams0.x), 0.0, 3.0);
                float filterType = max(round(_LayerParams0.y), 0.0);
                float lutStrength = saturate(_LayerParams0.z);
                float sharpen = saturate(_LayerParams0.w);
                float grainIntensity = saturate(_LayerParams1.x);
                float grainSize = clamp(_LayerParams1.y, 0.3, 3.0);
                float weaveAmount = clamp(_LayerParams1.z, 0.0, 2.0);

                float3 color = ProcessFilm(baseUV, mode, filterType, lutStrength, grainIntensity, grainSize, weaveAmount);

                if (sharpen > 0.0001)
                {
                    float2 texel = _BlitTexture_TexelSize.xy;
                    float3 north = ProcessFilm(baseUV + float2(0.0, texel.y), mode, filterType, lutStrength, grainIntensity, grainSize, weaveAmount);
                    float3 south = ProcessFilm(baseUV - float2(0.0, texel.y), mode, filterType, lutStrength, grainIntensity, grainSize, weaveAmount);
                    float3 east = ProcessFilm(baseUV + float2(texel.x, 0.0), mode, filterType, lutStrength, grainIntensity, grainSize, weaveAmount);
                    float3 west = ProcessFilm(baseUV - float2(texel.x, 0.0), mode, filterType, lutStrength, grainIntensity, grainSize, weaveAmount);
                    color += (color * 4.0 - north - south - east - west) * sharpen * 0.45;
                }

                return half4(lerp(original.rgb, saturate(color), amount), original.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
