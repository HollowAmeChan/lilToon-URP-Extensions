Shader "Hidden/lilToon-Shoost/URP/Shoost/Gradient"
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
            Name "Shoost Gradient"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGradient

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x mode, y radius, z smoothness, w opacity
            float4 _LayerParams1; // x center x, y center y, z angle degrees, w invert
            float4 _LayerParams2; // x scale x, y scale y, z resolution scale, w dither strength
            float4 _LayerParams3; // background color

            half3 ColorBurn(half3 baseColor, half3 layerColor)
            {
                return saturate(1.0 - (1.0 - baseColor) / max(layerColor, 0.0001));
            }

            half3 ColorDodge(half3 baseColor, half3 layerColor)
            {
                return saturate(baseColor / max(1.0 - layerColor, 0.0001));
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
                if (mode == 1) return saturate(baseColor + layerColor);
                if (mode == 2) return baseColor * layerColor;
                if (mode == 3) return 1.0 - (1.0 - baseColor) * (1.0 - layerColor);
                if (mode == 4) return min(baseColor, layerColor);
                if (mode == 5) return ColorBurn(baseColor, layerColor);
                if (mode == 6) return saturate(baseColor + layerColor - 1.0);
                if (mode == 7) return max(baseColor, layerColor);
                if (mode == 8) return ColorDodge(baseColor, layerColor);
                if (mode == 9) return Overlay(baseColor, layerColor);
                if (mode == 10) return SoftLight(baseColor, layerColor);
                if (mode == 11) return Overlay(layerColor, baseColor);
                if (mode == 12) return VividLight(baseColor, layerColor);
                if (mode == 13) return saturate(baseColor + 2.0 * layerColor - 1.0);
                if (mode == 14) return lerp(min(baseColor, 2.0 * layerColor), max(baseColor, 2.0 * (layerColor - 0.5)), step(0.5, layerColor));
                if (mode == 15) return step(0.5, VividLight(baseColor, layerColor));
                if (mode == 16) return abs(baseColor - layerColor);
                if (mode == 17) return baseColor + layerColor - 2.0 * baseColor * layerColor;
                if (mode == 18) return saturate(baseColor - layerColor);
                if (mode == 19) return saturate(baseColor / max(layerColor, 0.0001));
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

            float ResolveGradientMask(float2 uv)
            {
                int mode = (int)clamp(round(_LayerParams0.x), 0.0, 3.0);
                float2 center = 0.5 + _LayerParams1.xy;
                float2 delta = uv - center;
                float radius = max(_LayerParams0.y, 0.0001);
                float smoothness = max(_LayerParams0.z, 0.0001);
                float mask = 1.0;

                if (mode == 1)
                {
                    float angleRadians = radians(_LayerParams1.z + 90.0);
                    float2 direction = float2(cos(angleRadians), sin(angleRadians));
                    float linearPosition = dot(delta, direction) / radius + 0.5;
                    float softness = lerp(0.02, 1.0, saturate(smoothness / 10.0));
                    mask = smoothstep(0.5 - softness, 0.5 + softness, linearPosition);
                }
                else if (mode == 2 || mode == 3)
                {
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float2 scale = mode == 3 ? max(_LayerParams2.xy, 0.0001) : float2(1.0, 1.0);
                    delta.x *= aspect;
                    delta /= scale;

                    float distanceValue = length(delta);
                    float softness = radius * saturate(smoothness / 10.0);
                    float edge0 = max(radius - softness, 0.0);
                    float edge1 = max(radius, edge0 + 0.0001);
                    mask = 1.0 - smoothstep(edge0, edge1, distanceValue);
                }

                if (mode != 0 && _LayerParams2.w > 0.0001)
                {
                    float2 pixel = floor(uv * _ScreenParams.xy);
                    mask += (Hash12(pixel) - 0.5) * (_LayerParams2.w / 255.0);
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
                half4 layer = lerp(fromColor, toColor, mask);
                half3 blended = ApplyLayerBlend(source.rgb, layer.rgb, _LayerBlendMode);
                half alpha = amount * layer.a;
                return half4(lerp(source.rgb, blended, alpha), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
