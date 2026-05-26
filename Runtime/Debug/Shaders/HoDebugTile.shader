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

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"

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
            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_FLOAT(_HoShadowCastAtlas);
            TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas);
            TEXTURE2D_X(_lilHoSSSSourceTexture);
            TEXTURE2D_X(_lilHoSSSTransmissionTexture);

            float _HoShadowCastActive;
            int _HoShadowCastSliceCount;
            float4 _HoShadowCastAtlasSize;
            float4 _HoShadowCastSliceData[32];
            float4 _HoShadowCastSecondDirectionalParams;
            float4 _HoShadowCastSecondDirectionalAtlasSize;
            float4 _HoShadowCastSecondDirectionalLightData[4];
            float4 _HoShadowCastSecondDirectionalSliceData[16];

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
                int mode = _HoDebugTileMode;

                if (mode == 1) return half4(maskId.rrr, 1.0h);
                if (mode == 2) return half4(HashColor(maskId.gba) * step(0.0001h, maskId.r), 1.0h);
                if (mode == 3) return half4(Heat(maskId.a) * step(0.0001h, maskId.a), 1.0h);
                if (mode == 4) return half4(surfaceData.rrr, 1.0h);
                if (mode == 5) return half4(Heat(surfaceData.g) * step(0.0001h, surfaceData.g), 1.0h);
                if (mode == 6) return half4(HashColor(float3(surfaceData.b, surfaceData.b * 2.17, surfaceData.b * 4.31)) * step(0.0001h, surfaceData.b), 1.0h);
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
                if (mode == 21) return half4(HashColor(float3(maskId.g, maskId.g * 2.17, maskId.g * 4.31)) * step(0.0001h, maskId.g), 1.0h);
                if (mode == 22) return half4(HashColor(float3(maskId.b, maskId.b * 2.17, maskId.b * 4.31)) * step(0.0001h, maskId.b), 1.0h);
                if (mode == 23) return half4(Heat(maskId.a), 1.0h);
                if (mode == 24) return half4(0.15h, 0.78h, 1.0h, 1.0h) * step(0.0001h, maskId.r);
                if (mode == 25)
                {
                    half4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_PointClamp, uv);
                    return half4(surfaceColor.rgb, 1.0h);
                }

                return DebugScalar(maskId.r);
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

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
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
                int sliceCount = min(_HoShadowCastSliceCount, 32);
                float sliceLine = 0.0;

                [unroll]
                for (int i = 0; i < 32; i++)
                {
                    if (i >= sliceCount)
                    {
                        break;
                    }

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
                for (int sliceOffset = 0; sliceOffset < 4; sliceOffset++)
                {
                    if (sliceOffset >= sliceCount)
                    {
                        break;
                    }

                    int sliceIndex = firstSlice + sliceOffset;
                    if (sliceIndex < 0 || sliceIndex >= 16)
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
                int sliceCount = min((int)round(_HoShadowCastSecondDirectionalParams.y) * (int)round(_HoShadowCastSecondDirectionalParams.z), 16);
                int lightCount = min((int)round(_HoShadowCastSecondDirectionalParams.y), 4);
                float cascadeLine = 0.0;
                float blockLine = 0.0;

                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    if (i >= sliceCount)
                    {
                        break;
                    }

                    float4 slice = _HoShadowCastSecondDirectionalSliceData[i];
                    cascadeLine = max(cascadeLine, RectLine(uv, float4(slice.xy, slice.zz), cascadeLineUv));
                }

                [unroll]
                for (int lightIndex = 0; lightIndex < 4; lightIndex++)
                {
                    if (lightIndex >= lightCount)
                    {
                        break;
                    }

                    int firstSlice = (int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].x);
                    int perLightSliceCount = min((int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].y), 4);
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
                half4 source = SAMPLE_TEXTURE2D_X(_lilHoSSSSourceTexture, sampler_LinearClamp, uv);
                half4 transmission = SAMPLE_TEXTURE2D_X(_lilHoSSSTransmissionTexture, sampler_LinearClamp, uv);
                half coverage = saturate(maskId.r * surfaceData.r) * TileSssGeometryValid(normalDepth);
                int mode = _HoDebugTileMode;

                if (mode == 1) return half4(coverage.xxx, 1.0h);
                if (mode == 2) return half4(source.rgb, 1.0h);
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
                color.a = 1.0h;
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
