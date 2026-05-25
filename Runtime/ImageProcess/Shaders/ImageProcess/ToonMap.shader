Shader "Hidden/lilToon/URP/ImageProcess/ToonMap"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ImageProcess ToonMap"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragToonMap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x: mode, 0 = None, 1 = Neutral, 2 = ACES

            float3 ApplyImageProcessToonMap(float3 color, float mode)
            {
                color = max(color, 0.0);

                if (mode < 0.5)
                {
                    return color;
                }

                if (mode < 1.5)
                {
                    return saturate(NeutralTonemap(color));
                }

                return saturate(AcesTonemap(unity_to_ACES(color)));
            }

            half4 FragToonMap(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
                    return source;
                }

                float mode = floor(_LayerParams0.x + 0.5);
                float3 mapped = ApplyImageProcessToonMap(source.rgb, mode);
                return half4(lerp(source.rgb, mapped, amount), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
