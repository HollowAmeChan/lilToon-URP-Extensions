Shader "Hidden/lilToon-Shoost/URP/Shoost/CinematicBars"
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
            Name "Shoost Cinematic Bars"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCinematicBars

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;

            half4 FragCinematicBars(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                float screenAspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float targetAspect = max(_LayerParams0.x, 0.0001);
                float dynamicBarHeight = targetAspect > screenAspect
                    ? saturate((1.0 - screenAspect / targetAspect) * 0.5)
                    : 0.0;
                float barHeight = saturate(dynamicBarHeight + max(_LayerParams0.y, 0.0));
                float softnessUv = max(_LayerParams0.z, 0.0) / max(_ScreenParams.y, 1.0);
                float distanceToHorizontalEdge = min(input.texcoord.y, 1.0 - input.texcoord.y);
                float barMask = softnessUv > 0.000001
                    ? 1.0 - smoothstep(barHeight, barHeight + softnessUv, distanceToHorizontalEdge)
                    : 1.0 - step(barHeight, distanceToHorizontalEdge);
                float opacity = saturate(_Intensity * _LayerColor.a);

                source.rgb = lerp(source.rgb, _LayerColor.rgb, saturate(barMask) * opacity);
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
