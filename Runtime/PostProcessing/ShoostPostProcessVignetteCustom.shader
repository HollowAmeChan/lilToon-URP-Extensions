Shader "Hidden/lilToon-Shoost/URP/Shoost/VignetteCustom"
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
            Name "Shoost Vignette Custom"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDarken

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;

            float2 ResolveCenter()
            {
                float2 center = _LayerParams0.xy;
                if (abs(center.x) + abs(center.y) < 0.0001)
                {
                    center = float2(0.5, 0.5);
                }

                return center;
            }

            float ResolveRadius()
            {
                return _LayerParams0.z > 0.0001 ? _LayerParams0.z : 0.35;
            }

            float ResolveSoftness()
            {
                return _LayerParams0.w > 0.0001 ? _LayerParams0.w : 0.25;
            }

            half ComputeVignetteMask(float2 uv)
            {
                float2 center = ResolveCenter();
                float2 delta = uv - center;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                delta.x *= aspect;

                float radius = ResolveRadius();
                float softness = ResolveSoftness();
                float edge0 = max(radius - softness, 0.0001);
                float edge1 = max(radius, edge0 + 0.0001);
                float vignette = 1.0 - smoothstep(edge0, edge1, length(delta));

                return saturate(1.0 - (1.0 - vignette) * saturate(_Intensity));
            }

            half4 FragDarken(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half vignette = ComputeVignetteMask(input.texcoord);
                source.rgb *= vignette;
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Shoost Vignette Custom Tint"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTint

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;

            float2 ResolveCenter()
            {
                float2 center = _LayerParams0.xy;
                if (abs(center.x) + abs(center.y) < 0.0001)
                {
                    center = float2(0.5, 0.5);
                }

                return center;
            }

            float ResolveRadius()
            {
                return _LayerParams0.z > 0.0001 ? _LayerParams0.z : 0.35;
            }

            float ResolveSoftness()
            {
                return _LayerParams0.w > 0.0001 ? _LayerParams0.w : 0.25;
            }

            half ComputeVignetteMask(float2 uv)
            {
                float2 center = ResolveCenter();
                float2 delta = uv - center;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                delta.x *= aspect;

                float radius = ResolveRadius();
                float softness = ResolveSoftness();
                float edge0 = max(radius - softness, 0.0001);
                float edge1 = max(radius, edge0 + 0.0001);
                float vignette = 1.0 - smoothstep(edge0, edge1, length(delta));

                return saturate(1.0 - (1.0 - vignette) * saturate(_Intensity));
            }

            half4 FragTint(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half vignette = ComputeVignetteMask(input.texcoord);
                half3 finalColor = lerp(_LayerColor.rgb, source.rgb, vignette);
                return half4(finalColor, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
