Shader "Hidden/lilToon-HoPost/URP/HoPost/LayerBlit"
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
            Name "HoPost Layer Blit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float _LayerTextureEnabled;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;
            float4 _LayerParams3;
            float4 _LayerParams4;
            float4 _LayerParams5;
            TEXTURE2D_X(_LayerTexture);
            SAMPLER(sampler_LayerTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            }
            ENDHLSL
        }
    }
}
