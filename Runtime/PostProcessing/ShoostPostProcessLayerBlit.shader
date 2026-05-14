Shader "Hidden/lilToon/URP/Shoost/PostProcessLayerBlit"
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
            Name "Shoost Post Process Layer Blit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;
            float4 _LayerParams3;
            TEXTURE2D_X(_LayerTexture);

            half3 ApplyLayerBlend(half3 baseColor, half3 layerColor, float blendMode)
            {
                if (blendMode < 0.5)
                {
                    return layerColor;
                }

                if (blendMode < 1.5)
                {
                    return baseColor + layerColor;
                }

                if (blendMode < 2.5)
                {
                    return baseColor * layerColor;
                }

                half3 one = half3(1.0, 1.0, 1.0);
                return one - (one - baseColor) * (one - layerColor);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 layer = source * (half4)_LayerColor;
                layer.rgb = ApplyLayerBlend(source.rgb, layer.rgb, _LayerBlendMode);
                return half4(lerp(source.rgb, layer.rgb, saturate(_Intensity)), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
