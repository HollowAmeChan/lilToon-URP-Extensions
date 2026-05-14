Shader "Hidden/lilToon-Shoost/URP/Shoost/KawaseBlur"
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
            Name "Shoost Kawase Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _Radius;
            float _ScreenRatio;

            float2 ResolveOffset()
            {
                float radius = _Radius > 0.0001 ? _Radius : 0.5;
                float2 texel = _BlitTexture_TexelSize.xy * (radius + 0.5);
                texel.x /= max(_ScreenRatio, 0.0001);
                return texel;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 texel = ResolveOffset();
                half3 blur = source.rgb * 4.0;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, 0.0)).rgb * 2.0;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb * 2.0;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, -texel.y)).rgb * 2.0;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb * 2.0;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, -texel.y)).rgb;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, -texel.y)).rgb;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, texel.y)).rgb;
                blur += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, texel.y)).rgb;
                blur /= 16.0;

                half3 finalColor = lerp(source.rgb, blur, saturate(_Intensity));
                return half4(finalColor, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
