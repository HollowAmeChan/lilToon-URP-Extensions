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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

        float3 HoSSGIViewPosition(float2 uv, float linearDepth)
        {
            float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
            return ComputeViewSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_P) * float3(1.0, -1.0, -1.0);
        }

        float3 HoSSGIGetDirectLighting(float3 positionWS, float3 normalWS)
        {
            Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
            float3 lighting = mainLight.color
                * (mainLight.distanceAttenuation * mainLight.shadowAttenuation)
                * saturate(dot(normalWS, mainLight.direction));

            #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = ComputeNormalizedDeviceCoordinatesWithZ(positionWS, UNITY_MATRIX_VP).xy;
                uint additionalLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                    additionalLightCount = 1u;
                #endif
                LIGHT_LOOP_BEGIN(additionalLightCount)
                {
                    Light light = GetAdditionalLight(lightIndex, positionWS);
                    lighting += light.color
                        * (light.distanceAttenuation * light.shadowAttenuation)
                        * saturate(dot(normalWS, light.direction));
                }
                LIGHT_LOOP_END
            #endif

            return max(lighting, 0.0);
        }
        TEXTURE2D_X(_HoSSGIGeometry);
        TEXTURE2D_X(_HoSSGISurfaceColor);
        TEXTURE2D_X(_HoGITexture);
        int _HoSSGIDebugMode;
        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            float4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoSSGISurfaceColor, sampler_LinearClamp, uv);
            float3 source = surfaceColor.rgb;
            float4 gi = SAMPLE_TEXTURE2D_X(_HoGITexture, sampler_LinearClamp, uv);
            if (_HoSSGIDebugMode == 1) return float4(source, 1);
            if (_HoSSGIDebugMode == 2) return float4((step(0.0001, geometry.a) * step(0.0001, surfaceColor.a)).xxx, 1);
            if (_HoSSGIDebugMode == 3) return float4(geometry.rgb, 1);
            if (_HoSSGIDebugMode == 4) return float4(gi.rgb, 1);
            if (_HoSSGIDebugMode == 5) return float4(gi.a.xxx, 1);
            if (_HoSSGIDebugMode == 6)
            {
                if (geometry.a < 0.0001 || surfaceColor.a < 0.0001) return 0;
                float3 positionVS = HoSSGIViewPosition(uv, geometry.a);
                float3 positionWS = mul(UNITY_MATRIX_I_V, float4(positionVS, 1.0)).xyz;
                float3 normalWS = normalize((float3)geometry.rgb * 2.0 - 1.0);
                return float4(surfaceColor.rgb * HoSSGIGetDirectLighting(positionWS, normalWS), 1);
            }
            return float4(source, 1);
        }
        ENDHLSL
        Pass
        {
            Name "Ho-SSGI Debug"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            ENDHLSL
        }
    }
}
