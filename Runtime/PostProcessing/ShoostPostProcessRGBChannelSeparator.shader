Shader "Hidden/lilToon-Shoost/URP/Shoost/RGBChannelSeparator"
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
            Name "Shoost RGB Channel Separator"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRGBChannelSeparator

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;

            half4 SelectChannel(half4 source, int channel)
            {
                if (channel == 1)
                {
                    return half4(source.r, source.r, source.r, source.a);
                }

                if (channel == 2)
                {
                    return half4(source.g, source.g, source.g, source.a);
                }

                if (channel == 3)
                {
                    return half4(source.b, source.b, source.b, source.a);
                }

                if (channel == 4)
                {
                    return half4(source.a, source.a, source.a, source.a);
                }

                return source;
            }

            half4 FragRGBChannelSeparator(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                half4 selected = SelectChannel(source, (int)round(_LayerParams0.x));
                return lerp(source, selected, intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
