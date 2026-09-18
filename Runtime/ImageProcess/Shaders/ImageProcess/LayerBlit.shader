Shader "Hidden/lilToon/URP/ImageProcess/PostProcessLayerBlit"
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
            Name "ImageProcess Post Process Layer Blit"

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
            TEXTURE2D_X(_LayerTexture);

            // The 24 Photoshop/PDF blend modes (ColorBurn .. ApplyLayerBlend) live in a shared
            // include so this file, Gradient/GradientMap and Halftone cannot drift apart.
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 layer = source * (half4)_LayerColor;
                if (_LayerTextureEnabled > 0.5)
                {
                    half4 textureLayer = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearClamp, uv);
                    layer = textureLayer * (half4)_LayerColor;
                }

                layer.rgb = ApplyLayerBlend(source.rgb, layer.rgb, _LayerBlendMode);
                return half4(lerp(source.rgb, layer.rgb, saturate(_Intensity) * layer.a), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
