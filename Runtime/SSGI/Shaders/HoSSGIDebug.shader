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
        TEXTURE2D_X(_HoSSGISource);
        TEXTURE2D_X(_HoSSGIGeometry);
        TEXTURE2D_X(_HoSSGISurfaceColor);
        TEXTURE2D_X(_HoGITexture);
        int _HoSSGIDebugMode;
        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_LinearClamp, uv).rgb;
            float4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoSSGISurfaceColor, sampler_LinearClamp, uv);
            float4 gi = SAMPLE_TEXTURE2D_X(_HoGITexture, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 1) return float4(source, 1);
            if (_HoSSGIDebugMode == 2) return float4(step(0.0001, geometry.a).xxx, 1);
            if (_HoSSGIDebugMode == 3) return float4(geometry.rgb, 1);
            if (_HoSSGIDebugMode == 4) return float4(gi.rgb, 1);
            if (_HoSSGIDebugMode == 5) return float4(gi.a.xxx, 1);
            if (_HoSSGIDebugMode == 6) return surfaceColor;
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
