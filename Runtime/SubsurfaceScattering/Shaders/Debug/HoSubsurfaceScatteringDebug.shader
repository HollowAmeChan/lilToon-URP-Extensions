Shader "Hidden/lilToon/URP/HoSubsurfaceScattering/DebugView"
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

        Pass
        {
            Name "HoSSS Debug"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture);
            TEXTURE2D_X(_lilHoSSSSourceTexture);
            TEXTURE2D_X(_lilHoSSSTransmissionTexture);

            float _HoMetadataBufferActive;
            float4 _lilHoSSSParams;
            float4 _lilHoSSSGateParams;
            float4 _lilHoSSSColor;
            float4 _lilHoSSSTransmissionParams;
            float4 _lilHoSSSTransmissionColor;
            float4 _lilHoSSSTransmissionShapeParams;
            float4 _lilHoSSSDebugParams;
            float4 _lilHoSSSProfileIds[8];
            float4 _lilHoSSSProfileDiffusionParams[8];
            float4 _lilHoSSSProfileTransmissionParams[8];
            float4 _lilHoSSSProfileShapeParams[8];

            float HoSSSCoverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv).r;
            }

            float4 HoSSSNormalDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
            }

            float4 HoSSSSurfaceData(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture, sampler_PointClamp, uv);
            }

            float3 HoSSSDecodeNormal(float3 encodedNormal)
            {
                return normalize(encodedNormal * 2.0 - 1.0);
            }

            float HoSSSProfileByte(float4 surfaceData)
            {
                return round(saturate(surfaceData.b) * 255.0);
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
                return step(0.5, _HoMetadataBufferActive) * saturate(HoSSSCoverage(uv) * HoSSSThinness(surfaceData)) * HoSSSGeometryValid(normalDepth);
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

            float2 HoSSSTransmissionDirection(float3 centerNormalView, float rimFactor)
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

            float3 HoSSSDebugColor(
                float debugMode,
                float centerMask,
                float compositeWeight,
                float profileByte,
                float thickness,
                float profileRadius,
                float2 transmissionDirection,
                float4 source,
                float3 tintedDiffusion,
                float3 transmission,
                float transmissionGate,
                float transmissionRim)
            {
                if (debugMode < 1.5)
                {
                    return centerMask.xxx;
                }

                if (debugMode < 2.5)
                {
                    return source.rgb;
                }

                if (debugMode < 3.5)
                {
                    return tintedDiffusion;
                }

                if (debugMode < 4.5)
                {
                    return transmission;
                }

                if (debugMode < 5.5)
                {
                    return transmissionGate.xxx;
                }

                if (debugMode < 6.5)
                {
                    return compositeWeight.xxx;
                }

                if (debugMode < 7.5)
                {
                    return (profileByte / 255.0).xxx;
                }

                if (debugMode < 8.5)
                {
                    return thickness.xxx;
                }

                if (debugMode < 9.5)
                {
                    return saturate(profileRadius / 32.0).xxx;
                }

                if (debugMode < 10.5)
                {
                    return float3(transmissionDirection * 0.5 + 0.5, 0.0);
                }

                return transmissionRim.xxx;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 metadataSource = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_PointClamp, uv);
                float4 sss = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
                float4 normalDepth = HoSSSNormalDepth(uv);
                float4 surfaceData = HoSSSSurfaceData(uv);
                float profileByte = HoSSSProfileByte(surfaceData);
                float4 profileDiffusionParams = HoSSSProfileDiffusionParams(profileByte);
                float4 profileTransmissionParams = HoSSSProfileTransmissionParams(profileByte);
                float4 profileShapeParams = HoSSSProfileShapeParams(profileByte);
                float thickness = HoSSSThinness(surfaceData);
                float3 centerNormal = HoSSSDecodeNormal(normalDepth.rgb);
                float3 centerNormalView = TransformWorldToViewDir(centerNormal, true);
                float transmissionRim = HoSSSRimFactor(centerNormalView);
                float2 transmissionDirection = HoSSSTransmissionDirection(centerNormalView, transmissionRim);
                float centerMask = HoSSSSurfaceMask(uv, normalDepth, surfaceData);
                float mask = saturate(centerMask * step(1.0e-4, sss.a) * _lilHoSSSParams.x);
                float3 diffusionColor = max(float3(profileDiffusionParams.zw, profileShapeParams.y), 0.0);
                float3 transmissionColor = max(float3(profileTransmissionParams.zw, profileShapeParams.z), 0.0);
                float3 tintedDiffusion = sss.rgb * diffusionColor;
                float4 transmissionSample = SAMPLE_TEXTURE2D_X(_lilHoSSSTransmissionTexture, sampler_LinearClamp, uv);
                float3 transmission = transmissionSample.rgb * transmissionColor * 0.45;
                float compositeWeight = mask * saturate(profileShapeParams.w) * (1.0 - saturate(profileDiffusionParams.y));
                float debugMode = max(_lilHoSSSDebugParams.x, 1.0);
                float3 debugColor = HoSSSDebugColor(
                    debugMode,
                    centerMask,
                    compositeWeight,
                    profileByte,
                    thickness,
                    profileDiffusionParams.x,
                    transmissionDirection,
                    metadataSource,
                    tintedDiffusion,
                    transmission,
                    transmissionSample.a,
                    transmissionRim);

                return float4(debugColor, cameraColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
