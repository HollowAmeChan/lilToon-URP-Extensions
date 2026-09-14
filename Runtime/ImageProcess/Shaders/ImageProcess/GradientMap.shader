Shader "Hidden/lilToon/URP/ImageProcess/GradientMap"
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
            Name "ImageProcess GradientMap"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGradientMap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // HoSampleGradient*: the same ramp sampling the MaterialGradient module uses.
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/MaterialGradient/Shaders/HoMaterialGradientSampling.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            // x input source (0 luma, 1 red, 2 green, 3 blue, 4 maximum, 5 average, 6 saturation),
            // y black point, z white point, w output dither (8-bit LSB)
            float4 _LayerParams0;
            // x interpolation space (bake-time only, kept in the layer for tooling), y reverse,
            // z posterise bands (0/1 = off, >=2 = band count), w reserved
            float4 _LayerParams6;
            TEXTURE2D(_LayerRampTex);
            SAMPLER(sampler_LayerRampTex);
            float _LayerRampTexEnabled;
            // (1 / width, 1 / height, width, height) of the baked ramp, bound by the renderer.
            float4 _LayerRampTexelSize;

            // Same 24 Photoshop/PDF blend modes as LayerBlit.shader and Gradient.shader. Kept local
            // on purpose: making it an include is a separate change that also touches those two
            // shaders.
            half3 ColorBurn(half3 baseColor, half3 layerColor)
            {
                return max(1.0 - (1.0 - baseColor) / max(layerColor, 0.0001), 0.0);
            }

            half3 ColorDodge(half3 baseColor, half3 layerColor)
            {
                return max(baseColor / max(1.0 - layerColor, 0.0001), 0.0);
            }

            half3 Overlay(half3 baseColor, half3 layerColor)
            {
                return lerp(2.0 * baseColor * layerColor, 1.0 - 2.0 * (1.0 - baseColor) * (1.0 - layerColor), step(0.5, baseColor));
            }

            half3 SoftLight(half3 baseColor, half3 layerColor)
            {
                half3 dark = baseColor - (1.0 - 2.0 * layerColor) * baseColor * (1.0 - baseColor);
                half3 light = baseColor + (2.0 * layerColor - 1.0) * (sqrt(saturate(baseColor)) - baseColor);
                return lerp(dark, light, step(0.5, layerColor));
            }

            half3 VividLight(half3 baseColor, half3 layerColor)
            {
                half3 burn = ColorBurn(baseColor, 2.0 * layerColor);
                half3 dodge = ColorDodge(baseColor, 2.0 * (layerColor - 0.5));
                return lerp(burn, dodge, step(0.5, layerColor));
            }

            // Photoshop / PDF non-separable blend-mode luminance coefficients. The reference
            // definitions of hue/saturation/color/luminosity use these weights, so they are kept
            // as-is here rather than switched to Rec.709.
            half Lum(half3 color)
            {
                return dot(color, half3(0.3, 0.59, 0.11));
            }

            half Sat(half3 color)
            {
                return max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
            }

            half3 ClipColor(half3 color)
            {
                half l = Lum(color);
                half n = min(color.r, min(color.g, color.b));
                half x = max(color.r, max(color.g, color.b));

                if (n < 0.0)
                {
                    color = l + ((color - l) * l) / max(l - n, 0.0001);
                }

                if (x > 1.0)
                {
                    color = l + ((color - l) * (1.0 - l)) / max(x - l, 0.0001);
                }

                return saturate(color);
            }

            half3 SetLum(half3 color, half luminance)
            {
                return ClipColor(color + (luminance - Lum(color)));
            }

            half3 SetSat(half3 color, half saturation)
            {
                half cMin = min(color.r, min(color.g, color.b));
                half cMax = max(color.r, max(color.g, color.b));
                half delta = cMax - cMin;

                if (delta <= 0.0001)
                {
                    return half3(0.0, 0.0, 0.0);
                }

                return saturate((color - cMin) * saturation / delta);
            }

            half3 ApplyLayerBlend(half3 baseColor, half3 layerColor, float blendMode)
            {
                int mode = (int)round(blendMode);
                if (mode == 0) return layerColor;
                if (mode == 1) return max(baseColor + layerColor, 0.0);
                if (mode == 2) return baseColor * layerColor;
                if (mode == 3) return 1.0 - (1.0 - baseColor) * (1.0 - layerColor);
                if (mode == 4) return min(baseColor, layerColor);
                if (mode == 5) return ColorBurn(baseColor, layerColor);
                if (mode == 6) return max(baseColor + layerColor - 1.0, 0.0);
                if (mode == 7) return max(baseColor, layerColor);
                if (mode == 8) return ColorDodge(baseColor, layerColor);
                if (mode == 9) return Overlay(baseColor, layerColor);
                if (mode == 10) return SoftLight(baseColor, layerColor);
                if (mode == 11) return Overlay(layerColor, baseColor);
                if (mode == 12) return VividLight(baseColor, layerColor);
                if (mode == 13) return max(baseColor + 2.0 * layerColor - 1.0, 0.0);
                if (mode == 14) return lerp(min(baseColor, 2.0 * layerColor), max(baseColor, 2.0 * (layerColor - 0.5)), step(0.5, layerColor));
                if (mode == 15) return step(0.5, VividLight(baseColor, layerColor));
                if (mode == 16) return abs(baseColor - layerColor);
                if (mode == 17) return baseColor + layerColor - 2.0 * baseColor * layerColor;
                if (mode == 18) return max(baseColor - layerColor, 0.0);
                if (mode == 19) return max(baseColor / max(layerColor, 0.0001), 0.0);
                if (mode == 20) return SetLum(SetSat(layerColor, Sat(baseColor)), Lum(baseColor));
                if (mode == 21) return SetLum(SetSat(baseColor, Sat(layerColor)), Lum(baseColor));
                if (mode == 22) return SetLum(layerColor, Lum(baseColor));
                if (mode == 23) return SetLum(baseColor, Lum(layerColor));
                return layerColor;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Value that indexes the ramp. Rec.709 luma is the weighting video colour-difference
            // encoding uses; the other entries exist because ramps indexed by a single channel,
            // by HSV value or by saturation are all standard (GPU Gems 1 ch.22 builds its
            // colour-correction map from a luminance value the same way).
            float ResolveGradientMapInput(float3 color, int source)
            {
                if (source == 1) return color.r;
                if (source == 2) return color.g;
                if (source == 3) return color.b;
                if (source == 4) return max(color.r, max(color.g, color.b));
                if (source == 5) return (color.r + color.g + color.b) * (1.0 / 3.0);
                if (source == 6) return max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            half4 FragGradientMap(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001 || _LayerRampTexEnabled < 0.5)
                {
                    // Without a baked ramp there is nothing to map with; pass the frame through.
                    return source;
                }

                int sourceIndex = (int)clamp(round(_LayerParams0.x), 0.0, 6.0);
                float value = ResolveGradientMapInput(source.rgb, sourceIndex);

                // Input window: the black point maps to the start of the ramp, the white point to
                // the end (the blackpoint/whitepoint pair of a grade node).
                float blackPoint = _LayerParams0.y;
                float whitePoint = _LayerParams0.z;
                float range = whitePoint - blackPoint;
                float normalized = range > 0.000001
                    ? (value - blackPoint) / range
                    : (value >= whitePoint ? 1.0 : 0.0);
                normalized = saturate(normalized);

                if (_LayerParams6.y > 0.5)
                {
                    // Reverse the ramp: black now indexes the last key.
                    normalized = 1.0 - normalized;
                }

                float bands = floor(_LayerParams6.z + 0.5);
                if (bands >= 2.0)
                {
                    // Posterise the ramp position into N flat bands, first at 0 and last at 1.
                    normalized = saturate(floor(normalized * bands) / max(bands - 1.0, 1.0));
                }

                // One fetch: the ramp texture already carries the gradient, the interpolation space
                // it was baked in, its alpha keys, and the Blend/Fixed stepping.
                //
                // The bake puts sample i at t = i / (Resolution - 1), while a texture fetch assumes
                // texel centres at (i + 0.5) / Resolution. Stretching the coordinate by that half
                // texel makes the fetch reproduce the baked ramp exactly, endpoints included,
                // instead of shifting it by up to half a texel.
                float rampTexel = _LayerRampTexelSize.x;
                float rampCoordinate = normalized * (1.0 - rampTexel) + rampTexel * 0.5;
                half4 ramp = HoSampleGradient(TEXTURE2D_ARGS(_LayerRampTex, sampler_LayerRampTex), rampCoordinate);

                half3 blended = ApplyLayerBlend(source.rgb, ramp.rgb, _LayerBlendMode);
                half alpha = amount * saturate(ramp.a);
                half3 result = lerp(source.rgb, blended, alpha);

                float ditherStrength = max(_LayerParams0.w, 0.0);
                if (ditherStrength > 0.0001)
                {
                    // Output dither: symmetric triangular noise, +-0.5 LSB per unit of strength,
                    // applied in display space (the space the 8-bit quantisation happens in).
                    float2 pixel = floor(input.texcoord * _ScreenParams.xy);
                    float3 noise = float3(
                        Hash12(pixel + float2(0.5, 0.5)),
                        Hash12(pixel + float2(37.7, 11.3)),
                        Hash12(pixel + float2(91.3, 73.1)));
                    noise = noise * 2.0 - 1.0;
                    noise = sign(noise) * (1.0 - sqrt(max(1.0 - abs(noise), 0.0)));
                    result += half3(noise * (ditherStrength * 0.5 / 255.0));
                }

                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
