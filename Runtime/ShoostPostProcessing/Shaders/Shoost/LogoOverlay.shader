Shader "Hidden/lilToon-Shoost/URP/Shoost/LogoOverlay"
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
            Name "Shoost Logo Overlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLogoOverlay

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;
            float4 _LayerParams3;
            float4 _LayerParams4;
            float4 _LayerParams5;
            float4 _LayerParams6;
            float4 _LayerParams7;
            float4 _LayerParams8;
            float4 _LayerParams9;
            float4 _LayerParams10;
            float4 _LayerParams11;
            float4 _LogoTextureEnabled0;
            float4 _LogoTextureEnabled1;

            TEXTURE2D(_LogoTexture0);
            TEXTURE2D(_LogoTexture1);
            TEXTURE2D(_LogoTexture2);
            TEXTURE2D(_LogoTexture3);
            TEXTURE2D(_LogoTexture4);
            TEXTURE2D(_LogoTexture5);
            TEXTURE2D(_LogoTexture6);
            TEXTURE2D(_LogoTexture7);
            float4 _LogoTexture0_TexelSize;
            float4 _LogoTexture1_TexelSize;
            float4 _LogoTexture2_TexelSize;
            float4 _LogoTexture3_TexelSize;
            float4 _LogoTexture4_TexelSize;
            float4 _LogoTexture5_TexelSize;
            float4 _LogoTexture6_TexelSize;
            float4 _LogoTexture7_TexelSize;

            float GetOrder(int index)
            {
                if (index < 4)
                {
                    return _LayerParams8[index];
                }

                return _LayerParams9[index - 4];
            }

            float GetAutoAspect(int index)
            {
                if (index < 4)
                {
                    return _LayerParams10[index];
                }

                return _LayerParams11[index - 4];
            }

            half4 SampleLogoTexture(int index, float2 uv)
            {
                if (index == 0) return SAMPLE_TEXTURE2D(_LogoTexture0, sampler_LinearClamp, uv);
                if (index == 1) return SAMPLE_TEXTURE2D(_LogoTexture1, sampler_LinearClamp, uv);
                if (index == 2) return SAMPLE_TEXTURE2D(_LogoTexture2, sampler_LinearClamp, uv);
                if (index == 3) return SAMPLE_TEXTURE2D(_LogoTexture3, sampler_LinearClamp, uv);
                if (index == 4) return SAMPLE_TEXTURE2D(_LogoTexture4, sampler_LinearClamp, uv);
                if (index == 5) return SAMPLE_TEXTURE2D(_LogoTexture5, sampler_LinearClamp, uv);
                if (index == 6) return SAMPLE_TEXTURE2D(_LogoTexture6, sampler_LinearClamp, uv);
                return SAMPLE_TEXTURE2D(_LogoTexture7, sampler_LinearClamp, uv);
            }

            float4 GetLogoParams(int index)
            {
                if (index == 0) return _LayerParams0;
                if (index == 1) return _LayerParams1;
                if (index == 2) return _LayerParams2;
                if (index == 3) return _LayerParams3;
                if (index == 4) return _LayerParams4;
                if (index == 5) return _LayerParams5;
                if (index == 6) return _LayerParams6;
                return _LayerParams7;
            }

            float4 GetLogoTexelSize(int index)
            {
                if (index == 0) return _LogoTexture0_TexelSize;
                if (index == 1) return _LogoTexture1_TexelSize;
                if (index == 2) return _LogoTexture2_TexelSize;
                if (index == 3) return _LogoTexture3_TexelSize;
                if (index == 4) return _LogoTexture4_TexelSize;
                if (index == 5) return _LogoTexture5_TexelSize;
                if (index == 6) return _LogoTexture6_TexelSize;
                return _LogoTexture7_TexelSize;
            }

            float GetLogoEnabled(int index)
            {
                if (index < 4)
                {
                    return _LogoTextureEnabled0[index];
                }

                return _LogoTextureEnabled1[index - 4];
            }

            half3 AlphaOverLogo(half3 baseColor, float2 screenUv, int index)
            {
                if (GetLogoEnabled(index) < 0.5)
                {
                    return baseColor;
                }

                float4 param = GetLogoParams(index);
                float2 center = param.xy;
                float heightUv = max(param.z, 0.0001);
                float opacity = saturate(param.w) * saturate(_Intensity) * saturate(_LayerColor.a);
                float4 texelSize = GetLogoTexelSize(index);
                float textureAspect = max(texelSize.z, 1.0) / max(texelSize.w, 1.0);
                float screenAspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float widthUv = heightUv;
                if (GetAutoAspect(index) > 0.5)
                {
                    widthUv = heightUv * textureAspect / max(screenAspect, 0.0001);
                }

                float2 logoUv = (screenUv - center) / float2(widthUv, heightUv) + 0.5;
                float inside = step(0.0, logoUv.x) * step(logoUv.x, 1.0) * step(0.0, logoUv.y) * step(logoUv.y, 1.0);
                half4 logo = SampleLogoTexture(index, logoUv);
                half alpha = saturate(logo.a * opacity * inside);
                return lerp(baseColor, logo.rgb * (half3)_LayerColor.rgb, alpha);
            }

            half4 FragLogoOverlay(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half3 color = source.rgb;

                [unroll]
                for (int rank = 0; rank < 8; rank++)
                {
                    [unroll]
                    for (int index = 0; index < 8; index++)
                    {
                        if ((int)round(GetOrder(index)) == rank)
                        {
                            color = AlphaOverLogo(color, uv, index);
                        }
                    }
                }

                return half4(color, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
