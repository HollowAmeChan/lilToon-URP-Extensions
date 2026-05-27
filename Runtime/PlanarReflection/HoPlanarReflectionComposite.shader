Shader "Hidden/lilToon/URP/PlanarReflection/Composite"
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
            Name "Planar Reflection Composite"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"

            float _HoMetadataBufferActive;
            float _HoPlanarReflectionCompositeActive;
            float4 _HoPlanarReflectionCompositeParams;
            float4 _HoPlanarReflectionCompositeOptions;
            float4 _HoPlanarReflectionCompositeTint;
            float4 _HoPlanarReflectionDebugParams;
            float4 _HoPlanarReflectionDebugInputStatus;
            float4 _LILPBRPlanarReflectionParams;
            float4 _LILPBRPlanarReflectionTexture_TexelSize;

            TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
            TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture);
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D(_LILPBRPlanarReflectionTexture);
            SAMPLER(sampler_LILPBRPlanarReflectionTexture);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 cameraColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                int debugMode = (int)round(_HoPlanarReflectionDebugParams.x);
                if (debugMode == 1)
                {
                    return half4(
                        saturate(_HoPlanarReflectionCompositeActive * _LILPBRPlanarReflectionParams.x * _HoPlanarReflectionDebugInputStatus.x),
                        saturate(_HoMetadataBufferActive * _HoPlanarReflectionDebugInputStatus.y * _HoPlanarReflectionDebugInputStatus.w),
                        saturate(_HoPlanarReflectionDebugInputStatus.z),
                        1.0h);
                }

                if (_HoPlanarReflectionCompositeActive < 0.5 ||
                    _HoMetadataBufferActive < 0.5 ||
                    _LILPBRPlanarReflectionParams.x < 0.5)
                {
                    if (debugMode > 0)
                    {
                        return half4(1.0h, 0.0h, 1.0h, 1.0h);
                    }

                    return cameraColor;
                }

                half4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                half4 custom0 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture, sampler_PointClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);

                half waterMask = saturate(maskId.r) * LilHoGeometryBufferCoverage(normalDepth);
                half smoothness = saturate(custom0.r);
                half wetness = saturate(custom0.g);
                half normalStrength = saturate(custom0.b);
                half materialReflectionStrength = saturate(custom0.a);

                half minSmoothness = saturate(_HoPlanarReflectionCompositeParams.z);
                half smoothnessFade = saturate((smoothness - minSmoothness) / max(1.0h - minSmoothness, 0.0001h));
                half centerWeight = waterMask * wetness * materialReflectionStrength * smoothnessFade;

                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);
                if (debugMode == 2) return half4(waterMask, waterMask, waterMask, 1.0h);
                if (debugMode == 3) return half4(smoothness, smoothness, smoothness, 1.0h);
                if (debugMode == 4) return half4(wetness, wetness, wetness, 1.0h);
                if (debugMode == 5) return half4(normalStrength, normalStrength, normalStrength, 1.0h);
                if (debugMode == 6) return half4(materialReflectionStrength, materialReflectionStrength, materialReflectionStrength, 1.0h);
                if (debugMode == 7) return half4(normalWS * 0.5 + 0.5, 1.0h);
                if (debugMode == 8)
                {
                    half depthDebug = saturate(normalDepth.a / max(_HoPlanarReflectionDebugParams.y, 0.0001));
                    return half4(depthDebug, depthDebug, depthDebug, 1.0h);
                }
                if (debugMode == 14) return custom0;

                if (centerWeight <= 0.0001h)
                {
                    if (debugMode > 0)
                    {
                        return half4(0.0h, 0.0h, 0.0h, 1.0h);
                    }

                    return cameraColor;
                }

                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float2 distortion = normalVS.xy * _HoPlanarReflectionCompositeParams.y * normalStrength * wetness;
                float2 distortedScreenUv = uv + distortion;
                if (debugMode == 9)
                {
                    return half4(saturate(distortion * _HoPlanarReflectionDebugParams.z + 0.5).xy, 0.0h, 1.0h);
                }

                if (debugMode == 10)
                {
                    return half4(saturate(distortedScreenUv).xy, 0.0h, 1.0h);
                }

                float2 reflectionTexel = max(abs(_LILPBRPlanarReflectionTexture_TexelSize.xy) * 0.5, float2(1.0e-5, 1.0e-5));
                float edgeExtendDistance = max(_HoPlanarReflectionCompositeOptions.z, 0.0);
                float2 edgeInset = max(reflectionTexel, float2(edgeExtendDistance, edgeExtendDistance));
                float2 extendedScreenUv = clamp(distortedScreenUv, edgeInset, 1.0 - edgeInset);
                float2 overflow = abs(distortedScreenUv - extendedScreenUv);
                float edgeExtend = max(overflow.x, overflow.y);

                half depthGate = 1.0h;
                float depthTolerance = _HoPlanarReflectionCompositeParams.w;
                if (_HoPlanarReflectionCompositeOptions.y > 0.5 && depthTolerance > 0.0001)
                {
                    half4 distortedNormalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, extendedScreenUv);
                    depthGate = saturate(1.0h - (half)(abs((float)distortedNormalDepth.a - (float)normalDepth.a) / depthTolerance));
                }

                if (debugMode == 13)
                {
                    return half4(depthGate, depthGate, depthGate, 1.0h);
                }

                float2 reflectionUv = extendedScreenUv;
                if (_HoPlanarReflectionCompositeOptions.x > 0.5)
                {
                    reflectionUv.y = 1.0 - reflectionUv.y;
                }

                half3 reflection = SAMPLE_TEXTURE2D(_LILPBRPlanarReflectionTexture, sampler_LILPBRPlanarReflectionTexture, reflectionUv).rgb;
                reflection *= _HoPlanarReflectionCompositeTint.rgb;

                half compositeWeight = saturate(centerWeight * depthGate * _HoPlanarReflectionCompositeParams.x * _HoPlanarReflectionCompositeTint.a);
                if (debugMode == 11) return half4(reflection, 1.0h);
                if (debugMode == 12) return half4(compositeWeight, compositeWeight, compositeWeight, 1.0h);
                if (debugMode == 15)
                {
                    half edgeExtendDebug = saturate(edgeExtend / max(edgeExtendDistance, max(reflectionTexel.x, reflectionTexel.y)));
                    return half4(edgeExtendDebug, edgeExtendDebug, edgeExtendDebug, 1.0h);
                }

                cameraColor.rgb = lerp(cameraColor.rgb, reflection, compositeWeight);
                return cameraColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
