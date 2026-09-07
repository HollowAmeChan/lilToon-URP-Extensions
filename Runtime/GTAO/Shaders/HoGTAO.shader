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
        float4x4 _HoGTAOInvProjMatrix;
        float4x4 _HoGTAOProjMatrix;
        float4x4 _HoGTAOViewMatrix;
        float _HoGTAODebugMode;
        float _HoGTAOHistoryBlend;
        float _HoGTAOWorldSpaceRadius;
        float _HoGTAOScreenSpaceRadius;
        float _HoGTAOThickness;
        float _HoGTAOUseAttenuation;
        float _HoGTAOUseLinearThickness;
        float _HoGTAOSliceCount;
        float _HoGTAOStepCount;
        float _HoGTAOFrameIndex;

        static const float HoGTAOSliceRotations[6] = { 60.0, 300.0, 180.0, 240.0, 120.0, 0.0 };

        float3 HoGTAOViewPosition(float2 uv, float linearDepth)
        {
            float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
            return ComputeViewSpacePosition(uv, deviceDepth, _HoGTAOInvProjMatrix) * float3(1.0, -1.0, -1.0);
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

        void HoGTAOUpdateBitmask(inout uint bitmask, float2 horizonSamples)
        {
            uint2 horizonInt = uint2(round(saturate(horizonSamples) * 32.0));
            uint horizonMin = horizonInt.x < 32u ? 0xFFFFFFFFu << horizonInt.x : 0u;
            uint horizonMax = horizonInt.y != 0u ? 0xFFFFFFFFu >> (32u - horizonInt.y) : 0u;
            bitmask |= horizonMin & horizonMax;
        }

        bool HoGTAOSample(float2 uv, out float3 positionVS, out float3 normalWS)
        {
            half4 nd = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, saturate(uv));
            if (nd.a < 0.0001h)
            {
                positionVS = 0.0;
                normalWS = 0.0;
                return false;
            }

            positionVS = HoGTAOViewPosition(saturate(uv), nd.a);
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
            float noiseX = frac(52.9829189 * frac(dot(uv * _ScreenParams.xy, float2(0.06711056, 0.00583715))));
            float noiseY = frac(52.9829189 * frac(dot((_ScreenParams.xy - uv * _ScreenParams.xy).yx, float2(0.06711056, 0.00583715))) * 0.5 + 0.25);
            float thickness = _HoGTAOUseLinearThickness > 0.5
                ? max(_HoGTAOThickness * 0.1 * linearDepth, _HoGTAOThickness)
                : _HoGTAOThickness;

            float visibility = 0.0;
            float weightTotal = 0.0;
            int slices = max(1, (int)_HoGTAOSliceCount);
            int steps = max(1, (int)_HoGTAOStepCount);
            int rotationIndex = ((int)_HoGTAOFrameIndex) % 6;

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
                    float2 offset = round(stride * samplingDirection) * _ScreenParams.zw;
                    float3 samplePosition;
                    float3 sampleNormal;
                    if (HoGTAOSample(uv - offset, samplePosition, sampleNormal))
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

                    if (HoGTAOSample(uv + offset, samplePosition, sampleNormal))
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

            return saturate(visibility / max(weightTotal, 1.0e-5));
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
            if (_HoGTAODebugMode > 2.5)
            {
                return half4(nd.rgb, 1.0h);
            }
            if (nd.a < 0.0001h)
            {
                return half4(1.0h, 1.0h, 1.0h, 1.0h);
            }
            half ao = HoGTAOCompute(input.texcoord, nd);
            return half4(ao, ao, ao, 1.0h);
        }

        half4 Temporal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half current = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            half previous = SAMPLE_TEXTURE2D_X(_HoGTAOHistoryPrevTex, sampler_PointClamp, input.texcoord).r;
            half ao = lerp(current, previous, saturate(_HoGTAOHistoryBlend));
            return half4(ao, ao, ao, 1.0h);
        }

        half4 OutputAO(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half ao = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            return half4(ao, ao, ao, 1.0h);
        }

        half4 DebugOutput(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }
        ENDHLSL

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
            Name "Ho-GTAO Debug Output"
            Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DebugOutput
            ENDHLSL
        }
    }
}
