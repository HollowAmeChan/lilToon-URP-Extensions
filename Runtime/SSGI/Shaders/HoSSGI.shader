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
        TEXTURE2D_X(_HoSSGISourceHistory);
        TEXTURE2D_X(_HoGeometryBufferSkyTexture);
        TEXTURE2D_X(_HoGITexture);
        TEXTURE2D_X(_HoSSGIRawGI);
        TEXTURE2D_X(_HoSSGIRawGIInput);
        TEXTURE2D_X(_HoSSGIHistory);
        TEXTURE2D_X(_HoSSGIHistoryDepth);
        TEXTURE2D_X(_HoSSGIDenoisedHistory);
        TEXTURE2D_X(_HoSSGIMotionVectors);
        TEXTURE2D_X(_HoSSGIReservoirColor);
        TEXTURE2D_X(_HoSSGIReservoirAux);
        TEXTURE2D_X(_HoSSGIReservoirRay);
        TEXTURE2D_X(_HoSSGIReservoirHistoryColor);
        TEXTURE2D_X(_HoSSGIReservoirHistoryAux);
        TEXTURE2D_X(_HoSSGIReservoirHistoryRay);
        TEXTURE2D_X(_HoSSGIOcclusionAux);
        TEXTURE2D_X(_HoSSGIOcclusionRay);
        TEXTURE2D_X(_HoSSGIOcclusionHistoryAux);
        TEXTURE2D_X(_HoSSGIOcclusionHistoryRay);
        TEXTURE2D_X(_HoSSGISpatialGuidance);
        TEXTURE2D_X(_HoSSGISampleCountHistory);
        TEXTURE2D_X(_HoSSGIInvalidityHistory);
        TEXTURE2D_X(_HoSSGICurrentInvalidity);
        TEXTURE2D_X(_HoAOTexture);
        TEXTURE2D_X_FLOAT(_HoSSGIDepthPyramidMip0);
        TEXTURE2D_X_FLOAT(_HoSSGIDepthPyramidMip1);
        TEXTURE2D_X_FLOAT(_HoSSGIDepthPyramidMip2);
        TEXTURE2D_X_FLOAT(_HoSSGIDepthPyramidMip3);
        TEXTURE2D_X_FLOAT(_HoSSGIDepthPyramidMip4);
        TEXTURE2D_X(_BlitTexture);
        int _HoSSGIRayCount;
        int _HoSSGIStepCount;
        float _HoSSGIRayLength;
        float _HoSSGIThickness;
        float _HoSSGIIntensity;
        float _HoSSGISourceSaturation;
        int _HoSSGIFrameIndex;
        float4x4 _HoSSGIPreviousInverseViewProjection;
        float _HoSSGIPreviousMatrixValid;
        float _HoSSGITemporalBlend;
        float _HoSSGISpatialRadius;
        float _HoSSGIHistoryValid;
        float _HoSSGIUseMotion;
        float _HoSSGIReservoirReuse;
        float _HoSSGIReservoirValidation;
        float _HoSSGIFireflyEnabled;
        float _HoSSGIUseAO;
        float _HoGeometryBufferSkyTextureValid;
        float4 _HoSSGIDepthPyramidTexelSize;

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

        struct HoSSGIOcclusionReservoir
        {
            float occlusion;
            float wsum;
            float m;
            float distance;
            float3 direction;
        };

        struct HoSSGITraceOutput
        {
            float4 gi : SV_Target0;
            float4 reservoirColor : SV_Target1;
            float4 reservoirAux : SV_Target2;
            float4 reservoirRay : SV_Target3;
            float4 occlusionAux : SV_Target4;
            float4 occlusionRay : SV_Target5;
        };

        struct HoSSGITemporalOutput
        {
            float4 gi : SV_Target0;
            float4 reservoirColor : SV_Target1;
            float4 reservoirAux : SV_Target2;
            float4 reservoirRay : SV_Target3;
            float4 occlusionAux : SV_Target4;
            float4 occlusionRay : SV_Target5;
        };

        struct HoSSGIFireflyOutput
        {
            float4 reservoirColor : SV_Target0;
            float4 reservoirAux : SV_Target1;
            float4 reservoirRay : SV_Target2;
        };

        struct HoSSGISpatialOutput
        {
            float4 reservoirColor : SV_Target0;
            float4 reservoirAux : SV_Target1;
            float4 reservoirRay : SV_Target2;
            float4 guidance : SV_Target3;
        };

        struct HoSSGISpatialValidationOutput
        {
            float4 gi : SV_Target0;
            float4 guidance : SV_Target1;
        };

        struct HoSSGIMetadataOutput
        {
            float4 sampleCount : SV_Target0;
            float4 invalidity : SV_Target1;
        };

        float HoSSGILuminance(float3 value)
        {
            return dot(max(value, 0.0), float3(0.2126, 0.7152, 0.0722));
        }

        // Match HTrace's spatial denoising curve. The maximum channel, rather
        // than luminance, keeps a saturated emissive candidate from dominating
        // the bilateral weights and makes the inverse well behaved per channel.
        float3 HoSSGISpatialDenoisingTonemap(float3 value)
        {
            value = max(value, 0.0);
            return value / (max(max(value.r, value.g), value.b) + 1.0);
        }

        float3 HoSSGISpatialDenoisingTonemapInverse(float3 value)
        {
            float maximum = max(max(value.r, value.g), value.b);
            return max(value, 0.0) / max(1.0 - maximum, 0.05);
        }

        float3 HoSSGIDirectClipToAABB(float3 history, float3 minimum, float3 maximum)
        {
            float3 center = 0.5 * (maximum + minimum);
            float3 extents = max(0.5 * (maximum - minimum), 1.0e-5);
            float3 unitOffset = (history - center) / extents;
            float maxUnit = max(max(abs(unitOffset.x), abs(unitOffset.y)), abs(unitOffset.z));
            return maxUnit > 1.0 ? center + (history - center) / maxUnit : history;
        }

        float HoSSGISampleDepthPyramid(float2 uv, int mip)
        {
            float depth;
            if (mip <= 0)
                depth = SAMPLE_TEXTURE2D_X(_HoSSGIDepthPyramidMip0, sampler_PointClamp, uv).r;
            else if (mip == 1)
                depth = SAMPLE_TEXTURE2D_X(_HoSSGIDepthPyramidMip1, sampler_PointClamp, uv).r;
            else if (mip == 2)
                depth = SAMPLE_TEXTURE2D_X(_HoSSGIDepthPyramidMip2, sampler_PointClamp, uv).r;
            else if (mip == 3)
                depth = SAMPLE_TEXTURE2D_X(_HoSSGIDepthPyramidMip3, sampler_PointClamp, uv).r;
            else
                depth = SAMPLE_TEXTURE2D_X(_HoSSGIDepthPyramidMip4, sampler_PointClamp, uv).r;
            return depth > 0.0001 ? depth : 0.0;
        }

        float4 DepthPyramidBase(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float depth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).a;
            return float4(max(depth, 0.0), 0.0, 0.0, 1.0);
        }

        float4 DepthPyramidDownsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 texel = _HoSSGIDepthPyramidTexelSize.xy;
            float2 uv = input.texcoord;
            float4 depths = float4(
                SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * float2(-0.5, -0.5)).r,
                SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * float2( 0.5, -0.5)).r,
                SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * float2(-0.5,  0.5)).r,
                SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * float2( 0.5,  0.5)).r);
            // Linear eye depth increases away from the camera. Keep the
            // nearest valid surface in the footprint so coarse traversal does
            // not step through a foreground surface and miss its hit.
            float reducedDepth = 1.0e30;
            if (depths.x > 0.0001) reducedDepth = min(reducedDepth, depths.x);
            if (depths.y > 0.0001) reducedDepth = min(reducedDepth, depths.y);
            if (depths.z > 0.0001) reducedDepth = min(reducedDepth, depths.z);
            if (depths.w > 0.0001) reducedDepth = min(reducedDepth, depths.w);
            return float4(reducedDepth < 1.0e29 ? reducedDepth : 0.0, 0.0, 0.0, 1.0);
        }

        float HoSSGIReservoirRandom(float2 pixel, float salt)
        {
            // Reservoir replacement must be re-seeded every frame. A fixed
            // per-pixel value repeats the same accept/reject decisions and
            // leaves a stationary noise pattern even when candidates change.
            float frame = (float)(_HoSSGIFrameIndex & 1023);
            float2 seed = pixel + salt + frame * float2(17.0, 29.0);
            return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
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

        float HoSSGIReservoirWeight(HoSSGIReservoir reservoir)
        {
            // Persist the normalized ReSTIR weight like HTrace. Keeping Wsum
            // only in the live estimate avoids overflowing half precision when
            // a stationary pixel has accumulated many candidates.
            return reservoir.wsum / max(reservoir.m * reservoir.target, 1.0e-6);
        }

        void HoSSGIOcclusionUpdate(
            float sampleOcclusion,
            float sampleDistance,
            float3 sampleDirection,
            float sampleM,
            inout HoSSGIOcclusionReservoir reservoir,
            float randomValue)
        {
            sampleOcclusion = saturate(sampleOcclusion);
            float sampleWeight = sampleOcclusion;
            reservoir.wsum += sampleWeight;
            reservoir.m += max(sampleM, 0.0);
            float selectionProbability = sampleWeight / max(reservoir.wsum, 1.0e-6);
            if (sampleWeight > 0.0 && randomValue < selectionProbability)
            {
                reservoir.occlusion = sampleOcclusion;
                reservoir.distance = max(sampleDistance, 0.0);
                reservoir.direction = sampleDirection;
            }
        }

        void HoSSGIOcclusionMerge(
            inout HoSSGIOcclusionReservoir reservoir,
            HoSSGIOcclusionReservoir candidate,
            float reuseWeight,
            float randomValue)
        {
            candidate.wsum *= max(reuseWeight, 0.0);
            candidate.m *= max(reuseWeight, 0.0);
            float candidateWeight = max(candidate.wsum, 0.0);
            reservoir.wsum += candidateWeight;
            reservoir.m += max(candidate.m, 0.0);
            float selectionProbability = candidateWeight / max(reservoir.wsum, 1.0e-6);
            if (candidateWeight > 0.0 && randomValue < selectionProbability)
            {
                reservoir.occlusion = candidate.occlusion;
                reservoir.distance = candidate.distance;
                reservoir.direction = candidate.direction;
            }
        }

        float HoSSGIResolveOcclusion(HoSSGIOcclusionReservoir reservoir)
        {
            return saturate(reservoir.wsum / max(reservoir.m, 1.0e-6));
        }

        float HoSSGIOcclusionWeight(HoSSGIOcclusionReservoir reservoir)
        {
            return reservoir.wsum / max(reservoir.m * max(reservoir.occlusion, 1.0e-6), 1.0e-6);
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

        float4 HoSSGIPackOcclusionRay(HoSSGIOcclusionReservoir reservoir)
        {
            return float4(HoSSGIEncodeOcta(reservoir.direction), 0.0, 1.0);
        }

        void HoSSGIUnpackOcclusionRay(float4 packed, inout HoSSGIOcclusionReservoir reservoir)
        {
            reservoir.direction = HoSSGIDecodeOcta(packed.xy);
        }

        HoSSGIOcclusionReservoir HoSSGILoadOcclusionCurrent(float2 uv)
        {
            float4 packedAux = SAMPLE_TEXTURE2D_X(_HoSSGIOcclusionAux, sampler_PointClamp, uv);
            HoSSGIOcclusionReservoir reservoir;
            reservoir.occlusion = saturate(packedAux.x);
            reservoir.m = max(packedAux.y, 0.0);
            reservoir.wsum = max(packedAux.z, 0.0) * reservoir.m * max(reservoir.occlusion, 1.0e-6);
            reservoir.distance = max(packedAux.w, 0.0);
            HoSSGIUnpackOcclusionRay(SAMPLE_TEXTURE2D_X(_HoSSGIOcclusionRay, sampler_PointClamp, uv), reservoir);
            return reservoir;
        }

        HoSSGIOcclusionReservoir HoSSGILoadOcclusionHistory(float2 uv)
        {
            float4 packedAux = SAMPLE_TEXTURE2D_X(_HoSSGIOcclusionHistoryAux, sampler_PointClamp, uv);
            HoSSGIOcclusionReservoir reservoir;
            reservoir.occlusion = saturate(packedAux.x);
            reservoir.m = max(packedAux.y, 0.0);
            reservoir.wsum = max(packedAux.z, 0.0) * reservoir.m * max(reservoir.occlusion, 1.0e-6);
            reservoir.distance = max(packedAux.w, 0.0);
            HoSSGIUnpackOcclusionRay(SAMPLE_TEXTURE2D_X(_HoSSGIOcclusionHistoryRay, sampler_PointClamp, uv), reservoir);
            return reservoir;
        }

        float4 HoSSGIPackOcclusionAux(HoSSGIOcclusionReservoir reservoir)
        {
            return float4(
                saturate(reservoir.occlusion),
                max(reservoir.m, 0.0),
                HoSSGIOcclusionWeight(reservoir),
                max(reservoir.distance, 0.0));
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
            reservoir.m = max(packedAux.x, 0.0);
            reservoir.target = max(packedAux.y, 0.0);
            reservoir.wsum = max(packedColor.a, 0.0) * reservoir.m * reservoir.target;
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
            reservoir.m = max(packedAux.x, 0.0);
            reservoir.target = max(packedAux.y, 0.0);
            reservoir.wsum = max(packedColor.a, 0.0) * reservoir.m * reservoir.target;
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

        float3 HoSSGIPreviousWorldPosition(float2 uv, float linearDepth)
        {
            float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
            return ComputeWorldSpacePosition(uv, deviceDepth, _HoSSGIPreviousInverseViewProjection);
        }

        float HoSSGILinearDepth(float3 positionWS)
        {
            return max(-mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).z, 0.0);
        }

        float HoSSGIValidateOcclusionRay(
            float3 originPositionWS,
            HoSSGIOcclusionReservoir reservoir)
        {
            if (reservoir.occlusion <= 0.001 || reservoir.distance <= 0.001)
                return 1.0;

            float3 directionWS = normalize(reservoir.direction);
            float3 expectedHitWS = originPositionWS + directionWS * reservoir.distance;
            float3 hitNDC = ComputeNormalizedDeviceCoordinatesWithZ(expectedHitWS, UNITY_MATRIX_VP);
            if (hitNDC.z < 0.0 || hitNDC.z > 1.0 || any(hitNDC.xy < 0.0) || any(hitNDC.xy > 1.0))
                return 0.0;

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
                    float2 sampleUV = hitNDC.xy + float2(x, y) * texel;
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) continue;
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001h) continue;
                    float depthAgreement = exp2(-abs(sampleGeometry.a - expectedDepth)
                        / max(depthTolerance, 0.01));
                    float3 sampleNormal = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                    float normalAgreement = saturate((dot(sampleNormal, -directionWS) - 0.1) / 0.9);
                    bestAgreement = max(bestAgreement, depthAgreement * normalAgreement);
                }
            }
            return saturate(bestAgreement);
        }

        float2 HoSSGIHash2(float2 p)
        {
            float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return frac(sin(q) * 43758.5453);
        }

        float HoSSGIRadicalInverse(uint bits)
        {
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            return (float)bits * 2.3283064365386963e-10;
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

        float HoSSGIExponentialFalloff(float hitDistance, float maxDistance)
        {
            maxDistance = max(maxDistance, 1.0e-4);
            float threshold = 0.35 * maxDistance;
            if (hitDistance <= threshold)
                return 1.0;
            float normalizedDistance = saturate((hitDistance - threshold)
                / max(maxDistance - threshold, 1.0e-4));
            return exp2(-3.0 * normalizedDistance);
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

        bool HoSSGIRefineCrossing(
            float3 previousPositionWS,
            float3 currentPositionWS,
            float previousDelta,
            float currentDelta,
            float thickness,
            out float3 refinedPositionWS,
            out float refinedDelta)
        {
            // HTrace's refine path samples a quarter step back from the
            // crossing. This reduces hit-position quantization without adding
            // a second full march.
            float3 middlePositionWS = lerp(currentPositionWS, previousPositionWS, 0.25);
            float3 middleNDC = ComputeNormalizedDeviceCoordinatesWithZ(middlePositionWS, UNITY_MATRIX_VP);
            if (middleNDC.z < 0.0 || middleNDC.z > 1.0 || any(middleNDC.xy < 0.0) || any(middleNDC.xy > 1.0))
                return false;
            float middleSurfaceDepth = HoSSGISampleDepthPyramid(middleNDC.xy, 0);
            if (middleSurfaceDepth <= 0.0001)
                return false;
            float middleRayDepth = HoSSGILinearDepth(middlePositionWS);
            refinedPositionWS = middlePositionWS;
            refinedDelta = middleRayDepth - middleSurfaceDepth;
            return refinedDelta >= -thickness && previousDelta < -thickness;
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
            // HTrace validates with the configured march budget. A fixed eight
            // samples rejects long historical rays at the endpoint and turns
            // temporal/spatial reuse into moving noise when StepCount is high.
            int validationSteps = max(8, min(_HoSSGIStepCount, 128));
            [loop]
            for (int stepIndex = 1; stepIndex <= validationSteps; stepIndex++)
            {
                float t = (stepIndex - 0.5) / validationSteps;
                float3 rayPositionWS = lerp(rayStartWS, rayEndWS, t);
                float3 rayNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayPositionWS, UNITY_MATRIX_VP);
                float2 sampleUV = rayNDC.xy;
                if (any(sampleUV <= 0.001) || any(sampleUV >= 0.999)) break;
                // Validation must use the exact mip0 surface. The coarse linear
                // depth pyramid is intentionally conservative and can otherwise
                // reject a valid historical hit before the distance check.
                float surfaceDepth = HoSSGISampleDepthPyramid(sampleUV, 0);
                if (surfaceDepth <= 0.0001)
                {
                    previousDelta = -2.0 * max(_HoSSGIThickness, 0.01);
                    continue;
                }
                float rayDepth = HoSSGILinearDepth(rayPositionWS);
                float depthDelta = rayDepth - surfaceDepth;
                float previousSampleDelta = previousDelta;
                bool crossedSurface = depthDelta >= -max(_HoSSGIThickness, 0.01)
                    && previousDelta < -max(_HoSSGIThickness, 0.01);
                previousDelta = depthDelta;
                if (crossedSurface)
                {
                    half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                    if (sampleGeometry.a < 0.0001h)
                        continue;
                    float exactDelta = rayDepth - sampleGeometry.a;
                    bool exactCrossedSurface = exactDelta >= -max(_HoSSGIThickness, 0.01)
                        && previousSampleDelta < -max(_HoSSGIThickness, 0.01);
                    previousDelta = exactDelta;
                    if (!exactCrossedSurface)
                        continue;
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
            float distanceWeight = HoSSGIExponentialFalloff(reservoir.distance, _HoSSGIRayLength);
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
                    float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_PointClamp, sampleUV).rgb;
                    float3 candidate = HoSSGIColor(source) * distanceWeight;
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

        float HoSSGIEvaluateReservoirTarget(float3 originPositionWS, HoSSGIReservoir reservoir)
        {
            if (reservoir.hit < 0.5 || reservoir.distance <= 0.001)
                return 0.0;

            float3 directionWS = normalize(reservoir.direction);
            float3 expectedHitWS = originPositionWS + directionWS * reservoir.distance;
            float3 hitNDC = ComputeNormalizedDeviceCoordinatesWithZ(expectedHitWS, UNITY_MATRIX_VP);
            if (hitNDC.z < 0.0 || hitNDC.z > 1.0 || any(hitNDC.xy < 0.0) || any(hitNDC.xy > 1.0))
                return 0.0;

            float3 sourceAccum = 0.0;
            float weightAccum = 0.0;
            float2 texel = rcp(max(_ScreenParams.xy, 1.0));
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
                    float tapWeight = exp2(-0.75 * (x * x + y * y));
                    sourceAccum += HoSSGIColor(SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_PointClamp, sampleUV).rgb) * tapWeight;
                    weightAccum += tapWeight;
                }
            }
            if (weightAccum <= 1.0e-5)
                return 0.0;
            float3 source = sourceAccum / weightAccum;
            return HoSSGILuminance(source) * HoSSGIExponentialFalloff(reservoir.distance, _HoSSGIRayLength);
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
            output.occlusionAux = 0;
            output.occlusionRay = 0;
            if (center.a < 0.0001) return output;

            float3 centerNormalWS = normalize((float3)center.rgb * 2.0 - 1.0);
            float3 centerPositionWS = HoSSGIWorldPosition(uv, center.a);
            float2 pixel = uv * _ScreenParams.xy;
            float2 noise = HoSSGIHash2(pixel + (float)(_HoSSGIFrameIndex & 15));
            int frameCycle = _HoSSGIFrameIndex - (_HoSSGIFrameIndex / 16) * 16;
            float frameOffset = (float)frameCycle;
            float3 radiance = 0;
            float hits = 0;
            HoSSGIReservoir reservoir = (HoSSGIReservoir)0;
            HoSSGIOcclusionReservoir occlusionReservoir = (HoSSGIOcclusionReservoir)0;
            int rays = max(1, _HoSSGIRayCount);
            int steps = max(4, _HoSSGIStepCount);
            float3 tangent = HoSSGIBuildTangent(centerNormalWS);
            float3 bitangent = normalize(cross(centerNormalWS, tangent));
            [loop]
            for (int ray = 0; ray < rays; ray++)
            {
                // Cosine-weighted hemisphere sampling follows HTrace's world-space
                // ray path. Screen UV is only used for visibility traversal.
                // HTrace uses a frame-indexed low-discrepancy/BND sequence.
                // Keep the same property here without a runtime blue-noise
                // texture: the pixel hash only rotates the sequence, while the
                // 16-frame phase advances samples deterministically.
                uint sampleIndex = (uint)ray + 1u + (uint)frameCycle * 131u;
                float u = frac((ray + 0.5) / rays + noise.x + frameOffset * 0.61803398875);
                float v = frac(HoSSGIRadicalInverse(sampleIndex) + noise.y + frameOffset * 0.754877666);
                float phi = 6.2831853 * u;
                float cosTheta = sqrt(saturate(v));
                float sinTheta = sqrt(saturate(1.0 - v));
                float3 rayDirWS = normalize(
                    tangent * (cos(phi) * sinTheta)
                    + bitangent * (sin(phi) * sinTheta)
                    + centerNormalWS * cosTheta);

                // Match HTrace's footprint-aware normal bias to avoid self hits
                // without requiring an excessively large thickness value.
                float2 screenTexel = rcp(max(_ScreenParams.xy, 1.0));
                float3 cornerPositionWS = HoSSGIWorldPosition(saturate(uv + screenTexel * 0.5), center.a);
                float normalBias = abs(dot(cornerPositionWS - centerPositionWS, centerNormalWS)) * 2.0;
                float3 normalForBias = dot(centerNormalWS, rayDirWS) < 0.0 ? -centerNormalWS : centerNormalWS;
                float3 rayStartWS = centerPositionWS + normalForBias * max(normalBias, 0.01) + rayDirWS * 0.01;
                float3 rayEndWS = centerPositionWS + rayDirWS * _HoSSGIRayLength;
                float3 startNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayStartWS, UNITY_MATRIX_VP);
                float3 endNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayEndWS, UNITY_MATRIX_VP);
                float3 candidateColor = 0.0;
                float candidateDistance = 0.0;
                float candidateHit = 0.0;
                if (startNDC.z < 0.0 || startNDC.z > 1.0)
                {
                    reservoir.m += 1.0;
                    occlusionReservoir.m += 1.0;
                    continue;
                }
                float2 clippedEndUV;
                float clippedRay = HoSSGIClipRayToScreen(startNDC.xy, endNDC.xy, clippedEndUV);
                if (clippedRay <= 0.001)
                {
                    reservoir.m += 1.0;
                    occlusionReservoir.m += 1.0;
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
                    int depthMip = clamp((int)floor(t * 4.0), 0, 4);
                    float surfaceDepth = HoSSGISampleDepthPyramid(sampleUV, depthMip);
                    if (surfaceDepth <= 0.0001)
                    {
                        // Invalid coverage is an empty segment. Resetting the
                        // previous sign lets the first valid surface after a
                        // gap register as a hit instead of being skipped.
                        previousDelta = -2.0 * thickness;
                        hasPrevious = true;
                        continue;
                    }
                    float rayDepth = HoSSGILinearDepth(rayPositionWS);
                    float depthDelta = rayDepth - surfaceDepth;
                    float previousSampleDelta = previousDelta;
                    bool crossedSurface = hasPrevious && depthDelta >= -thickness && previousDelta < -thickness;
                    previousDelta = depthDelta;
                    hasPrevious = true;
                    if (crossedSurface)
                    {
                        // Coarse mips only find a possible crossing. Re-read
                        // mip 0 so a nearby surface in the pyramid footprint
                        // cannot become a false hit for this pixel.
                        half4 sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                        if (sampleGeometry.a < 0.0001)
                            continue;
                        float exactDelta = rayDepth - sampleGeometry.a;
                        bool exactCrossedSurface = exactDelta >= -thickness && previousSampleDelta < -thickness;
                        previousDelta = exactDelta;
                        if (!exactCrossedSurface)
                            continue;
                        float3 refinedPositionWS;
                        float refinedDelta;
                        if (HoSSGIRefineCrossing(
                            rayPositionWS - (rayEndWS - rayStartWS) * (1.0 / steps),
                            rayPositionWS,
                            previousSampleDelta,
                            exactDelta,
                            thickness,
                            refinedPositionWS,
                            refinedDelta))
                        {
                            sampleUV = ComputeNormalizedDeviceCoordinatesWithZ(refinedPositionWS, UNITY_MATRIX_VP).xy;
                            sampleGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, sampleUV);
                            if (sampleGeometry.a < 0.0001)
                                continue;
                            exactDelta = refinedDelta;
                            previousDelta = refinedDelta;
                        }
                        float3 samplePositionWS = HoSSGIWorldPosition(sampleUV, sampleGeometry.a);
                        float sampleDepth = sampleGeometry.a;
                        float3 source = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_PointClamp, sampleUV).rgb;
                        float3 sampleNormalWS = normalize((float3)sampleGeometry.rgb * 2.0 - 1.0);
                        float hitTolerance = max(thickness, max(sampleDepth * 0.02, 0.01));
                        bool depthValid = abs(exactDelta) <= hitTolerance * 2.0;
                        bool frontFace = dot(sampleNormalWS, rayDirWS) <= 0.0;
                        if (!depthValid || !frontFace)
                        {
                            previousDelta = 2.0 * thickness;
                            continue;
                        }
                        candidateDistance = distance(centerPositionWS, samplePositionWS);
                        float distanceWeight = HoSSGIExponentialFalloff(candidateDistance, _HoSSGIRayLength);
                        candidateColor = HoSSGIColor(source) * distanceWeight;
                        // HitFound is geometric validity, not radiance
                        // brightness. A black or fully saturated surface must
                        // still participate in reservoir validation.
                        candidateHit = 1.0;
                        radiance += candidateColor;
                        hits += 1.0;
                        break;
                    }
                }

                if (candidateHit < 0.5 && _HoGeometryBufferSkyTextureValid > 0.5)
                {
                    float3 skyNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayEndWS, UNITY_MATRIX_VP);
                    if (skyNDC.z >= 0.0 && skyNDC.z <= 1.0 && all(skyNDC.xy >= 0.0) && all(skyNDC.xy <= 1.0))
                    {
                        float3 fallbackSky = SAMPLE_TEXTURE2D_X(_HoGeometryBufferSkyTexture, sampler_LinearClamp, skyNDC.xy).rgb;
                        float fallbackDistanceWeight = exp2(-2.0 * saturate(clippedRay)) * rcp(1.0 + clippedRay * clippedRay);
                        candidateColor = HoSSGIColor(fallbackSky) * (3.14159265 * fallbackDistanceWeight * 0.5);
                        candidateDistance = _HoSSGIRayLength;
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

                // Keep near occlusion as an independent reservoir. It is a
                // visibility signal for reuse and denoising, not GI energy.
                float nearRange = min(1.0, max(_HoSSGIRayLength, 0.01));
                float nearOcclusion = candidateHit > 0.5 && candidateDistance > 0.001
                    ? saturate(1.0 - candidateDistance / nearRange)
                    : 0.0;
                HoSSGIOcclusionUpdate(
                    nearOcclusion,
                    candidateDistance,
                    rayDirWS,
                    1.0,
                    occlusionReservoir,
                    HoSSGIReservoirRandom(pixel, (float)(ray + 1) * 0.71 + (float)steps * 0.017));
            }

            float confidence = saturate(hits / rays);
            float3 reservoirRadiance = HoSSGIResolveReservoir(reservoir);
            // Keep the raw average available to compare estimator behavior while
            // using the reservoir estimate as the producer result.
            output.gi = float4(max(reservoirRadiance, 0.0), confidence);
            output.reservoirColor = float4(max(reservoir.color, 0.0), HoSSGIReservoirWeight(reservoir));
            output.reservoirAux = float4(max(reservoir.m, 0.0), max(reservoir.target, 0.0), saturate(reservoir.hit), max(reservoir.distance, 0.0));
            output.reservoirRay = HoSSGIPackReservoirRay(reservoir);
            output.occlusionAux = HoSSGIPackOcclusionAux(occlusionReservoir);
            output.occlusionRay = HoSSGIPackOcclusionRay(occlusionReservoir);
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
            output.occlusionAux = 0;
            output.occlusionRay = 0;
            if (geometry.a < 0.0001h) return output;

            float2 motion = _HoSSGIUseMotion > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoSSGIMotionVectors, sampler_LinearClamp, uv).xy
                : float2(0.0, 0.0);
            half3 currentNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            half currentNormalValid = step(0.0001h, dot(geometry.rgb, geometry.rgb));
            float3 originPositionWS = HoSSGIWorldPosition(uv, geometry.a);
            HoSSGIReservoir currentReservoir = HoSSGILoadReservoir(uv);
            HoSSGIReservoir merged = currentReservoir;
            HoSSGIOcclusionReservoir currentOcclusion = HoSSGILoadOcclusionCurrent(uv);
            HoSSGIOcclusionReservoir mergedOcclusion = currentOcclusion;
            float currentConfidence = saturate(current.a);
            float historyConfidenceSum = 0.0;

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
                    half planeAgreement = 1.0h;
                    if (_HoSSGIPreviousMatrixValid > 0.5)
                    {
                        float3 currentPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                        float3 previousPositionWS = HoSSGIPreviousWorldPosition(tapUV, previousDepth);
                        planeAgreement = step(abs(dot(previousPositionWS - currentPositionWS, currentNormal)), max(0.08h * geometry.a, 0.05h));
                    }
                    half accepted = depthAgreement * normalAgreement * planeAgreement * currentNormalValid * previousNormalValid
                        * step(0.0001h, previousDepth);
                    // Reservoir history is accepted by geometry and selected
                    // ray/light validation below. Confidence is a producer
                    // diagnostic (hit ratio), not a history validity weight;
                    // using it here prevents low-hit surfaces from ever
                    // accumulating toward a stable estimate.
                    float historyScale = _HoSSGITemporalBlend * tapWeight * accepted;
                    if (historyScale <= 0.0) continue;

                    HoSSGIReservoir historyReservoir = HoSSGILoadHistoryReservoir(tapUV);
                    float tapM = historyReservoir.m * historyScale;
                    historyReservoir.wsum *= historyScale;
                    historyReservoir.m = tapM;
                    float previousTarget = historyReservoir.target;
                    float currentTarget = HoSSGIEvaluateReservoirTarget(originPositionWS, historyReservoir);
                    if (historyReservoir.hit > 0.5 && currentTarget > 1.0e-5 && previousTarget > 1.0e-5)
                    {
                        // Reweight the representative history sample to the
                        // current frame's target function before merging. This
                        // is the continuous equivalent of HTrace's unpacked
                        // W = Wsum/(M*target) reconstruction.
                        historyReservoir.wsum *= currentTarget / previousTarget;
                        historyReservoir.target = currentTarget;
                    }
                    if (_HoSSGIReservoirValidation > 0.5)
                    {
                        float geometryValidation = HoSSGIValidateReservoirRay(originPositionWS, currentNormal, historyReservoir);
                        float lightingValidation = HoSSGIValidateReservoirLighting(originPositionWS, historyReservoir);
                        float validation = geometryValidation * lightingValidation;
                        historyReservoir.wsum *= validation;
                        historyReservoir.m *= validation;
                        tapM = historyReservoir.m;
                    }
                    historyConfidenceSum += saturate(history.a) * tapM;
                    HoSSGIReservoirMerge(merged, historyReservoir,
                        HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(historyTap + 17)));

                    HoSSGIOcclusionReservoir historyOcclusion = HoSSGILoadOcclusionHistory(tapUV);
                    if (_HoSSGIReservoirValidation > 0.5)
                    {
                        float occlusionValidation = HoSSGIValidateOcclusionRay(
                            originPositionWS, historyOcclusion);
                        historyOcclusion.wsum *= occlusionValidation;
                        historyOcclusion.m *= occlusionValidation;
                    }
                    HoSSGIOcclusionMerge(
                        mergedOcclusion,
                        historyOcclusion,
                        historyScale,
                        HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(historyTap + 53)));
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
            if (mergedOcclusion.m > maxHistoryM)
            {
                float occlusionHistoryScale = maxHistoryM / max(mergedOcclusion.m, 1.0e-5);
                mergedOcclusion.m = maxHistoryM;
                mergedOcclusion.wsum *= occlusionHistoryScale;
            }

            float totalM = max(merged.m, 1.0e-6);
            float confidence = saturate((currentConfidence * max(currentReservoir.m, 0.0)
                + historyConfidenceSum) / totalM);
            float3 resolved = HoSSGIResolveReservoir(merged);
            output.gi = float4(resolved, confidence);
            output.reservoirColor = float4(max(merged.color, 0.0), HoSSGIReservoirWeight(merged));
            output.reservoirAux = float4(max(merged.m, 0.0), max(merged.target, 0.0), saturate(merged.hit), max(merged.distance, 0.0));
            output.reservoirRay = HoSSGIPackReservoirRay(merged);
            output.occlusionAux = HoSSGIPackOcclusionAux(mergedOcclusion);
            output.occlusionRay = HoSSGIPackOcclusionRay(mergedOcclusion);
            return output;
        }

        HoSSGIMetadataOutput TemporalMetadata(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            HoSSGIMetadataOutput output;
            output.sampleCount = 0;
            output.invalidity = float4(0.0, 1.0, 0.0, 1.0);
            if (geometry.a < 0.0001h)
                return output;

            float2 motion = _HoSSGIUseMotion > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoSSGIMotionVectors, sampler_LinearClamp, uv).xy
                : float2(0.0, 0.0);
            float2 previousUV = uv - motion;
            float accepted = 0.0;
            float previousCount = 0.0;
            float2 previousInvalidity = 0.0;
            float metadataWeight = 0.0;
            if (_HoSSGIHistoryValid > 0.5 && all(previousUV >= 0.0) && all(previousUV <= 1.0))
            {
                float2 previousPixel = previousUV * _ScreenParams.xy - 0.5;
                float2 previousBasePixel = floor(previousPixel);
                float2 previousFraction = frac(previousPixel);
                const float2 metadataOffsets[4] =
                {
                    float2(0.0, 0.0), float2(1.0, 0.0),
                    float2(0.0, 1.0), float2(1.0, 1.0)
                };
                float4 metadataWeights = float4(
                    (1.0 - previousFraction.x) * (1.0 - previousFraction.y),
                    previousFraction.x * (1.0 - previousFraction.y),
                    (1.0 - previousFraction.x) * previousFraction.y,
                    previousFraction.x * previousFraction.y);
                float3 normal = normalize((float3)geometry.rgb * 2.0 - 1.0);
                float3 currentPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                [unroll]
                for (int metadataTap = 0; metadataTap < 4; metadataTap++)
                {
                    float2 tapUV = (previousBasePixel + metadataOffsets[metadataTap] + 0.5)
                        / max(_ScreenParams.xy, 1.0);
                    float tapWeight = metadataWeights[metadataTap];
                    if (tapWeight <= 1.0e-4 || any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                    half4 previousGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIHistoryDepth, sampler_PointClamp, tapUV);
                    float depthAgreement = step(abs(geometry.a - previousGeometry.a), max(0.08 * geometry.a, 0.05));
                    float3 previousNormal = normalize((float3)previousGeometry.rgb * 2.0 - 1.0);
                    float normalAgreement = step(0.5, dot(normal, previousNormal));
                    float planeAgreement = 1.0;
                    if (_HoSSGIPreviousMatrixValid > 0.5)
                    {
                        float3 previousPositionWS = HoSSGIPreviousWorldPosition(tapUV, previousGeometry.a);
                        planeAgreement = step(abs(dot(previousPositionWS - currentPositionWS, normal)), max(0.08 * geometry.a, 0.05));
                    }
                    float tapAccepted = tapWeight * depthAgreement * normalAgreement * planeAgreement
                        * step(0.0001, previousGeometry.a);
                    if (tapAccepted <= 0.0) continue;
                    previousCount += SAMPLE_TEXTURE2D_X(_HoSSGISampleCountHistory, sampler_PointClamp, tapUV).r * tapAccepted;
                    previousInvalidity += SAMPLE_TEXTURE2D_X(_HoSSGIInvalidityHistory, sampler_PointClamp, tapUV).rg * tapAccepted;
                    metadataWeight += tapAccepted;
                }
                if (metadataWeight > 1.0e-4)
                {
                    previousCount /= metadataWeight;
                    previousInvalidity /= metadataWeight;
                    // Match HTrace's four-tap acceptance threshold: a partial
                    // footprint above 0.15 is a valid history, even when only
                    // one tap survives disocclusion.
                    accepted = metadataWeight > 0.15 ? 1.0 : 0.0;
                }
            }

            float sampleCount = accepted > 0.5 ? min(previousCount + 1.0, 16.0) : 1.0;
            // HTrace uses this channel as a history-confidence scale for its
            // adaptive clamp (despite the historical name "invalidity"). Keep
            // it driven by the same four-tap acceptance used for sample count;
            // leaving it at zero permanently makes the denoiser clamp every
            // frame to its narrowest box and prevents convergence.
            float temporalInvalidity = accepted > 0.5
                ? saturate(max(previousInvalidity.x, accepted))
                : 0.0;
            output.sampleCount = float4(sampleCount, 0.0, 0.0, 1.0);
            output.invalidity = float4(temporalInvalidity, accepted, 0.0, 1.0);
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
            float sampleCount = SAMPLE_TEXTURE2D_X(_HoSSGISampleCountHistory, sampler_PointClamp, uv).r;
            float sampleCountScale = clamp(4.0 - sampleCount, 1.0, 2.0);
            float threshold = mean + sampleCountScale * sqrt(variance) + 0.001;
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

        HoSSGISpatialOutput SpatialResampling(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 centerGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            HoSSGISpatialOutput output;
            output.reservoirColor = 0;
            output.reservoirAux = 0;
            output.reservoirRay = 0;
            output.guidance = 0;
            if (centerGeometry.a < 0.0001h) return output;

            float3 centerNormal = normalize((float3)centerGeometry.rgb * 2.0 - 1.0);
            float centerDepth = centerGeometry.a;
            float3 centerPositionWS = HoSSGIWorldPosition(uv, centerDepth);
            float3 tangent = HoSSGIBuildTangent(centerNormal);
            float3 bitangent = normalize(cross(centerNormal, tangent));
            float worldRadius = max(centerDepth * max(_HoSSGISpatialRadius, 0.5) * 0.0025, 0.002);
            float sigma = max(worldRadius * 0.8, 0.001);
            float randomAngle = HoSSGIHash2(uv * _ScreenParams.xy
                + (float)(_HoSSGIFrameIndex & 1023) * float2(0.37, 0.61)).x * 6.2831853;
            float2x2 rotation = float2x2(cos(randomAngle), -sin(randomAngle), sin(randomAngle), cos(randomAngle));
            HoSSGIReservoir centerReservoir = HoSSGILoadReservoir(uv);
            HoSSGIReservoir merged = centerReservoir;
            HoSSGIOcclusionReservoir centerOcclusion = HoSSGILoadOcclusionCurrent(uv);
            HoSSGIOcclusionReservoir mergedOcclusion = centerOcclusion;
            half4 centerTemporal = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, uv);
            float confidenceSum = centerTemporal.a;
            float confidenceWeight = 1.0;
            float centerAO = _HoSSGIUseAO > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoAOTexture, sampler_LinearClamp, uv).r
                : 1.0;
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
                float2 offsetWS = mul(rotation, poisson[i] * worldRadius);
                float3 samplePositionWS = centerPositionWS + tangent * offsetWS.x + bitangent * offsetWS.y;
                float3 sampleNDC = ComputeNormalizedDeviceCoordinatesWithZ(samplePositionWS, UNITY_MATRIX_VP);
                float2 tapUV = sampleNDC.xy;
                if (sampleNDC.z < 0.0 || sampleNDC.z > 1.0 || any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                half4 tapGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, tapUV);
                if (tapGeometry.a < 0.0001h) continue;
                float3 tapPositionWS = HoSSGIWorldPosition(tapUV, tapGeometry.a);
                float depthDelta = abs(tapGeometry.a - centerDepth) / max(centerDepth, 0.05);
                float3 sampleNormal = normalize((float3)tapGeometry.rgb * 2.0 - 1.0);
                float planeDistance = abs(dot(tapPositionWS - centerPositionWS, centerNormal));
                float planeDistanceNormalized = planeDistance / max(centerDepth, 0.05);
                float planeWeight = exp2(-100.0 * planeDistanceNormalized * planeDistanceNormalized);
                float normalWeight = saturate(dot(centerNormal, sampleNormal));
                float depthWeight = exp2(-32.0 * depthDelta * depthDelta);
                float gaussianWeight = exp2(-dot(offsetWS, offsetWS) / max(2.0 * sigma * sigma, 1.0e-5));
                float tapAO = _HoSSGIUseAO > 0.5
                    ? SAMPLE_TEXTURE2D_X(_HoAOTexture, sampler_LinearClamp, tapUV).r
                    : 1.0;
                float aoWeight = exp2(-max(5.0, 10.0 * (1.0 - centerAO)) * abs(centerAO - tapAO));
                HoSSGIOcclusionReservoir tapOcclusion = HoSSGILoadOcclusionCurrent(tapUV);
                float occlusionWeight = exp2(-10.0 * abs(
                    HoSSGIResolveOcclusion(centerOcclusion) - HoSSGIResolveOcclusion(tapOcclusion)));
                float reuseWeight = planeWeight * normalWeight * depthWeight * aoWeight
                    * occlusionWeight * gaussianWeight;
                if (reuseWeight <= 0.001) continue;

                HoSSGIReservoir tapReservoir = HoSSGILoadReservoir(tapUV);
                if (tapReservoir.hit > 0.5 && tapReservoir.target > 1.0e-5)
                {
                    // A spatial candidate was generated at the neighbour's
                    // receiver. Re-evaluate its representative hit against
                    // the current receiver before exchanging Wsum/M, just as
                    // temporal reuse does for a reprojected history sample.
                    float previousTapTarget = tapReservoir.target;
                    float currentTapTarget = HoSSGIEvaluateReservoirTarget(
                        centerPositionWS, tapReservoir);
                    if (currentTapTarget > 1.0e-5)
                    {
                        tapReservoir.wsum *= currentTapTarget / previousTapTarget;
                        tapReservoir.target = currentTapTarget;
                    }
                    else
                    {
                        tapReservoir.wsum = 0.0;
                        tapReservoir.m = 0.0;
                    }
                }
                tapReservoir.wsum *= reuseWeight;
                tapReservoir.m *= reuseWeight;
                HoSSGIReservoirMerge(merged, tapReservoir, HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(i + 31)));
                HoSSGIOcclusionMerge(
                    mergedOcclusion,
                    tapOcclusion,
                    reuseWeight,
                    HoSSGIReservoirRandom(uv * _ScreenParams.xy, (float)(i + 67)));

                half4 tapTemporal = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, tapUV);
                confidenceSum += tapTemporal.a * reuseWeight;
                confidenceWeight += reuseWeight;
            }

            output.reservoirColor = float4(max(merged.color, 0.0), HoSSGIReservoirWeight(merged));
            output.reservoirAux = float4(max(merged.m, 0.0), max(merged.target, 0.0), saturate(merged.hit), max(merged.distance, 0.0));
            output.reservoirRay = HoSSGIPackReservoirRay(merged);
            // z is an independent near-occlusion estimate. The selected GI
            // ray visibility is still published by SpatialValidation in the
            // validated guidance texture and remains separate from this signal.
            output.guidance = float4(
                saturate(confidenceSum / max(confidenceWeight, 1.0e-5)),
                saturate(confidenceWeight / 9.0),
                HoSSGIResolveOcclusion(mergedOcclusion),
                1.0);
            return output;
        }

        HoSSGISpatialValidationOutput SpatialValidation(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            half4 temporal = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_PointClamp, uv);
            HoSSGISpatialValidationOutput output;
            output.gi = 0;
            output.guidance = float4(0.0, 0.0, 1.0, 0.0);
            if (geometry.a < 0.0001h) return output;

            HoSSGIReservoir reservoir = HoSSGILoadReservoir(uv);
            float validation = 1.0;
            float spatialVisibility = 1.0;
            if (_HoSSGIReservoirValidation > 0.5)
            {
                float3 originPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                spatialVisibility = HoSSGIValidateReservoirRay(originPositionWS, normalize((float3)geometry.rgb * 2.0 - 1.0), reservoir);
                validation = spatialVisibility;
            }
            float4 guidanceData = SAMPLE_TEXTURE2D_X(_HoSSGISpatialGuidance, sampler_PointClamp, uv);
            float2 guidance = guidanceData.xy;
            if (validation < 0.15)
            {
                output.gi = temporal;
                // Keep the independent near-occlusion guidance in z. The
                // validation result is retained in w for diagnostics without
                // replacing the occlusion signal used by the denoiser.
                output.guidance = float4(guidance.x, guidance.y, guidanceData.z, spatialVisibility);
                return output;
            }

            float3 resolved = HoSSGIResolveReservoir(reservoir) * validation;
            output.gi = float4(max(resolved, 0.0), saturate(temporal.a * validation));
            output.guidance = float4(guidance.x, guidance.y, guidanceData.z, spatialVisibility);
            return output;
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
            float3 centerPositionWS = HoSSGIWorldPosition(uv, centerDepth);
            float2 texel = rcp(max(_ScreenParams.xy, 1.0)) * max(_HoSSGISpatialRadius, 0.5);
            float3 centerTone = HoSSGISpatialDenoisingTonemap(center.rgb);
            float4 centerGuidanceData = SAMPLE_TEXTURE2D_X(_HoSSGISpatialGuidance, sampler_PointClamp, uv);
            float centerGuidance = centerGuidanceData.x;
            float centerOcclusion = centerGuidanceData.z;
            float centerAO = _HoSSGIUseAO > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoAOTexture, sampler_LinearClamp, uv).r
                : 1.0;
            float3 sum = centerTone;
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
                    float3 tapPositionWS = HoSSGIWorldPosition(tapUV, tapGeometry.a);
                    float planeDistance = abs(dot(tapPositionWS - centerPositionWS, centerNormal));
                    float planeDistanceNormalized = planeDistance / max(centerDepth, 0.05);
                    float planeWeight = exp2(-100.0 * planeDistanceNormalized * planeDistanceNormalized);
                    float gaussianWeight = exp2(-0.55 * dot(offset, offset));
                    float4 tapGuidanceData = SAMPLE_TEXTURE2D_X(_HoSSGISpatialGuidance, sampler_PointClamp, tapUV);
                    float tapGuidance = tapGuidanceData.x;
                    float guidanceWeight = exp2(-4.0 * abs(tapGuidance - centerGuidance));
                    float tapOcclusion = tapGuidanceData.z;
                    float occlusionWeight = exp2(-max(5.0, 10.0 * (1.0 - centerOcclusion))
                        * abs(centerOcclusion - tapOcclusion));
                    float tapAO = _HoSSGIUseAO > 0.5
                        ? SAMPLE_TEXTURE2D_X(_HoAOTexture, sampler_LinearClamp, tapUV).r
                        : 1.0;
                    float aoWeight = exp2(-max(5.0, 10.0 * (1.0 - centerAO)) * abs(centerAO - tapAO));
                    float tapWeight = normalWeight * depthWeight * planeWeight * guidanceWeight
                        * occlusionWeight * aoWeight * gaussianWeight;
                    if (tapWeight <= 0.001) continue;
                    half4 tap = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_LinearClamp, tapUV);
                    sum += HoSSGISpatialDenoisingTonemap(tap.rgb) * tapWeight;
                    confidence += tap.a * tapWeight;
                    weightSum += tapWeight;
                }
            }

            float3 filteredToneMapped = sum / max(weightSum, 1.0e-5);
            float3 filtered = HoSSGISpatialDenoisingTonemapInverse(filteredToneMapped);
            return float4(max(filtered, 0.0), saturate(confidence / max(weightSum, 1.0e-5)));
        }

        float4 SourceReprojection(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 current = SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_LinearClamp, uv);
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            if (geometry.a < 0.0001h || _HoSSGIHistoryValid < 0.5 || _HoSSGIUseMotion < 0.5)
                return current;

            float2 motion = SAMPLE_TEXTURE2D_X(_HoSSGIMotionVectors, sampler_LinearClamp, uv).xy;
            float2 previousUV = uv - motion;
            if (any(previousUV < 0.0) || any(previousUV > 1.0))
                return current;

            float2 previousPixel = previousUV * _ScreenParams.xy - 0.5;
            float2 basePixel = floor(previousPixel);
            float2 fraction = frac(previousPixel);
            const float2 offsets[4] =
            {
                float2(0.0, 0.0), float2(1.0, 0.0),
                float2(0.0, 1.0), float2(1.0, 1.0)
            };
            float4 weights = float4(
                (1.0 - fraction.x) * (1.0 - fraction.y),
                fraction.x * (1.0 - fraction.y),
                (1.0 - fraction.x) * fraction.y,
                fraction.x * fraction.y);
            float3 historyColor = 0.0;
            float weightSum = 0.0;
            float3 currentNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            float currentSourceLuminance = HoSSGILuminance(current.rgb);
            float sourceMoment1 = currentSourceLuminance;
            float sourceMoment2 = currentSourceLuminance * currentSourceLuminance;
            float sourceMomentWeight = 1.0;
            [unroll]
            for (int momentY = -1; momentY <= 1; momentY++)
            {
                [unroll]
                for (int momentX = -1; momentX <= 1; momentX++)
                {
                    if (momentX == 0 && momentY == 0) continue;
                    float2 momentUV = uv + float2(momentX, momentY) * rcp(max(_ScreenParams.xy, 1.0));
                    if (any(momentUV < 0.0) || any(momentUV > 1.0)) continue;
                    half4 momentGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, momentUV);
                    if (momentGeometry.a < 0.0001h) continue;
                    float momentWeight = exp2(-0.75 * (momentX * momentX + momentY * momentY));
                    float momentLuminance = HoSSGILuminance(SAMPLE_TEXTURE2D_X(_HoSSGISource, sampler_LinearClamp, momentUV).rgb);
                    sourceMoment1 += momentLuminance * momentWeight;
                    sourceMoment2 += momentLuminance * momentLuminance * momentWeight;
                    sourceMomentWeight += momentWeight;
                }
            }
            sourceMoment1 /= max(sourceMomentWeight, 1.0e-5);
            sourceMoment2 /= max(sourceMomentWeight, 1.0e-5);
            float sourceThreshold = sourceMoment1 + 2.0 * sqrt(max(sourceMoment2 - sourceMoment1 * sourceMoment1, 0.0));
            sourceThreshold = max(sourceThreshold, max(currentSourceLuminance * 4.0, 0.25));
            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float2 tapUV = (basePixel + offsets[i] + 0.5) / max(_ScreenParams.xy, 1.0);
                if (any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                half4 previousGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIHistoryDepth, sampler_PointClamp, tapUV);
                if (previousGeometry.a < 0.0001h) continue;
                    float depthAgreement = step(abs(geometry.a - previousGeometry.a), max(0.08 * geometry.a, 0.05));
                    float3 previousNormal = normalize((float3)previousGeometry.rgb * 2.0 - 1.0);
                    float normalAgreement = step(0.5, dot(currentNormal, previousNormal));
                    float planeAgreement = 1.0;
                    if (_HoSSGIPreviousMatrixValid > 0.5)
                    {
                        float3 currentPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                        float3 previousPositionWS = HoSSGIPreviousWorldPosition(tapUV, previousGeometry.a);
                        planeAgreement = step(abs(dot(previousPositionWS - currentPositionWS, currentNormal)), max(0.08 * geometry.a, 0.05));
                    }
                    float tapWeight = weights[i] * depthAgreement * normalAgreement * planeAgreement;
                if (tapWeight <= 1.0e-4) continue;
                historyColor += SAMPLE_TEXTURE2D_X(_HoSSGISourceHistory, sampler_LinearClamp, tapUV).rgb * tapWeight;
                weightSum += tapWeight;
            }

            if (weightSum <= 0.15)
                return current;
            historyColor /= weightSum;
            float historyLuminance = HoSSGILuminance(historyColor);
            if (historyLuminance > sourceThreshold)
                historyColor *= sourceThreshold / max(historyLuminance, 1.0e-5);
            float historyWeight = saturate(_HoSSGITemporalBlend * weightSum);
            return float4(lerp(current.rgb, historyColor, historyWeight), current.a);
        }

        float4 SourceHistoryCopy(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        }

        float4 TemporalDenoise(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, uv);
            half4 current = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_LinearClamp, uv);
            if (geometry.a < 0.0001h)
                return 0;

            float currentConfidence = saturate(current.a);
            float2 texel = rcp(max(_ScreenParams.xy, 1.0));
            float3 centerNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            float centerDepth = geometry.a;
            float3 moment1 = current.rgb;
            float3 moment2 = current.rgb * current.rgb;
            float momentWeight = 1.0;
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0) continue;
                    float2 tapUV = uv + float2(x, y) * texel;
                    if (any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                    half4 tapGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIGeometry, sampler_PointClamp, tapUV);
                    if (tapGeometry.a < 0.0001h) continue;
                    float depthDelta = abs(tapGeometry.a - centerDepth) / max(centerDepth, 0.05);
                    float normalWeight = saturate(dot(centerNormal, normalize((float3)tapGeometry.rgb * 2.0 - 1.0)));
                    if (normalWeight < 0.35 || depthDelta > 0.08) continue;
                    float3 tap = SAMPLE_TEXTURE2D_X(_HoSSGIRawGIInput, sampler_LinearClamp, tapUV).rgb;
                    float tapWeight = exp(-0.75 * (x * x + y * y));
                    tapWeight *= normalWeight;
                    tapWeight *= exp2(-28.0 * depthDelta * depthDelta);
                    moment1 += tap * tapWeight;
                    moment2 += tap * tap * tapWeight;
                    momentWeight += tapWeight;
                }
            }

            moment1 /= max(momentWeight, 1.0e-5);
            moment2 /= max(momentWeight, 1.0e-5);
            float3 standardDeviation = sqrt(max(moment2 - moment1 * moment1, 0.0));
            float2 currentInvalidity = SAMPLE_TEXTURE2D_X(_HoSSGICurrentInvalidity, sampler_PointClamp, uv).rg;
            float clampMultiplier = lerp(1.0, 5.0, pow(saturate(currentInvalidity.x), 5.0));
            float3 minimum = lerp(current.rgb, moment1, 0.25) - standardDeviation * 0.5 * clampMultiplier;
            float3 maximum = lerp(current.rgb, moment1, 0.25) + standardDeviation * 0.5 * clampMultiplier;

            float3 history = 0.0;
            float historyConfidence = 0.0;
            float historyWeight = 0.0;
            float historySampleCount = 1.0;
            float2 historyInvalidity = 0.0;
            if (_HoSSGIHistoryValid > 0.5 && _HoSSGIUseMotion > 0.5)
            {
                float2 previousUV = uv - SAMPLE_TEXTURE2D_X(_HoSSGIMotionVectors, sampler_LinearClamp, uv).xy;
                if (all(previousUV >= 0.0) && all(previousUV <= 1.0))
                {
                    float2 previousPixel = previousUV * _ScreenParams.xy - 0.5;
                    float2 previousBasePixel = floor(previousPixel);
                    float2 previousFraction = frac(previousPixel);
                    const float2 denoiseOffsets[4] =
                    {
                        float2(0.0, 0.0), float2(1.0, 0.0),
                        float2(0.0, 1.0), float2(1.0, 1.0)
                    };
                    float4 denoiseWeights = float4(
                        (1.0 - previousFraction.x) * (1.0 - previousFraction.y),
                        previousFraction.x * (1.0 - previousFraction.y),
                        (1.0 - previousFraction.x) * previousFraction.y,
                        previousFraction.x * previousFraction.y);
                    float3 historyAccum = 0.0;
                    float historyConfidenceAccum = 0.0;
                    float sampleCountAccum = 0.0;
                    float2 invalidityAccum = 0.0;
                    float acceptedWeight = 0.0;
                    float3 currentPositionWS = HoSSGIWorldPosition(uv, geometry.a);
                    [unroll]
                    for (int denoiseTap = 0; denoiseTap < 4; denoiseTap++)
                    {
                        float2 tapUV = (previousBasePixel + denoiseOffsets[denoiseTap] + 0.5)
                            / max(_ScreenParams.xy, 1.0);
                        float tapWeight = denoiseWeights[denoiseTap];
                        if (tapWeight <= 1.0e-4 || any(tapUV < 0.0) || any(tapUV > 1.0)) continue;
                        half4 previousGeometry = SAMPLE_TEXTURE2D_X(_HoSSGIHistoryDepth, sampler_PointClamp, tapUV);
                        float previousGeometryValid = step(0.0001, previousGeometry.a);
                        float depthAgreement = step(abs(geometry.a - previousGeometry.a), max(0.08 * geometry.a, 0.05));
                        float normalAgreement = step(0.5, dot(centerNormal, normalize((float3)previousGeometry.rgb * 2.0 - 1.0)));
                        float planeAgreement = 1.0;
                        if (_HoSSGIPreviousMatrixValid > 0.5)
                        {
                            float3 previousPositionWS = HoSSGIPreviousWorldPosition(tapUV, previousGeometry.a);
                            planeAgreement = step(abs(dot(previousPositionWS - currentPositionWS, centerNormal)), max(0.08 * geometry.a, 0.05));
                        }
                        float acceptedTap = tapWeight * previousGeometryValid * depthAgreement * normalAgreement * planeAgreement;
                        if (acceptedTap <= 1.0e-4) continue;
                        half4 historySample = SAMPLE_TEXTURE2D_X(_HoSSGIDenoisedHistory, sampler_LinearClamp, tapUV);
                        historyAccum += historySample.rgb * acceptedTap;
                        historyConfidenceAccum += saturate(historySample.a) * acceptedTap;
                        sampleCountAccum += SAMPLE_TEXTURE2D_X(_HoSSGISampleCountHistory, sampler_PointClamp, tapUV).r * acceptedTap;
                        invalidityAccum += SAMPLE_TEXTURE2D_X(_HoSSGIInvalidityHistory, sampler_PointClamp, tapUV).rg * acceptedTap;
                        acceptedWeight += acceptedTap;
                    }

                    if (acceptedWeight > 0.15)
                    {
                        history = historyAccum / acceptedWeight;
                        historyConfidence = saturate(historyConfidenceAccum / acceptedWeight);
                        historySampleCount = min(16.0, sampleCountAccum / acceptedWeight + 1.0);
                        historyInvalidity = invalidityAccum / acceptedWeight;
                        history = HoSSGIDirectClipToAABB(history, minimum, maximum);
                        float temporalWeight = 1.0 - rcp(max(historySampleCount, 1.0));
                        float currentValidity = step(0.95, currentInvalidity.y);
                        // Confidence is a diagnostic hit ratio; the history
                        // validity channels and geometric acceptance control
                        // accumulation.
                        historyWeight = _HoSSGITemporalBlend * temporalWeight
                            * saturate(historyInvalidity.y) * currentValidity;
                    }
                }
            }

            float3 outputColor = lerp(current.rgb, history, saturate(historyWeight));
            float outputConfidence = saturate(lerp(currentConfidence, max(currentConfidence, historyConfidence), saturate(historyWeight)));
            return float4(max(outputColor, 0.0), outputConfidence);
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
            Name "Ho-SSGI Spatial Resampling"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SpatialResampling
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

        Pass
        {
            Name "Ho-SSGI Source Reprojection"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SourceReprojection
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Source History"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SourceHistoryCopy
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Temporal Accumulation"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TemporalDenoise
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Denoised History"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SourceHistoryCopy
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Spatial Validation"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SpatialValidation
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Temporal Metadata"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TemporalMetadata
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Depth Pyramid Base"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthPyramidBase
            ENDHLSL
        }

        Pass
        {
            Name "Ho-SSGI Depth Pyramid Downsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthPyramidDownsample
            ENDHLSL
        }
    }
}
