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

            float _Intensity;
            float _LayerBlendMode;
            // x input source (0 luma, 1 red, 2 green, 3 blue, 4 maximum, 5 average, 6 saturation),
            // y black point, z white point, w output dither (8-bit LSB)
            float4 _LayerParams0;
            // stop positions, non-decreasing
            float4 _LayerParams1;
            // stop colours (rgba)
            float4 _LayerParams2;
            float4 _LayerParams3;
            float4 _LayerParams4;
            float4 _LayerParams5;
            // x interpolation space (0 display, 1 linear light, 2 Oklab), y reverse,
            // z posterise bands (0/1 = off, >=2 = band count), w reserved
            float4 _LayerParams6;

            // Same 24 premultiplied-off Photoshop/PDF blend modes as LayerBlit.shader and
            // Gradient.shader. Kept local on purpose: making it an include is a separate change
            // that also touches those two shaders.
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

            float3 GradientMapSrgbToLinear(float3 c)
            {
                // HLSL has no GLSL-style component-selection intrinsic. Use a component-wise step
                // mask so this also compiles on the D3D11 backend used by the editor.
                float3 low = c / 12.92;
                float3 high = pow(max(c + 0.055, 0.0) / 1.055, 2.4);
                float3 lowMask = 1.0 - step(float3(0.04045, 0.04045, 0.04045), c);
                return lerp(high, low, lowMask);
            }

            float3 GradientMapLinearToSrgb(float3 c)
            {
                float3 low = c * 12.92;
                float3 high = 1.055 * pow(max(c, 0.0), 1.0 / 2.4) - 0.055;
                float3 lowMask = 1.0 - step(float3(0.0031308, 0.0031308, 0.0031308), c);
                return lerp(high, low, lowMask);
            }

            // Oklab (Bjorn Ottosson 2020; the matrices below are the linear-sRGB form as
            // recalculated in the CSS Color 4 sample conversions). Derivation and numeric checks:
            // .codex-research/gradient_map_sim/oklab_check.py.
            float3 GradientMapLinearSrgbToOklab(float3 c)
            {
                float l = 0.4122214695 * c.r + 0.5363325373 * c.g + 0.0514459933 * c.b;
                float m = 0.2119034958 * c.r + 0.6806995506 * c.g + 0.1073969535 * c.b;
                float s = 0.0883024592 * c.r + 0.2817188391 * c.g + 0.6299787017 * c.b;

                // Sign-preserving cube root: HLSL pow() returns NaN for negative bases and the
                // cone responses can go slightly negative for out-of-gamut input.
                l = sign(l) * pow(abs(l), 1.0 / 3.0);
                m = sign(m) * pow(abs(m), 1.0 / 3.0);
                s = sign(s) * pow(abs(s), 1.0 / 3.0);

                return float3(
                    0.2104542683 * l + 0.7936177747 * m - 0.0040720430 * s,
                    1.9779985324 * l - 2.4285922420 * m + 0.4505937096 * s,
                    0.0259040425 * l + 0.7827717125 * m - 0.8086757549 * s);
            }

            float3 GradientMapOklabToLinearSrgb(float3 lab)
            {
                float l = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
                float m = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
                float s = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;
                l = l * l * l;
                m = m * m * m;
                s = s * s * s;

                return float3(
                    4.0767416361 * l - 3.3077115393 * m + 0.2309699032 * s,
                    -1.2684379733 * l + 2.6097573493 * m - 0.3413193760 * s,
                    -0.0041960761 * l - 0.7034186179 * m + 1.7076146941 * s);
            }

            float4 GradientMapLerp(float4 colorA, float4 colorB, float local, int space)
            {
                float4 result;
                float3 rgb;

                if (space == 1)
                {
                    // Interpolate in linear light, then return to display space. Neither space is
                    // universally "right": display-space blending darkens and over-saturates the
                    // middle of wide ramps, linear-light blending lifts the middle of dark ramps.
                    rgb = GradientMapLinearToSrgb(lerp(GradientMapSrgbToLinear(colorA.rgb), GradientMapSrgbToLinear(colorB.rgb), local));
                }
                else if (space == 2)
                {
                    // Oklab: straight lines between two colours keep their perceived lightness and
                    // hue, which is what stops wide ramps from turning muddy in the middle.
                    float3 labA = GradientMapLinearSrgbToOklab(GradientMapSrgbToLinear(colorA.rgb));
                    float3 labB = GradientMapLinearSrgbToOklab(GradientMapSrgbToLinear(colorB.rgb));
                    rgb = GradientMapLinearToSrgb(GradientMapOklabToLinearSrgb(lerp(labA, labB, local)));
                }
                else
                {
                    rgb = lerp(colorA.rgb, colorB.rgb, local);
                }

                // Linear-light and Oklab interpolation can leave the display gamut in the middle of
                // a segment. Clip to [0,1]: simple clipping, not a gamut-mapping algorithm.
                result.rgb = saturate(rgb);
                result.a = lerp(colorA.a, colorB.a, local);
                return result;
            }

            // Ramp lookup. Stop positions are expected non-decreasing (the inspector clamps them);
            // zero-width or inverted segments fall back to a hard step instead of dividing by zero.
            float4 SampleGradientMapRamp(float t, int space)
            {
                float4 colorA = _LayerParams2;
                float4 colorB = _LayerParams2;
                float local = 0.0;

                if (t >= _LayerParams1.w)
                {
                    colorA = _LayerParams5;
                    colorB = _LayerParams5;
                }
                else if (t >= _LayerParams1.z)
                {
                    colorA = _LayerParams4;
                    colorB = _LayerParams5;
                    local = (t - _LayerParams1.z) / max(_LayerParams1.w - _LayerParams1.z, 0.000001);
                }
                else if (t >= _LayerParams1.y)
                {
                    colorA = _LayerParams3;
                    colorB = _LayerParams4;
                    local = (t - _LayerParams1.y) / max(_LayerParams1.z - _LayerParams1.y, 0.000001);
                }
                else if (t >= _LayerParams1.x)
                {
                    colorA = _LayerParams2;
                    colorB = _LayerParams3;
                    local = (t - _LayerParams1.x) / max(_LayerParams1.y - _LayerParams1.x, 0.000001);
                }

                return GradientMapLerp(colorA, colorB, saturate(local), space);
            }

            half4 FragGradientMap(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
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
                    // Reverse the ramp: black now indexes the last stop.
                    normalized = 1.0 - normalized;
                }

                float bands = floor(_LayerParams6.z + 0.5);
                if (bands >= 2.0)
                {
                    // Posterise the ramp position into N flat bands, first at 0 and last at 1.
                    normalized = saturate(floor(normalized * bands) / max(bands - 1.0, 1.0));
                }

                int space = (int)clamp(round(_LayerParams6.x), 0.0, 2.0);
                float4 ramp = SampleGradientMapRamp(normalized, space);

                half3 blended = ApplyLayerBlend(source.rgb, half3(ramp.rgb), _LayerBlendMode);
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
