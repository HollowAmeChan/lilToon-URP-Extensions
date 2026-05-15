Shader "Hidden/lilToon-Shoost/URP/Shoost/ChangeFrameRate"
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
        TEXTURE2D_X(_FrozenFrameTex);

        half4 FragCapture(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        }

        half4 FragBlend(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            half4 frozen = SAMPLE_TEXTURE2D_X(_FrozenFrameTex, sampler_LinearClamp, input.texcoord);
            return half4(lerp(source.rgb, frozen.rgb, saturate(_Intensity)), source.a);
        }
        ENDHLSL

        Pass
        {
            Name "Shoost Change Frame Rate Capture"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCapture
            ENDHLSL
        }

        Pass
        {
            Name "Shoost Change Frame Rate Blend"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlend
            ENDHLSL
        }
    }

    Fallback Off
}
