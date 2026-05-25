Shader "Hidden/lilToon/URP/ImageProcess/RGBSplit"
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
            Name "ImageProcess RGB Split"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRGBSplit

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _Mode;
            float _Angle;
            float4 _LayerParams0;

            float2 GetDirection()
            {
                float angle = _Angle;
                return float2(cos(angle), sin(angle));
            }

            half4 FragRGBSplit(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_LayerParams0.y) * saturate(_Intensity) * 0.01;
                float screenRatio = _ScreenParams.x / max(_ScreenParams.y, 1.0);

                if (_Mode < 0.5)
                {
                    float2 direction = GetDirection();
                    float2 offset = float2(direction.x / screenRatio, direction.y) * intensity;
                    half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset).r;
                    half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord - offset).b;
                    return half4(red, source.g, blue, source.a);
                }

                float2 centered = input.texcoord - 0.5;
                centered.x /= screenRatio;
                float2 offset = centered * intensity * 2.0;
                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord - offset).r;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset).b;
                return half4(red, source.g, blue, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
