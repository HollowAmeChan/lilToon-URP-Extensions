Shader "Hidden/lilToon/URP/ImageProcess/AutoWhiteBalance"
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
            Name "ImageProcess Auto White Balance"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAutoWhiteBalance

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;

            float3 GetWhiteBalanceScale(float temperature, float tint)
            {
                float temp = temperature / 100.0;
                float tintValue = tint / 100.0;
                return max(float3(
                    1.0 + temp * 0.12 - tintValue * 0.03,
                    1.0 - abs(temp) * 0.04 + tintValue * 0.08,
                    1.0 - temp * 0.12 - tintValue * 0.03), 0.001);
            }

            half4 FragAutoWhiteBalance(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float3 balanced = source.rgb * max(_LayerColor.rgb, 0.001) * GetWhiteBalanceScale(_LayerParams0.x, _LayerParams0.y);
                if (_LayerParams0.z > 0.5)
                {
                    float sourceLum = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                    float balancedLum = dot(balanced, float3(0.2126, 0.7152, 0.0722));
                    balanced *= sourceLum / max(balancedLum, 0.0001);
                }

                return half4(lerp(source.rgb, balanced, intensity), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
