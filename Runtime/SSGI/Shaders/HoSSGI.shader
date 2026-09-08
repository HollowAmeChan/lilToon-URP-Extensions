Shader "Hidden/lilToon/URP/HoSSGI"
{
    // Temporal and bilateral reconstruction are kept in the same shader so the
    // producer can publish both raw and filtered GI for feature-local debugging.
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
        TEXTURE2D_X(_HoGITexture);
        TEXTURE2D_X(_HoSSGIRawGI);
        TEXTURE2D_X(_HoSSGIRawGIInput);
        TEXTURE2D_X(_HoSSGIHistory);
        TEXTURE2D_X(_HoSSGIHistoryDepth);
        TEXTURE2D_X(_HoSSGIMotionVectors);
        TEXTURE2D_X(_HoSSGIReservoirColor);
        TEXTURE2D_X(_HoSSGIReservoirAux);
        TEXTURE2D_X(_HoSSGIReservoirRay);
        TEXTURE2D_X(_HoSSGIReservoirHistoryColor);
        TEXTURE2D_X(_HoSSGIReservoirHistoryAux);
        TEXTURE2D_X(_HoSSGIReservoirHistoryRay);
        TEXTURE2D_X(_BlitTexture);
        int _HoSSGIRayCount;
        int _HoSSGIStepCount;
        float _HoSSGIRayLength;
        float _HoSSGIThickness;
        float _HoSSGIIntensity;
        float _HoSSGISourceSaturation;
        float _HoSSGITemporalBlend;
        float _HoSSGISpatialRadius;
        float _HoSSGIHistoryValid;
        float _HoSSGIUseMotion;
        float _HoSSGIReservoirReuse;
        float _HoSSGIReservoirValidation;
        float _HoSSGIFireflyEnabled;

        struct HoSSGIReservoir
        {
            float3 color;
            float wsum;
            float m;
            float target;
            float hit;
            float distance;
            float3 direction;
            float3 originNormal;
        };

        struct HoSSGITraceOutput
        {
            float4 gi : SV_Target0;
            float4 reservoirColor : SV_Target1;
            float4 reservoirAux : SV_Target2;
            float4 reservoirRay : SV_Target3;
        };

        struct HoSSGITemporalOutput
        {
            float4 gi : SV_Target0;
            float4 reservoirColor : SV_Target1;
            float4 reservoirAux : SV_Target2;
            float4 reservoirRay : SV_Target3;
        };

        struct HoSSGIFireflyOutput
        {
            float4 reservoirColor : SV_Target0;
            float4 reservoirAux : SV_Target1;
            float4 reservoirRay : SV_Target2;
        };

        float HoSSGILuminance(float3 value)
        {
            return dot(max(value, 0.0), float3(0.2126, 0.7152, 0.0722));
        }

        float HoSSGIReservoirRandom(float2 pixel, float salt)
        {
            return frac(sin(dot(pixel + salt, float2(12.9898, 78.233))) * 43758.5453);
        }

        void HoSSGIReservoirUpdate(
            float3 sampleColor,
            float sampleTarget,
            float sampleHit,
            float sampleDistance,
            float3 sampleDirection,
            float3 sampleOriginNormal,
            float sampleM,
            inout HoSSGIReservoir reservoir,
            float randomValue)
        {
            sampleTarget = max(sampleTarget, 0.0);
            reservoir.wsum += sampleTarget;
            reservoir.m += max(sampleM, 0.0);
            float selectionProbability = sampleTarget / max(reservoir.wsum, 1.0e-6);
            if (sampleTarget > 0.0 && randomValue < selectionProbability)
            {
                reservoir.color = sampleColor;
                reservoir.target = sampleTarget;
                reservoir.hit = sampleHit;
                reservoir.distance = sampleDistance;
                reservoir.direction = sampleDirection;
                reservoir.originNormal = sampleOriginNormal;
            }
        }

        void HoSSGIReservoirMerge(inout HoSSGIReservoir reservoir, HoSSGIReservoir candidate, float randomValue)
        {
            float candidateWsum = max(candidate.wsum, 0.0);
            reservoir.wsum += candidateWsum;
            reservoir.m += max(candidate.m, 0.0);
            float selectionProbability = candidateWsum / max(reservoir.wsum, 1.0e-6);
            if (candidateWsum > 0.0 && randomValue < selectionProbability)
            {
                reservoir.color = candidate.color;
                reservoir.target = candidate.target;
                reservoir.hit = candidate.hit;
                reservoir.distance = candidate.distance;
                reservoir.direction = candidate.direction;
                reservoir.originNormal = candidate.originNormal;
            }
        }

        float3 HoSSGIResolveReservoir(HoSSGIReservoir reservoir)
        {
            float denominator = max(reservoir.m * reservoir.target, 1.0e-6);
            return max(reservoir.color * (reservoir.wsum / denominator), 0.0);
        }

        float2 HoSSGIEncodeOcta(float3 normal)
        {
            normal = normalize(normal);
            normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 1.0e-6);
            if (normal.z < 0.0)
            {
                float2 signXY = float2(normal.x >= 0.0 ? 1.0 : -1.0, normal.y >= 0.0 ? 1.0 : -1.0);
                normal.xy = (1.0 - abs(normal.yx)) * signXY;
            }
            return normal.xy * 0.5 + 0.5;
        }

        float3 HoSSGIDecodeOcta(float2 encoded)
        {
            float3 normal = float3(encoded * 2.0 - 1.0, 1.0 - abs(encoded.x * 2.0 - 1.0) - abs(encoded.y * 2.0 - 1.0));
            if (normal.z < 0.0)
            {
                float2 signXY = float2(normal.x >= 0.0 ? 1.0 : -1.0, normal.y >= 0.0 ? 1.0 : -1.0);
                normal.xy = (1.0 - abs(normal.yx)) * signXY;
            }
            return normalize(normal);
        }

        float4 HoSSGIPackReservoirRay(HoSSGIReservoir reservoir)
        {
            return float4(HoSSGIEncodeOcta(reservoir.direction), HoSSGIEncodeOcta(reservoir.originNormal));
        }

        void HoSSGIUnpackReservoirRay(float4 packed, inout HoSSGIReservoir reservoir)
        {
            reservoir.direction = HoSSGIDecodeOcta(packed.xy);
            reservoir.originNormal = HoSSGIDecodeOcta(packed.zw);
        }

        HoSSGIReservoir HoSSGILoadReservoir(float2 uv)
        {
            half4 packedColor = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirColor, sampler_PointClamp, uv);
            half4 packedAux = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirAux, sampler_PointClamp, uv);
            HoSSGIReservoir reservoir;
            reservoir.color = packedColor.rgb;
            reservoir.wsum = max(packedColor.a, 0.0);
            reservoir.m = max(packedAux.x, 0.0);
            reservoir.target = max(packedAux.y, 0.0);
            reservoir.hit = packedAux.z;
            reservoir.distance = max(packedAux.w, 0.0);
            HoSSGIUnpackReservoirRay(SAMPLE_TEXTURE2D_X(_HoSSGIReservoirRay, sampler_PointClamp, uv), reservoir);
            return reservoir;
        }

        HoSSGIReservoir HoSSGILoadHistoryReservoir(float2 uv)
        {
            half4 packedColor = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirHistoryColor, sampler_PointClamp, uv);
            half4 packedAux = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirHistoryAux, sampler_PointClamp, uv);
            HoSSGIReservoir reservoir;
            reservoir.color = packedColor.rgb;
            reservoir.wsum = max(packedColor.a, 0.0);
            reservoir.m = max(packedAux.x, 0.0);
            reservoir.target = max(packedAux.y, 0.0);
            reservoir.hit = packedAux.z;
            reservoir.distance = max(packedAux.w, 0.0);
            HoSSGIUnpackReservoirRay(SAMPLE_TEXTURE2D_X(_HoSSGIReservoirHistoryRay, sampler_PointClamp, uv), reservoir);
            return reservoir;
        }

        float3 HoSSGIWorldPosition(float2 uv, float linearDepth)
        {
            float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
            return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
        }

        float HoSSGILinearDepth(float3 positionWS)
        {
            return max(-mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).z, 0.0);
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

        float HoSSGIClipRayToScreen(float2 startUV, float2 endUV, out float2 clippedEndUV)
        {
            float2 direction = endUV - startUV;
            float maxT = 1.0;
            if (direction.x < 0.0) maxT = min(maxT, (0.001 - startUV.x) / direction.x);
            if (direction.x > 0.0) maxT = min(maxT, (0.999 - startUV.x) / direction.x);
            if (direction.y < 0.0) maxT = min(maxT, (0.001 - startUV.y) / direction.y);
            if (direction.y > 0.0) maxT = min(maxT, (0.999 - startUV.y) / direction.y);
            maxT = saturate(maxT);
            clippedEndUV = startUV + direction * maxT;
            return maxT;
        }

        float HoSSGIValidateReservoirRay(
            float3 originPositionWS,
            float3 receiverNormalWS,
            HoSSGIReservoir reservoir)
        {
            if (reservoir.hit < 0.5 || reservoir.distance <= 0.001)
                return 1.0;

            float3 directionWS = normalize(reservoir.direction);
            if (dot(directionWS, directionWS) < 0.25)
                return 0.0;

            float3 expectedHitWS = originPositionWS + directionWS * reservoir.distance;
            float3 hitNDC = ComputeNormalizedDeviceCoordinatesWithZ(expectedHitWS, UNITY_MATRIX_VP);
            if (hitNDC.z < 0.0 || hitNDC.z > 1.0 || any(hitNDC.xy < 0.0) || any(hitNDC.xy > 1.0))
                return 0.0;

            float2 hitUV = hitNDC.xy;
            float expectedDepth = HoSSGILinearDepth(expectedHitWS);
            float2 texel = rcp(max(_ScreenParams.xy, 1.0));
            float depthTolerance = max(_HoSSGIThickness * 2.0, expectedDepth * 0.05);
            float bestAgreement = 0.0;
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float2 sampleUV = hitUV + float2(x, y) * texel;
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) continue;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001h) continue;
                    float depthAgreement = exp2(-abs(sampleGeometry.a - expectedDepth) / max(depthTolerance, 0.01));
                    float3 sampleNormal = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                    float normalAgreement = saturate((dot(sampleNormal, -directionWS) - 0.1) / 0.9);
                    float receiverAgreement = saturate((dot(receiverNormalWS, reservoir.originNormal) - 0.25) / 0.75);
                    bestAgreement = max(bestAgreement, depthAgreement * normalAgreement * receiverAgreement);
                }
            }

            float3 rayStartWS = originPositionWS + receiverNormalWS * 0.01 + directionWS * 0.01;
            float3 rayEndWS = originPositionWS + directionWS * min(
                max(reservoir.distance + _HoSSGIThickness * 2.0, 0.01),
                max(_HoSSGIRayLength, reservoir.distance));
            float3 startNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayStartWS, UNITY_MATRIX_VP);
            float3 endNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayEndWS, UNITY_MATRIX_VP);
            float2 clippedEndUV;
            float clippedRay = HoSSGIClipRayToScreen(startNDC.xy, endNDC.xy, clippedEndUV);
            if (clippedRay <= 0.001)
                return saturate(bestAgreement * 0.25);

            rayEndWS = lerp(rayStartWS, rayEndWS, clippedRay);
            float previousDelta = -2.0 * max(_HoSSGIThickness, 0.01);
            float remarchedDistance = 0.0;
            bool remarchedHit = false;
            [loop]
            for (int stepIndex = 1; stepIndex <= 8; stepIndex++)
            {
                float t = (stepIndex + 0.5) / 8.0;
                float3 rayPositionWS = lerp(rayStartWS, rayEndWS, t);
                float3 rayNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayPositionWS, UNITY_MATRIX_VP);
                float2 sampleUV = rayNDC.xy;
                if (any(sampleUV <= 0.001) || any(sampleUV >= 0.999)) break;
                half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                if (sampleGeometry.a < 0.0001h)
                {
                    previousDelta = -2.0 * max(_HoSSGIThickness, 0.01);
                    continue;
                }
                float depthDelta = HoSSGILinearDepth(rayPositionWS) - sampleGeometry.a;
                bool crossedSurface = depthDelta >= -max(_HoSSGIThickness, 0.01)
                    && previousDelta < -max(_HoSSGIThickness, 0.01);
                previousDelta = depthDelta;
                if (crossedSurface)
                {
                    float3 samplePositionWS = HoSSGIWorldPosition(sampleUV, sampleGeometry.a);
                    remarchedDistance = distance(originPositionWS, samplePositionWS);
                    remarchedHit = true;
                    break;
                }
            }

            if (!remarchedHit)
                return 0.0;
            float distanceAgreement = exp2(-abs(remarchedDistance - reservoir.distance)
                / max(reservoir.distance * 0.2, max(_HoSSGIThickness, 0.01)));
            return saturate(bestAgreement * distanceAgreement);
        }

        float HoSSGIValidateReservoirLighting(float3 originPositionWS, HoSSGIReservoir reservoir)
        {
            if (reservoir.hit < 0.5 || reservoir.distance <= 0.001)
                return 1.0;

            float3 directionWS = normalize(reservoir.direction);
            float3 expectedHitWS = originPositionWS + directionWS * reservoir.distance;
            float3 hitNDC = ComputeNormalizedDeviceCoordinatesWithZ(expectedHitWS, UNITY_MATRIX_VP);
            if (hitNDC.z < 0.0 || hitNDC.z > 1.0 || any(hitNDC.xy < 0.0) || any(hitNDC.xy > 1.0))
                return 0.0;

            float storedLuminance = HoSSGILuminance(reservoir.color);
            float2 texel = rcp(max(_ScreenParams.xy, 1.0));
            float currentLuminance = 0.0;
            float weightSum = 0.0;
            float t = saturate(reservoir.distance / max(_HoSSGIRayLength, 0.01));
            float distanceWeight = exp2(-2.0 * t) * rcp(1.0 + t * t);
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float2 sampleUV = hitNDC.xy + float2(x, y) * texel;
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) continue;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001h) continue;
                    float3 samplePositionWS = HoSSGIWorldPosition(sampleUV, sampleGeometry.a);
                    float3 sampleNormalWS = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                    float3 lightDirection = normalize(samplePositionWS - originPositionWS);
                    float sourceCosine = saturate(dot(sampleNormalWS, -lightDirection));
                    float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_PointClamp, sampleUV).rgb;
                    float3 candidate = HoSSGIColor(source) * (3.14159265 * sourceCosine * distanceWeight);
                    float tapWeight = exp2(-0.75 * (x * x + y * y));
                    currentLuminance += HoSSGILuminance(candidate) * tapWeight;
                    weightSum += tapWeight;
                }
            }
            currentLuminance /= max(weightSum, 1.0e-5);
            float lightingChange = abs(storedLuminance - currentLuminance)
                / max(storedLuminance + currentLuminance, 0.001);
            return saturate(1.0 - lightingChange * 2.0);
        }

        HoSSGITraceOutput Trace(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 center = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            HoSSGITraceOutput output;
            output.gi = 0;
            output.reservoirColor = 0;
            output.reservoirAux = 0;
            output.reservoirRay = 0;
            if (center.a < 0.0001) return output;

            float3 centerNormalWS = normalize((float3)center.rgb * 2.0 - 1.0);
            float3 centerPositionWS = HoSSGIWorldPosition(uv, center.a);
            float2 pixel = uv * _ScreenParams.xy;
            float2 noise = HoSSGIHash2(pixel + floor(_Time.y * 60.0));
            float3 radiance = 0;
            float hits = 0;
            HoSSGIReservoir reservoir = (HoSSGIReservoir)0;
            int rays = max(1, _HoSSGIRayCount);
            int steps = max(4, _HoSSGIStepCount);
            float3 tangent = HoSSGIBuildTangent(centerNormalWS);
            float3 bitangent = normalize(cross(centerNormalWS, tangent));
            [loop]
            for (int ray = 0; ray < rays; ray++)
            {
                // Cosine-weighted hemisphere sampling follows HTrace's world-space
                // ray path. Screen UV is only used for visibility traversal.
                float u = (ray + 0.5) / rays;
                float v = frac(noise.x + ray * 0.61803398875);
                float phi = 6.2831853 * u + noise.y * 6.2831853;
                float cosTheta = sqrt(saturate(v));
                float sinTheta = sqrt(saturate(1.0 - v));
                float3 rayDirWS = normalize(
                    tangent * (cos(phi) * sinTheta)
                    + bitangent * (sin(phi) * sinTheta)
                    + centerNormalWS * cosTheta);

                // Match HTrace's footprint-aware normal bias to avoid self hits
                // without requiring an excessively large thickness value.
                float2 screenTexel = rcp(max(_ScreenParams.xy, 1.0));
                float3 cornerPositionWS = HoSSGIWorldPosition(uv + screenTexel * 0.5, center.a);
                float normalBias = abs(dot(cornerPositionWS - centerPositionWS, centerNormalWS)) * 2.0;
                float3 normalForBias = dot(centerNormalWS, rayDirWS) < 0.0 ? -centerNormalWS : centerNormalWS;
                float3 rayStartWS = centerPositionWS + normalForBias * max(normalBias, 0.01) + rayDirWS * 0.01;
                float3 rayEndWS = centerPositionWS + rayDirWS * _HoSSGIRayLength;
                float3 startNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayStartWS, UNITY_MATRIX_VP);
                float3 endNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayEndWS, UNITY_MATRIX_VP);
                float2 screenDelta = endNDC.xy - startNDC.xy;
                float3 candidateColor = 0.0;
                float candidateDistance = 0.0;
                float candidateHit = 0.0;
                if (dot(screenDelta, screenDelta) < 1.0e-8 || startNDC.z < 0.0 || startNDC.z > 1.0)
                {
                    reservoir.m += 1.0;
                    continue;
                }
                float2 clippedEndUV;
                float clippedRay = HoSSGIClipRayToScreen(startNDC.xy, endNDC.xy, clippedEndUV);
                if (clippedRay <= 0.001)
                {
                    reservoir.m += 1.0;
                    continue;
                }
                rayEndWS = lerp(rayStartWS, rayEndWS, clippedRay);
                float thickness = max(_HoSSGIThickness, 0.01);
                float previousDelta = -2.0 * thickness;
                bool hasPrevious = true;

                [loop]
                for (int stepIndex = 1; stepIndex <= steps; stepIndex++)
                {
                    // HTrace marches quadratically, concentrating samples near
                    // the receiver where screen-space intersections are stable.
                    float t = pow(saturate((stepIndex + noise.y) / steps), 2.0);
                    float3 rayPositionWS = lerp(rayStartWS, rayEndWS, t);
                    float3 rayNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayPositionWS, UNITY_MATRIX_VP);
                    float2 sampleUV = rayNDC.xy;
                    if (any(sampleUV <= 0.001) || any(sampleUV >= 0.999)) break;
                    if (distance(sampleUV * _ScreenParams.xy, pixel) < 1.5) continue;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001)
                    {
                        // Invalid coverage is an empty segment. Resetting the
                        // previous sign lets the first valid surface after a
                        // gap register as a hit instead of being skipped.
                        previousDelta = -2.0 * thickness;
                        hasPrevious = true;
                        continue;
                    }
                    float rayDepth = HoSSGILinearDepth(rayPositionWS);
                    float3 samplePositionWS = HoSSGIWorldPosition(sampleUV, sampleGeometry.a);
                    float sampleDepth = sampleGeometry.a;
                    float depthDelta = rayDepth - sampleDepth;
                    bool crossedSurface = hasPrevious && depthDelta >= -thickness && previousDelta < -thickness;
                    previousDelta = depthDelta;
                    hasPrevious = true;
                    if (crossedSurface)
                    {
                        float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_PointClamp, sampleUV).rgb;
                        float3 sampleNormalWS = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                        float3 lightDirection = normalize(samplePositionWS - centerPositionWS);
                        float sourceCosine = saturate(dot(sampleNormalWS, -lightDirection));
                        float distanceWeight = exp2(-2.0 * t) * rcp(1.0 + t * t);
                        candidateColor = HoSSGIColor(source) * (3.14159265 * sourceCosine * distanceWeight);
                        candidateDistance = distance(centerPositionWS, samplePositionWS);
                        candidateHit = step(1.0e-5, HoSSGILuminance(candidateColor));
                        radiance += candidateColor;
                        hits += 1.0;
                        break;
                    }
                }

                float candidateTarget = HoSSGILuminance(candidateColor);
                HoSSGIReservoirUpdate(
                    candidateColor,
                    candidateTarget,
                    candidateHit,
                    candidateDistance,
                    rayDirWS,
                    centerNormalWS,
                    1.0,
                    reservoir,
                    HoSSGIReservoirRandom(pixel, (float)(ray + 1) * 0.37 + (float)steps * 0.013));
            }

            float confidence = saturate(hits / rays);
            float3 reservoirRadiance = HoSSGIResolveReservoir(reservoir);
            // Keep the raw average available to compare estimator behavior while
            // using the reservoir estimate as the producer result.
            output.gi = float4(max(reservoirRadiance, 0.0), confidence);
            output.reservoirColor = float4(max(reservoir.color, 0.0), max(reservoir.wsum, 0.0));
            output.reservoirAux = float4(max(reservoir.m, 0.0), max(reservoir.target, 0.0), saturate(reservoir.hit), max(reservoir.distance, 0.0));
            output.reservoirRay = HoSSGIPackReservoirRay(reservoir);
            return output;
        }

        HoSSGITemporalOutput Temporal(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 current = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, uv);
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            HoSSGITemporalOutput output;
            output.gi = 0;
            output.reservoirColor = 0;
            output.reservoirAux = 0;
            output.reservoirRay = 0;
            if (geometry.a < 0.0001h) return output;

            float2 motion = _HoSSGIUseMotion > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoSSGIMotionVectors, sampler_LinearClamp, uv).xy
                : float2(0.0, 0.0);
            half3 currentNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            half currentNormalValid = step(0.0001h, dot(geometry.rgb, geometry.rgb));
            HoSSGIReservoir currentReservoir = HoSSGILoadReservoir(uv);
            HoSSGIReservoir merged = currentReservoir;
            float currentConfidence = saturate(current.a);
            float historyConfidenceSum = 0.0;
            float historyM = 0.0;

            float2 previousUVUnclamped = uv - motion;
            bool historyUVValid = previousUVUnclamped.x >= 0.0 && previousUVUnclamped.x <= 1.0
                && previousUVUnclamped.y >= 0.0 && previousUVUnclamped.y <= 1.0;
            if (_HoSSGIReservoirReuse > 0.5 && _HoSSGIHistoryValid > 0.5 && historyUVValid)
            {
                float2 historyTexel = rcp(max(_ScreenParams.xy, 1.0));
                float2 previousPixel = previousUVUnclamped * _ScreenParams.xy - 0.5;
                float2 previousBasePixel = floor(previousPixel);
                float2 previousFraction = frac(previousPixel);
                const float2 historyOffsets[4] =
                {
                    float2(0.0, 0.0), float2(1.0, 0.0),
                    float2(0.0, 1.0), float2(1.0, 1.0)
                };
                float4 historyWeights = float4(
                    (1.0 - previousFraction.x) * (1.0 - previousFraction.y),
                    previousFraction.x * (1.0 - previousFraction.y),
                    (1.0 - previousFraction.x) * previousFraction.y,
                    previousFraction.x * previousFraction.y);

                [unroll]
                for (int historyTap = 0; historyTap < 4; historyTap++)
                {
                    float2 tapPixel = previousBasePixel + historyOffsets[historyTap];
                    float2 tapUV = (tapPixel + 0.5) * historyTexel;
                    float tapWeight = historyWeights[historyTap];
                    if (tapWeight <= 1.0e-4 || any(tapUV < 0.0) || any(tapUV > 1.0)) continue;

                    half4 history = SAMPLE_TEXTURE2D_X(_HoSSGIHistory, sampler_PointClamp, tapUV);
                    half4 previousGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIHistoryDepth, sampler_PointClamp, tapUV);
                    half previousDepth = previousGeometry.a;
                    half depthAgreement = step(abs(geometry.a - previousDepth), max(0.08h * geometry.a, 0.05h));
                    half3 previousNormal = normalize((float3)previousGeometry.rgb * 2.0 - 1.0);
                    half normalAgreement = step(0.5h, dot(currentNormal, previousNormal));
                    half previousNormalValid = step(0.0001h, dot(previousGeometry.rgb, previousGeometry.rgb));
                    half accepted = depthAgreement * normalAgreement * currentNormalValid * previousNormalValid
                        * step(0.0001h, previousDepth);
                    float currentLum = HoSSGILuminance(current.rgb);
                    float historyLum = HoSSGILuminance(history.rgb);
                    float lightingChange = abs(currentLum - historyLum) / max(currentLum + historyLum, 0.001);
                    float historyScale = _HoSSGITemporalBlend * tapWeight * accepted
                        * saturate(1.0 - lightingChange * 2.0) * saturate(history.a);
                    if (historyScale <= 0.0) continue;

                    HoSSGIReservoir historyReservoir = HoSSGILoadHistoryReservoir(tapUV);
                    float tapM = historyReservoir.m * historyScale;
                    historyReservoir.wsum *= historyScale;
                    historyReservoir.m = tapM;
                    if (_HoSSGIReservoirValidation > 0.5)
                    {
                        float3 originPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                        float geometryValidation = HoSSGIValidateReservoirRay(originPositionWS, currentNormal, historyReservoir);
                        float lightingValidation = HoSSGIValidateReservoirLighting(originPositionWS, historyReservoir);
                        float validation = geometryValidation * lightingValidation;
                        historyReservoir.wsum *= validation;
                        historyReservoir.m *= validation;
                        tapM = historyReservoir.m;
                    }
                    historyM += tapM;
                    historyConfidenceSum += saturate(history.a) * tapM;
                    HoSSGIReservoirMerge(merged, historyReservoir,
                        HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(historyTap + 17)));
                }
            }

            // Keep recurrent history bounded like HTrace. Without this cap a
            // stationary pixel can accumulate an unbounded M/Wsum and become
            // slow to react when lighting or geometry changes.
            float maxHistoryM = min(100.0, 32.0 * max((float)_HoSSGIRayCount / 4.0, 1.0));
            if (merged.m > maxHistoryM)
            {
                float historyScale = maxHistoryM / max(merged.m, 1.0e-5);
                merged.m = maxHistoryM;
                merged.wsum *= historyScale;
            }

            float totalM = max(merged.m, 1.0e-6);
            float confidence = saturate((currentConfidence * max(currentReservoir.m, 0.0)
                + historyConfidenceSum) / totalM);
            float3 resolved = HoSSGIResolveReservoir(merged);
            output.gi = float4(resolved, confidence);
            output.reservoirColor = float4(max(merged.color, 0.0), max(merged.wsum, 0.0));
            output.reservoirAux = float4(max(merged.m, 0.0), max(merged.target, 0.0), saturate(merged.hit), max(merged.distance, 0.0));
            output.reservoirRay = HoSSGIPackReservoirRay(merged);
            return output;
        }

        HoSSGIFireflyOutput Firefly(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            HoSSGIFireflyOutput output;
            half4 packedColor = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirColor, sampler_PointClamp, uv);
            half4 packedAux = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirAux, sampler_PointClamp, uv);
            output.reservoirColor = packedColor;
            output.reservoirAux = packedAux;
            output.reservoirRay = SAMPLE_TEXTURE2D_X(_HoSSGIReservoirRay, sampler_PointClamp, uv);

            if (_HoSSGIFireflyEnabled < 0.5)
                return output;

            half4 centerGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            if (centerGeometry.a < 0.0001h || packedAux.y <= 1.0e-5h)
                return output;

            // HTrace suppresses isolated reservoir spikes before spatial reuse.
            // Use strict bounds so edge pixels never fold the opposite border into
            // their local statistics.
            float2 texel = rcp(max(_ScreenParams.xy, 1.0));
            float mean = 0.0;
            float mean2 = 0.0;
            float statisticsWeight = 0.0;
            [unroll]
            for (int y = -3; y <= 3; y++)
            {
                [unroll]
                for (int x = -3; x <= 3; x++)
                {
                    float2 tapUV = uv + float2(x, y) * texel;
                    bool tapInside = tapUV.x >= 0.0 && tapUV.x <= 1.0
                        && tapUV.y >= 0.0 && tapUV.y <= 1.0;
                    if (!tapInside) continue;
                    half4 tapGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, tapUV);
                    if (tapGeometry.a < 0.0001h) continue;
                    HoSSGIReservoir tapReservoir = HoSSGILoadReservoir(tapUV);
                    float tapLuminance = HoSSGILuminance(HoSSGIResolveReservoir(tapReservoir));
                    float tapWeight = exp2(-0.35 * (x * x + y * y));
                    mean += tapLuminance * tapWeight;
                    mean2 += tapLuminance * tapLuminance * tapWeight;
                    statisticsWeight += tapWeight;
                }
            }

            mean /= max(statisticsWeight, 1.0e-5);
            mean2 /= max(statisticsWeight, 1.0e-5);
            float variance = max(mean2 - mean * mean, 0.0);
            float threshold = mean + 2.0 * sqrt(variance) + 0.001;
            float centerLuminance = HoSSGILuminance(HoSSGIResolveReservoir(HoSSGILoadReservoir(uv)));
            float scale = min(1.0, threshold / max(centerLuminance, 1.0e-5));
            output.reservoirColor.a = packedColor.a * scale;
            return output;
        }

        float4 DepthHistory(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }

        float4 SpatialDenoise(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 centerGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            if (centerGeometry.a < 0.0001h) return 0;

            float3 centerNormal = normalize((float3)centerGeometry.rgb * 2.0 - 1.0);
            float centerDepth = centerGeometry.a;
            float3 centerPositionWS = HoSSGIWorldPosition(uv, centerDepth);
            float3 tangent = HoSSGIBuildTangent(centerNormal);
            float3 bitangent = normalize(cross(centerNormal, tangent));
            float worldRadius = max(centerDepth * max(_HoSSGISpatialRadius, 0.5) * 0.0025, 0.002);
            float sigma = max(worldRadius * 0.8, 0.001);
            float randomAngle = HoSSGIHash2(uv * _ScreenParams.xy).x * 6.2831853;
            float2x2 rotation = float2x2(cos(randomAngle), -sin(randomAngle), sin(randomAngle), cos(randomAngle));
            HoSSGIReservoir centerReservoir = HoSSGILoadReservoir(uv);
            HoSSGIReservoir merged = centerReservoir;
            half4 centerTemporal = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, uv);
            float confidenceSum = centerTemporal.a;
            float confidenceWeight = 1.0;
            const float2 poisson[8] =
            {
                float2(-0.326, -0.406), float2(0.695, -0.113),
                float2(-0.842, 0.684), float2(0.451, 0.553),
                float2(-0.758, 0.143), float2(0.103, -0.887),
                float2(0.912, 0.517), float2(-0.601, -0.712)
            };
            [unroll]
            for (int i = 0; i < 8; i++)
            {
                if (_HoSSGIReservoirReuse <= 0.5) break;
                float2 point = mul(rotation, poisson[i] * worldRadius);
                float3 samplePositionWS = centerPositionWS + tangent * point.x + bitangent * point.y;
                float3 sampleNDC = ComputeNormalizedDeviceCoordinatesWithZ(samplePositionWS, UNITY_MATRIX_VP);
                float2 tapUV = sampleNDC.xy;
                if (sampleNDC.z < 0.0 || sampleNDC.z > 1.0 || any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                half4 tapGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, tapUV);
                if (tapGeometry.a < 0.0001h) continue;
                float depthDelta = abs(tapGeometry.a - centerDepth) / max(centerDepth, 0.05);
                float3 sampleNormal = normalize((float3)tapGeometry.rgb * 2.0 - 1.0);
                float planeDistance = abs(dot(samplePositionWS - centerPositionWS, centerNormal));
                float planeDistanceNormalized = planeDistance / max(centerDepth, 0.05);
                float planeWeight = exp2(-100.0 * planeDistanceNormalized * planeDistanceNormalized);
                float normalWeight = saturate(dot(centerNormal, sampleNormal));
                float depthWeight = exp2(-32.0 * depthDelta * depthDelta);
                float gaussianWeight = exp2(-dot(point, point) / max(2.0 * sigma * sigma, 1.0e-5));
                float reuseWeight = planeWeight * normalWeight * depthWeight * gaussianWeight;
                if (reuseWeight <= 0.001) continue;

                HoSSGIReservoir tapReservoir = HoSSGILoadReservoir(tapUV);
                tapReservoir.wsum *= reuseWeight;
                tapReservoir.m *= reuseWeight;
                HoSSGIReservoirMerge(merged, tapReservoir, HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(i + 31)));

                half4 tapTemporal = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, tapUV);
                confidenceSum += tapTemporal.a * reuseWeight;
                confidenceWeight += reuseWeight;
            }

            if (_HoSSGIReservoirValidation > 0.5)
            {
                float3 centerPositionWS = HoSSGIWorldPosition(uv, centerDepth);
                float validation = HoSSGIValidateReservoirRay(centerPositionWS, centerNormal, merged);
                if (validation < 0.15)
                {
                    merged = centerReservoir;
                }
                else
                {
                    merged.wsum *= validation;
                }
            }

            float3 resolved = HoSSGIResolveReservoir(merged);
            float confidence = saturate(confidenceSum / max(confidenceWeight, 1.0e-5));
            return float4(resolved, confidence);
        }

        float4 BilateralDenoise(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 centerGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            half4 center = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_LinearClamp, uv);
            if (centerGeometry.a < 0.0001h)
                return 0;

            float3 centerNormal = normalize((float3)centerGeometry.rgb * 2.0 - 1.0);
            float centerDepth = centerGeometry.a;
            float2 texel = rcp(max(_ScreenParams.xy, 1.0)) * max(_HoSSGISpatialRadius * 0.5, 0.5);
            float3 centerToneMapped = center.rgb / (1.0 + HoSSGILuminance(center.rgb));
            float3 sum = centerToneMapped;
            float confidence = center.a;
            float weightSum = 1.0;

            [unroll]
            for (int y = -2; y <= 2; y++)
            {
                [unroll]
                for (int x = -2; x <= 2; x++)
                {
                    if (x == 0 && y == 0) continue;
                    float2 offset = float2(x, y);
                    float2 tapUV = uv + offset * texel;
                    if (any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                    half4 tapGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, tapUV);
                    if (tapGeometry.a < 0.0001h) continue;
                    float depthDelta = abs(tapGeometry.a - centerDepth) / max(centerDepth, 0.05);
                    float normalWeight = saturate((dot(centerNormal, normalize((float3)tapGeometry.rgb * 2.0 - 1.0)) - 0.25) * 1.3333);
                    float depthWeight = exp2(-28.0 * depthDelta * depthDelta);
                    float gaussianWeight = exp2(-0.55 * dot(offset, offset));
                    float tapWeight = normalWeight * depthWeight * gaussianWeight;
                    if (tapWeight <= 0.001) continue;
                    half4 tap = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_LinearClamp, tapUV);
                    sum += (tap.rgb / (1.0 + HoSSGILuminance(tap.rgb))) * tapWeight;
                    confidence += tap.a * tapWeight;
                    weightSum += tapWeight;
                }
            }

            float3 filteredToneMapped = sum / max(weightSum, 1.0e-5);
            float filteredLuminance = HoSSGILuminance(filteredToneMapped);
            float3 filtered = filteredToneMapped / max(1.0 - filteredLuminance, 0.05);
            return float4(max(filtered, 0.0), saturate(confidence / max(weightSum, 1.0e-5)));
        }

        HoSSGITraceOutput Frag(Varyings input) { return Trace(input); }

        float4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            half4 gi = SAMPLE_TEXTURE2D_X(_HoGITexture, sampler_LinearClamp, input.texcoord);
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, input.texcoord);
            half receiverValid = step(0.0001h, geometry.a);
            return half4(color.rgb + gi.rgb * (_HoSSGIIntensity * receiverValid), color.a);
        }
        ENDHLSL

        Pass
        {
            Name "Ho-SSGI Raw Trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Temporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Temporal
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Depth History"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthHistory
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Spatial Denoise"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SpatialDenoise
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Firefly"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Firefly
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Bilateral Denoise"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BilateralDenoise
            ENDHLSL
        }
    }
}
