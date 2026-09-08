Shader "Hidden/lilToon/URP/HoSSGI"
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

        TEXTURE2D_X(_HoSSGIGeometry);
        TEXTURE2D_X(_HoSSGISurfaceColor);
        int _HoSSGIRayCount;
        int _HoSSGIStepCount;
        float _HoSSGIRayLength;
        float _HoSSGIThickness;
        float _HoSSGIIntensity;
        float _HoSSGISourceSaturation;

        float3 HoSSGIViewPosition(float2 uv, float linearDepth)
        {
            float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
            return ComputeViewSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_P) * float3(1.0, -1.0, -1.0);
        }

        float3 HoSSGIViewNormal(float3 normalWS)
        {
            return normalize(mul((float3x3)UNITY_MATRIX_V, normalWS) * float3(1.0, -1.0, -1.0));
        }

        float2 HoSSGIHash2(float2 p)
        {
            float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return frac(sin(q) * 43758.5453);
        }

        float3 HoSSGIBuildTangent(float3 normal)
        {
            return normalize(abs(normal.y) < 0.95 ? cross(normal, float3(0, 1, 0)) : cross(normal, float3(1, 0, 0)));
        }

        float3 HoSSGIColor(float3 value)
        {
            float luminance = dot(value, float3(0.2126, 0.7152, 0.0722));
            return lerp(luminance.xxx, value, _HoSSGISourceSaturation);
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

        float4 Trace(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 center = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            half4 centerBase = SAMPLE_TEXTURE2D_X(_HoSSGISurfaceColor, sampler_PointClamp, uv);
            if (center.a < 0.0001 || centerBase.a < 0.0001) return 0;

            float3 centerNormalWS = normalize((float3)center.rgb * 2.0 - 1.0);
            float3 centerNormalVS = HoSSGIViewNormal(centerNormalWS);
            float3 centerPositionVS = HoSSGIViewPosition(uv, center.a);
            float2 pixel = uv * _ScreenParams.xy;
            float2 noise = HoSSGIHash2(pixel + floor(_Time.y * 60.0));
            float3 radiance = 0;
            float hits = 0;
            int rays = max(1, _HoSSGIRayCount);
            int steps = max(4, _HoSSGIStepCount);
            float3 tangent = HoSSGIBuildTangent(centerNormalVS);
            float3 bitangent = normalize(cross(centerNormalVS, tangent));
            float3 centerPositionWS = mul(UNITY_MATRIX_I_V, float4(centerPositionVS, 1.0)).xyz;

            [loop]
            for (int ray = 0; ray < rays; ray++)
            {
                // Cosine-weighted hemisphere sample. The ray is generated in view
                // space and projected to screen space, keeping the depth test and
                // the screen trajectory on the same geometry.
                float u = (ray + 0.5) / rays;
                float v = frac(noise.x + ray * 0.61803398875);
                float phi = 6.2831853 * u + noise.y * 6.2831853;
                float cosTheta = sqrt(saturate(v));
                float sinTheta = sqrt(saturate(1.0 - v));
                float3 rayDirVS = normalize(
                    tangent * (cos(phi) * sinTheta)
                    + bitangent * (sin(phi) * sinTheta)
                    + centerNormalVS * cosTheta);

                float3 rayEndVS = centerPositionVS + rayDirVS * _HoSSGIRayLength;
                float3 startNDC = ComputeNormalizedDeviceCoordinatesWithZ(centerPositionVS + rayDirVS * 0.02, UNITY_MATRIX_P);
                float3 endNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayEndVS, UNITY_MATRIX_P);
                float2 screenDelta = endNDC.xy - startNDC.xy;
                if (dot(screenDelta, screenDelta) < 1.0e-8) continue;
                float previousDelta = 0.0;
                bool hasPrevious = false;

                [loop]
                for (int stepIndex = 1; stepIndex <= steps; stepIndex++)
                {
                    float t = (stepIndex + noise.y) / steps;
                    float3 rayPositionVS = lerp(centerPositionVS + rayDirVS * 0.02, rayEndVS, t);
                    float3 rayNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayPositionVS, UNITY_MATRIX_P);
                    float2 sampleUV = rayNDC.xy;
                    if (any(sampleUV <= 0.001) || any(sampleUV >= 0.999)) break;
                    if (distance(sampleUV * _ScreenParams.xy, pixel) < 1.5) continue;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    half4 sampleBase = SAMPLE_TEXTURE2D_X(_HoSSGISurfaceColor, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001 || sampleBase.a < 0.0001) continue;
                    float3 samplePositionVS = HoSSGIViewPosition(sampleUV, sampleGeometry.a);
                    float rayDepth = -rayPositionVS.z;
                    float sampleDepth = -samplePositionVS.z;
                    float depthDelta = rayDepth - sampleDepth;
                    float thickness = max(_HoSSGIThickness, 0.01);
                    bool crossedSurface = hasPrevious && depthDelta >= -thickness && previousDelta < -thickness;
                    previousDelta = depthDelta;
                    hasPrevious = true;
                    if (crossedSurface)
                    {
                        float3 source = sampleBase.rgb;
                        float3 sampleNormalWS = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                        float3 sampleNormalVS = HoSSGIViewNormal(sampleNormalWS);
                        float3 lightDirection = normalize(samplePositionVS - centerPositionVS);
                        float receiverCosine = saturate(dot(centerNormalVS, lightDirection));
                        float sourceCosine = saturate(dot(sampleNormalVS, -lightDirection));
                        float distanceWeight = exp2(-3.0 * t) * rcp(1.0 + t * t * 2.0);
                        float3 samplePositionWS = mul(UNITY_MATRIX_I_V, float4(samplePositionVS, 1.0)).xyz;
                        float3 directLighting = HoSSGIGetDirectLighting(samplePositionWS, sampleNormalWS);
                        radiance += HoSSGIColor(source * directLighting) * receiverCosine * sourceCosine * distanceWeight;
                        hits += 1.0;
                        break;
                    }
                }
            }

            float confidence = saturate(hits / rays);
            return float4(radiance / max(rays, 1), confidence);
        }

        float4 Frag(Varyings input) : SV_Target { return Trace(input) * float4(_HoSSGIIntensity.xxx, 1); }
        ENDHLSL

        Pass
        {
            Name "Ho-SSGI Raw Trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            ENDHLSL
        }
    }
}
