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
        TEXTURE2D_X(_lilHoAovNormalDepthTexture);
        TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
        TEXTURE2D_X(_lilHoSSSSourceTexture);

        float _lilHoAovActive;

        float4 _lilHoSSSParams;     // x strength, y radius screen px, z source preserve, w RT scale compensation
        float4 _lilHoSSSGateParams; // x depth tolerance, y normal tolerance
        float4 _lilHoSSSColor;
        float4 _lilHoSSSDirection;

        float HoSSSCoverage(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv).r;
        }

        float4 HoSSSNormalDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_PointClamp, uv);
        }

        float3 HoSSSDecodeNormal(float3 encodedNormal)
        {
            return normalize(encodedNormal * 2.0 - 1.0);
        }

        float HoSSSThinness(float2 uv)
        {
            return saturate(SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_PointClamp, uv).r);
        }

        float HoSSSGeometryValid(float4 normalDepth)
        {
            float normalValid = step(1.0e-4, dot(normalDepth.rgb, normalDepth.rgb));
            float depthValid = step(1.0e-4, normalDepth.a);
            return normalValid * depthValid;
        }

        float HoSSSSurfaceMask(float2 uv, float4 normalDepth)
        {
            return step(0.5, _lilHoAovActive) * saturate(HoSSSCoverage(uv) * HoSSSThinness(uv)) * HoSSSGeometryValid(normalDepth);
        }

        float HoSSSDepthGate(float sampleDepth, float centerDepth)
        {
            float tolerance = max(_lilHoSSSGateParams.x, 1.0e-5);
            return saturate(1.0 - abs(sampleDepth - centerDepth) / tolerance);
        }

        float HoSSSNormalGate(float3 sampleNormal, float3 centerNormal)
        {
            float tolerance = max(_lilHoSSSGateParams.y, 1.0e-5);
            return saturate((dot(sampleNormal, centerNormal) - (1.0 - tolerance)) / tolerance);
        }

        float3 HoSSSProfileWeight(float distance01)
        {
            float3 profileRadius = float3(1.0, 0.62, 0.32);
            float3 d = distance01.xxx / profileRadius;
            return exp2(-d * d * 2.0);
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

                return float4(cameraColor.rgb * active, mask);
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

                float2 uv = input.texcoord;
                float4 centerNormalDepth = HoSSSNormalDepth(uv);
                float3 centerNormal = HoSSSDecodeNormal(centerNormalDepth.rgb);
                float centerDepth = centerNormalDepth.a;
                float centerMask = HoSSSSurfaceMask(uv, centerNormalDepth);
                if (centerMask <= 1.0e-4)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float centerThinness = saturate(centerMask);
                float radiusPx = max(_lilHoSSSParams.y * centerThinness, 0.0);
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
                        float4 sampleNormalDepth = HoSSSNormalDepth(sampleUv);
                        float3 sampleNormal = HoSSSDecodeNormal(sampleNormalDepth.rgb);
                        float sampleMask = HoSSSSurfaceMask(sampleUv, sampleNormalDepth);
                        float gate = sampleMask;
                        gate *= HoSSSDepthGate(sampleNormalDepth.a, centerDepth);
                        gate *= HoSSSNormalGate(sampleNormal, centerNormal);

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
                float centerMask = HoSSSSurfaceMask(uv, normalDepth);
                float mask = saturate(centerMask * step(1.0e-4, sss.a) * _lilHoSSSParams.x);
                float3 tintedDiffusion = sss.rgb * max(_lilHoSSSColor.rgb, 0.0);
                float3 targetColor = max(tintedDiffusion, cameraColor.rgb);

                cameraColor.rgb = lerp(cameraColor.rgb, targetColor, mask * saturate(_lilHoSSSColor.a) * (1.0 - saturate(_lilHoSSSParams.z)));
                return cameraColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
