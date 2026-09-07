Shader "Hidden/lilToon/URP/HoGTAO"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_HoGTAOCurrentTex);
        TEXTURE2D_X(_HoGTAOHistoryPrevTex);
        float _HoGTAODebugMode;
        float _HoGTAOHistoryBlend;

        half4 Generate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 normalDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            half coverage = step(0.0001h, normalDepth.a);
            if (_HoGTAODebugMode > 0.5 && _HoGTAODebugMode < 1.5)
            {
                // Transport probe until the HTrace estimator is reintroduced:
                // depth is deliberately visible so a valid, refreshing input
                // cannot be mistaken for a white fallback.
                half depthProbe = saturate(normalDepth.a / 50.0h);
                return half4(depthProbe, depthProbe, depthProbe, 1.0h);
            }
            if (_HoGTAODebugMode > 1.5)
            {
                return half4(normalDepth.rgb, saturate(normalDepth.a / 50.0h));
            }

            // Transport-only v0: prove GeometryBuffer -> AO -> consumer wiring.
            // The HTrace GTAO estimator will replace this value in the next step.
            return half4(coverage, coverage, coverage, 1.0h);
        }

        half4 Temporal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half current = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half previous = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, input.texcoord).r;
            half ao = lerp(current, previous, saturate(_HoGTAOHistoryBlend));
            return half4(ao, ao, ao, 1.0h);
        }

        half4 OutputAO(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half ao = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            return half4(ao, ao, ao, 1.0h);
        }

        half4 DebugOutput(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }
        ENDHLSL

        Pass
        {
            Name "Ho-GTAO Generate"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Generate
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Temporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Temporal
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Output"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutputAO
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Debug Output"
            Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DebugOutput
            ENDHLSL
        }
    }
}
