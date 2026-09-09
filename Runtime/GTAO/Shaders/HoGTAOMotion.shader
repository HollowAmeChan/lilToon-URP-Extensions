Shader "Hidden/lilToon/URP/HoGTAOMotion"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing
        #pragma multi_compile _ DOTS_INSTANCING_ON
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

        // Unity's motion-vector globals expose the previous camera VP through
        // _PrevViewProjMatrix; there is no UNITY_PREV_MATRIX_VP macro in URP.

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 previousPositionOS : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionCurrentWS : TEXCOORD0;
            float3 positionPreviousWS : TEXCOORD1;
            float2 currentPreviousViewZ : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output = (Varyings)0;
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            // Unity supplies the previous skinned position on TEXCOORD4 for
            // the MotionVectors renderer-list configuration.
            float3 previousPositionOS = unity_MotionVectorsParams.x == 1.0
                ? input.previousPositionOS
                : input.positionOS;
            output.positionCurrentWS = mul(UNITY_MATRIX_M, float4(input.positionOS, 1.0)).xyz;
            output.positionPreviousWS = mul(UNITY_PREV_MATRIX_M, float4(previousPositionOS, 1.0)).xyz;
            output.positionCS = mul(UNITY_MATRIX_VP, float4(output.positionCurrentWS, 1.0));

            float3 currentView = mul(UNITY_MATRIX_V, float4(output.positionCurrentWS, 1.0)).xyz;
            float3 previousView = mul(UNITY_MATRIX_V, float4(output.positionPreviousWS, 1.0)).xyz;
            output.currentPreviousViewZ = float2(-currentView.z, -previousView.z);
            return output;
        }

        float4 MotionMask(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float moving = step(1.0e-4, length(input.positionCurrentWS - input.positionPreviousWS));
            return float4(moving, moving, 0.0, 0.0);
        }

        float4 MotionDelta(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float3 viewDirection = normalize(_WorldSpaceCameraPos - GetAbsolutePositionWS(input.positionCurrentWS));
            float3 normalCurrent = -normalize(cross(ddx(input.positionCurrentWS), ddy(input.positionCurrentWS)));
            float3 normalPrevious = -normalize(cross(ddx(input.positionPreviousWS), ddy(input.positionPreviousWS)));
            float angleDivergence = dot(viewDirection, normalCurrent) - dot(viewDirection, normalPrevious);
            float normalVisibility = dot(viewDirection, normalPrevious) <= 0.05 && angleDivergence >= 0.0001
                ? -10000.0
                : 1.0;

            float skinnedMeshSign = unity_MotionVectorsParams.x ? -1.0 : 1.0;
            float positionDelta = (length(input.positionCurrentWS - input.positionPreviousWS) + 0.0001) * skinnedMeshSign;
            float viewDepthDelta = (input.currentPreviousViewZ.y - input.currentPreviousViewZ.x) * normalVisibility;
            float2 direction = PackNormalOctQuadEncode(input.positionCurrentWS - input.positionPreviousWS);
            return float4(viewDepthDelta, positionDelta, direction * 0.5 + 0.5);
        }
        ENDHLSL

        Pass
        {
            Name "Ho-GTAO Motion Mask"
            Tags { "LightMode" = "MotionVectors" }
            ZWrite Off
            ZTest Equal
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MotionMask
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Motion Delta"
            Tags { "LightMode" = "MotionVectors" }
            ZWrite Off
            ZTest Equal
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MotionDelta
            ENDHLSL
        }
    }

    FallBack Off
}
