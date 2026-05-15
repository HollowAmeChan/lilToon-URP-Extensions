Shader "Hidden/lilToon-Shoost/URP/Shoost/Fisheye"
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
            Name "Shoost Fisheye"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFisheye

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;

            half4 FragFisheye(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 centered = input.texcoord - 0.5;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                centered.x *= aspect;

                float scale = max(_LayerParams0.x, 0.01);
                float softness = saturate(_LayerParams0.y);
                bool circular = _LayerParams0.z > 0.5;

                float radius = circular ? length(centered) : max(abs(centered.x), abs(centered.y));
                float warp = radius * radius * scale * intensity * 0.65;

                float2 warpedUV = input.texcoord + centered * warp;
                half4 warped = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV);

                float edgeStart = max(0.5 - softness, 0.0);
                float edgeMask = smoothstep(edgeStart, 0.5, radius);
                warped.rgb = lerp(warped.rgb, _LayerColor.rgb, edgeMask * intensity);
                return warped;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
