Shader "Hidden/lilToon-Shoost/URP/Shoost/AOVComposite"
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
            Name "Shoost AOV Composite"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl"

            TEXTURE2D_X(_LayerResultTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 layerResult = SAMPLE_TEXTURE2D_X(_LayerResultTexture, sampler_LinearClamp, uv);
                half mask = (half)LilHoPostResolveAovMaskInternal(uv, true);
                if (LilHoPostShouldOutputAovDebug())
                {
                    return half4(mask, mask, mask, source.a);
                }

                return half4(lerp(source.rgb, layerResult.rgb, mask), lerp(source.a, layerResult.a, mask));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
