Shader "Hidden/lilToon/URP/HoSubsurfaceScattering"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_lilHoAovMaskIdTexture);
        TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
        TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
        TEXTURE2D_X(_lilHoAovSssTexture);
        TEXTURE2D_X(_lilHoSSSSourceTexture);
        TEXTURE2D_X(_lilHoSSSTransmissionTexture);

        float _lilHoAovActive;

        float4 _lilHoSSSParams;     // x strength, y radius screen px, z Burley sample budget, w RT scale compensation
        float4 _lilHoSSSGateParams; // x depth tolerance, y normal tolerance, z fallback source preserve
        float4 _lilHoSSSColor;
        float4 _lilHoSSSTransmissionParams; // x strength, y radius screen px, z samples, w main-light direction blend
        float4 _lilHoSSSTransmissionColor;
        float4 _lilHoSSSTransmissionShapeParams; // x depth weight, y edge boost, z rim weight, w smoothing
        float4 _lilHoSSSCompositeParams; // x transmission blend mode, y tint injection
        float4 _lilHoSSSProfileIds[8]; // x profile id, y enabled
        float4 _lilHoSSSProfileDiffusionParams[8]; // x radius, y source preserve, zw diffusion color rg
        float4 _lilHoSSSProfileTransmissionParams[8]; // x strength, y radius, zw transmission color rg
        float4 _lilHoSSSProfileShapeParams[8]; // x thickness scale, y diffusion color b, z transmission color b, w diffusion alpha
        float4 _lilHoSSSDirection;

        static const float LIL_HOSSS_PI = 3.14159265359;
        static const float LIL_HOSSS_LOG2_E = 1.44269504089;
        static const float LIL_HOSSS_GOLDEN_ANGLE = 2.39996322973;
        static const float LIL_HOSSS_BURLEY_FILTER_RADIUS = 16.5585;

        float HoSSSCoverage(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv).r;
        }

        float4 HoSSSNormalDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
        }

        float4 HoSSSNormalDepthLinear(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_LinearClamp, uv);
        }

        float3 HoSSSDecodeNormal(float3 encodedNormal)
        {
            return normalize(encodedNormal * 2.0 - 1.0);
        }

        float4 HoSSSSurfaceData(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_PointClamp, uv);
        }

        float4 HoSSSSurfaceDataLinear(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_LinearClamp, uv);
        }

        float HoSSSInterleavedNoise(float2 uv)
        {
            float2 pixel = floor(uv * _ScreenParams.xy);
            return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
        }

        float HoSSSProfileByte(float4 surfaceData)
        {
            return round(saturate(surfaceData.b) * 255.0);
        }

        float HoSSSProfileGate(float sampleProfileByte, float centerProfileByte)
        {
            return 1.0 - step(0.5, abs(sampleProfileByte - centerProfileByte));
        }

        float4 HoSSSProfileDiffusionParams(float profileByte)
        {
            float4 fallback = float4(_lilHoSSSParams.y, _lilHoSSSGateParams.z, _lilHoSSSColor.r, _lilHoSSSColor.g);
            [unroll]
            for (int i = 0; i < 8; i++)
            {
                if (_lilHoSSSProfileIds[i].y > 0.5 && abs(_lilHoSSSProfileIds[i].x - profileByte) < 0.5)
                {
                    return _lilHoSSSProfileDiffusionParams[i];
                }
            }

            return fallback;
        }

        float4 HoSSSProfileTransmissionParams(float profileByte)
        {
            float4 fallback = float4(_lilHoSSSTransmissionParams.x, _lilHoSSSTransmissionParams.y, _lilHoSSSTransmissionColor.r, _lilHoSSSTransmissionColor.g);
            [unroll]
            for (int i = 0; i < 8; i++)
            {
                if (_lilHoSSSProfileIds[i].y > 0.5 && abs(_lilHoSSSProfileIds[i].x - profileByte) < 0.5)
                {
                    return _lilHoSSSProfileTransmissionParams[i];
                }
            }

            return fallback;
        }

        float4 HoSSSProfileShapeParams(float profileByte)
        {
            float4 fallback = float4(1.0, _lilHoSSSColor.b, _lilHoSSSTransmissionColor.b, _lilHoSSSColor.a);
            [unroll]
            for (int i = 0; i < 8; i++)
            {
                if (_lilHoSSSProfileIds[i].y > 0.5 && abs(_lilHoSSSProfileIds[i].x - profileByte) < 0.5)
                {
                    return _lilHoSSSProfileShapeParams[i];
                }
            }

            return fallback;
        }

        float HoSSSTransmissionStrengthMultiplier(float4 surfaceData)
        {
            return lerp(1.0, 2.0, saturate(surfaceData.g));
        }

        float HoSSSTransmissionRadiusMultiplier(float4 surfaceData)
        {
            return lerp(0.5, 2.0, saturate(surfaceData.a));
        }

        float HoSSSThinness(float4 surfaceData)
        {
            float profileByte = HoSSSProfileByte(surfaceData);
            float thicknessScale = max(HoSSSProfileShapeParams(profileByte).x, 0.0);
            return saturate(surfaceData.r * thicknessScale);
        }

        float HoSSSGeometryValid(float4 normalDepth)
        {
            float normalValid = step(1.0e-4, dot(normalDepth.rgb, normalDepth.rgb));
            float depthValid = step(1.0e-4, normalDepth.a);
            return normalValid * depthValid;
        }

        float HoSSSSurfaceMask(float2 uv, float4 normalDepth, float4 surfaceData)
        {
            return step(0.5, _lilHoAovActive) * saturate(HoSSSCoverage(uv) * HoSSSThinness(surfaceData)) * HoSSSGeometryValid(normalDepth);
        }

        float HoSSSSurfaceMask(float2 uv, float4 normalDepth)
        {
            return HoSSSSurfaceMask(uv, normalDepth, HoSSSSurfaceData(uv));
        }

        float HoSSSDepthGate(float sampleDepth, float centerDepth)
        {
            float tolerance = max(_lilHoSSSGateParams.x, 1.0e-5);
            float depth01 = abs(sampleDepth - centerDepth) / tolerance;
            float softGate = 1.0 - smoothstep(0.45, 1.65, depth01);
            return softGate * softGate * (3.0 - 2.0 * softGate);
        }

        float HoSSSNormalGate(float3 sampleNormal, float3 centerNormal)
        {
            float tolerance = max(_lilHoSSSGateParams.y, 1.0e-5);
            float normal01 = saturate((dot(sampleNormal, centerNormal) - (1.0 - tolerance)) / tolerance);
            return smoothstep(0.0, 1.0, normal01);
        }

        float HoSSSTransmissionSampleWeight(
            float2 sampleUv,
            float centerDepth,
            float3 centerNormal,
            float centerProfileByte)
        {
            float4 sampleNormalDepth = HoSSSNormalDepthLinear(sampleUv);
            float4 sampleSurfaceData = HoSSSSurfaceDataLinear(sampleUv);
            float sampleMask = HoSSSSurfaceMask(sampleUv, sampleNormalDepth, sampleSurfaceData);
            float sampleProfileByte = HoSSSProfileByte(sampleSurfaceData);
            float3 sampleNormal = HoSSSDecodeNormal(sampleNormalDepth.rgb);

            float gate = sampleMask;
            gate *= HoSSSProfileGate(sampleProfileByte, centerProfileByte);
            gate *= HoSSSDepthGate(sampleNormalDepth.a, centerDepth);
            gate *= HoSSSNormalGate(sampleNormal, centerNormal);
            return gate;
        }

        float3 HoSSSTransmissionSampleColor(float2 sampleUv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, sampleUv).rgb;
        }

        float3 HoSSSProfileWeight(float distance01)
        {
            float3 profileRadius = float3(1.0, 0.62, 0.32);
            float3 d = distance01.xxx / profileRadius;
            return exp2(-d * d * 2.0);
        }

        float3 HoSSSEvalBurleyDiffusionProfile(float r, float3 shape)
        {
            float3 exp13 = exp2(((-LIL_HOSSS_LOG2_E / 3.0) * r) * shape);
            float3 expSum = exp13 * (1.0 + exp13 * exp13);
            return (shape * (1.0 / (8.0 * LIL_HOSSS_PI))) * expSum;
        }

        void HoSSSSampleBurleyDiffusionProfile(float u, out float radius01, out float rcpPdf)
        {
            u = 1.0 - saturate(u);
            u = max(u, 1.0e-4);

            float g = 1.0 + (4.0 * u) * (2.0 * u + sqrt(1.0 + (4.0 * u) * u));
            float n = exp2(log2(g) * (-1.0 / 3.0));
            float p = (g * n) * n;
            float c = 1.0 + p + n;
            float x = (3.0 / LIL_HOSSS_LOG2_E) * log2(c / (4.0 * u));

            float rcpExp = ((c * c) * c) / max((4.0 * u) * ((c * c) + (4.0 * u) * (4.0 * u)), 1.0e-5);
            rcpPdf = (8.0 * LIL_HOSSS_PI) * rcpExp;
            radius01 = saturate(x / LIL_HOSSS_BURLEY_FILTER_RADIUS);
        }

        float3 HoSSSBurleyProfileWeight(float radius01, float rcpPdf, float3 diffusionColor)
        {
            float3 scatteringDistance = max(diffusionColor, float3(0.08, 0.08, 0.08));
            float3 shape = rcp(scatteringDistance);
            float radius = max(radius01 * LIL_HOSSS_BURLEY_FILTER_RADIUS, 1.0e-4);
            return HoSSSEvalBurleyDiffusionProfile(radius, shape) * rcpPdf;
        }

        float2 HoSSSRotateOffset(float radius01, int sampleIndex, float phase)
        {
            float angle = (float)sampleIndex * LIL_HOSSS_GOLDEN_ANGLE + phase;
            float s;
            float c;
            sincos(angle, s, c);
            return float2(c, s) * radius01;
        }

        float HoSSSDiffusionSampleGate(
            float2 sampleUv,
            float centerDepth,
            float3 centerNormal,
            float centerProfileByte)
        {
            float4 sampleNormalDepth = HoSSSNormalDepthLinear(sampleUv);
            float4 sampleSurfaceData = HoSSSSurfaceDataLinear(sampleUv);
            float sampleMask = HoSSSSurfaceMask(sampleUv, sampleNormalDepth, sampleSurfaceData);
            float sampleProfileByte = HoSSSProfileByte(sampleSurfaceData);
            float3 sampleNormal = HoSSSDecodeNormal(sampleNormalDepth.rgb);

            float gate = sampleMask;
            gate *= HoSSSProfileGate(sampleProfileByte, centerProfileByte);
            gate *= HoSSSDepthGate(sampleNormalDepth.a, centerDepth);
            gate *= HoSSSNormalGate(sampleNormal, centerNormal);
            return gate;
        }

        float4 HoSSSSeparableDiffusion(float2 uv)
        {
            float4 centerNormalDepth = HoSSSNormalDepth(uv);
            float4 centerSurfaceData = HoSSSSurfaceData(uv);
            float centerProfileByte = HoSSSProfileByte(centerSurfaceData);
            float4 profileDiffusionParams = HoSSSProfileDiffusionParams(centerProfileByte);
            float3 centerNormal = HoSSSDecodeNormal(centerNormalDepth.rgb);
            float centerDepth = centerNormalDepth.a;
            float centerMask = HoSSSSurfaceMask(uv, centerNormalDepth, centerSurfaceData);
            if (centerMask <= 1.0e-4)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float centerThinness = saturate(centerMask);
            float radiusPx = max(profileDiffusionParams.x * centerThinness, 0.0);
            float2 stepUv = _BlitTexture_TexelSize.xy * _lilHoSSSDirection.xy * radiusPx * max(_lilHoSSSParams.w, 1.0e-5);

            float4 centerSource = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float3 sum = centerSource.rgb;
            float3 weightSum = 1.0;
            float alphaSum = centerSource.a;
            float alphaWeightSum = 1.0;

            [unroll]
            for (int i = 1; i <= 4; i++)
            {
                float distance01 = i / 4.0;
                float3 kernelWeight = HoSSSProfileWeight(distance01);
                float alphaKernelWeight = kernelWeight.g;

                [unroll]
                for (int side = -1; side <= 1; side += 2)
                {
                    float2 sampleUv = uv + stepUv * (distance01 * side);
                    float gate = HoSSSDiffusionSampleGate(sampleUv, centerDepth, centerNormal, centerProfileByte);

                    float3 weight = kernelWeight * gate;
                    float4 sampleSource = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv);
                    sum += sampleSource.rgb * weight;
                    weightSum += weight;
                    alphaSum += sampleSource.a * alphaKernelWeight * gate;
                    alphaWeightSum += alphaKernelWeight * gate;
                }
            }

            float3 diffused = sum / max(weightSum, 1.0e-5);
            float diffusedMask = saturate(alphaSum / max(alphaWeightSum, 1.0e-5));
            return float4(diffused, diffusedMask);
        }

        float4 HoSSSBurleyDiskDiffusion(float2 uv)
        {
            float4 centerNormalDepth = HoSSSNormalDepth(uv);
            float4 centerSurfaceData = HoSSSSurfaceData(uv);
            float centerProfileByte = HoSSSProfileByte(centerSurfaceData);
            float4 profileDiffusionParams = HoSSSProfileDiffusionParams(centerProfileByte);
            float4 profileShapeParams = HoSSSProfileShapeParams(centerProfileByte);
            float3 diffusionColor = max(float3(profileDiffusionParams.zw, profileShapeParams.y), float3(0.08, 0.08, 0.08));
            float3 centerNormal = HoSSSDecodeNormal(centerNormalDepth.rgb);
            float centerDepth = centerNormalDepth.a;
            float centerMask = HoSSSSurfaceMask(uv, centerNormalDepth, centerSurfaceData);
            if (centerMask <= 1.0e-4)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float radiusPx = max(profileDiffusionParams.x * saturate(centerMask), 0.0) * max(_lilHoSSSParams.w, 1.0e-5);
            float4 centerSource = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            if (radiusPx <= 1.0e-4)
            {
                return centerSource;
            }

            float phase = HoSSSInterleavedNoise(uv) * (2.0 * LIL_HOSSS_PI);
            float2 texelRadius = _BlitTexture_TexelSize.xy * radiusPx;
            int sampleCount = clamp((int)round(_lilHoSSSParams.z), 1, 24);
            float3 centerWeight = HoSSSBurleyProfileWeight(0.0, 1.0, diffusionColor) * 0.35;
            float3 sum = centerSource.rgb * centerWeight;
            float3 weightSum = centerWeight;
            float alphaSum = centerSource.a;
            float alphaWeightSum = 1.0;

            [loop]
            for (int i = 0; i < 24; i++)
            {
                if (i >= sampleCount)
                {
                    break;
                }

                float radius01;
                float rcpPdf;
                HoSSSSampleBurleyDiffusionProfile(((float)i + 0.5) / (float)sampleCount, radius01, rcpPdf);
                float2 sampleOffset = HoSSSRotateOffset(radius01, i, phase) * texelRadius;
                float2 sampleUv = uv + sampleOffset;
                float gate = HoSSSDiffusionSampleGate(sampleUv, centerDepth, centerNormal, centerProfileByte);
                float3 weight = HoSSSBurleyProfileWeight(radius01, rcpPdf, diffusionColor) * gate;
                float4 sampleSource = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv);
                sum += sampleSource.rgb * weight;
                weightSum += weight;
                alphaSum += sampleSource.a * gate;
                alphaWeightSum += gate;
            }

            float3 diffused = sum / max(weightSum, 1.0e-5);
            float diffusedMask = saturate(alphaSum / max(alphaWeightSum, 1.0e-5));
            return float4(diffused, diffusedMask);
        }

        float2 HoSSSNormalizeDirection(float2 direction, float2 fallbackDirection)
        {
            float lengthSq = dot(direction, direction);
            return lengthSq > 1.0e-4 ? direction * rsqrt(lengthSq) : fallbackDirection;
        }

        float2 HoSSSPerpendicular(float2 direction)
        {
            return float2(-direction.y, direction.x);
        }

        float HoSSSRimFactor(float3 centerNormalView)
        {
            float viewFacing = saturate(abs(centerNormalView.z));
            return pow(saturate(1.0 - viewFacing), 1.75);
        }

        float2 HoSSSTransmissionDirection(float2 uv, float3 centerNormalView, float rimFactor)
        {
            float3 mainLightView = TransformWorldToViewDir(_MainLightPosition.xyz, true);
            float2 lightDirection = HoSSSNormalizeDirection(-mainLightView.xy, float2(1.0, 0.0));
            float2 viewExitDirection = HoSSSNormalizeDirection(-centerNormalView.xy, lightDirection);
            float2 tangentDirection = HoSSSPerpendicular(viewExitDirection);
            tangentDirection *= sign(dot(tangentDirection, lightDirection) + 1.0e-4);
            float lightBlend = saturate(_lilHoSSSTransmissionParams.w);
            float edgeBlend = saturate(rimFactor);
            float2 edgeDirection = HoSSSNormalizeDirection(lerp(lightDirection, tangentDirection, edgeBlend), lightDirection);
            float normalBlend = saturate((1.0 - lightBlend) * edgeBlend);
            float2 surfaceDirection = HoSSSNormalizeDirection(lerp(edgeDirection, viewExitDirection, normalBlend), edgeDirection);
            return HoSSSNormalizeDirection(lerp(surfaceDirection, lightDirection, lightBlend), lightDirection);
        }

        float HoSSSEdgeBoost(float rimFactor)
        {
            return 1.0 + rimFactor * rimFactor * max(_lilHoSSSTransmissionShapeParams.y, 0.0);
        }

        float3 HoSSSDirectionalTransmission(
            float2 uv,
            float4 centerNormalDepth,
            float centerMask,
            float centerProfileByte,
            float4 centerSurfaceData,
            float4 profileTransmissionParams,
            out float debugGate,
            out float2 debugDirection)
        {
            debugGate = 0.0;
            debugDirection = 0.0;
            float strength = saturate(profileTransmissionParams.x * HoSSSTransmissionStrengthMultiplier(centerSurfaceData));
            float radiusPx = max(profileTransmissionParams.y * HoSSSTransmissionRadiusMultiplier(centerSurfaceData), 0.0);
            if (strength <= 1.0e-4 || radiusPx <= 1.0e-4 || centerMask <= 1.0e-4)
            {
                return 0.0;
            }

            float3 centerNormal = HoSSSDecodeNormal(centerNormalDepth.rgb);
            float3 centerNormalView = TransformWorldToViewDir(centerNormal, true);
            float centerDepth = centerNormalDepth.a;
            float rimFactor = HoSSSRimFactor(centerNormalView);
            float2 direction = HoSSSTransmissionDirection(uv, centerNormalView, rimFactor);
            debugDirection = direction;
            float edgeBoost = HoSSSEdgeBoost(rimFactor);
            float2 stepUv = _BlitTexture_TexelSize.xy * direction * radiusPx * edgeBoost;
            float smoothing = saturate(_lilHoSSSTransmissionShapeParams.w);
            float2 crossUv = _BlitTexture_TexelSize.xy * HoSSSPerpendicular(direction) * radiusPx * (0.08 + 0.18 * smoothing);
            int sampleCount = clamp((int)round(_lilHoSSSTransmissionParams.z), 2, 32);
            float jitter = HoSSSInterleavedNoise(uv) - 0.5;

            float3 sum = 0.0;
            float weightSum = 0.0;
            float gateSum = 0.0;

            [loop]
            for (int i = 1; i <= 32; i++)
            {
                if (i > sampleCount)
                {
                    break;
                }

                float distance01 = saturate((i - 0.5 + jitter) / (float)sampleCount);
                float2 sampleUv = uv + stepUv * distance01;
                float4 sampleNormalDepth = HoSSSNormalDepthLinear(sampleUv);
                float gate = HoSSSTransmissionSampleWeight(sampleUv, centerDepth, centerNormal, centerProfileByte);
                float depthDelta = max(centerDepth - sampleNormalDepth.a, 0.0);
                float depthBoost = smoothstep(0.0, 1.0, saturate(depthDelta / max(_lilHoSSSGateParams.x, 1.0e-5)));
                float falloff = exp2(-distance01 * distance01 * 3.0);
                float rimWeight = lerp(1.0, rimFactor, saturate(_lilHoSSSTransmissionShapeParams.z));
                float3 sampleColor = HoSSSTransmissionSampleColor(sampleUv);
                if (smoothing > 1.0e-4)
                {
                    float sideDistance = lerp(0.35, 1.0, distance01);
                    float2 sideOffset = crossUv * sideDistance;
                    float2 sideUvA = sampleUv + sideOffset;
                    float2 sideUvB = sampleUv - sideOffset;
                    float sideGateA = HoSSSTransmissionSampleWeight(sideUvA, centerDepth, centerNormal, centerProfileByte);
                    float sideGateB = HoSSSTransmissionSampleWeight(sideUvB, centerDepth, centerNormal, centerProfileByte);
                    float sideWeight = 0.5 * smoothing;
                    float crossWeight = 1.0 + sideWeight * (sideGateA + sideGateB);
                    sampleColor = (
                        sampleColor +
                        HoSSSTransmissionSampleColor(sideUvA) * sideGateA * sideWeight +
                        HoSSSTransmissionSampleColor(sideUvB) * sideGateB * sideWeight) / max(crossWeight, 1.0e-5);
                    gate = lerp(gate, max(gate, 0.5 * (sideGateA + sideGateB)), smoothing * 0.35);
                }

                float weight = gate * falloff * rimWeight * lerp(1.0, depthBoost, saturate(_lilHoSSSTransmissionShapeParams.x));
                sum += sampleColor * weight;
                weightSum += weight;
                gateSum += gate * falloff;
            }

            debugGate = saturate(gateSum / max((float)sampleCount, 1.0));
            return sum / max(weightSum, 1.0e-5) * strength;
        }

        float4 HoSSSTransmissionBlurSample(
            float2 uv,
            float centerDepth,
            float3 centerNormal,
            float centerProfileByte)
        {
            float4 sampleTransmission = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
            float4 sampleNormalDepth = HoSSSNormalDepthLinear(uv);
            float4 sampleSurfaceData = HoSSSSurfaceDataLinear(uv);
            float sampleMask = HoSSSSurfaceMask(uv, sampleNormalDepth, sampleSurfaceData);
            float sampleProfileByte = HoSSSProfileByte(sampleSurfaceData);
            float3 sampleNormal = HoSSSDecodeNormal(sampleNormalDepth.rgb);
            float gate = sampleMask;
            gate *= HoSSSProfileGate(sampleProfileByte, centerProfileByte);
            gate *= HoSSSDepthGate(sampleNormalDepth.a, centerDepth);
            gate *= HoSSSNormalGate(sampleNormal, centerNormal);
            return sampleTransmission * gate;
        }

        float4 HoSSSBlurTransmission(float2 uv)
        {
            float4 centerNormalDepth = HoSSSNormalDepth(uv);
            float4 centerSurfaceData = HoSSSSurfaceData(uv);
            float centerProfileByte = HoSSSProfileByte(centerSurfaceData);
            float3 centerNormal = HoSSSDecodeNormal(centerNormalDepth.rgb);
            float centerDepth = centerNormalDepth.a;
            float centerMask = HoSSSSurfaceMask(uv, centerNormalDepth, centerSurfaceData);
            if (centerMask <= 1.0e-4)
            {
                return 0.0;
            }

            float blurRadius = saturate(_lilHoSSSTransmissionShapeParams.w) * max(_lilHoSSSTransmissionParams.y, 1.0) * 0.35;
            float2 stepUv = _BlitTexture_TexelSize.xy * _lilHoSSSDirection.xy * blurRadius;
            float4 centerTransmission = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
            float4 sum = centerTransmission * 2.0;
            float weightSum = 2.0;

            [unroll]
            for (int i = 1; i <= 4; i++)
            {
                float distance01 = i / 4.0;
                float kernel = exp2(-distance01 * distance01 * 2.25);
                float2 sampleOffset = stepUv * distance01;
                float4 sampleA = HoSSSTransmissionBlurSample(uv + sampleOffset, centerDepth, centerNormal, centerProfileByte);
                float4 sampleB = HoSSSTransmissionBlurSample(uv - sampleOffset, centerDepth, centerNormal, centerProfileByte);
                sum += (sampleA + sampleB) * kernel;
                weightSum += (sampleA.a + sampleB.a) * kernel;
            }

            return sum / max(weightSum, 1.0e-5);
        }

        float3 HoSSSBlendTransmission(float3 cameraColor, float3 baseTarget, float3 transmission)
        {
            float transmissionMask = saturate(max(max(transmission.r, transmission.g), transmission.b));
            float tintInjection = saturate(_lilHoSSSCompositeParams.y);
            float blendMode = _lilHoSSSCompositeParams.x;
            float luma = dot(cameraColor, float3(0.2126, 0.7152, 0.0722));
            float3 chroma = transmission - dot(transmission, float3(0.333333, 0.333333, 0.333333));
            float3 softTint = cameraColor + chroma * tintInjection * (0.35 + 0.65 * luma);
            softTint += transmission * (0.18 + 0.35 * (1.0 - luma));
            float3 additive = cameraColor + transmission;
            float3 screen = 1.0 - (1.0 - saturate(cameraColor)) * (1.0 - saturate(transmission));
            float3 colorInject = lerp(cameraColor, cameraColor * (0.72 + transmission * 1.35) + transmission * 0.35, saturate(tintInjection + transmissionMask));
            float3 legacyMax = max(baseTarget + transmission, cameraColor);
            float3 result = softTint;

            result = blendMode < 0.5 ? softTint : result;
            result = blendMode >= 0.5 && blendMode < 1.5 ? additive : result;
            result = blendMode >= 1.5 && blendMode < 2.5 ? screen : result;
            result = blendMode >= 2.5 && blendMode < 3.5 ? colorInject : result;
            result = blendMode >= 3.5 ? legacyMax : result;
            return max(lerp(baseTarget, result, saturate(transmissionMask * (0.5 + tintInjection * 1.5))), 0.0);
        }

        ENDHLSL

        Pass
        {
            Name "Source"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 normalDepth = HoSSSNormalDepth(uv);
                float mask = HoSSSSurfaceMask(uv, normalDepth);
                float active = step(1.0e-4, mask);
                float4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 sssSource = SAMPLE_TEXTURE2D_X(_lilHoAovSssTexture, sampler_LinearClamp, uv);
                float sourceWeight = saturate(sssSource.a);
                float3 sourceColor = lerp(cameraColor.rgb, sssSource.rgb, sourceWeight);

                return float4(sourceColor * active, mask);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Diffusion"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return abs(_lilHoSSSDirection.y) > 0.5
                    ? HoSSSBurleyDiskDiffusion(input.texcoord)
                    : HoSSSSeparableDiffusion(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Transmission Gather"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 normalDepth = HoSSSNormalDepth(uv);
                float4 surfaceData = HoSSSSurfaceData(uv);
                float profileByte = HoSSSProfileByte(surfaceData);
                float4 profileTransmissionParams = HoSSSProfileTransmissionParams(profileByte);
                float centerMask = HoSSSSurfaceMask(uv, normalDepth, surfaceData);
                float transmissionGate;
                float2 transmissionDirection;
                float3 transmission = HoSSSDirectionalTransmission(
                    uv,
                    normalDepth,
                    centerMask,
                    profileByte,
                    surfaceData,
                    profileTransmissionParams,
                    transmissionGate,
                    transmissionDirection);
                return float4(transmission, transmissionGate);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Transmission Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return HoSSSBlurTransmission(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 sss = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
                float4 normalDepth = HoSSSNormalDepth(uv);
                float4 surfaceData = HoSSSSurfaceData(uv);
                float profileByte = HoSSSProfileByte(surfaceData);
                float4 profileDiffusionParams = HoSSSProfileDiffusionParams(profileByte);
                float4 profileTransmissionParams = HoSSSProfileTransmissionParams(profileByte);
                float4 profileShapeParams = HoSSSProfileShapeParams(profileByte);
                float centerMask = HoSSSSurfaceMask(uv, normalDepth, surfaceData);
                float mask = saturate(centerMask * step(1.0e-4, sss.a) * _lilHoSSSParams.x);
                float3 diffusionColor = max(float3(profileDiffusionParams.zw, profileShapeParams.y), 0.0);
                float3 transmissionColor = max(float3(profileTransmissionParams.zw, profileShapeParams.z), 0.0);
                float3 tintedDiffusion = sss.rgb * diffusionColor;
                float4 transmissionSample = SAMPLE_TEXTURE2D_X(_lilHoSSSTransmissionTexture, sampler_LinearClamp, uv);
                float3 transmission = transmissionSample.rgb * transmissionColor * 0.45;
                float compositeWeight = mask * saturate(profileShapeParams.w) * (1.0 - saturate(profileDiffusionParams.y));
                float3 diffusionTarget = max(tintedDiffusion, cameraColor.rgb);
                float3 targetColor = HoSSSBlendTransmission(cameraColor.rgb, diffusionTarget, transmission);

                cameraColor.rgb = lerp(cameraColor.rgb, targetColor, compositeWeight);
                return cameraColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
