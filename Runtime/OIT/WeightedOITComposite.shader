Shader "Hidden/lilToon/URP/WeightedOITComposite"
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
            Name "Weighted OIT Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);

            TEXTURE2D_X(_lilOITAccumulationTexture);
            TEXTURE2D_X(_lilOITRevealageTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 accumulation = SAMPLE_TEXTURE2D_X(_lilOITAccumulationTexture, sampler_LinearClamp, uv);
                half coverage = SAMPLE_TEXTURE2D_X(_lilOITRevealageTexture, sampler_LinearClamp, uv).r;

                half weight = max(accumulation.a, 1.0e-5h);
                half3 transparentColor = accumulation.rgb / weight;
                half transparentAlpha = saturate(coverage);

                cameraColor.rgb = lerp(cameraColor.rgb, transparentColor, transparentAlpha);
                return cameraColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
