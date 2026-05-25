Shader "Hidden/lilToon/URP/ImageProcess/RGBBlurV2"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Intensity;
        float4 _LayerParams0;
        float _Radius;
        TEXTURE2D_X(_OriginalTex);
        SAMPLER(sampler_OriginalTex);
        TEXTURE2D_X(_BlurredTex);
        SAMPLER(sampler_BlurredTex);

        half4 SampleSoftBlur(float2 uv)
        {
            float2 texel = _BlitTexture_TexelSize.xy * max(_Radius, 0.0001);
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.227027;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x * 1.384615, 0.0)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x * 1.384615, 0.0)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y * 1.384615)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y * 1.384615)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(2.230769, 2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(-2.230769, 2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(2.230769, -2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel * float2(2.230769, 2.230769)) * 0.035135;
            return color;
        }

        half4 FragBlur(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SampleSoftBlur(input.texcoord);
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 source = SAMPLE_TEXTURE2D_X(_OriginalTex, sampler_OriginalTex, input.texcoord);
            half4 blurred = SAMPLE_TEXTURE2D_X(_BlurredTex, sampler_BlurredTex, input.texcoord);
            float3 channelBlend = saturate(_LayerParams0.xyz) * saturate(_Intensity);
            half3 color = lerp(source.rgb, blurred.rgb, channelBlend);
            return half4(color, source.a);
        }
        ENDHLSL

        Pass
        {
            Name "ImageProcess RGB Blur V2 Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess RGB Blur V2 Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
