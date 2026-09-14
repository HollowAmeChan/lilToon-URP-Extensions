Shader "Hidden/lilToon/URP/ImageProcess/Gradient"
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
            Name "ImageProcess Gradient"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGradient

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            // x mode, y radius (legacy), z smoothness (legacy) / curve, w opacity
            float4 _LayerParams0;
            // x offset X (point A), y offset Y (point A), z angle degrees (legacy), w invert
            float4 _LayerParams1;
            // x scale X (legacy ellipse), y scale Y (legacy ellipse), z resolution scale, w dither (8-bit LSB)
            float4 _LayerParams2;
            // background color (rgba)
            float4 _LayerParams3;
            // x point B offset X, y point B offset Y, z curve, w mirror
            float4 _LayerParams4;
            // x ellipse aspect ratio, y interpolation space, z legacy aspect fix, w unused
            float4 _LayerParams5;

            static const float GradientPi = 3.14159265359;

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

            float2 ResolveGradientUV(float2 uv)
            {
                float resolutionScale = max(_LayerParams2.z, 0.01);
                float2 targetResolution = max(round(_ScreenParams.xy * resolutionScale), 1.0);
                return (floor(uv * targetResolution) + 0.5) / targetResolution;
            }

            // Falloff curves, matching the vocabulary compositing ramps use:
            // 0 linear, 1 smooth (both ends), 2 ease-in (point A end), 3 ease-out (point B end),
            // 4 smootherstep.
            float ApplyGradientCurve(float t, float curve)
            {
                int index = (int)clamp(round(curve), 0.0, 4.0);
                if (index == 1) return smoothstep(0.0, 1.0, t);
                if (index == 2) return t * t;
                if (index == 3) return 1.0 - (1.0 - t) * (1.0 - t);
                if (index == 4) return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                return t;
            }

            float3 GradientSrgbToLinear(float3 c)
            {
                // HLSL has no GLSL-style component-selection intrinsic. Use a component-wise step
                // mask so this also compiles on the D3D11 backend used by the editor.
                float3 low = c / 12.92;
                float3 high = pow(max(c + 0.055, 0.0) / 1.055, 2.4);
                float3 lowMask = 1.0 - step(float3(0.04045, 0.04045, 0.04045), c);
                return lerp(high, low, lowMask);
            }

            float3 GradientLinearToSrgb(float3 c)
            {
                float3 low = c * 12.92;
                float3 high = 1.055 * pow(max(c, 0.0), 1.0 / 2.4) - 0.055;
                float3 lowMask = 1.0 - step(float3(0.0031308, 0.0031308, 0.0031308), c);
                return lerp(high, low, lowMask);
            }

            // Legacy geometry (modes 0..3), unchanged from the original implementation.
            float ResolveLegacyGradientMask(float2 uv, int mode)
            {
                float2 center = 0.5 + _LayerParams1.xy;
                float2 delta = uv - center;
                float radius = max(_LayerParams0.y, 0.0001);
                float smoothness = max(_LayerParams0.z, 0.0001);
                float minSoftness = 1.5 / max(_ScreenParams.y, 1.0);
                float mask = 1.0;

                if (mode == 1)
                {
                    float angleRadians = radians(_LayerParams1.z + 90.0);
                    float2 direction = float2(cos(angleRadians), sin(angleRadians));
                    if (_LayerParams5.z > 0.5)
                    {
                        // Opt-in aspect correction: build the axis in aspect-corrected space so the
                        // on-screen angle matches the requested angle on any frame shape.
                        float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                        direction = normalize(float2(direction.x, direction.y / max(aspect, 0.0001)));
                    }

                    float linearPosition = dot(delta, direction) / radius + 0.5;
                    float softness = max(lerp(0.02, 1.0, saturate(smoothness / 10.0)), minSoftness / radius);
                    mask = smoothstep(0.5 - softness, 0.5 + softness, linearPosition);
                }
                else if (mode == 2 || mode == 3)
                {
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float2 scale = mode == 3 ? max(_LayerParams2.xy, 0.0001) : float2(1.0, 1.0);
                    delta.x *= aspect;
                    delta /= scale;

                    float distanceValue = length(delta);
                    float softness = max(radius * saturate(smoothness / 10.0), minSoftness);
                    float edge0 = max(radius - softness, 0.0);
                    float edge1 = max(radius, edge0 + minSoftness);
                    mask = 1.0 - smoothstep(edge0, edge1, distanceValue);
                }

                return mask;
            }

            // Two-point geometry (modes 4..7): point A = _LayerParams1.xy, point B = _LayerParams4.xy,
            // both relative to the screen centre, measured in aspect-corrected space so the
            // on-screen direction matches what the view control shows.
            float ResolveTwoPointGradientMask(float2 uv, int mode)
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float height = max(_ScreenParams.y, 1.0);
                float2 q = float2(uv.x * aspect, uv.y);
                float2 qa = float2((0.5 + _LayerParams1.x) * aspect, 0.5 + _LayerParams1.y);
                float2 qb = float2((0.5 + _LayerParams4.x) * aspect, 0.5 + _LayerParams4.y);
                float2 axis = qb - qa;
                float lengthAxis = max(length(axis), 2.0 / height);
                float2 direction = axis / lengthAxis;
                float2 relative = q - qa;
                float t;

                if (mode == 4)
                {
                    t = dot(relative, direction) / lengthAxis;
                }
                else if (mode == 6)
                {
                    float2 perpendicular = float2(-direction.y, direction.x);
                    float ellipseAspect = clamp(_LayerParams5.x, 0.05, 20.0);
                    float2 local = float2(dot(relative, direction), dot(relative, perpendicular) / max(ellipseAspect, 0.05));
                    t = length(local) / lengthAxis;
                }
                else if (mode == 7)
                {
                    float angle = atan2(relative.y, relative.x) - atan2(direction.y, direction.x);
                    t = frac(angle / (2.0 * GradientPi) + 1.0);
                }
                else
                {
                    t = length(relative) / lengthAxis;
                }

                t = saturate(t);
                if (_LayerParams4.w > 0.5)
                {
                    t = abs(t * 2.0 - 1.0);
                }

                return ApplyGradientCurve(t, _LayerParams4.z);
            }

            float ResolveGradientMask(float2 uv)
            {
                int mode = (int)clamp(round(_LayerParams0.x), 0.0, 7.0);
                float mask;

                if (mode >= 4)
                {
                    mask = ResolveTwoPointGradientMask(uv, mode);
                }
                else
                {
                    mask = ResolveLegacyGradientMask(uv, mode);
                }

                if (_LayerParams1.w > 0.5)
                {
                    mask = 1.0 - mask;
                }

                return saturate(mask);
            }

            half4 FragGradient(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity) * saturate(_LayerParams0.w);
                if (amount <= 0.0001)
                {
                    return source;
                }

                float2 uv = ResolveGradientUV(input.texcoord);
                float mask = ResolveGradientMask(uv);
                half4 fromColor = (half4)_LayerParams3;
                half4 toColor = (half4)_LayerColor;
                half3 layerRgb;

                if (_LayerParams5.y > 0.5)
                {
                    // Interpolate the two stops in linear light, then return to display space.
                    // Neither space is universally "right": display-space blending darkens and
                    // over-saturates the midpoint of wide ramps, while linear-light blending makes
                    // light-to-dark ramps look too bright in the middle. This is the same
                    // three-way choice engines and DCC tools expose (default here = display space,
                    // matching the previous behaviour).
                    layerRgb = GradientLinearToSrgb(lerp(GradientSrgbToLinear(fromColor.rgb), GradientSrgbToLinear(toColor.rgb), mask));
                }
                else
                {
                    layerRgb = lerp(fromColor.rgb, toColor.rgb, mask);
                }

                half layerAlpha = lerp(fromColor.a, toColor.a, mask);
                half3 blended = ApplyLayerBlend(source.rgb, layerRgb, _LayerBlendMode);
                half alpha = amount * layerAlpha;
                half3 result = lerp(source.rgb, blended, alpha);

                float ditherStrength = max(_LayerParams2.w, 0.0);
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
