Shader "Hidden/lilToon-Shoost/URP/Shoost/CenterColorCorrection"
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
            Name "Shoost Center Color Correction"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCenterColorCorrection

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x saturation, y brightness, z contrast, w invert
            float4 _LayerParams1; // x radius, y smoothness, z center x, w center y
            float4 _LayerParams2; // x opacity, y hue degrees

            float ShoostCenterColorCorrectionLuminance(float3 color)
            {
                return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
            }

            float3 ShoostCenterColorCorrectionApplyColor(float3 color, float saturation, float brightness, float contrast)
            {
                float luma = ShoostCenterColorCorrectionLuminance(color);
                color = lerp(luma.xxx, color, 1.0 + saturation);
                color += brightness;
                color = (color - 0.5) * (1.0 + contrast) + 0.5;
                return max(color, 0.0);
            }

            float3 ShoostCenterColorCorrectionRgbToHsv(float3 color)
            {
                float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(color.bg, k.wz), float4(color.gb, k.xy), step(color.b, color.g));
                float4 q = lerp(float4(p.xyw, color.r), float4(color.r, p.yzx), step(p.x, color.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 ShoostCenterColorCorrectionHsvToRgb(float3 hsv)
            {
                float3 p = abs(frac(hsv.xxx + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return hsv.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), hsv.y);
            }

            float3 ShoostCenterColorCorrectionApplyHueOffset(float3 color, float hueDegrees)
            {
                float hueOffset = hueDegrees / 360.0;
                if (abs(hueOffset) <= 0.00001)
                {
                    return color;
                }

                float3 hsv = ShoostCenterColorCorrectionRgbToHsv(max(color, 0.0));
                hsv.x = frac(hsv.x + hueOffset + 1.0);
                return ShoostCenterColorCorrectionHsvToRgb(hsv);
            }

            float ShoostCenterColorCorrectionResolveMask(float2 uv)
            {
                float radius = max(_LayerParams1.x, 0.0001);
                float smoothness = saturate(_LayerParams1.y);
                float2 center = 0.5 + _LayerParams1.zw * 0.5;
                float2 delta = uv - center;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                delta.x *= aspect;

                float distanceValue = length(delta);
                float softness = max(radius * smoothness, 0.0001);
                float edge0 = max(radius - softness, 0.0);
                float edge1 = max(radius, edge0 + 0.0001);
                float mask = 1.0 - smoothstep(edge0, edge1, distanceValue);

                if (_LayerParams0.w > 0.5)
                {
                    mask = 1.0 - mask;
                }

                return saturate(mask);
            }

            half4 FragCenterColorCorrection(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity) * saturate(_LayerParams2.x);
                if (amount <= 0.0001)
                {
                    return source;
                }

                float mask = ShoostCenterColorCorrectionResolveMask(input.texcoord);
                float3 corrected = ShoostCenterColorCorrectionApplyColor(source.rgb, _LayerParams0.x, _LayerParams0.y, _LayerParams0.z);
                corrected = ShoostCenterColorCorrectionApplyHueOffset(corrected, _LayerParams2.y);
                return half4(lerp(source.rgb, corrected, amount * mask), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
