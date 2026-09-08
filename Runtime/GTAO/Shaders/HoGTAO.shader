Shader "Hidden/lilToon/URP/HoGTAOv4"
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

        TEXTURE2D_X(_HoGTAOHistoryPrevTex);
        TEXTURE2D_X_FLOAT(_HoGTAOHistoryPrevDepthTex);
        float4 _HoGTAOHistoryPrevTex_TexelSize;
        TEXTURE2D_X(_MotionVectorTexture);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip0);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip1);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip2);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip3);
        TEXTURE2D_X_FLOAT(_HoGTAODepthInput);
        TEXTURE2D_X(_HoGTAOGeometryInput);
        float4 _HoGTAODepthInputTexelSize;
        float4x4 _HoGTAOInvProjMatrix;
        float4 _HoGTAODepthToViewParams;
        float _HoGTAOOrthographic;
        float4x4 _HoGTAOProjMatrix;
        float4x4 _HoGTAOViewMatrix;
        float _HoGTAODebugMode;
        float _HoGTAOHistoryBlend;
        float _HoGTAOHistoryValid;
        float _HoGTAOTemporalMaxFrames;
        float _HoGTAOTemporalRejection;
        float _HoGTAOUseMotionVectors;
        float _HoGTAOWorldSpaceRadius;
        float _HoGTAOScreenSpaceRadius;
        float _HoGTAOThickness;
        float _HoGTAOUseAttenuation;
        float _HoGTAOUseLinearThickness;
        float _HoGTAOSliceCount;
        float _HoGTAOStepCount;
        float _HoGTAOFrameIndex;
        float _HoGTAOSpatialRadius;
        float _HoGTAOSpatialAdaptivity;
        float _HoGTAOSpatialResolution;
        float _HoGTAOSpatialFilter;
        float _HoGTAOSpatialStep;
        float _HoGTAOPixelSpreadMultiplier;

        static const float HoGTAOSliceRotations[6] = { 60.0, 300.0, 180.0, 240.0, 120.0, 0.0 };
        static const float HoGTAONoiseOffsets[4] = { 0.0, 0.5, 0.25, 0.75 };

        float4 DepthCopy(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            // GeometryBuffer uses zero alpha for sky/uncovered pixels. Keep that
            // convention so pyramid reduction can ignore invalid samples.
            float depth = max((float)nd.a, 0.0);
            return float4(depth, depth, depth, depth);
        }

        float4 DepthDownsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 texel = _HoGTAODepthInputTexelSize.xy;
            float2 uv = input.texcoord;
            float d0 = SAMPLE_TEXTURE2D_X(_HoGTAODepthInput, sampler_PointClamp, uv + texel * float2(-0.5, -0.5)).r;
            float d1 = SAMPLE_TEXTURE2D_X(_HoGTAODepthInput, sampler_PointClamp, uv + texel * float2( 0.5, -0.5)).r;
            float d2 = SAMPLE_TEXTURE2D_X(_HoGTAODepthInput, sampler_PointClamp, uv + texel * float2(-0.5,  0.5)).r;
            float d3 = SAMPLE_TEXTURE2D_X(_HoGTAODepthInput, sampler_PointClamp, uv + texel * float2( 0.5,  0.5)).r;
            // HTrace's GTAO pyramid uses the farthest depth in each footprint.
            return max(max(d0, d1), max(d2, d3));
        }

        float HoGTAOSampleDepth(float2 uv, float lod)
        {
            if (lod < 0.5) return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip0, sampler_PointClamp, uv).r;
            if (lod < 1.5) return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip1, sampler_PointClamp, uv).r;
            if (lod < 2.5) return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip2, sampler_PointClamp, uv).r;
            return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip3, sampler_PointClamp, uv).r;
        }

        float3 HoGTAOViewPosition(float2 uv, float linearDepth)
        {
            // GeometryBuffer stores linear eye depth. Match HTrace's direct
            // reconstruction from linear depth instead of attempting to reverse
            // the device-depth transform (which is projection/reversed-Z
            // dependent and was the source of the previous distorted AO).
            float2 viewXY = uv * _HoGTAODepthToViewParams.xy + _HoGTAODepthToViewParams.zw;
            if (_HoGTAOOrthographic > 0.5)
                return float3(viewXY, -linearDepth) * float3(1.0, -1.0, -1.0);
            return float3(viewXY * linearDepth, -linearDepth) * float3(1.0, -1.0, -1.0);
        }

        float HoGTAOFastSqrt(float x)
        {
            return asfloat(0x1FBD1DF5 + (asint(x) >> 1));
        }

        float HoGTAOFastACos(float x)
        {
            float ax = abs(x);
            float result = (-0.156583 * ax + PI * 0.5) * HoGTAOFastSqrt(max(1.0 - ax, 0.0));
            return x >= 0.0 ? result : PI - result;
        }

        float HoGTAOInterleavedGradientNoise(float2 pixelCoord, int frame)
        {
            pixelCoord += frame * (float2(47.0, 17.0) * 0.695);
            return frac(52.9829189 * frac(dot(pixelCoord, float2(0.06711056, 0.00583715))));
        }

        float2 HoGTAOPackNormal(float3 normalWS)
        {
            return PackNormalOctQuadEncode(normalize(normalWS)) * 0.5 + 0.5;
        }

        float3 HoGTAOUnpackNormal(float2 encoded)
        {
            return normalize(UnpackNormalOctQuadEncode(encoded * 2.0 - 1.0));
        }

        void HoGTAOUpdateBitmask(inout uint bitmask, float2 horizonSamples)
        {
            uint2 horizonInt = uint2(round(saturate(horizonSamples) * 32.0));
            uint horizonMin = horizonInt.x < 32u ? 0xFFFFFFFFu << horizonInt.x : 0u;
            uint horizonMax = horizonInt.y != 0u ? 0xFFFFFFFFu >> (32u - horizonInt.y) : 0u;
            bitmask |= horizonMin & horizonMax;
        }

        bool HoGTAOSample(float2 uv, float lod, out float3 positionVS, out float3 normalWS)
        {
            float sampledDepth = HoGTAOSampleDepth(saturate(uv), lod);
            if (sampledDepth < 0.0001)
            {
                positionVS = 0.0;
                normalWS = 0.0;
                return false;
            }

            positionVS = HoGTAOViewPosition(saturate(uv), sampledDepth);
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, saturate(uv));
            normalWS = normalize((float3)nd.rgb * 2.0 - 1.0);
            return true;
        }

        float HoGTAOCompute(float2 uv, half4 centerND)
        {
            float linearDepth = centerND.a;
            float3 positionVS = HoGTAOViewPosition(uv, linearDepth);
            float3 normalWS = normalize((float3)centerND.rgb * 2.0 - 1.0);
            float3 normalVS = normalize(mul((float3x3)_HoGTAOViewMatrix, normalWS));
            normalVS *= float3(1.0, -1.0, -1.0);
            float3 viewDirection = normalize(-positionVS);
            positionVS *= lerp(0.996, 1.0, abs(dot(normalVS, viewDirection)));

            float radiusScale = 2.0 / max(abs(_HoGTAOProjMatrix[0][0]), 1.0e-5) / max(_ScreenParams.x, 1.0);
            float screenRadius = max(_HoGTAOWorldSpaceRadius / max(linearDepth * radiusScale, 1.0e-5), _HoGTAOScreenSpaceRadius);
            float worldRadius = max(screenRadius * linearDepth * radiusScale, 1.0e-4);
            float falloff = rcp(worldRadius);
            falloff *= falloff;
            float minStep = 1.3 / max(screenRadius, 1.0);
            int frameIndex = (int)_HoGTAOFrameIndex;
            float2 pixelCoord = floor(uv * _ScreenParams.xy);
            float noiseX = HoGTAOInterleavedGradientNoise(pixelCoord, 0);
            float noiseY = frac(HoGTAOInterleavedGradientNoise((_ScreenParams.xy - pixelCoord.yx), 6 - (frameIndex % 6))
                + HoGTAONoiseOffsets[(frameIndex / 3) % 4]);
            float thickness = _HoGTAOUseLinearThickness > 0.5
                ? max(_HoGTAOThickness * 0.1 * linearDepth, _HoGTAOThickness)
                : _HoGTAOThickness;

            float visibility = 0.0;
            float weightTotal = 0.0;
            int slices = max(1, (int)_HoGTAOSliceCount);
            int steps = max(1, (int)_HoGTAOStepCount);
            int rotationIndex = frameIndex % 6;

            [loop]
            for (int slice = 0; slice < slices; slice++)
            {
                float phi = (slice + noiseX + HoGTAOSliceRotations[rotationIndex] / 360.0) * (PI / slices);
                float3 sliceDirection = float3(cos(phi), sin(phi), 0.0);
                float2 samplingDirection = float2(sliceDirection.x, -sliceDirection.y) * screenRadius;
                float3 ortho = sliceDirection - dot(sliceDirection, viewDirection) * viewDirection;
                float3 axis = normalize(cross(ortho, viewDirection));
                float3 projectedNormal = normalVS - axis * dot(normalVS, axis);
                float projectedLength = length(projectedNormal);
                if (projectedLength < 1.0e-4)
                {
                    continue;
                }

                float normalSign = sign(dot(ortho, projectedNormal));
                float cosN = saturate(dot(projectedNormal, viewDirection) / projectedLength);
                float nAngle = normalSign * acos(cosN);
                float2 minHorizon = float2(cos(nAngle - PI * 0.5), cos(nAngle + PI * 0.5));
                uint bitmask = 0u;

                [loop]
                for (int step = 0; step < steps; step++)
                {
                    float stride = pow((step + noiseY) / steps, 2.0) + minStep;
                    float2 offset = round(stride * samplingDirection) * rcp(_ScreenParams.xy);
                    float3 samplePosition;
                    float3 sampleNormal;
                    float lod = clamp(floor(log2(max(length(stride * samplingDirection), 1.0)) - 3.0), 0.0, 3.0);
                    if (HoGTAOSample(uv - offset, lod, samplePosition, sampleNormal))
                    {
                        float3 delta = samplePosition - positionVS;
                        float h = dot(delta, viewDirection);
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float2 horizon = rsqrt(float2(d2, max(d2 + thickness * thickness - h * thickness * 2.0, 1.0e-6))) * float2(h, h - thickness);
                        float sampleWeight = rcp(1.0 + d2 * falloff);
                        horizon = _HoGTAOUseAttenuation > 0.5 ? lerp(minHorizon.xx, horizon, sampleWeight) : horizon;
                        horizon = float2(HoGTAOFastACos(saturate(horizon.x)), HoGTAOFastACos(saturate(horizon.y)));
                        float2 normalized = saturate((nAngle + horizon + PI * 0.5) / PI);
                        normalized *= normalized * (3.0 - 2.0 * normalized);
                        HoGTAOUpdateBitmask(bitmask, normalized);
                    }

                    if (HoGTAOSample(uv + offset, lod, samplePosition, sampleNormal))
                    {
                        float3 delta = samplePosition - positionVS;
                        float h = dot(delta, viewDirection);
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float2 horizon = rsqrt(float2(d2, max(d2 + thickness * thickness - h * thickness * 2.0, 1.0e-6))) * float2(h, h - thickness);
                        float sampleWeight = rcp(1.0 + d2 * falloff);
                        horizon = _HoGTAOUseAttenuation > 0.5 ? lerp(minHorizon.yy, horizon, sampleWeight) : horizon;
                        horizon = float2(HoGTAOFastACos(saturate(horizon.x)), HoGTAOFastACos(saturate(horizon.y)));
                        float2 normalized = saturate((nAngle - horizon + PI * 0.5) / PI);
                        normalized *= normalized * (3.0 - 2.0 * normalized);
                        HoGTAOUpdateBitmask(bitmask, normalized.yx);
                    }
                }

                visibility += (1.0 - saturate((float)countbits(bitmask) / 26.0)) * projectedLength;
                weightTotal += projectedLength;
            }

            return 1.0 - saturate(visibility / max(weightTotal, 1.0e-5));
        }

        // HTrace's default profile uses HorizonSearch (TracingMode=0), not
        // VisibilityBitmasks. Keep this continuous estimator as the production
        // path so the result has the same smooth gray-scale response as HTrace;
        // the bitmask estimator above remains available for later high-density
        // comparison once the baseline is validated.
        float HoGTAOComputeHorizonSearch(float2 uv, half4 centerND)
        {
            float linearDepth = centerND.a;
            float3 positionVS = HoGTAOViewPosition(uv, linearDepth);
            float3 normalWS = normalize((float3)centerND.rgb * 2.0 - 1.0);
            float3 normalVS = normalize(mul((float3x3)_HoGTAOViewMatrix, normalWS));
            normalVS *= float3(1.0, -1.0, -1.0);
            float3 viewDirection = normalize(-positionVS);
            positionVS *= lerp(0.996, 1.0, abs(dot(normalVS, viewDirection)));

            float radiusScale = 2.0 / max(abs(_HoGTAOProjMatrix[0][0]), 1.0e-5) / max(_ScreenParams.x, 1.0);
            float screenRadius = max(_HoGTAOWorldSpaceRadius / max(linearDepth * radiusScale, 1.0e-5), _HoGTAOScreenSpaceRadius);
            float worldRadius = max(screenRadius * linearDepth * radiusScale, 1.0e-4);
            float falloff = rcp(worldRadius);
            falloff *= falloff;
            float minStep = 1.3 / max(screenRadius, 1.0);
            int frameIndex = (int)_HoGTAOFrameIndex;
            float2 pixelCoord = floor(uv * _ScreenParams.xy);
            float noiseX = HoGTAOInterleavedGradientNoise(pixelCoord, 0);
            float noiseY = frac(HoGTAOInterleavedGradientNoise((_ScreenParams.xy - pixelCoord.yx), 6 - (frameIndex % 6))
                + HoGTAONoiseOffsets[(frameIndex / 3) % 4]);

            float occlusion = 0.0;
            float totalWeight = 0.0;
            int slices = max(1, (int)_HoGTAOSliceCount);
            int steps = max(1, (int)_HoGTAOStepCount);
            int rotationIndex = frameIndex % 6;

            [loop]
            for (int slice = 0; slice < slices; slice++)
            {
                float phi = (slice + noiseX + HoGTAOSliceRotations[rotationIndex] / 360.0) * (PI / slices);
                float3 sliceDirection = float3(cos(phi), sin(phi), 0.0);
                float2 samplingDirection = float2(sliceDirection.x, -sliceDirection.y) * screenRadius;
                float3 ortho = sliceDirection - dot(sliceDirection, viewDirection) * viewDirection;
                float3 axis = normalize(cross(ortho, viewDirection));
                float3 projectedNormal = normalVS - axis * dot(normalVS, axis);
                float projectedLength = length(projectedNormal);
                if (projectedLength < 1.0e-4)
                    continue;

                float normalSign = sign(dot(ortho, projectedNormal));
                float cosN = saturate(dot(projectedNormal, viewDirection) / projectedLength);
                float nAngle = normalSign * acos(cosN);
                float2 maxHorizon = float2(cos(nAngle - PI * 0.5), cos(nAngle + PI * 0.5));

                [loop]
                for (int step = 0; step < steps; step++)
                {
                    float stride = pow((step + noiseY) / steps, 2.0) + minStep;
                    float2 offset = round(stride * samplingDirection) * rcp(_ScreenParams.xy);

                    float3 samplePosition;
                    float3 sampleNormal;
                    float lod = clamp(log2(max(length(stride * samplingDirection), 1.0)) - 3.0, 0.0, 3.0);
                    if (HoGTAOSample(uv - offset, lod, samplePosition, sampleNormal))
                    {
                        float3 delta = samplePosition - positionVS;
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float sampleHorizon = dot(delta, viewDirection) * rsqrt(d2);
                        float sampleWeight = rcp(0.95 + d2 * 2.0 * falloff);
                        sampleHorizon = lerp(maxHorizon.x, sampleHorizon, sampleWeight);
                        maxHorizon.x = max(maxHorizon.x, sampleHorizon);
                    }

                    if (HoGTAOSample(uv + offset, lod, samplePosition, sampleNormal))
                    {
                        float3 delta = samplePosition - positionVS;
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float sampleHorizon = dot(delta, viewDirection) * rsqrt(d2);
                        float sampleWeight = rcp(0.95 + d2 * 2.0 * falloff);
                        sampleHorizon = lerp(maxHorizon.y, sampleHorizon, sampleWeight);
                        maxHorizon.y = max(maxHorizon.y, sampleHorizon);
                    }
                }

                maxHorizon = float2(HoGTAOFastACos(clamp(maxHorizon.x, -1.0, 1.0)), HoGTAOFastACos(clamp(maxHorizon.y, -1.0, 1.0)));
                maxHorizon.x = nAngle + max(-maxHorizon.x - nAngle, -PI * 0.5);
                maxHorizon.y = nAngle + min(+maxHorizon.y - nAngle, +PI * 0.5);
                float sinN = sin(nAngle);
                float2 integratedArc = -cos(2.0 * maxHorizon - nAngle) + cosN + 2.0 * maxHorizon * sinN;
                occlusion += 0.25 * (integratedArc.x + integratedArc.y) * projectedLength;
                totalWeight += projectedLength * (nAngle * sinN + cosN);
            }

            return 1.0 - saturate(occlusion / max(totalWeight, 1.0e-5));
        }

        half4 Generate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            if (_HoGTAODebugMode > 1.5 && _HoGTAODebugMode < 2.5)
            {
                half depth = saturate(nd.a / 50.0h);
                return half4(depth, depth, depth, 1.0h);
            }
            if (_HoGTAODebugMode > 2.5 && _HoGTAODebugMode < 3.5)
            {
                return half4(nd.rgb, 1.0h);
            }
            if (nd.a < 0.0001h)
            {
                // Internal history/filter buffers store AO amount (0 = no
                // occlusion). Final composition turns this into visibility.
                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            half ao = HoGTAOComputeHorizonSearch(input.texcoord, nd);
            return half4(ao, ao, ao, 1.0h);
        }

        half4 Temporal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half current = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoGTAOGeometryInput, sampler_PointClamp, input.texcoord);
            float2 motion = _HoGTAOUseMotionVectors > 0.5
                ? SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_LinearClamp, input.texcoord).xy
                : float2(0.0, 0.0);
            // URP stores forward motion in screen-UV space. Reproject the current
            // pixel backwards to locate its previous-frame history sample.
            float2 previousUV = saturate(input.texcoord - motion);
            // HTrace uses four bilinear history taps and rejects each tap by
            // depth before accumulating it. A single filtered history sample
            // lets an edge bleed across the reprojection footprint.
            float2 historyTexel = _HoGTAOHistoryPrevTex_TexelSize.xy;
            float2 historySize = _HoGTAOHistoryPrevTex_TexelSize.zw;
            float2 historyCoord = previousUV * historySize - 0.5;
            float2 historyBase = floor(historyCoord);
            float2 historyFrac = frac(historyCoord);
            float4 historyWeights = float4(
                (1.0 - historyFrac.x) * (1.0 - historyFrac.y),
                historyFrac.x * (1.0 - historyFrac.y),
                (1.0 - historyFrac.x) * historyFrac.y,
                historyFrac.x * historyFrac.y);
            static const float2 historyOffsets[4] =
            {
                float2(0.0, 0.0), float2(1.0, 0.0), float2(0.0, 1.0), float2(1.0, 1.0)
            };
            float previousAccumulated = 0.0;
            float previousCountAccumulated = 0.0;
            float historyWeightSum = 0.0;
            [unroll]
            for (int historyTap = 0; historyTap < 4; historyTap++)
            {
                float2 historyUV = saturate((historyBase + historyOffsets[historyTap] + 0.5) * historyTexel);
                half4 historyData = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, historyUV);
                half historyDepth = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevDepthTex, sampler_PointClamp, historyUV).r;
                float validDepth = step(0.0001, historyDepth)
                    * step(abs((float)geometry.a - historyDepth), max(0.05 * geometry.a, 0.05));
                float tapWeight = historyWeights[historyTap] * validDepth;
                previousAccumulated += historyData.r * tapWeight;
                previousCountAccumulated += historyData.g * max(_HoGTAOTemporalMaxFrames, 1.0) * tapWeight;
                historyWeightSum += tapWeight;
            }
            // SceneView and the first valid motion-vector frame can expose a
            // cleared/unstable motion field. Preserve temporal accumulation by
            // falling back to the current UV history footprint before declaring
            // a full-frame disocclusion.
            if (historyWeightSum < 1.0e-5 && _HoGTAOUseMotionVectors > 0.5)
            {
                float2 fallbackUV = input.texcoord;
                half4 fallbackData = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, fallbackUV);
                half fallbackDepth = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevDepthTex, sampler_PointClamp, fallbackUV).r;
                float fallbackValid = step(0.0001, fallbackDepth)
                    * step(abs((float)geometry.a - fallbackDepth), max(0.05 * geometry.a, 0.05));
                previousAccumulated = fallbackData.r * fallbackValid;
                previousCountAccumulated = fallbackData.g * max(_HoGTAOTemporalMaxFrames, 1.0) * fallbackValid;
                historyWeightSum = fallbackValid;
            }
            half previous = historyWeightSum > 1.0e-5 ? previousAccumulated / historyWeightSum : 0.0h;
            half previousCount = historyWeightSum > 1.0e-5 ? previousCountAccumulated / historyWeightSum : 0.0h;
            float3 currentNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            float3 previousNormal = HoGTAOUnpackNormal(SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, previousUV).ba);
            // Sky/uncovered pixels have no surface history to validate. Keep
            // them white in the diagnostic instead of falsely marking them as
            // temporal disocclusions.
            if (geometry.a < 0.0001h && _HoGTAODebugMode > 4.5)
            {
                return half4(1.0h, 1.0h, 1.0h, 1.0h);
            }
            half depthValid = step(0.0001h, geometry.a) * step(1.0e-5, historyWeightSum);
            half depthAgreement = step(1.0e-5, historyWeightSum);
            // Keep normal agreement observable through the stored history, but
            // do not let a stale/legacy history payload invalidate the entire
            // frame. HTrace's primary reprojection validity is depth/footprint
            // coverage; normal rejection is applied only after that history is
            // established and version-compatible.
            half normalAgreement = previousCount > 0.5h
                ? step(0.5h, dot(currentNormal, previousNormal))
                : 1.0h;
            half accepted = saturate(_HoGTAOHistoryValid) * depthValid * depthAgreement;
            half sampleCount = min(previousCount + 1.0h, max(_HoGTAOTemporalMaxFrames, 1.0));
            sampleCount = lerp(1.0h, sampleCount, accepted);

            // HTrace history clamp: use a small Gaussian neighborhood of the
            // current trace to reject stale reprojected values before blending.
            float2 temporalTexel = _BlitTexture_TexelSize.xy;
            float moment = 0.0;
            float moment2 = 0.0;
            float momentWeight = 0.0;
            [unroll]
            for (int y = -2; y <= 2; y++)
            {
                [unroll]
                for (int x = -2; x <= 2; x++)
                {
                    float2 tapUV = saturate(input.texcoord + float2(x, y) * temporalTexel);
                    float tap = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, tapUV).r;
                    float tapWeight = exp(-3.0 * (float)(x * x + y * y) / 9.0);
                    moment += tap * tapWeight;
                    moment2 += tap * tap * tapWeight;
                    momentWeight += tapWeight;
                }
            }
            float mean = moment / max(momentWeight, 1.0e-5);
            float variance = max(0.0, moment2 / max(momentWeight, 1.0e-5) - mean * mean);
            float stdDev = sqrt(variance);
            float clampWeight = saturate(1.0 - _HoGTAOTemporalRejection);
            float clampMultiplier = lerp(2.0, 5.0, clampWeight);
            float clampMin = current - stdDev * 0.5 * clampMultiplier;
            float clampMax = current + stdDev * 0.5 * clampMultiplier;
            previous = clamp(previous, clampMin, clampMax);
            half historyWeight = accepted * (1.0h - rcp(max(sampleCount, 1.0h)));
            if (_HoGTAODebugMode > 4.5)
            {
                // Stable history is a grayscale sample-age signal; rejected
                // history is red, matching HTrace's disocclusion diagnostic.
                half historyAge = saturate(sampleCount / max(_HoGTAOTemporalMaxFrames, 1.0h));
                return accepted > 0.5h
                    ? half4(historyAge, historyAge, historyAge, 1.0h)
                    : half4(1.0h, 0.0h, 0.0h, 1.0h);
            }
            half ao = lerp(current, previous, historyWeight);
            float2 packedNormal = HoGTAOPackNormal(currentNormal);
            return half4(ao, sampleCount / max(_HoGTAOTemporalMaxFrames, 1.0h), packedNormal.x, packedNormal.y);
        }

        half4 OutputAO(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half ao = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half visibility = 1.0h - ao;
            return half4(visibility, visibility, visibility, 1.0h);
        }

        half4 Spatial(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 centerND = SAMPLE_TEXTURE2D_X(_HoGTAOGeometryInput, sampler_PointClamp, uv);
            half centerAO = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).r;
            if (centerND.a < 0.0001h)
            {
                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }

            float2 texel = rcp(_ScreenParams.xy) * max(_HoGTAOSpatialResolution, 1.0)
                * (_HoGTAOSpatialFilter > 0.5 ? max(_HoGTAOSpatialStep, 1.0) : max(_HoGTAOSpatialRadius, 0.5));
            float3 centerNormal = normalize((float3)centerND.rgb * 2.0 - 1.0);
            float centerDepth = centerND.a;
            float3 centerPositionVS = HoGTAOViewPosition(uv, centerDepth);
            float3 centerNormalVS = normalize(mul((float3x3)_HoGTAOViewMatrix, centerNormal));
            centerNormalVS *= float3(1.0, -1.0, -1.0);
            float depthScale = lerp(0.35, 1.0, saturate(1.0 - centerAO) * saturate(_HoGTAOSpatialAdaptivity));
            float sum = centerAO;
            float weightSum = 1.0;
            static const float2 taps[8] =
            {
                float2(-1.0, -1.0), float2(0.0, -1.0), float2(1.0, -1.0), float2(-1.0, 0.0),
                float2(1.0, 0.0), float2(-1.0, 1.0), float2(0.0, 1.0), float2(1.0, 1.0)
            };

            [unroll]
            for (int i = 0; i < 8; i++)
            {
                float2 sampleUV = uv + taps[i] * texel;
                half4 sampleND = SAMPLE_TEXTURE2D_X(_HoGTAOGeometryInput, sampler_PointClamp, sampleUV);
                if (sampleND.a < 0.0001h)
                {
                    continue;
                }

                float depthDelta = abs(sampleND.a - centerDepth) / max(centerDepth, 0.05);
                float3 sampleNormal = normalize((float3)sampleND.rgb * 2.0 - 1.0);
                float3 samplePositionVS = HoGTAOViewPosition(sampleUV, sampleND.a);
                float planeDistance = abs(dot(samplePositionVS - centerPositionVS, centerNormalVS)) / max(centerDepth, 0.05);
                float normalWeight = _HoGTAOSpatialFilter > 0.5
                    ? step(0.85, dot(centerNormal, sampleNormal))
                    : saturate((dot(centerNormal, sampleNormal) - 0.5) * 2.0);
                float depthWeight = _HoGTAOSpatialFilter > 0.5
                    // HTrace Box filter: PlaneFilterWeight=50000 and
                    // PlaneWeighting uses exp2(-100 * weight * delta^2).
                    ? exp2(-5000000.0 * planeDistance * planeDistance / max(_HoGTAOPixelSpreadMultiplier, 1.0e-4))
                    : exp2(-48.0 * depthDelta * depthDelta / max(depthScale, 0.05));
                float tapDistance = dot(taps[i], taps[i]);
                float spatialWeight = _HoGTAOSpatialFilter > 0.5
                    ? (tapDistance <= 1.01 ? 1.0 : 0.5)
                    : exp2(-0.75 * tapDistance);
                float weight = normalWeight * depthWeight * spatialWeight;
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, sampleUV).r * weight;
                weightSum += weight;
            }

            half filtered = (half)saturate(sum / max(weightSum, 1.0e-5));
            return half4(filtered, filtered, filtered, 1.0h);
        }

        half4 DebugOutput(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }

        half4 DepthHistory(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).aaaa;
        }
        ENDHLSL

        Pass
        {
            Name "Ho-GTAO Depth Copy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthCopy
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Depth Downsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthDownsample
            ENDHLSL
        }

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
            Name "Ho-GTAO Spatial"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Spatial
            ENDHLSL
        }

        Pass
        {
            Name "Ho-GTAO Depth History"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthHistory
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
