// External-system diagnostics only. Feature-owned buffer/effect views live in their own shaders.
Shader "Hidden/lilToon/URP/Debug/DebugTile"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "APV Debug"
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
            #endif
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            int _HoDebugTileMode;
            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
            float HoAPVLuminance(float3 color)
            {
                return dot(color, float3(0.22, 0.707, 0.071));
            }

            half3 HoAPVTonemap(float3 color)
            {
                color = max(color, 0.0);
                return saturate(color / (1.0 + color));
            }

            float3 HoAPVFixedLightDirection(APVSample apvSample)
            {
                float4 SHAr = unity_SHAr;
                float4 SHAg = unity_SHAg;
                float4 SHAb = unity_SHAb;
                if (apvSample.status != APV_SAMPLE_STATUS_INVALID)
                {
                    apvSample.Decode();
                    SHAr = float4(apvSample.L1_R, apvSample.L0.r);
                    SHAg = float4(apvSample.L1_G, apvSample.L0.g);
                    SHAb = float4(apvSample.L1_B, apvSample.L0.b);
                }

                float3 mainDirection = _MainLightPosition.xyz * HoAPVLuminance(_MainLightColor.rgb);
                float3 shDirection = (SHAr.xyz + SHAg.xyz + SHAb.xyz) * 0.333333;
                return float3(shDirection.x, abs(shDirection.y), shDirection.z)
                    + mainDirection
                    + float3(0.0, 0.001, 0.0);
            }

            float3 HoAPVToonIndirect(APVSample apvSample, float3 lightDirection)
            {
                float4 SHAr = unity_SHAr;
                float4 SHAg = unity_SHAg;
                float4 SHAb = unity_SHAb;
                float4 SHBr = unity_SHBr;
                float4 SHBg = unity_SHBg;
                float4 SHBb = unity_SHBb;
                float3 SHC = unity_SHC.rgb;

                if (apvSample.status != APV_SAMPLE_STATUS_INVALID)
                {
                    apvSample.Decode();
                    SHAr = float4(apvSample.L1_R, apvSample.L0.r);
                    SHAg = float4(apvSample.L1_G, apvSample.L0.g);
                    SHAb = float4(apvSample.L1_B, apvSample.L0.b);

                    #if defined(PROBE_VOLUMES_L2)
                    SHBr = apvSample.L2_R;
                    SHBg = apvSample.L2_G;
                    SHBb = apvSample.L2_B;
                    SHC = apvSample.L2_C;
                    #endif

                    SHBr *= _APVWeight;
                    SHBg *= _APVWeight;
                    SHBb *= _APVWeight;
                    SHC *= _APVWeight;
                }

                float3 N = lightDirection * 0.666666;
                float4 vB = N.xyzz * N.yzzx;
                float3 result = float3(SHAr.w, SHAg.w, SHAb.w);
                result.r += dot(SHBr, vB);
                result.g += dot(SHBg, vB);
                result.b += dot(SHBb, vB);
                result += SHC * (N.x * N.x - N.y * N.y);

                float3 l1 = float3(
                    dot(SHAr.xyz, N),
                    dot(SHAg.xyz, N),
                    dot(SHAb.xyz, N));
                return saturate(result - l1);
            }
            #endif

            half4 ResolveAdaptiveProbeVolumeColor(float2 uv)
            {
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                if (LilHoGeometryBufferCoverage(normalDepth) < 0.5h)
                {
                    return half4(1.0h, 0.0h, 1.0h, 1.0h);
                }

                #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                float linearDepth = max((float)normalDepth.a, 1.0e-4);
                float zParam = abs(_ZBufferParams.z) > 1.0e-6 ? _ZBufferParams.z : 1.0e-6;
                float deviceDepth = saturate((rcp(linearDepth) - _ZBufferParams.w) / zParam);
                float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 absolutePositionWS = GetAbsolutePositionWS(positionWS);
                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);
                float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                APVSample apvSample = SampleAPV(absolutePositionWS, normalWS, 0xFFFFFFFFu, viewDirectionWS);

                if (_HoDebugTileMode == 1)
                {
                    return apvSample.status != APV_SAMPLE_STATUS_INVALID
                        ? half4(0.10h, 1.0h, 0.15h, 1.0h)
                        : half4(1.0h, 0.08h, 0.05h, 1.0h);
                }

                if (apvSample.status == APV_SAMPLE_STATUS_INVALID)
                {
                    return half4(1.0h, 0.08h, 0.05h, 1.0h);
                }

                if (_HoDebugTileMode == 2)
                {
                    float3 irradiance;
                    EvaluateAdaptiveProbeVolume(apvSample, normalWS, irradiance);
                    return half4(HoAPVTonemap(irradiance), 1.0h);
                }

                APVSample directionSample = SampleAPV(absolutePositionWS, 0.0, 0xFFFFFFFFu, viewDirectionWS);
                float3 lightDirection = HoAPVFixedLightDirection(directionSample);
                return half4(HoAPVToonIndirect(apvSample, lightDirection), 1.0h);
                #else
                return half4(1.0h, 0.0h, 1.0h, 1.0h);
                #endif
            }


            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return ResolveAdaptiveProbeVolumeColor(input.texcoord);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
