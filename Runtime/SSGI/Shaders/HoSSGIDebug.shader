Shader "Hidden/lilToon/URP/HoSSGI/Debug"
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

        struct Attributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 texcoord : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        TEXTURE2D_X(_HoSSGIGeometry);
        TEXTURE2D_X(_HoSSGISource);
        TEXTURE2D_X(_HoSSGICameraSource);
        TEXTURE2D_X(_HoSSGIRawGI);
        TEXTURE2D_X(_HoGITexture);
        TEXTURE2D_X(_HoSSGIReservoirColor);
        TEXTURE2D_X(_HoSSGIReservoirAux);
        TEXTURE2D_X(_HoSSGISpatialGuidance);
        TEXTURE2D_X(_HoSSGISampleCountHistory);
        TEXTURE2D_X(_HoSSGIInvalidityHistory);
        TEXTURE2D_X(_HoSSGITemporalDebug);
        TEXTURE2D_X(_HoSSGISpatialResolveDebug);
        TEXTURE2D_X(_HoSSGITemporalDenoisedDebug);
        TEXTURE2D_X(_HoSSGIBilateralFirstDebug);
        int _HoSSGIDebugMode;
        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_LinearClamp, uv).rgb;
            float3 cameraSource = SAMPLE_TEXTURE2D_X(_HoSSGICameraSource, sampler_LinearClamp, uv).rgb;
            float4 rawGi = SAMPLE_TEXTURE2D_X(_HoSSGIRawGI, sampler_LinearClamp, uv);
            float4 gi = SAMPLE_TEXTURE2D_X(_HoGITexture, sampler_LinearClamp, uv);
            half4 reservoirColor = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirColor, sampler_PointClamp, uv);
            half4 reservoirAux = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirAux, sampler_PointClamp, uv);
            if (_HoSSGIDebugMode == 1) return float4(source, 1);
            if (_HoSSGIDebugMode == 2) return float4(step(0.0001, geometry.a).xxx, 1);
            if (_HoSSGIDebugMode == 3) return float4(geometry.rgb, 1);
            if (_HoSSGIDebugMode == 4) return float4(gi.rgb, 1);
            if (_HoSSGIDebugMode == 5) return float4(gi.a.xxx, 1);
            if (_HoSSGIDebugMode == 6) return float4(rawGi.rgb, rawGi.a);
            if (_HoSSGIDebugMode == 7)
            {
                float weight = reservoirAux.y > 1.0e-5
                    ? reservoirColor.a / max(reservoirAux.x * reservoirAux.y, 1.0e-5)
                    : 0.0;
                return float4((1.0 - exp(-max(weight, 0.0))).xxx, 1.0);
            }
            if (_HoSSGIDebugMode == 8)
                return float4((1.0 - exp(-max(reservoirAux.x, 0.0) / 8.0)).xxx, 1.0);
            if (_HoSSGIDebugMode == 9)
                return float4(saturate(reservoirAux.z).xxx, 1.0);
            if (_HoSSGIDebugMode == 10) return float4(cameraSource, 1.0);
            if (_HoSSGIDebugMode == 11) return SAMPLE_TEXTURE2D_X(_HoSSGISpatialGuidance, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 12)
            {
                float count = SAMPLE_TEXTURE2D_X(_HoSSGISampleCountHistory, sampler_PointClamp, uv).r;
                return float4((1.0 - exp(-count / 8.0)).xxx, 1.0);
            }
            if (_HoSSGIDebugMode == 13)
                return SAMPLE_TEXTURE2D_X(_HoSSGIInvalidityHistory, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 14)
                return SAMPLE_TEXTURE2D_X(_HoSSGITemporalDebug, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 15)
                return SAMPLE_TEXTURE2D_X(_HoSSGISpatialResolveDebug, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 16)
                return SAMPLE_TEXTURE2D_X(_HoSSGITemporalDenoisedDebug, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 17)
                return SAMPLE_TEXTURE2D_X(_HoSSGIBilateralFirstDebug, sampler_LinearClamp, uv);
            return float4(source, 1);
        }
        ENDHLSL
        Pass
        {
            Name "Ho-SSGI Debug"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
