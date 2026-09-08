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

        float3 HoSSGIColor(float3 value)
        {
            float luminance = dot(value, float3(0.2126, 0.7152, 0.0722));
            return lerp(luminance.xxx, value, _HoSSGISourceSaturation);
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
            float2 noise = HoSSGIHash2(pixel + _Time.y);
            float2 texel = _ScreenParams.zw;
            float3 radiance = 0;
            float hits = 0;
            int rays = max(1, _HoSSGIRayCount);
            int steps = max(4, _HoSSGIStepCount);

            [loop]
            for (int ray = 0; ray < rays; ray++)
            {
                float angle = (ray + noise.x) * 6.2831853 / rays;
                float2 direction = float2(cos(angle), sin(angle));
                float2 marchDir = direction * lerp(0.003, 0.04, noise.y);
                float3 tangent = normalize(abs(centerNormalVS.y) < 0.99 ? cross(centerNormalVS, float3(0, 1, 0)) : cross(centerNormalVS, float3(1, 0, 0)));
                float3 bitangent = normalize(cross(centerNormalVS, tangent));
                float3 rayDirVS = normalize(tangent * direction.x + bitangent * direction.y + centerNormalVS * (0.25 + 0.5 * noise.x));

                [loop]
                for (int stepIndex = 1; stepIndex <= steps; stepIndex++)
                {
                    float t = (stepIndex + noise.y) / steps;
                    float2 sampleUV = uv + marchDir * (t * _HoSSGIRayLength * 0.18);
                    if (any(sampleUV <= 0.001) || any(sampleUV >= 0.999)) break;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    half4 sampleBase = SAMPLE_TEXTURE2D_X(_HoSSGISurfaceColor, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001 || sampleBase.a < 0.0001) continue;
                    float3 samplePositionVS = HoSSGIViewPosition(sampleUV, sampleGeometry.a);
                    float depthDelta = centerPositionVS.z - samplePositionVS.z;
                    float rayDepth = centerPositionVS.z + rayDirVS.z * t * _HoSSGIRayLength;
                    if (abs(rayDepth - samplePositionVS.z) < max(_HoSSGIThickness, 0.01) && depthDelta > 0.0)
                    {
                        float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_LinearClamp, sampleUV).rgb;
                        source *= lerp(1.0, sampleBase.rgb, 0.25);
                        float cosine = saturate(dot(centerNormalWS, normalize((float3)sampleGeometry.rgb * 2.0 - 1.0)));
                        float distanceWeight = rcp(1.0 + t * t * 4.0);
                        radiance += HoSSGIColor(source) * cosine * distanceWeight;
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
            ENDHLSL
        }
    }
}
