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
            float4 _LILPBRPlanarReflectionParams;

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
                if (_HoPlanarReflectionCompositeActive < 0.5 ||
                    _HoMetadataBufferActive < 0.5 ||
                    _LILPBRPlanarReflectionParams.x < 0.5)
                {
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
                if (centerWeight <= 0.0001h)
                {
                    return cameraColor;
                }

                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float2 distortion = normalVS.xy * _HoPlanarReflectionCompositeParams.y * normalStrength * wetness;
                float2 distortedScreenUv = uv + distortion;
                if (any(distortedScreenUv < 0.0) || any(distortedScreenUv > 1.0))
                {
                    return cameraColor;
                }

                half depthGate = 1.0h;
                float depthTolerance = _HoPlanarReflectionCompositeParams.w;
                if (_HoPlanarReflectionCompositeOptions.y > 0.5 && depthTolerance > 0.0001)
                {
                    half4 distortedNormalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, distortedScreenUv);
                    depthGate = saturate(1.0h - (half)(abs((float)distortedNormalDepth.a - (float)normalDepth.a) / depthTolerance));
                }

                float2 reflectionUv = distortedScreenUv;
                if (_HoPlanarReflectionCompositeOptions.x > 0.5)
                {
                    reflectionUv.y = 1.0 - reflectionUv.y;
                }

                half3 reflection = SAMPLE_TEXTURE2D(_LILPBRPlanarReflectionTexture, sampler_LILPBRPlanarReflectionTexture, reflectionUv).rgb;
                reflection *= _HoPlanarReflectionCompositeTint.rgb;

                half compositeWeight = saturate(centerWeight * depthGate * _HoPlanarReflectionCompositeParams.x * _HoPlanarReflectionCompositeTint.a);
                cameraColor.rgb = lerp(cameraColor.rgb, reflection, compositeWeight);
                return cameraColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
