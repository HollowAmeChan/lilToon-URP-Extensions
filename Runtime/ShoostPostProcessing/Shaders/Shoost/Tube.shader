Shader "Hidden/lilToon-Shoost/URP/Shoost/Tube"
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
            Name "Shoost Tube"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTube

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerTextureEnabled;
            float4 _LayerParams0; // x mode, y sharpen, z gate weave irregularity, w motion trail
            TEXTURE2D_X(_LayerTexture);
            static const float TubeGateWeaveMotionScale = 0.055;
            static const float TubeFilmBreathScale = 0.35;

            float Hash12(float2 p)
            {
                return frac(sin(dot(p, float2(89.44, 19.36))) * 22189.220703);
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

            float3 ProfileMotion(float mode)
            {
                if (mode < 0.5)
                {
                    return float3(0.5, 0.5, 0.5);
                }

                if (mode < 1.5)
                {
                    return float3(0.7, 0.4, 0.2);
                }

                if (mode < 2.5)
                {
                    return float3(1.0, 0.6, 0.4);
                }

                return float3(0.4, 0.2, 0.1);
            }

            float3 RgbToYiq(float3 rgb)
            {
                return float3(
                    dot(rgb, float3(0.299, 0.587, 0.114)),
                    dot(rgb, float3(0.596, -0.274, -0.322)),
                    dot(rgb, float3(0.211, -0.523, 0.313)));
            }

            float3 YiqToRgb(float3 yiq)
            {
                return saturate(float3(
                    dot(yiq, float3(1.0, 0.956, 0.621)),
                    dot(yiq, float3(1.0, -0.272, -0.647)),
                    dot(yiq, float3(1.0, -1.106, 1.703))));
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

            float3 ApplyTvProfileLut(float3 color, float mode)
            {
                if (_LayerTextureEnabled > 0.5)
                {
                    float contribution = ProfileValue(mode, 1.0, 1.0, 0.5, 0.5);
                    return lerp(color, SampleStripLut(color), contribution);
                }

                if (mode < 0.5)
                {
                    float luma = dot(color, float3(0.299, 0.587, 0.114));
                    return luma.xxx;
                }

                return color;
            }

            float2 ApplyGateWeave(float2 uv, float mode, float irregularity)
            {
                float time = _Time.y;
                float posAmp = ProfileValue(mode, 0.020, 0.020, 0.020, 0.010);
                float posFreq = ProfileValue(mode, 20.0, 20.0, 15.0, 10.0);
                float rotAmp = ProfileValue(mode, 0.050, 0.030, 0.020, 0.010);
                float rotFreq = 15.0;
                float scaleAmp = 0.005;
                float scaleFreq = 5.0;

                irregularity *= TubeGateWeaveMotionScale;
                float2 positionNoise = float2(sin(time * posFreq), sin(time * posFreq * 1.2 + 1.5708));
                float rotationNoise = sin(time * rotFreq);
                float scaleNoise = sin(time * scaleFreq);

                float2 centered = uv - 0.5;
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                centered.x *= aspect;

                float rotation = rotationNoise * rotAmp * irregularity;
                float s = sin(rotation);
                float c = cos(rotation);
                centered = float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);
                centered /= max(1.0 + scaleNoise * scaleAmp * irregularity, 0.0001);
                centered.x /= aspect;

                return centered + 0.5 + positionNoise * posAmp * irregularity;
            }

            float3 ApplyFilmBreathColor(float3 color, float mode, float irregularity)
            {
                float time = _Time.y;
                irregularity *= TubeFilmBreathScale;

                float exposureAmp = ProfileValue(mode, 0.30, 0.20, 0.10, 0.20);
                float exposureFreq = 16.0;
                float contrastAmp = ProfileValue(mode, 0.10, 0.30, 0.30, 0.30);
                float contrastFreq = ProfileValue(mode, 12.0, 15.0, 15.0, 15.0);
                float colorAmp = 0.20;
                float colorFreq = 16.0;

                float exposure = 1.0 + sin(time * exposureFreq) * exposureAmp * irregularity;
                float contrast = 1.0 + sin(time * contrastFreq) * contrastAmp * irregularity;
                float3 colorWave = float3(
                    sin(time * colorFreq),
                    sin(time * colorFreq + 2.0944),
                    sin(time * colorFreq + 4.1888)) * colorAmp * irregularity;

                color = color * exposure;
                color = (color - 0.5) * contrast + 0.5;
                return color + colorWave;
            }

            float2 ApplySixtiesJitter(float2 uv, float mode, float irregularity)
            {
                if (mode >= 0.5 || irregularity <= 0.0001)
                {
                    return uv;
                }

                irregularity *= TubeGateWeaveMotionScale;
                float lineIndex = floor(uv.y * _ScreenParams.y / 3.0);
                float horizontal = (Hash12(float2(lineIndex, floor(_Time.y * 24.0))) - 0.5) * 0.0066 * irregularity;
                float vertical = sin(_Time.y * 0.99 * 6.2831853 + Hash12(float2(floor(_Time.y * 25.0), 4.7)) * 6.2831853) * 0.0032 * irregularity;
                return uv + float2(horizontal, vertical);
            }

            float3 ApplyRgbBlur(float2 uv, float3 color, float mode)
            {
                float radiusR = ProfileValue(mode, 3.0, 2.0, 1.0, 0.7);
                float radiusG = ProfileValue(mode, 0.5, 0.5, 0.0, 0.0);
                float radiusB = ProfileValue(mode, 3.0, 1.0, 2.0, 0.7);
                float2 vectorR = float2(0.0, 0.0);
                float2 vectorG = mode < 0.5 ? float2(0.02, 0.0) : float2(0.0, 0.0);
                float2 vectorB = mode < 0.5 ? float2(0.0, 0.03) : mode < 1.5 ? float2(-0.3, 0.03) : mode < 2.5 ? float2(0.0, 0.0) : float2(-0.05, 0.03);
                float intensity = 1.0;
                float2 texel = _BlitTexture_TexelSize.xy;

                float3 blurred = color;
                blurred.r = SampleScene(uv + vectorR * texel * radiusR).r;
                blurred.g = SampleScene(uv + vectorG * texel * radiusG).g;
                blurred.b = SampleScene(uv + vectorB * texel * radiusB).b;
                return lerp(color, blurred, saturate(intensity));
            }

            float3 ApplyTubeBleeding(float2 uv, float3 color, float mode)
            {
                float active = step(0.5, mode);
                float bleeding = ProfileValue(mode, 0.5, 0.5, 0.3, 0.3);
                float downScale = ProfileValue(mode, 1.0, 2.0, 2.0, 2.0);
                float fringing = ProfileValue(mode, 0.0, 0.0, 0.1, 0.2);
                float opacity = ProfileValue(mode, 1.0, 0.5, 0.5, 0.3);

                float3 yiq = RgbToYiq(LinearToSrgb(color));
                float sampleCount = max(round(bleeding * 6.0), 1.0);
                float2 chromaOffset = float2(_BlitTexture_TexelSize.x * downScale * 2.0, 0.0);
                float2 accum = yiq.yz;
                [unroll]
                for (int i = 1; i <= 6; i++)
                {
                    if ((float)i <= sampleCount)
                    {
                        float fi = (float)i;
                        float3 left = RgbToYiq(LinearToSrgb(SampleScene(uv - chromaOffset * fi)));
                        float3 right = RgbToYiq(LinearToSrgb(SampleScene(uv + chromaOffset * fi)));
                        accum.x += left.y;
                        accum.y += right.z;
                    }
                }

                yiq.yz = accum / (sampleCount + 1.0);
                float3 tube = SrgbToLinear(YiqToRgb(yiq));
                if (fringing > 0.0001)
                {
                    float2 fringeOffset = float2(_BlitTexture_TexelSize.x * fringing * 16.0, 0.0);
                    tube.r = SampleScene(uv + fringeOffset).r;
                    tube.b = SampleScene(uv - fringeOffset).b;
                }

                return lerp(color, tube, active * opacity);
            }

            float3 ApplyMotionTrail(float2 uv, float3 color, float mode, float enabled)
            {
                if (enabled <= 0.5)
                {
                    return color;
                }

                float trailFrames = ProfileValue(mode, 3.0, 3.0, 2.0, 2.0);
                float3 persistence = ProfileMotion(mode);
                float2 direction = float2(_BlitTexture_TexelSize.x * 8.0, _BlitTexture_TexelSize.y * -3.0);
                float3 accum = color;
                float3 weight = 1.0;

                [unroll]
                for (int i = 1; i <= 3; i++)
                {
                    if ((float)i <= trailFrames)
                    {
                        float fi = (float)i;
                        float3 sampleColor = SampleScene(uv - direction * fi);
                        float3 sampleWeight = pow(persistence, fi);
                        accum += sampleColor * sampleWeight;
                        weight += sampleWeight;
                    }
                }

                return accum / weight;
            }

            float3 ProcessTubeBeforeSharpen(float2 baseUV, float mode, float irregularity, float motionTrail)
            {
                float2 uv = ApplyGateWeave(baseUV, mode, irregularity);
                uv = ApplySixtiesJitter(uv, mode, irregularity);

                float3 color = SampleScene(uv);
                color = ApplyFilmBreathColor(color, mode, irregularity);
                color = ApplyRgbBlur(uv, color, mode);
                color = ApplyTvProfileLut(color, mode);
                color = ApplyTubeBleeding(uv, color, mode);
                color = ApplyMotionTrail(uv, color, mode, motionTrail);

                float time = _Time.y;
                float exposureNoise = Hash12(float2(floor(time * 16.0), mode + 21.0));
                color = (color - 0.5) * (1.0 + (exposureNoise - 0.5) * 0.10 * irregularity * TubeFilmBreathScale) + 0.5;
                color += (Hash12(baseUV * _ScreenParams.xy + floor(time * 24.0)) - 0.5) * ProfileValue(mode, 0.035, 0.025, 0.018, 0.010);
                return color;
            }

            float3 ApplySharpen(float2 baseUV, float3 color, float mode, float amount, float irregularity, float motionTrail)
            {
                amount = saturate(amount);
                if (amount <= 0.0001)
                {
                    return color;
                }

                float2 texel = _BlitTexture_TexelSize.xy;
                float3 sum =
                    ProcessTubeBeforeSharpen(baseUV + texel * float2(-1.0, -1.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2( 0.0, -1.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2( 1.0, -1.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2(-1.0,  0.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2( 1.0,  0.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2(-1.0,  1.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2( 0.0,  1.0), mode, irregularity, motionTrail) +
                    ProcessTubeBeforeSharpen(baseUV + texel * float2( 1.0,  1.0), mode, irregularity, motionTrail);
                return color + (color * 8.0 - sum) * amount;
            }

            half4 FragTube(Varyings input) : SV_Target
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
                float sharpen = saturate(_LayerParams0.y);
                float irregularity = clamp(_LayerParams0.z, 0.0, 2.0);
                float motionTrail = step(0.5, _LayerParams0.w);

                float3 color = ProcessTubeBeforeSharpen(baseUV, mode, irregularity, motionTrail);
                color = ApplySharpen(baseUV, color, mode, sharpen, irregularity, motionTrail);

                float3 hdrResidual = max(original.rgb - saturate(original.rgb), 0.0);
                return half4(lerp(original.rgb, max(color, 0.0) + hdrResidual, amount), original.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
