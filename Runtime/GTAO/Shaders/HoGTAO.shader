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
        TEXTURE2D_X(_HoGTAOHistoryPrevNormalTex);
        float4 _HoGTAOHistoryPrevTex_TexelSize;
        TEXTURE2D_X(_MotionVectorTexture);
        TEXTURE2D_X(_HoGTAOMotionMask);
        TEXTURE2D_X(_HoGTAOMotionDelta);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip0);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip1);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip2);
        TEXTURE2D_X_FLOAT(_HoGTAODepthMip3);
        TEXTURE2D_X_FLOAT(_HoGTAODepthInput);
        // Raw device depth produced by Ho-GeometryBuffer.  Do not rebuild
        // raw depth from the fp16 linear-depth channel: that round trip loses
        // far-plane precision and produces visible contour bands.
        TEXTURE2D_X_FLOAT(_HoGeometryBufferDepthTexture);
        TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
        TEXTURE2D_X(_HoGeometryBufferCoverageTexture);
        float _HoGeometryBufferCoverageTextureValid;
        TEXTURE2D_X_FLOAT(_HoGTAOSpatialDepthTexture);
        TEXTURE2D_X(_HoGTAOGeometryInput);
        float4 _HoGTAODepthInputTexelSize;
        float4x4 _HoGTAOInvProjMatrix;
        float4x4 _HoGTAOInvViewMatrix;
        float4x4 _HoGTAOPreviousViewMatrix;
        float4 _HoGTAODepthToViewParams;
        float4 _HoGTAOPreviousDepthToViewParams;
        float4 _HoGTAOPreviousZBufferParams;
        float _HoGTAOOrthographic;
        float _HoGTAOPreviousOrthographic;
        float4x4 _HoGTAOProjMatrix;
        float4x4 _HoGTAOViewMatrix;
        float _HoGTAODebugMode;
        float _HoGTAOHistoryBlend;
        float _HoGTAOHistoryValid;
        float _HoGTAOTemporalMaxFrames;
        float _HoGTAOTemporalRejection;
        float _HoGTAOUseMotionVectors;
        float _HoGTAOUseCameraMotion;
        float _HoGTAOUseObjectMotion;
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

        // R of the GeometryBuffer coverage texture: fraction of the pixel that
        // carries geometry at all. Without MSAA this degrades to "is there
        // geometry here", which is what the depth-normal alpha already says.
        half HoGTAOGeometryCoverageAt(float2 uv, half4 normalDepth)
        {
            if (_HoGeometryBufferCoverageTextureValid > 0.5)
            {
                return saturate(SAMPLE_TEXTURE2D_X(
                    _HoGeometryBufferCoverageTexture,
                    sampler_PointClamp,
                    saturate(uv)).r);
            }

            return step(0.0001h, normalDepth.a);
        }

        // G of the same texture: share of the pixel owned by the surface the
        // MSAA resolve selected. It equals the total coverage on a single
        // surface pixel and is smaller at a silhouette shared with a farther
        // surface - the only pixels where a per pixel occlusion value cannot
        // represent what the pixel's colour actually is.
        half HoGTAOGeometrySelectedCoverageAt(float2 uv, half4 normalDepth)
        {
            if (_HoGeometryBufferCoverageTextureValid > 0.5)
            {
                return saturate(SAMPLE_TEXTURE2D_X(
                    _HoGeometryBufferCoverageTexture,
                    sampler_PointClamp,
                    saturate(uv)).g);
            }

            return step(0.0001h, normalDepth.a);
        }

        float4 DepthCopy(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float rawDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half coverage = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, input.texcoord).a;
            rawDepth = coverage > 0.0001h ? rawDepth : 0.0;
            // The pyramid uses zero for uncovered pixels on the reversed-Z
            // desktop path, matching HTrace's depth-pyramid convention.
            #if UNITY_REVERSED_Z
                rawDepth = rawDepth <= UNITY_RAW_FAR_CLIP_VALUE + 1.0e-5 ? 0.0 : rawDepth;
            #else
                rawDepth = rawDepth >= UNITY_RAW_FAR_CLIP_VALUE - 1.0e-5 ? 0.0 : rawDepth;
            #endif
            return float4(rawDepth, rawDepth, rawDepth, rawDepth);
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
            // HTrace stores SampleData.LOD as uint.  The assignment from the
            // logarithmic footprint therefore truncates to an integer, and
            // the point sampler selects one mip rather than blending adjacent
            // levels. Keep the same discrete footprint semantics here.
            int mip = clamp((int)lod, 0, 3);
            if (mip == 0)
                return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip0, sampler_PointClamp, uv).r;
            if (mip == 1)
                return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip1, sampler_PointClamp, uv).r;
            if (mip == 2)
                return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip2, sampler_PointClamp, uv).r;
            return SAMPLE_TEXTURE2D_X(_HoGTAODepthMip3, sampler_PointClamp, uv).r;
        }

        bool HoGTAOIsFarClip(float rawDepth)
        {
            #if UNITY_REVERSED_Z
                return rawDepth <= UNITY_RAW_FAR_CLIP_VALUE + 1.0e-5;
            #else
                return rawDepth >= UNITY_RAW_FAR_CLIP_VALUE - 1.0e-5;
            #endif
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

        float3 HoGTAOPreviousViewPosition(float2 uv, float linearDepth)
        {
            float2 viewXY = uv * _HoGTAOPreviousDepthToViewParams.xy + _HoGTAOPreviousDepthToViewParams.zw;
            if (_HoGTAOPreviousOrthographic > 0.5)
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

        void HoGTAOUpdateBitmask(inout uint bitmask, float2 horizonSamples)
        {
            uint2 horizonInt = uint2(round(saturate(horizonSamples) * 32.0));
            uint horizonMin = horizonInt.x < 32u ? 0xFFFFFFFFu << horizonInt.x : 0u;
            uint horizonMax = horizonInt.y != 0u ? 0xFFFFFFFFu >> (32u - horizonInt.y) : 0u;
            bitmask |= horizonMin & horizonMax;
        }

        bool HoGTAOSample(float2 uv, float lod, out float3 positionVS, out float3 normalWS)
        {
            float sampledRawDepth = HoGTAOSampleDepth(saturate(uv), lod);
            if (HoGTAOIsFarClip(sampledRawDepth))
            {
                positionVS = 0.0;
                normalWS = 0.0;
                return false;
            }

            float sampledLinearDepth = LinearEyeDepth(sampledRawDepth, _ZBufferParams);
            positionVS = HoGTAOViewPosition(saturate(uv), sampledLinearDepth);
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, saturate(uv));
            normalWS = normalize((float3)nd.rgb * 2.0 - 1.0);
            return true;
        }

        float2 HoGTAOCameraMotion(float2 uv)
        {
            float rawDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferDepthTexture, sampler_PointClamp, saturate(uv)).r;
            if (HoGTAOIsFarClip(rawDepth))
                return 0.0;

            float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
            float3 currentPositionVS = HoGTAOViewPosition(saturate(uv), linearDepth);
            float3 worldPosition = mul(
                _HoGTAOInvViewMatrix,
                float4(currentPositionVS * float3(1.0, -1.0, -1.0), 1.0)).xyz;
            float3 previousUnityPosition = mul(
                _HoGTAOPreviousViewMatrix,
                float4(worldPosition, 1.0)).xyz;
            float3 previousPositionVS = previousUnityPosition * float3(1.0, -1.0, -1.0);
            float previousDepth = abs(previousUnityPosition.z);
            if (previousDepth <= 1.0e-5)
                return 0.0;

            float2 previousViewXY = _HoGTAOPreviousOrthographic > 0.5
                ? previousPositionVS.xy
                : previousPositionVS.xy / previousDepth;
            float2 previousUV = (previousViewXY - _HoGTAOPreviousDepthToViewParams.zw)
                / max(_HoGTAOPreviousDepthToViewParams.xy, 1.0e-5);
            return uv - previousUV;
        }

        half4 CameraMotion(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(HoGTAOCameraMotion(input.texcoord), 0.0h, 1.0h);
        }

        float HoGTAOHitVelocity(float2 originUV, float2 hitUV)
        {
            float originObjectMask = _HoGTAOUseObjectMotion > 0.5
                ? step(1.0e-6, length(SAMPLE_TEXTURE2D_X(_HoGTAOMotionMask, sampler_PointClamp, saturate(originUV)).rg))
                : 0.0;
            float hitObjectMask = _HoGTAOUseObjectMotion > 0.5
                ? step(1.0e-6, length(SAMPLE_TEXTURE2D_X(_HoGTAOMotionMask, sampler_PointClamp, saturate(hitUV)).rg))
                : 0.0;
            if (_HoGTAOUseObjectMotion > 0.5 && (originObjectMask > 0.5 || hitObjectMask > 0.5))
            {
                float4 originDelta = SAMPLE_TEXTURE2D_X(_HoGTAOMotionDelta, sampler_PointClamp, saturate(originUV));
                float4 hitDelta = SAMPLE_TEXTURE2D_X(_HoGTAOMotionDelta, sampler_PointClamp, saturate(hitUV));
                float originMagnitude = originObjectMask > 0.5 ? abs(originDelta.g) : 0.0;
                float hitMagnitude = hitObjectMask > 0.5 ? abs(hitDelta.g) : 0.0;
                float maximumMagnitude = max(originMagnitude, hitMagnitude);
                float2 originDirectionOct = originDelta.ba * 2.0 - 1.0;
                float2 hitDirectionOct = hitDelta.ba * 2.0 - 1.0;
                float3 originDirection = UnpackNormalOctQuadEncode(originDirectionOct);
                float3 hitDirection = UnpackNormalOctQuadEncode(hitDirectionOct);
                float directionAgreement = dot(originDirection, hitDirection);
                float magnitudeDivergence = abs(hitMagnitude - originMagnitude)
                    / max(maximumMagnitude, 1.0e-6);
                if (magnitudeDivergence > 0.4 || directionAgreement < 0.5 || hitMagnitude < 1.0e-6)
                {
                    // HTrace deliberately gives a zero-motion hit a full
                    // rejection impulse when the origin is moving.
                    return maximumMagnitude > 1.0e-6 ? saturate(maximumMagnitude * 10.0) : 1.0;
                }
                return 0.0;
            }

            float2 originMotion = _HoGTAOUseMotionVectors > 0.5
                ? SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_LinearClamp, saturate(originUV)).xy
                : (_HoGTAOUseCameraMotion > 0.5 ? HoGTAOCameraMotion(originUV) : 0.0);
            float2 hitMotion = _HoGTAOUseMotionVectors > 0.5
                ? SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_LinearClamp, saturate(hitUV)).xy
                : (_HoGTAOUseCameraMotion > 0.5 ? HoGTAOCameraMotion(hitUV) : 0.0);
            float originMagnitude = length(originMotion);
            float hitMagnitude = length(hitMotion);
            float maximumMagnitude = max(originMagnitude, hitMagnitude);
            if (maximumMagnitude < 1.0e-6)
            {
                return 0.0;
            }

            float directionAgreement = dot(
                originMotion / max(originMagnitude, 1.0e-6),
                hitMotion / max(hitMagnitude, 1.0e-6));
            float magnitudeDivergence = abs(hitMagnitude - originMagnitude)
                / max(maximumMagnitude, 1.0e-6);
            // Match HTrace's UpdateHitVelocity: a hit is unstable when its
            // motion differs materially from the origin, changes direction,
            // or has no velocity while the origin is moving.
            if (magnitudeDivergence > 0.4 || directionAgreement < 0.5 || hitMagnitude < 1.0e-6)
            {
                return saturate(maximumMagnitude * 10.0);
            }

            return 0.0;
        }

        float HoGTAOCompute(float2 uv, half4 centerND, out float hitVelocity)
        {
            hitVelocity = 0.0;
            float centerRawDepth = HoGTAOSampleDepth(saturate(uv), 0.0);
            if (HoGTAOIsFarClip(centerRawDepth))
                return 0.0;
            float linearDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
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
            // HTrace keeps the reciprocal screen radius unconditionally.  A
            // second clamp at one pixel changes the near/far balance and
            // makes distant slices disproportionately camera-facing.
            float minStep = 1.3 / max(screenRadius, 1.0e-4);
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
                // Keep every slice in the normalization, including the
                // measure-zero case where the projected normal collapses.
                // Dropping that slice makes the result depend on the view
                // direction and can leave weightTotal at zero, producing a
                // strong directional dark bias. HTrace keeps the slice in the
                // integral; use a tiny stable weight here to avoid NaNs.
                projectedLength = max(projectedLength, 1.0e-4);

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
                    // HTrace passes a fractional LOD; hard flooring here
                    // creates visible distance bands as the march crosses
                    // each depth-pyramid level.
                    float lod = clamp(log2(max(length(stride * samplingDirection), 1.0)) - 3.0, 0.0, 3.0);
                    if (HoGTAOSample(uv - offset, lod, samplePosition, sampleNormal))
                    {
                        hitVelocity = max(hitVelocity, HoGTAOHitVelocity(uv, uv - offset));
                        float3 delta = samplePosition - positionVS;
                        float h = dot(delta, viewDirection);
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float2 horizon = rsqrt(float2(d2, max(d2 + thickness * thickness - h * thickness * 2.0, 1.0e-6))) * float2(h, h - thickness);
                        float sampleWeight = rcp(1.0 + d2 * falloff);
                        horizon = _HoGTAOUseAttenuation > 0.5 ? lerp(minHorizon.xx, horizon, sampleWeight) : horizon;
                        // Horizon cosine is signed.  Saturating here folds the
                        // back half of the arc onto pi/2, which is not HTrace's
                        // bitmask integration and produces strong view
                        // direction bias.  Keep the signed domain and only
                        // protect the fast acos approximation's input range.
                        horizon = float2(HoGTAOFastACos(clamp(horizon.x, -1.0, 1.0)), HoGTAOFastACos(clamp(horizon.y, -1.0, 1.0)));
                        float2 normalized = saturate((nAngle + horizon + PI * 0.5) / PI);
                        normalized *= normalized * (3.0 - 2.0 * normalized);
                        HoGTAOUpdateBitmask(bitmask, normalized);
                    }

                    if (HoGTAOSample(uv + offset, lod, samplePosition, sampleNormal))
                    {
                        hitVelocity = max(hitVelocity, HoGTAOHitVelocity(uv, uv + offset));
                        float3 delta = samplePosition - positionVS;
                        float h = dot(delta, viewDirection);
                        float d2 = max(dot(delta, delta), 1.0e-6);
                        float2 horizon = rsqrt(float2(d2, max(d2 + thickness * thickness - h * thickness * 2.0, 1.0e-6))) * float2(h, h - thickness);
                        float sampleWeight = rcp(1.0 + d2 * falloff);
                        horizon = _HoGTAOUseAttenuation > 0.5 ? lerp(minHorizon.yy, horizon, sampleWeight) : horizon;
                        horizon = float2(HoGTAOFastACos(clamp(horizon.x, -1.0, 1.0)), HoGTAOFastACos(clamp(horizon.y, -1.0, 1.0)));
                        float2 normalized = saturate((nAngle - horizon + PI * 0.5) / PI);
                        normalized *= normalized * (3.0 - 2.0 * normalized);
                        HoGTAOUpdateBitmask(bitmask, normalized.yx);
                    }
                }

                visibility += (1.0 - saturate((float)countbits(bitmask) / 26.0)) * projectedLength;
                weightTotal += projectedLength;
            }

            hitVelocity = saturate(hitVelocity) * saturate(_HoGTAOTemporalRejection);
            return 1.0 - saturate(visibility / max(weightTotal, 1.0e-5));
        }

        half4 Generate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            if (_HoGTAODebugMode > 1.5 && _HoGTAODebugMode < 2.5)
            {
                float rawDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferDepthTexture, sampler_PointClamp, input.texcoord).r;
                float depth = HoGTAOIsFarClip(rawDepth)
                    ? 0.0
                    : saturate(LinearEyeDepth(rawDepth, _ZBufferParams) / 50.0);
                return half4(depth, depth, depth, 1.0h);
            }
            if (_HoGTAODebugMode > 2.5 && _HoGTAODebugMode < 3.5)
            {
                return half4(nd.rgb, 1.0h);
            }
            if (_HoGTAODebugMode > 3.5 && _HoGTAODebugMode < 4.5)
            {
                float2 motionMask = _HoGTAOUseObjectMotion > 0.5
                    ? SAMPLE_TEXTURE2D_X(_HoGTAOMotionMask, sampler_PointClamp, input.texcoord).rg
                    : 0.0;
                float4 motionDelta = _HoGTAOUseObjectMotion > 0.5
                    ? SAMPLE_TEXTURE2D_X(_HoGTAOMotionDelta, sampler_PointClamp, input.texcoord)
                    : 0.0;
                float2 nativeMotion = _HoGTAOUseMotionVectors > 0.5
                    ? SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_LinearClamp, input.texcoord).xy
                    : (_HoGTAOUseCameraMotion > 0.5 ? HoGTAOCameraMotion(input.texcoord) : 0.0);
                float objectMoved = step(2.0e-4, abs(motionDelta.g));
                float objectMask = step(1.0e-6, length(motionMask));
                // HTrace's Motion debug keeps zero motion black and visualizes
                // the signed camera vector directly; object motion uses the
                // same blue/cyan overrides as its main-buffer view.
                float3 combinedMotion = float3(nativeMotion * 10.0, 0.0);
                if (objectMoved > 0.5)
                    combinedMotion = float3(0.0, 1.0, 1.0);
                else if (objectMask > 0.5)
                    combinedMotion = float3(0.0, 0.0, 1.0);
                return half4(combinedMotion, 1.0h);
            }
            float centerRawDepth = HoGTAOSampleDepth(saturate(input.texcoord), 0.0);
            if (HoGTAOIsFarClip(centerRawDepth))
            {
                // Internal history/filter buffers store AO amount (0 = no
                // occlusion). Final composition turns this into visibility.
                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            // Highest-quality HTrace profile: Visibility Bitmasks. This is the
            // only tracing path that consumes Thickness and preserves thin
            // geometric occlusion bands.
            float hitVelocity;
            half ao = HoGTAOCompute(input.texcoord, nd, hitVelocity);
            return half4(ao, hitVelocity, 0.0h, 1.0h);
        }

        void Temporal(
            Varyings input,
            out half4 historyOutput : SV_Target0,
            out half4 normalOutput : SV_Target1,
            out half4 debugOutput : SV_Target2)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            debugOutput = 0.0h;
            half4 currentData = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            half current = currentData.r;
            float currentVelocity = currentData.g;
            half4 geometry = SAMPLE_TEXTURE2D_X(_HoGTAOGeometryInput, sampler_PointClamp, input.texcoord);
            float currentRawDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferDepthTexture, sampler_PointClamp, input.texcoord).r;
            bool currentSurfaceValid = !HoGTAOIsFarClip(currentRawDepth);
            float currentLinearDepth = currentSurfaceValid
                ? LinearEyeDepth(currentRawDepth, _ZBufferParams)
                : 0.0;
            float3 currentPositionVS = currentSurfaceValid
                ? HoGTAOViewPosition(input.texcoord, currentLinearDepth)
                : 0.0;
            float3 currentNormalWS = normalize((float3)geometry.rgb * 2.0 - 1.0);
            float3 currentNormalVS = normalize(mul((float3x3)_HoGTAOViewMatrix, currentNormalWS));
            currentNormalVS *= float3(1.0, -1.0, -1.0);
            float3 currentViewDirection = normalize(-currentPositionVS);
            float viewAlignment = 1.0 - abs(dot(currentNormalVS, currentViewDirection));
            float3 currentWorldPosition = mul(
                _HoGTAOInvViewMatrix,
                float4(currentPositionVS * float3(1.0, -1.0, -1.0), 1.0)).xyz;
            float3 currentPreviousUnityPosition = mul(
                _HoGTAOPreviousViewMatrix,
                float4(currentWorldPosition, 1.0)).xyz;
            float3 currentPreviousPositionVS = currentPreviousUnityPosition * float3(1.0, -1.0, -1.0);
            float currentPreviousDepth = abs(currentPreviousUnityPosition.z);
            float objectMotionMask = _HoGTAOUseObjectMotion > 0.5
                ? step(1.0e-6, length(SAMPLE_TEXTURE2D_X(_HoGTAOMotionMask, sampler_PointClamp, input.texcoord).rg))
                : 0.0;
            float objectMotionDepthDelta = _HoGTAOUseObjectMotion > 0.5
                ? SAMPLE_TEXTURE2D_X(_HoGTAOMotionDelta, sampler_PointClamp, input.texcoord).r
                : 0.0;
            currentPreviousDepth += objectMotionDepthDelta;
            float depthThreshold = lerp(0.005, 0.10, pow(saturate(viewAlignment), 8.0))
                * currentPreviousDepth * max(_HoGTAOPixelSpreadMultiplier, 1.0e-4);
            if (objectMotionMask > 0.5 && abs(objectMotionDepthDelta) < 1.0e-6)
            {
                float relaxMultiplier = max(50.0 / max(currentPreviousDepth, 1.0e-4), 1.0);
                depthThreshold *= relaxMultiplier;
            }
            float2 motion = _HoGTAOUseMotionVectors > 0.5
                ? SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_LinearClamp, input.texcoord).xy
                : (_HoGTAOUseCameraMotion > 0.5 ? HoGTAOCameraMotion(input.texcoord) : float2(0.0, 0.0));
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
            float previousVelocityAccumulated = 0.0;
            float previousCountAccumulated = 0.0;
            float historyWeightSum = 0.0;
            [unroll]
            for (int historyTap = 0; historyTap < 4; historyTap++)
            {
                float2 historyCoordTap = historyBase + historyOffsets[historyTap] + 0.5;
                float inside = step(0.0, historyCoordTap.x)
                    * step(historyCoordTap.x, historySize.x - 1.0)
                    * step(0.0, historyCoordTap.y)
                    * step(historyCoordTap.y, historySize.y - 1.0);
                float2 historyUV = saturate(historyCoordTap * historyTexel);
                half4 historyData = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, historyUV);
                half4 normalData = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevNormalTex, sampler_PointClamp, historyUV);
                float historyRawDepth = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevDepthTex, sampler_PointClamp, historyUV).r;
                float historyLinearDepth = HoGTAOIsFarClip(historyRawDepth)
                    ? 0.0
                    : LinearEyeDepth(historyRawDepth, _HoGTAOPreviousZBufferParams);
                float3 historyNormalWS = normalize((float3)normalData.gba * 2.0 - 1.0);
                float normalValid = step(0.5, dot(currentNormalWS, historyNormalWS));
                float validDepth = currentSurfaceValid && !HoGTAOIsFarClip(historyRawDepth)
                    ? inside
                        * step(abs(currentPreviousDepth - historyLinearDepth), max(depthThreshold, 1.0e-4))
                        * normalValid
                    : 0.0;
                float tapWeight = historyWeights[historyTap] * validDepth;
                previousAccumulated += historyData.r * tapWeight;
                previousVelocityAccumulated += historyData.g * tapWeight;
                previousCountAccumulated += historyData.b * max(_HoGTAOTemporalMaxFrames, 1.0) * tapWeight;
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
                float fallbackRawDepth = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevDepthTex, sampler_PointClamp, fallbackUV).r;
                float fallbackLinearDepth = HoGTAOIsFarClip(fallbackRawDepth)
                    ? 0.0
                    : LinearEyeDepth(fallbackRawDepth, _HoGTAOPreviousZBufferParams);
                half4 fallbackNormalData = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevNormalTex, sampler_PointClamp, fallbackUV);
                float fallbackValid = currentSurfaceValid && !HoGTAOIsFarClip(fallbackRawDepth)
                    ? step(abs(currentPreviousDepth - fallbackLinearDepth), max(depthThreshold, 1.0e-4))
                        * step(0.5, dot(currentNormalWS, normalize((float3)fallbackNormalData.gba * 2.0 - 1.0)))
                    : 0.0;
                previousAccumulated = fallbackData.r * fallbackValid;
                previousVelocityAccumulated = fallbackData.g * fallbackValid;
                previousCountAccumulated = fallbackData.b * max(_HoGTAOTemporalMaxFrames, 1.0) * fallbackValid;
                historyWeightSum = fallbackValid;
            }
            half previous = historyWeightSum > 1.0e-5 ? previousAccumulated / historyWeightSum : 0.0h;
            half previousVelocity = historyWeightSum > 1.0e-5
                ? previousVelocityAccumulated / historyWeightSum
                : 0.0h;
            half previousCount = historyWeightSum > 1.0e-5 ? previousCountAccumulated / historyWeightSum : 0.0h;
            float3 currentNormal = normalize((float3)geometry.rgb * 2.0 - 1.0);
            // Sky/uncovered pixels have no surface history to validate. Keep
            // them white in the diagnostic instead of falsely marking them as
            // temporal disocclusions.
            if (!currentSurfaceValid && _HoGTAODebugMode > 4.5)
            {
                historyOutput = half4(0.0h, 0.0h, 0.0h, 1.0h);
                normalOutput = historyOutput;
                debugOutput = half4(1.0h, 1.0h, 1.0h, 1.0h);
                return;
            }
            half depthValid = (currentSurfaceValid ? 1.0h : 0.0h) * step(1.0e-5, historyWeightSum);
            half depthAgreement = step(1.0e-5, historyWeightSum);
            // HTrace applies normal rejection per bilinear history tap.  A
            // second whole-pixel normal vote would reject otherwise valid
            // history at silhouette edges and noticeably slow convergence.
            half accepted = saturate(_HoGTAOHistoryValid) * depthValid * depthAgreement;
            half sampleCount = min(previousCount + 1.0h, max(_HoGTAOTemporalMaxFrames, 1.0));
            sampleCount = lerp(1.0h, sampleCount, accepted);
            float temporalWeight = 1.0 - rcp(max((float)sampleCount, 1.0));
            float velocityAccumulated = lerp(currentVelocity, previousVelocity, temporalWeight * 0.5);

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
            float clampWeight = _HoGTAOUseMotionVectors > 0.5
                ? saturate(pow(max(1.0 - velocityAccumulated, 1.0e-6), 10.0))
                : saturate(1.0 - _HoGTAOTemporalRejection);
            float clampMultiplier = lerp(2.0, 5.0, clampWeight);
            float clampMin = current - stdDev * 0.5 * clampMultiplier;
            float clampMax = current + stdDev * 0.5 * clampMultiplier;
            previous = clamp(previous, clampMin, clampMax);
            half historyWeight = accepted * (half)temporalWeight;
            if (_HoGTAODebugMode > 4.5)
            {
                // Match HTrace's Temporal Disocclusion view: inspect the
                // reprojected history count before adding this frame, and
                // attenuate it with the same velocity rejection exponent.
                float reprojectedAge = saturate(previousCount / max(_HoGTAOTemporalMaxFrames, 1.0));
                float reprojectedVelocity = saturate(previousVelocity);
                float velocityAge = saturate(pow(max(1.0 - reprojectedVelocity, 1.0e-6), 10.0));
                half historyAge = (half)saturate(reprojectedAge * velocityAge);
                debugOutput = previousCount >= 1.0h && historyWeightSum > 1.0e-5
                    ? half4(historyAge, historyAge, historyAge, 1.0h)
                    : half4(1.0h, 0.0h, 0.0h, 1.0h);
                half debugAo = lerp(current, previous, historyWeight);
                historyOutput = half4(debugAo, velocityAccumulated, sampleCount / max(_HoGTAOTemporalMaxFrames, 1.0h), 0.0h);
                normalOutput = half4(debugAo, currentNormal * 0.5h + 0.5h);
                return;
            }
            half ao = lerp(current, previous, historyWeight);
            historyOutput = half4(ao, velocityAccumulated, sampleCount / max(_HoGTAOTemporalMaxFrames, 1.0h), 0.0h);
            normalOutput = half4(ao, currentNormal * 0.5h + 0.5h);
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
            half4 centerAOData = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            half centerAO = centerAOData.r;
            float centerRawDepth = SAMPLE_TEXTURE2D_X(_HoGTAOSpatialDepthTexture, sampler_PointClamp, uv).r;
            if (HoGTAOIsFarClip(centerRawDepth))
            {
                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }

            float2 texel = rcp(_ScreenParams.xy) * max(_HoGTAOSpatialResolution, 1.0)
                * (_HoGTAOSpatialFilter > 0.5 ? max(_HoGTAOSpatialStep, 1.0) : max(_HoGTAOSpatialRadius, 0.5));
            // Spatial ping-pong stores AO amount in R and the filtered world
            // normal in GBA, matching HTrace's OcclusionNormal buffer.
            float3 centerNormal = normalize((float3)centerAOData.gba * 2.0 - 1.0);
            float centerDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
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
                float sampleRawDepth = SAMPLE_TEXTURE2D_X(_HoGTAOSpatialDepthTexture, sampler_PointClamp, sampleUV).r;
                if (HoGTAOIsFarClip(sampleRawDepth))
                {
                    continue;
                }

                float sampleDepth = LinearEyeDepth(sampleRawDepth, _ZBufferParams);
                float depthDelta = abs(sampleDepth - centerDepth) / max(centerDepth, 0.05);
                half4 sampleAOData = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, sampleUV);
                float3 sampleNormal = normalize((float3)sampleAOData.gba * 2.0 - 1.0);
                float3 samplePositionVS = HoGTAOViewPosition(sampleUV, sampleDepth);
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

            // MSAA silhouette composite.
            // Everything above produced one occlusion value for this pixel,
            // traced from the surface the MSAA resolve selected (the nearest
            // sample). At a silhouette the pixel's colour is a coverage
            // weighted mixture, so a pixel that is partly the background must
            // not present the resolved surface's occlusion for all of it: the
            // foreground surface at its own silhouette is unoccluded by
            // construction (nothing lies nearer along the view direction),
            // which is the hard bright line this replaces.
            // The background surface's occlusion cannot be traced here - this
            // pixel's depth belongs to the foreground - so it is taken from the
            // neighbouring pixels where it is the resolved surface, and mixed in
            // by the share of the pixel the foreground owns. Compositing after
            // the denoiser keeps the filter from averaging the result back
            // toward the foreground.
            if (_HoGeometryBufferCoverageTextureValid > 0.5)
            {
                half centerCoverage = HoGTAOGeometryCoverageAt(uv, centerND);
                half centerSelectedCoverage = HoGTAOGeometrySelectedCoverageAt(uv, centerND);
                if (centerSelectedCoverage < centerCoverage - 0.001h)
                {
                    float2 oneTexel = rcp(_ScreenParams.xy);
                    float backgroundOcclusion = 0.0;
                    float backgroundWeight = 0.0;
                    // Two rings. The second is walked only when the first found
                    // no background pixel at all, which is what happens at sharp
                    // features: a converging hair tip can carry silhouette
                    // pixels on every side of its first ring, so the nearest
                    // solid background pixel sits two texels away. The guard
                    // keeps the common case at eight taps.
                    [unroll]
                    for (int ring = 1; ring <= 2; ring++)
                    {
                        // Ring 2 is only walked when ring 1 found nothing at
                        // all; the guard is evaluated once per ring so ring 1
                        // still averages all of its taps.
                        if (backgroundWeight > 0.5)
                        {
                            continue;
                        }

                        [unroll]
                        for (int behindTap = 0; behindTap < 8; behindTap++)
                        {
                            float2 behindUV = uv + taps[behindTap] * oneTexel * ring;
                            half4 behindND = SAMPLE_TEXTURE2D_X(_HoGTAOGeometryInput, sampler_PointClamp, behindUV);
                            if (HoGTAOGeometryCoverageAt(behindUV, behindND) <= 0.0001h)
                            {
                                continue;
                            }

                            // Only a pixel whose resolved surface owns most of
                            // itself can stand in for the background: another
                            // silhouette pixel holds the foreground's occlusion,
                            // not the background's.
                            if (HoGTAOGeometrySelectedCoverageAt(behindUV, behindND) < 0.5h)
                            {
                                continue;
                            }

                            float behindRawDepth = SAMPLE_TEXTURE2D_X(_HoGTAOSpatialDepthTexture, sampler_PointClamp, behindUV).r;
                            if (HoGTAOIsFarClip(behindRawDepth))
                            {
                                continue;
                            }

                            float behindDepth = LinearEyeDepth(behindRawDepth, _ZBufferParams);
                            if (behindDepth <= centerDepth + max(1.0e-4, centerDepth * 1.0e-3))
                            {
                                // Same surface or in front of it: not the
                                // background.
                                continue;
                            }

                            backgroundOcclusion += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, behindUV).r;
                            backgroundWeight += 1.0;
                        }
                    }

                    if (backgroundWeight > 0.5)
                    {
                        float backgroundShare = saturate(
                            1.0 - (float)centerSelectedCoverage / max((float)centerCoverage, 1.0e-5));
                        filtered = (half)lerp(
                            (float)filtered,
                            backgroundOcclusion / backgroundWeight,
                            backgroundShare);
                    }
                }
            }

            return half4(filtered, centerNormal * 0.5h + 0.5h);
        }

        half4 DebugOutput(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }

        half4 DepthHistory(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float rawDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half coverage = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, input.texcoord).a;
            rawDepth = coverage > 0.0001h ? rawDepth : 0.0;
            return float4(rawDepth, rawDepth, rawDepth, rawDepth);
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

        Pass
        {
            Name "Ho-GTAO Camera Motion"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CameraMotion
            ENDHLSL
        }

    }
}
