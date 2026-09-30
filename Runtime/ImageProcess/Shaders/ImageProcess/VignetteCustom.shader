Shader "Hidden/lilToon/URP/ImageProcess/VignetteCustom"
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
            Name "ImageProcess Vignette Custom"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDarken

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;
            float4 _LayerParams1; // x 色散开关（>0.5 开）, y 色散强度 0..1

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
                return _LayerParams0.z > 0.0001 ? _LayerParams0.z : 1.0;
            }

            float ResolveSoftness()
            {
                return _LayerParams0.w > 0.0001 ? _LayerParams0.w : 0.5;
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

            // 色散（可开关）：沿"暗角中心 -> 当前像素"的径向把 R/B 分开采样，G 用原来的中心采样。
            // 强度沿用暗角的径向落区（半径以内基本为 0、越过柔和带后最强），所以彩边只出现在暗角边缘；
            // 偏移量 = 该落区值 * 色散强度 * 16 像素（0..1 强度对应 0..16 像素）。
            // 开关关闭时直接返回传进来的颜色 —— 那两次采样根本不会执行，关掉时逐像素与旧版一致。
            half3 ApplyVignetteDispersion(float2 uv, half3 centerColor)
            {
                if (_LayerParams1.x <= 0.5)
                {
                    return centerColor;
                }

                float2 delta = uv - ResolveCenter();
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                delta.x *= aspect;
                float distanceFromCenter = length(delta);

                // 色散范围 = 暗角自己的径向落区：半径以内基本没有、越过柔和带后达到最强。
                // 所以"范围"用「半径 / 柔和度」控制（与压暗同一条曲线），"强弱"用「色散强度」控制，
                // 且不乘 _Intensity —— 强度只负责压暗，不该顺带改色散。
                float radius = ResolveRadius();
                float softness = ResolveSoftness();
                float edge0 = max(radius - softness, 0.0001);
                float edge1 = max(radius, edge0 + 0.0001);
                float edgeAmount = smoothstep(edge0, edge1, distanceFromCenter);

                float2 direction = delta / max(distanceFromCenter, 0.0001);
                float dispersionPixels = edgeAmount * saturate(_LayerParams1.y) * 16.0;
                float2 offset = direction * dispersionPixels * _BlitTexture_TexelSize.xy;

                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset).r;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).b;
                return half3(red, centerColor.g, blue);
            }

            half4 FragDarken(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                source.rgb = ApplyVignetteDispersion(input.texcoord, source.rgb);
                half vignette = ComputeVignetteMask(input.texcoord);
                source.rgb *= vignette;
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Vignette Custom Tint"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTint

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;
            float4 _LayerParams1; // x 色散开关（>0.5 开）, y 色散强度 0..1

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
                return _LayerParams0.z > 0.0001 ? _LayerParams0.z : 1.0;
            }

            float ResolveSoftness()
            {
                return _LayerParams0.w > 0.0001 ? _LayerParams0.w : 0.5;
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

            // 色散（可开关）：沿"暗角中心 -> 当前像素"的径向把 R/B 分开采样，G 用原来的中心采样。
            // 强度沿用暗角的径向落区（半径以内基本为 0、越过柔和带后最强），所以彩边只出现在暗角边缘；
            // 偏移量 = 该落区值 * 色散强度 * 16 像素（0..1 强度对应 0..16 像素）。
            // 开关关闭时直接返回传进来的颜色 —— 那两次采样根本不会执行，关掉时逐像素与旧版一致。
            half3 ApplyVignetteDispersion(float2 uv, half3 centerColor)
            {
                if (_LayerParams1.x <= 0.5)
                {
                    return centerColor;
                }

                float2 delta = uv - ResolveCenter();
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                delta.x *= aspect;
                float distanceFromCenter = length(delta);

                // 色散范围 = 暗角自己的径向落区：半径以内基本没有、越过柔和带后达到最强。
                // 所以"范围"用「半径 / 柔和度」控制（与压暗同一条曲线），"强弱"用「色散强度」控制，
                // 且不乘 _Intensity —— 强度只负责压暗，不该顺带改色散。
                float radius = ResolveRadius();
                float softness = ResolveSoftness();
                float edge0 = max(radius - softness, 0.0001);
                float edge1 = max(radius, edge0 + 0.0001);
                float edgeAmount = smoothstep(edge0, edge1, distanceFromCenter);

                float2 direction = delta / max(distanceFromCenter, 0.0001);
                float dispersionPixels = edgeAmount * saturate(_LayerParams1.y) * 16.0;
                float2 offset = direction * dispersionPixels * _BlitTexture_TexelSize.xy;

                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset).r;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).b;
                return half3(red, centerColor.g, blue);
            }

            half4 FragTint(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                source.rgb = ApplyVignetteDispersion(input.texcoord, source.rgb);
                half vignette = ComputeVignetteMask(input.texcoord);
                half3 finalColor = lerp(_LayerColor.rgb, source.rgb, vignette);
                return half4(finalColor, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
