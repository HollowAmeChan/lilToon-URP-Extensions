Shader "Hidden/lilToon/URP/Debug/DebugTile"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Debug Tile"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferPalette.hlsl"

            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
            #endif

            int _HoDebugTileRenderKind;
            int _HoDebugTileMode;
            float4 _HoDebugTileRect;
            float4 _HoDebugTileGrid;
            float4 _HoDebugTileLabel0;
            float4 _HoDebugTileLabel1;
            float4 _HoDebugTileLabel2;
            float4 _HoDebugTileLabel3;
            float4 _HoDebugTileGeometryDepthParams;

            TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture);
            TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture);
            TEXTURE2D_X_FLOAT(_HoMetadataBufferMBufferDepthTexture);
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture);
            TEXTURE2D_FLOAT(_HoShadowCastAtlas);
            TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas);
            TEXTURE2D_X(_lilHoSSSSourceTexture);
            TEXTURE2D_X(_lilHoSSSTransmissionTexture);
            TEXTURE2D(_LILPBRPlanarReflectionTexture);
            SAMPLER(sampler_LILPBRPlanarReflectionTexture);
            TEXTURE2D_X(_HoObjectBufferId0Texture);
            TEXTURE2D_X(_HoObjectBufferId1Texture);
            TEXTURE2D_X(_HoObjectBufferCoverageTexture);
            TEXTURE2D_X(_HoObjectBufferSelectionTexture);

            float _HoShadowCastActive;
            int _HoShadowCastSliceCount;
            float4 _HoShadowCastAtlasSize;
            float4 _HoShadowCastSliceData[HO_SHADOW_CAST_ARRAY_SLICES];
            float4 _HoShadowCastSecondDirectionalParams;
            float4 _HoShadowCastSecondDirectionalAtlasSize;
            float4 _HoShadowCastSecondDirectionalLightData[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS];
            float4 _HoShadowCastSecondDirectionalSliceData[HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES];
            float _HoMetadataBufferActive;
            float _HoPlanarReflectionCompositeActive;
            float4 _HoPlanarReflectionCompositeParams;
            float4 _HoPlanarReflectionCompositeOptions;
            float4 _HoPlanarReflectionCompositeTint;
            float4 _HoPlanarReflectionDebugParams;
            float4 _HoPlanarReflectionDebugInputStatus;
            float4 _LILPBRPlanarReflectionParams;
            float4 _LILPBRPlanarReflectionTexture_TexelSize;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 quadUv[6] =
                {
                    float2(0.0, 0.0),
                    float2(0.0, 1.0),
                    float2(1.0, 1.0),
                    float2(0.0, 0.0),
                    float2(1.0, 1.0),
                    float2(1.0, 0.0)
                };

                float2 uv = quadUv[input.vertexID];
                float2 position = _HoDebugTileRect.xy + uv * _HoDebugTileRect.zw;
                output.positionCS = float4(position * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
                output.uv = uv;
                return output;
            }

            half3 HashColor(float3 value)
            {
                half r = frac(sin(dot(value, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                half g = frac(sin(dot(value, float3(39.3468, 11.135, 83.155))) * 24634.6345);
                half b = frac(sin(dot(value, float3(73.1567, 52.235, 9.151))) * 14578.2341);
                return half3(r, g, b);
            }

            half3 Heat(float value)
            {
                value = saturate(value);
                return saturate(half3(value * 2.0, 1.0 - abs(value - 0.5) * 2.0, (1.0 - value) * 2.0));
            }

            half3 ShadowDepthRamp(float value)
            {
                value = saturate(value);
                half3 farColor = half3(0.055h, 0.070h, 0.085h);
                half3 midColor = half3(0.200h, 0.330h, 0.400h);
                half3 nearColor = half3(0.780h, 0.620h, 0.340h);
                half midWeight = smoothstep(0.05h, 0.70h, value);
                half nearWeight = smoothstep(0.68h, 1.00h, value);
                return lerp(lerp(farColor, midColor, midWeight), nearColor, nearWeight);
            }

            half4 DebugScalar(half value)
            {
                return half4(value, value, value, 1.0h);
            }

            float3 QuantizeEncodedId(float3 value)
            {
                return ceil(saturate(value) * 255.0);
            }

            float QuantizeEncodedId(float value)
            {
                return ceil(saturate(value) * 255.0);
            }

            half3 HashEncodedId(float3 value)
            {
                return HashColor(QuantizeEncodedId(value));
            }

            half3 HashEncodedId(float value)
            {
                float id = QuantizeEncodedId(value);
                return HashColor(float3(id, id * 2.17, id * 4.31)) * step(0.5, id);
            }

            half4 DebugSurfaceColor(half4 surfaceColor, float2 uv)
            {
                half coverage = saturate(surfaceColor.a);
                return half4(surfaceColor.rgb, coverage);
            }

            half4 DebugMBufferDepth(float rawDepth)
            {
                half valid = step(0.0001h, abs(rawDepth - 1.0h));
                half depth = saturate((LinearEyeDepth(rawDepth, _ZBufferParams) - _HoDebugTileGeometryDepthParams.x) * _HoDebugTileGeometryDepthParams.z);
                return half4(half3(depth, depth, depth) * valid, 1.0h);
            }

            half GetObjectCustomValue(int customIndex, float2 uv)
            {
                if (customIndex < 4)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv);
                    return values[customIndex];
                }

                half4 values4 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_PointClamp, uv);
                return values4[customIndex - 4];
            }

            half4 ResolveMetadataColor(float2 uv)
            {
                half4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                half4 surfaceData = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture, sampler_PointClamp, uv);
                half valid = step(0.0001h, maskId.r);
                int mode = _HoDebugTileMode;

                if (mode == 1) return half4(maskId.rrr, 1.0h);
                if (mode == 2) return half4(HashEncodedId(maskId.gba) * valid * step(0.0001h, max(max(maskId.g, maskId.b), maskId.a)), 1.0h);
                if (mode == 3) return half4(Heat(maskId.a) * step(0.0001h, maskId.a), 1.0h);
                if (mode == 4) return half4(surfaceData.rrr, 1.0h);
                if (mode == 5) return half4(Heat(surfaceData.g) * step(0.0001h, surfaceData.g), 1.0h);
                if (mode == 6) return half4(HashEncodedId(surfaceData.b), 1.0h);
                if (mode == 7) return half4(surfaceData.aaa, 1.0h);

                if (mode >= 8 && mode <= 11)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture, sampler_PointClamp, uv);
                    return DebugScalar(values[mode - 8]);
                }

                if (mode >= 12 && mode <= 19)
                {
                    return DebugScalar(GetObjectCustomValue(mode - 12, uv));
                }

                if (mode == 20) return half4(maskId.gba, 1.0h);
                if (mode == 21) return half4(HashEncodedId(maskId.g) * valid, 1.0h);
                if (mode == 22) return half4(HashEncodedId(maskId.b) * valid, 1.0h);
                if (mode == 23) return half4(Heat(maskId.a), 1.0h);
                if (mode == 24)
                {
                    half4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_PointClamp, uv);
                    return DebugSurfaceColor(surfaceColor, uv);
                }
                if (mode == 25)
                {
                    float rawDepth = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMBufferDepthTexture, sampler_PointClamp, uv).r;
                    return DebugMBufferDepth(rawDepth);
                }

                return DebugScalar(maskId.r);
            }

            uint HoObjectBufferDecodeByte(float encoded)
            {
                return (uint)round(saturate(encoded) * 255.0);
            }

            uint HoObjectBufferDecodeLayerId(float4 id0, float4 id1, int layer)
            {
                float4 packed = layer < 2 ? id0 : id1;
                float2 pair = (layer & 1) == 0 ? packed.xy : packed.zw;
                return (HoObjectBufferDecodeByte(pair.x) << 8) | HoObjectBufferDecodeByte(pair.y);
            }

            half HoObjectBufferLayerCoverage(float4 coverage, int layer)
            {
                return layer == 0 ? coverage.r : (layer == 1 ? coverage.g : (layer == 2 ? coverage.b : coverage.a));
            }

            // ObjectBuffer 的视图在**两个入口**都要能看：DebugTile 的九宫格，和 feature 自带的整屏视图。
            // 两边必须逐模式同色，否则"同一份数据在两个入口看到两种颜色"，排查时会被这件事误导。
            // 这里的规则与 Runtime/ObjectBuffer/Shaders/Debug/HoObjectBufferDebug.shader 一一对应：
            // 层视图查 palette 的 displayColor（未注册 = unknown 行的洋红），覆盖率 0 = 0.06 灰底。
            half3 HoObjectBufferLayerColor(uint partId, half coverage)
            {
                HoObjectPartData part = HoObjectBufferLoadPart(partId);
                return part.displayColor.rgb * saturate(coverage) + 0.06;
            }

            half4 ResolveObjectBufferColor(float2 uv)
            {
                float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                float4 id1 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture, sampler_PointClamp, uv);
                float4 coverage = SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv);
                int mode = _HoDebugTileMode;

                if (mode >= 1 && mode <= 4)
                {
                    // 层1..3 在轮廓像素上会出现第二个身份（一个像素上有两个物体），那是设计结果，不是 bug。
                    int layer = mode - 1;
                    return half4(HoObjectBufferLayerColor(
                        HoObjectBufferDecodeLayerId(id0, id1, layer),
                        HoObjectBufferLayerCoverage(coverage, layer)), 1.0h);
                }

                if (mode == 5)
                {
                    return DebugScalar(saturate(coverage.r + coverage.g + coverage.b + coverage.a));
                }

                if (mode == 6)
                {
                    return half4(coverage.rgb, 1.0h);
                }

                if (mode == 7)
                {
                    half4 selection = SAMPLE_TEXTURE2D_X(_HoObjectBufferSelectionTexture, sampler_PointClamp, uv);
                    uint selectionIdA;
                    float coverageA;
                    uint selectionIdB;
                    float coverageB;
                    HoObjectBufferUnpackSelection(selection, selectionIdA, coverageA, selectionIdB, coverageB);
                    if (selectionIdA == 0u)
                    {
                        return half4(0.0h, 0.0h, 0.0h, 1.0h);
                    }

                    HoObjectSelectionData selectionRow = HoObjectBufferLoadSelection(selectionIdA);
                    return half4(selectionRow.displayColor.rgb * saturate(coverageA) + 0.06, 1.0h);
                }

                if (mode == 8)
                {
                    HoObjectPartData part = HoObjectBufferLoadPart(HoObjectBufferDecodeLayerId(id0, id1, 0));
                    return half4(saturate(part.thickness), saturate(part.curvature), saturate((float)part.materialClass * 0.25), 1.0h);
                }

                // 9 = Valid：能走到这里就说明"表在、图在、pass 跑了"（没产出时 feature 视图会给暗红）。
                return half4(0.0h, 0.6h, 0.0h, 1.0h);
            }

            half4 ResolveGeometryColor(float2 uv)
            {
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                int mode = _HoDebugTileMode;
                if (mode == 1)
                {
                    return DebugScalar(LilHoGeometryBufferCoverage(normalDepth));
                }

                if (mode == 2)
                {
                    half depth = saturate((LilHoGeometryBufferLinearDepthOrFar(normalDepth, _HoDebugTileGeometryDepthParams.y) - _HoDebugTileGeometryDepthParams.x) * _HoDebugTileGeometryDepthParams.z);
                    return DebugScalar(depth);
                }

                if (mode == 3)
                {
                    return half4(LilHoGeometryBufferEncodedNormalOrBlack(normalDepth), 1.0h);
                }

                if (mode == 4)
                {
                    return half4(Heat(LilHoGeometryBufferNormalValid(normalDepth)), 1.0h);
                }

                if (mode == 7)
                {
                    half4 outlineNormalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture, sampler_PointClamp, uv);
                    return half4(outlineNormalDepth.rgb, 1.0h);
                }

                if (mode == 8)
                {
                    half outlineDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture, sampler_PointClamp, uv).a;
                    half depth = saturate((outlineDepth - _HoDebugTileGeometryDepthParams.x) * _HoDebugTileGeometryDepthParams.z);
                    return DebugScalar(depth);
                }

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }

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

            float RectLine(float2 uv, float4 rect, float lineUv)
            {
                float2 minUv = rect.xy;
                float2 maxUv = rect.xy + rect.zw;
                if (any(uv < minUv) || any(uv > maxUv))
                {
                    return 0.0;
                }

                float2 edgeDistance = min(uv - minUv, maxUv - uv);
                return 1.0 - step(lineUv, min(edgeDistance.x, edgeDistance.y));
            }

            half3 ApplyShadowCastSliceOverlay(float2 uv, half3 color)
            {
                float lineUv = max(max(_HoShadowCastAtlasSize.z, _HoShadowCastAtlasSize.w) * 2.0, 0.001);
                int sliceCount = min(_HoShadowCastSliceCount, HO_SHADOW_CAST_ARRAY_SLICES);
                float sliceLine = 0.0;

                [loop]
                for (int i = 0; i < sliceCount; i++)
                {
                    float4 slice = _HoShadowCastSliceData[i];
                    sliceLine = max(sliceLine, RectLine(uv, float4(slice.xy, slice.zz), lineUv));
                }

                return lerp(color, half3(0.34h, 0.78h, 0.86h), saturate(sliceLine * 0.72));
            }

            float SecondDirectionalBlockLine(float2 uv, int firstSlice, int sliceCount, float lineUv)
            {
                if (sliceCount <= 0)
                {
                    return 0.0;
                }

                float2 blockMin = float2(1.0, 1.0);
                float2 blockMax = float2(0.0, 0.0);
                [unroll]
                for (int sliceOffset = 0; sliceOffset < HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES; sliceOffset++)
                {
                    if (sliceOffset >= sliceCount)
                    {
                        break;
                    }

                    int sliceIndex = firstSlice + sliceOffset;
                    if (sliceIndex < 0 || sliceIndex >= HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES)
                    {
                        continue;
                    }

                    float4 slice = _HoShadowCastSecondDirectionalSliceData[sliceIndex];
                    if (slice.z <= 0.0)
                    {
                        continue;
                    }

                    blockMin = min(blockMin, slice.xy);
                    blockMax = max(blockMax, slice.xy + slice.zz);
                }

                if (any(blockMax <= blockMin))
                {
                    return 0.0;
                }

                return RectLine(uv, float4(blockMin, max(blockMax - blockMin, float2(0.0, 0.0))), lineUv);
            }

            half3 ApplyShadowCastSecondDirectionalOverlay(float2 uv, half3 color)
            {
                float atlasTexel = max(_HoShadowCastSecondDirectionalAtlasSize.z, _HoShadowCastSecondDirectionalAtlasSize.w);
                float cascadeLineUv = max(atlasTexel * 2.0, 0.001);
                float blockLineUv = max(atlasTexel * 4.0, 0.0015);
                int sliceCount = min((int)round(_HoShadowCastSecondDirectionalParams.y) * (int)round(_HoShadowCastSecondDirectionalParams.z), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES);
                int lightCount = min((int)round(_HoShadowCastSecondDirectionalParams.y), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS);
                float cascadeLine = 0.0;
                float blockLine = 0.0;

                [unroll]
                for (int i = 0; i < HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES; i++)
                {
                    if (i >= sliceCount)
                    {
                        break;
                    }

                    float4 slice = _HoShadowCastSecondDirectionalSliceData[i];
                    cascadeLine = max(cascadeLine, RectLine(uv, float4(slice.xy, slice.zz), cascadeLineUv));
                }

                [unroll]
                for (int lightIndex = 0; lightIndex < HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS; lightIndex++)
                {
                    if (lightIndex >= lightCount)
                    {
                        break;
                    }

                    int firstSlice = (int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].x);
                    int perLightSliceCount = min((int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].y), HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES);
                    blockLine = max(blockLine, SecondDirectionalBlockLine(uv, firstSlice, perLightSliceCount, blockLineUv));
                }

                color = lerp(color, half3(0.72h, 0.50h, 0.25h), saturate(cascadeLine * 0.60));
                return lerp(color, half3(0.92h, 0.80h, 0.34h), saturate(blockLine * 0.85));
            }

            half4 ResolveShadowCastColor(float2 uv)
            {
                bool debugSecondDirectional = _HoDebugTileMode == 2;
                float active = debugSecondDirectional ? _HoShadowCastSecondDirectionalParams.x : _HoShadowCastActive;
                int sliceCount = debugSecondDirectional
                    ? (int)round(_HoShadowCastSecondDirectionalParams.y) * (int)round(_HoShadowCastSecondDirectionalParams.z)
                    : _HoShadowCastSliceCount;
                if (active < 0.5 || sliceCount <= 0)
                {
                    return half4(0.0h, 0.0h, 0.0h, 1.0h);
                }

                float rawDepth = debugSecondDirectional
                    ? SAMPLE_TEXTURE2D(_HoShadowCastSecondDirectionalAtlas, sampler_PointClamp, uv)
                    : SAMPLE_TEXTURE2D(_HoShadowCastAtlas, sampler_PointClamp, uv);
                half valid = rawDepth < 0.99999;
                half3 atlasColor = lerp(ShadowDepthRamp(1.0 - rawDepth), half3(0.015h, 0.018h, 0.022h), 1.0h - valid);
                atlasColor = debugSecondDirectional
                    ? ApplyShadowCastSecondDirectionalOverlay(uv, atlasColor)
                    : ApplyShadowCastSliceOverlay(uv, atlasColor);
                return half4(atlasColor, 1.0h);
            }

            half TileSssGeometryValid(half4 normalDepth)
            {
                half normalValid = step(1.0e-4h, dot(normalDepth.rgb, normalDepth.rgb));
                half depthValid = step(1.0e-4h, normalDepth.a);
                return normalValid * depthValid;
            }

            half4 ResolveSubsurfaceScatteringColor(float2 uv)
            {
                half4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                half4 surfaceData = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture, sampler_PointClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                half4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_PointClamp, uv);
                half4 source = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
                half4 transmission = SAMPLE_TEXTURE2D_X(_lilHoSSSTransmissionTexture, sampler_LinearClamp, uv);
                half coverage = saturate(maskId.r * surfaceData.r) * TileSssGeometryValid(normalDepth);
                int mode = _HoDebugTileMode;

                if (mode == 1) return half4(coverage.xxx, 1.0h);
                if (mode == 2) return DebugSurfaceColor(surfaceColor, uv);
                if (mode == 3) return half4(source.rgb * coverage, 1.0h);
                if (mode == 4) return half4(transmission.rgb, 1.0h);
                if (mode == 5) return half4(transmission.aaa, 1.0h);
                if (mode == 6) return half4(saturate(coverage * step(1.0e-4h, source.a)).xxx, 1.0h);
                if (mode == 7) return half4(surfaceData.bbb, 1.0h);
                if (mode == 8) return half4(surfaceData.rrr, 1.0h);
                if (mode == 9) return half4(Heat(surfaceData.r), 1.0h);

                half3 normal = normalize(normalDepth.rgb * 2.0h - 1.0h);
                half3 normalView = TransformWorldToViewDir(normal, true);
                if (mode == 10)
                {
                    half2 direction = dot(normalView.xy, normalView.xy) > 1.0e-4h
                        ? normalize(-normalView.xy)
                        : half2(1.0h, 0.0h);
                    return half4(direction * 0.5h + 0.5h, 0.0h, 1.0h);
                }

                half rim = pow(saturate(1.0h - abs(normalView.z)), 1.75h);
                return half4(rim.xxx, 1.0h);
            }

            half4 ResolvePlanarReflectionColor(float2 uv)
            {
                int mode = _HoDebugTileMode;
                half metadataReady = saturate(_HoMetadataBufferActive * _HoPlanarReflectionDebugInputStatus.y * _HoPlanarReflectionDebugInputStatus.w);
                half geometryReady = saturate(_HoPlanarReflectionDebugInputStatus.z);
                if (mode == 1)
                {
                    return half4(
                        saturate(_HoPlanarReflectionCompositeActive * _LILPBRPlanarReflectionParams.x * _HoPlanarReflectionDebugInputStatus.x),
                        metadataReady,
                        geometryReady,
                        1.0h);
                }

                if (_HoPlanarReflectionCompositeActive < 0.5 ||
                    _LILPBRPlanarReflectionParams.x < 0.5 ||
                    metadataReady < 0.5h ||
                    geometryReady < 0.5h)
                {
                    return half4(1.0h, 0.0h, 1.0h, 1.0h);
                }

                half4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                half4 custom0 = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture, sampler_PointClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);

                half surfaceMask = saturate(maskId.r) * LilHoGeometryBufferCoverage(normalDepth);
                half smoothness = saturate(custom0.r);
                half wetness = saturate(custom0.g);
                half normalStrength = saturate(custom0.b);
                half materialReflectionStrength = saturate(custom0.a);

                half minSmoothness = saturate(_HoPlanarReflectionCompositeParams.z);
                half smoothnessFade = saturate((smoothness - minSmoothness) / max(1.0h - minSmoothness, 0.0001h));
                half centerWeight = surfaceMask * wetness * materialReflectionStrength * smoothnessFade;
                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);

                if (mode == 2) return half4(surfaceMask.xxx, 1.0h);
                if (mode == 3) return half4(smoothness.xxx, 1.0h);
                if (mode == 4) return half4(wetness.xxx, 1.0h);
                if (mode == 5) return half4(normalStrength.xxx, 1.0h);
                if (mode == 6) return half4(materialReflectionStrength.xxx, 1.0h);
                if (mode == 7) return half4(normalWS * 0.5 + 0.5, 1.0h);
                if (mode == 8)
                {
                    half depth = saturate(normalDepth.a / max(_HoDebugTileGeometryDepthParams.y, 0.0001));
                    return half4(depth.xxx, 1.0h);
                }
                if (mode == 14) return custom0;

                if (centerWeight <= 0.0001h)
                {
                    return half4(0.0h, 0.0h, 0.0h, 1.0h);
                }

                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float2 distortion = normalVS.xy * _HoPlanarReflectionCompositeParams.y * normalStrength * wetness;
                float2 distortedScreenUv = uv + distortion;
                if (mode == 9)
                {
                    return half4(saturate(distortion * max(_HoPlanarReflectionDebugParams.z, 0.0001) + 0.5).xy, 0.0h, 1.0h);
                }

                if (mode == 10)
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

                if (mode == 13)
                {
                    return half4(depthGate.xxx, 1.0h);
                }

                float2 reflectionUv = extendedScreenUv;
                if (_HoPlanarReflectionCompositeOptions.x > 0.5)
                {
                    reflectionUv.y = 1.0 - reflectionUv.y;
                }

                half3 reflection = SAMPLE_TEXTURE2D(_LILPBRPlanarReflectionTexture, sampler_LILPBRPlanarReflectionTexture, reflectionUv).rgb;
                reflection *= _HoPlanarReflectionCompositeTint.rgb;
                half compositeWeight = saturate(centerWeight * depthGate * _HoPlanarReflectionCompositeParams.x * _HoPlanarReflectionCompositeTint.a);

                if (mode == 11) return half4(reflection, 1.0h);
                if (mode == 12) return half4(compositeWeight.xxx, 1.0h);
                if (mode == 15)
                {
                    half edgeExtendDebug = saturate(edgeExtend / max(edgeExtendDistance, max(reflectionTexel.x, reflectionTexel.y)));
                    return half4(edgeExtendDebug, edgeExtendDebug, edgeExtendDebug, 1.0h);
                }

                return half4(surfaceMask.xxx, 1.0h);
            }

            uint PickVectorChar(float4 chars, int index)
            {
                if (index == 0) return (uint)round(chars.x);
                if (index == 1) return (uint)round(chars.y);
                if (index == 2) return (uint)round(chars.z);
                return (uint)round(chars.w);
            }

            uint LabelChar(int index)
            {
                if (index < 4) return PickVectorChar(_HoDebugTileLabel0, index);
                if (index < 8) return PickVectorChar(_HoDebugTileLabel1, index - 4);
                if (index < 12) return PickVectorChar(_HoDebugTileLabel2, index - 8);
                return PickVectorChar(_HoDebugTileLabel3, index - 12);
            }

            uint GlyphRow(uint c, uint row)
            {
                if (c == 32u) return 0u;
                if (c == 48u) { if (row == 0u) return 14u; if (row == 1u) return 17u; if (row == 2u) return 19u; if (row == 3u) return 21u; if (row == 4u) return 25u; if (row == 5u) return 17u; return 14u; }
                if (c == 49u) { if (row == 0u) return 4u; if (row == 1u) return 12u; if (row == 6u) return 14u; return 4u; }
                if (c == 50u) { if (row == 0u) return 14u; if (row == 1u) return 17u; if (row == 2u) return 1u; if (row == 3u) return 2u; if (row == 4u) return 4u; if (row == 5u) return 8u; return 31u; }
                if (c == 51u) { if (row == 0u) return 30u; if (row == 3u) return 14u; if (row == 6u) return 30u; return 1u; }
                if (c == 52u) { if (row == 0u) return 2u; if (row == 1u) return 6u; if (row == 2u) return 10u; if (row == 3u) return 18u; if (row == 4u) return 31u; return 2u; }
                if (c == 53u) { if (row == 0u) return 31u; if (row == 1u || row == 2u) return 16u; if (row == 3u) return 30u; if (row == 4u || row == 5u) return 1u; return 30u; }
                if (c == 54u) { if (row == 0u) return 14u; if (row == 1u || row == 2u) return 16u; if (row == 3u) return 30u; if (row == 4u || row == 5u) return 17u; return 14u; }
                if (c == 55u) { if (row == 0u) return 31u; if (row == 1u) return 1u; if (row == 2u) return 2u; if (row == 3u) return 4u; return 8u; }
                if (c == 56u) { if (row == 0u || row == 3u || row == 6u) return 14u; return 17u; }
                if (c == 57u) { if (row == 0u) return 14u; if (row == 1u || row == 2u) return 17u; if (row == 3u) return 15u; if (row == 4u || row == 5u) return 1u; return 14u; }
                if (c == 65u) { if (row == 0u) return 14u; if (row == 3u) return 31u; return 17u; }
                if (c == 66u) { if (row == 0u || row == 3u || row == 6u) return 30u; return 17u; }
                if (c == 67u) { if (row == 0u || row == 6u) return 15u; return 16u; }
                if (c == 68u) { if (row == 0u || row == 6u) return 30u; return 17u; }
                if (c == 69u) { if (row == 0u || row == 6u) return 31u; if (row == 3u) return 30u; return 16u; }
                if (c == 70u) { if (row == 0u) return 31u; if (row == 3u) return 30u; return 16u; }
                if (c == 71u) { if (row == 0u || row == 6u) return 15u; if (row == 3u) return 23u; if (row >= 4u) return 17u; return 16u; }
                if (c == 72u) { if (row == 3u) return 31u; return 17u; }
                if (c == 73u) { if (row == 0u || row == 6u) return 31u; return 4u; }
                if (c == 74u) { if (row == 0u) return 7u; if (row == 5u) return 18u; if (row == 6u) return 12u; return 2u; }
                if (c == 75u) { if (row == 0u || row == 6u) return 17u; if (row == 1u || row == 5u) return 18u; if (row == 2u || row == 4u) return 20u; return 24u; }
                if (c == 76u) { if (row == 6u) return 31u; return 16u; }
                if (c == 77u) { if (row == 1u) return 27u; if (row == 2u || row == 3u) return 21u; return 17u; }
                if (c == 78u) { if (row == 1u) return 25u; if (row == 2u) return 21u; if (row == 3u) return 19u; return 17u; }
                if (c == 79u) { if (row == 0u || row == 6u) return 14u; return 17u; }
                if (c == 80u) { if (row == 0u || row == 3u) return 30u; if (row == 1u || row == 2u) return 17u; return 16u; }
                if (c == 82u) { if (row == 0u || row == 3u) return 30u; if (row == 1u || row == 2u) return 17u; if (row == 4u) return 20u; if (row == 5u) return 18u; return 17u; }
                if (c == 83u) { if (row == 0u) return 15u; if (row == 1u || row == 2u) return 16u; if (row == 3u) return 14u; if (row == 4u || row == 5u) return 1u; return 30u; }
                if (c == 84u) { if (row == 0u) return 31u; return 4u; }
                if (c == 85u) { if (row == 6u) return 14u; return 17u; }
                if (c == 86u) { if (row <= 4u) return 17u; if (row == 5u) return 10u; return 4u; }
                if (c == 87u) { if (row == 6u) return 10u; if (row >= 3u) return 21u; return 17u; }
                if (c == 88u) { if (row == 0u || row == 6u) return 17u; if (row == 1u || row == 5u) return 10u; if (row == 2u || row == 4u) return 4u; return 4u; }
                if (c == 89u) { if (row <= 2u) return 17u; if (row == 3u) return 10u; return 4u; }
                return 0u;
            }

            half DrawLabel(float2 uv)
            {
                float density = max(_HoDebugTileGrid.x, _HoDebugTileGrid.y);
                float labelScale = saturate((density - 2.0) / 4.0);
                float cellHeight = lerp(0.085, 0.14, labelScale);
                float cellWidth = cellHeight * 0.42;
                float2 textOrigin = float2(0.02, 0.98);
                float xCell = (uv.x - textOrigin.x) / cellWidth;
                float yCell = (textOrigin.y - uv.y) / cellHeight;
                if (xCell < 0.0 || yCell < 0.0 || yCell >= 1.0)
                {
                    return 0.0h;
                }

                int charIndex = (int)floor(xCell);
                int col = (int)floor(frac(xCell) * 6.0);
                int row = (int)floor(yCell * 8.0);
                if (charIndex < 0 || charIndex >= 16 || col < 0 || col >= 5 || row < 0 || row >= 7)
                {
                    return 0.0h;
                }

                uint rowBits = GlyphRow(LabelChar(charIndex), (uint)row);
                return half((rowBits >> (uint)(4 - col)) & 1u);
            }

            half4 ApplyOverlay(half4 color, float2 uv)
            {
                half outerBorder = half(step(uv.x, 0.018) + step(uv.y, 0.018) + step(0.982, uv.x) + step(0.982, uv.y));
                if (outerBorder > 0.0h)
                {
                    return half4(0.02h, 0.02h, 0.02h, 1.0h);
                }

                half innerBorder = half(step(uv.x, 0.026) + step(uv.y, 0.026) + step(0.974, uv.x) + step(0.974, uv.y));
                if (innerBorder > 0.0h)
                {
                    return half4(0.92h, 0.92h, 0.86h, 1.0h);
                }

                float density = max(_HoDebugTileGrid.x, _HoDebugTileGrid.y);
                float labelScale = saturate((density - 2.0) / 4.0);
                float labelHeight = lerp(0.11, 0.18, labelScale);
                half labelBackground = half(step(uv.y, 0.985) * step(1.0 - labelHeight, uv.y) * step(uv.x, 0.52));
                half label = DrawLabel(uv);
                color.rgb = lerp(color.rgb, color.rgb * 0.2h, labelBackground);
                color.rgb = lerp(color.rgb, half3(1.0h, 1.0h, 1.0h), label);
                color.a = max(color.a, saturate(labelBackground + label));
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = half4(0.0h, 0.0h, 0.0h, 1.0h);
                if (_HoDebugTileRenderKind == 2)
                {
                    color = ResolveGeometryColor(input.uv);
                }
                else if (_HoDebugTileRenderKind == 3)
                {
                    color = ResolveShadowCastColor(input.uv);
                }
                else if (_HoDebugTileRenderKind == 4)
                {
                    color = ResolveSubsurfaceScatteringColor(input.uv);
                }
                else if (_HoDebugTileRenderKind == 5)
                {
                    color = ResolvePlanarReflectionColor(input.uv);
                }
                else if (_HoDebugTileRenderKind == 6)
                {
                    color = ResolveAdaptiveProbeVolumeColor(input.uv);
                }
                else if (_HoDebugTileRenderKind == 7)
                {
                    color = ResolveObjectBufferColor(input.uv);
                }
                else
                {
                    color = ResolveMetadataColor(input.uv);
                }

                return ApplyOverlay(color, input.uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
