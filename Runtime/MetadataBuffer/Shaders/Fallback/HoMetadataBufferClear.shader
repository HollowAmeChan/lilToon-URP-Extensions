Shader "Hidden/lilToon/URP/MetadataBuffer/Clear"
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
            Name "MetadataBuffer Clear"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct FragmentOutput
            {
                half4 maskId : SV_Target0;
                half4 normalDepth : SV_Target1;
                half4 surfaceData : SV_Target2;
                half4 custom0 : SV_Target3;
                half4 objectCustom0 : SV_Target4;
                half4 objectCustom1 : SV_Target5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            FragmentOutput Frag(Varyings input)
            {
                FragmentOutput output;
                output.maskId = 0;
                output.normalDepth = 0;
                output.surfaceData = 0;
                output.custom0 = 0;
                output.objectCustom0 = 0;
                output.objectCustom1 = 0;
                return output;
            }
            ENDHLSL
        }
    }
}
