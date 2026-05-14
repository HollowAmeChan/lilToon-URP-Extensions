Shader "Hidden/lilToon-Shoost/URP/Shoost/Sharpen"
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
            Name "Shoost Sharpen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSharpen

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _Sharpness;

            half4 FragSharpen(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 texel = _BlitTexture_TexelSize.xy;
                half4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half4 sum =
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0, -1.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 0.0, -1.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0, -1.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0,  0.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0,  0.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0,  1.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 0.0,  1.0)) +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0,  1.0));

                half sharpness = saturate(_Sharpness * _Intensity);
                half3 finalColor = center.rgb + ((center.rgb * 8.0) - sum.rgb) * sharpness;
                return half4(finalColor, center.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
