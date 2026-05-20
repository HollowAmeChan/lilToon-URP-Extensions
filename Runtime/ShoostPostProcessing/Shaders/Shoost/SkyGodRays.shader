Shader "Hidden/lilToon-Shoost/URP/Shoost/SkyGodRays"
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
            Name "Shoost Sky God Rays"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSkyGodRays

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            static const int ShoostSkyMaxSamples = 40;

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x radial center x, y radial center y, z AE radial amount, w layer exposure
            float4 _LayerParams1; // x/y fractal scale at 1920x1080, z AE contrast, w AE brightness
            float4 _LayerParams2; // x chromatic strength, y chromatic falloff pixels, z evolution speed, w seed
            float4 _LayerParams3; // x radial samples, y matte softness, z layer preview, w color dodge

            float4 ShoostSkyResolveParams(float4 value, float4 fallbackValue)
            {
                return dot(abs(value), float4(1.0, 1.0, 1.0, 1.0)) <= 0.00001 ? fallbackValue : value;
            }

            float ShoostSkyHash21(float2 value)
            {
                value = frac(value * float2(0.1031, 0.11369));
                value += dot(value, value.yx + 19.19);
                return frac((value.x + value.y) * value.x);
            }

            float ShoostSkyLinearNoise(float2 uv, float seed)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                float2 seedOffset = float2(seed, seed * 1.37);

                float a = ShoostSkyHash21(cell + seedOffset);
                float b = ShoostSkyHash21(cell + float2(1.0, 0.0) + seedOffset);
                float c = ShoostSkyHash21(cell + float2(0.0, 1.0) + seedOffset);
                float d = ShoostSkyHash21(cell + float2(1.0, 1.0) + seedOffset);
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            float ShoostSkyEvolvedLinearNoise(float2 uv, float seed, float evolution)
            {
                float cycle = evolution * 0.22;
                float cycleIndex = floor(cycle);
                float cycleBlend = frac(cycle);
                cycleBlend = cycleBlend * cycleBlend * (3.0 - 2.0 * cycleBlend);

                float2 baseUv = uv + float2(seed * 7.13, seed * 3.41);
                float first = ShoostSkyLinearNoise(baseUv, seed + cycleIndex * 17.0);
                float second = ShoostSkyLinearNoise(baseUv, seed + (cycleIndex + 1.0) * 17.0);
                return lerp(first, second, cycleBlend);
            }

            float ShoostSkyFractalMatte(float2 uv, float4 p1, float4 p2, float4 p3)
            {
                float2 screenSize = max(_ScreenParams.xy, float2(1.0, 1.0));
                float2 scale = max(p1.xy * (screenSize / float2(1920.0, 1080.0)), float2(16.0, 16.0));
                float2 noiseUv = uv * screenSize / scale;

                float raw = ShoostSkyEvolvedLinearNoise(noiseUv, p2.w, _Time.y * p2.z);
                float contrast = max(p1.z, 1.0) * 0.01;
                float brightness = p1.w * 0.01;
                float graded = saturate((raw - 0.5) * contrast + 0.5 + brightness);

                float softness = saturate(p3.y);
                float matte = smoothstep(0.06 + softness * 0.08, 0.74 - softness * 0.36, graded);
                matte = pow(matte, lerp(2.25, 0.95, softness));
                return matte;
            }

            float3 ShoostSkyChromaticLayer(float2 uv, float2 center, float4 p1, float4 p2, float4 p3)
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float radialDistance = length((uv - center) * float2(aspect, 1.0));
                float falloffPixels = max(p2.y, 1.0);
                float falloffUv = falloffPixels / max(max(_ScreenParams.x, _ScreenParams.y), 1.0);
                float falloff = saturate(radialDistance / max(falloffUv * 4.0, 0.001));

                float splitPixels = max(p2.x, 0.0) * falloffPixels * 0.33 * lerp(0.35, 1.0, falloff);
                float2 split = float2(0.0, splitPixels / max(_ScreenParams.y, 1.0));

                float core = ShoostSkyFractalMatte(uv, p1, p2, p3);
                float r1 = ShoostSkyFractalMatte(saturate(uv - split), p1, p2, p3);
                float g1 = ShoostSkyFractalMatte(uv, p1, p2, p3);
                float b1 = ShoostSkyFractalMatte(saturate(uv + split), p1, p2, p3);
                float r2 = ShoostSkyFractalMatte(saturate(uv - split * 2.0), p1, p2, p3);
                float b2 = ShoostSkyFractalMatte(saturate(uv + split * 2.0), p1, p2, p3);

                float3 chroma = float3(r1 + r2 * 0.32, g1 * 0.86, b1 + b2 * 0.32);
                chroma += core.xxx * 0.16;
                return saturate(chroma);
            }

            float3 ShoostSkyZoomBlurLayer(float2 uv, float2 center, float radialAmount, float samples, float4 p1, float4 p2, float4 p3)
            {
                float amount = saturate(radialAmount / 180.0);
                float3 sum = 0.0;
                float weightSum = 0.0;

                [loop]
                for (int i = 0; i < ShoostSkyMaxSamples; i++)
                {
                    if (i >= samples)
                    {
                        break;
                    }

                    float t = ((float)i + 0.5) / max(samples, 1.0);
                    float2 sampleUv = lerp(uv, center, t * amount);
                    float weight = lerp(1.0, 0.55, t);
                    sum += ShoostSkyChromaticLayer(saturate(sampleUv), center, p1, p2, p3) * weight;
                    weightSum += weight;
                }

                return sum / max(weightSum, 0.0001);
            }

            float3 ShoostSkyColorDodge(float3 underlying, float3 source)
            {
                source = saturate(source);
                float3 result = underlying / max(1.0 - source, 0.045);
                return min(result, 6.0);
            }

            half4 FragSkyGodRays(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float opacity = saturate(_Intensity);
                if (opacity <= 0.0001)
                {
                    return source;
                }

                float4 p0 = ShoostSkyResolveParams(_LayerParams0, float4(1.22, 0.99, 181.0, 1.08));
                float4 p1 = ShoostSkyResolveParams(_LayerParams1, float4(130.0, 85.0, 234.0, -53.0));
                float4 p2 = ShoostSkyResolveParams(_LayerParams2, float4(1.04, 146.0, 3.0, 3.0));
                float4 p3 = ShoostSkyResolveParams(_LayerParams3, float4(32.0, 0.36, 0.0, 0.21));

                float2 center = p0.xy;
                float samples = clamp(round(p3.x), 6.0, (float)ShoostSkyMaxSamples);
                float3 layer = ShoostSkyZoomBlurLayer(input.texcoord, center, p0.z, samples, p1, p2, p3);

                float3 layerColor = _LayerColor.rgb;
                layerColor = dot(abs(layerColor), float3(1.0, 1.0, 1.0)) <= 0.00001 ? float3(1.0, 1.0, 1.0) : layerColor;
                float layerAlpha = _LayerColor.a <= 0.0001 ? 1.0 : _LayerColor.a;

                float exposure = max(p0.w, 0.0);
                float preview = saturate(p3.z);
                float dodgeAmount = saturate(p3.w);
                float3 colorLayer = saturate(layer * layerColor * exposure);
                if (preview > 0.999)
                {
                    return half4(colorLayer * opacity * layerAlpha, source.a);
                }

                float amount = opacity * layerAlpha;
                float3 dodged = ShoostSkyColorDodge(source.rgb, colorLayer * amount);
                float3 screened = 1.0 - (1.0 - saturate(source.rgb)) * (1.0 - saturate(colorLayer * amount));
                float3 result = lerp(source.rgb, lerp(screened, dodged, dodgeAmount), amount);
                return half4(max(result, 0.0), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
