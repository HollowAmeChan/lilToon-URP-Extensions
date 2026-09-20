Shader "Hidden/lilToon/URP/AttributeComposite/SelectionResolve"
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
            Name "HoAC Selection Resolve"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            // 有 SB 语义 lane 时用 `SurfaceOverride` 逐 sample 合成；两个关键字都关 = 纯物体位（ObjectOnly）。
            // 与 OB 的 resolve 同一套写法：**声明的采样数由关键字给出**，逐 sample Load。
            #pragma multi_compile_local_fragment _ _HO_SURFACE_SEMANTIC_MSAA_2 _HO_SURFACE_SEMANTIC_MSAA_4

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形的 Vert / Varyings 由 Blit.hlsl 提供（与 OB 的 resolve 同一做法）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

            #if defined(_HO_SURFACE_SEMANTIC_MSAA_2)
                #define HO_AC_SURFACE_SAMPLES 2
            #else
                #define HO_AC_SURFACE_SAMPLES 4
            #endif

            // MSAA 纹理不能采样，只能按像素 Load；XR 下是 array（与 OB 的 resolve 同一对宏）。
            #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                #define HO_AC_SURFACE_TEXTURE_MS(type, name) Texture2DMSArray<type, HO_AC_SURFACE_SAMPLES> name
                #define HO_AC_SURFACE_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_ARRAY_MSAA(name, coord, SLICE_ARRAY_INDEX, sampleIndex)
            #else
                #define HO_AC_SURFACE_TEXTURE_MS(type, name) Texture2DMS<type, HO_AC_SURFACE_SAMPLES> name
                #define HO_AC_SURFACE_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_MSAA(name, coord, sampleIndex)
            #endif

            // 输入：OB 身份池的 4 层 (组,槽位) + 逐层覆盖率。
            TEXTURE2D_X(_HoObjectBufferId0Texture);
            TEXTURE2D_X(_HoObjectBufferId1Texture);
            TEXTURE2D_X(_HoObjectBufferCoverageTexture);

            // runtime catalog：lane → (SemanticId, object 位, sourceMode)。
            struct HoACLaneData
            {
                uint semanticId;
                uint objectTagBit;
                uint sourceMode;
                uint reserved;
            };

            StructuredBuffer<HoACLaneData> _HoACLanes;
            float _HoACLaneCount;

            #if defined(_HO_SURFACE_SEMANTIC_MSAA_2) || defined(_HO_SURFACE_SEMANTIC_MSAA_4)
                // SB 的语义 lane：MSAA 纹理**不能采样**，只能逐 sample Load。
                // 配对与 SB 写出时同一套：一张 RGBA8MS 装两条 lane（R/G = A，B/A = B）。
                // 逐 sample 的 owner（`_HoSurfaceSemanticOwnerTexture`）不参与合成公式 —— 那道门由 SB
                // 的材质 pass 按 palette 表在**数据上**把住了；owner 现在是给对齐诊断/调试视图用的。
                HO_AC_SURFACE_TEXTURE_MS(float4, _HoSurfaceSemanticLane0Texture);
                HO_AC_SURFACE_TEXTURE_MS(float4, _HoSurfaceSemanticLane1Texture);
                HO_AC_SURFACE_TEXTURE_MS(float4, _HoSurfaceSemanticLane2Texture);
                HO_AC_SURFACE_TEXTURE_MS(float4, _HoSurfaceSemanticLane3Texture);
            #endif
            /// <summary>SB 语义 lane 的实际（自建）采样数：由 SB 的 pass 设，与相机 AA 无关。</summary>
            float _HoSurfaceSemanticSampleCount;

            // 输出：4 张 RGBA8，每张两条 lane 的 `(SemanticId, coverage)` ——
            // 布局与 OB 的 Selection 行一致（R=ID0, G=覆盖率0, B=ID1, A=覆盖率1）。
            struct AcResolveOutput
            {
                half4 lanes01 : SV_Target0;
                half4 lanes23 : SV_Target1;
                half4 lanes45 : SV_Target2;
                half4 lanes67 : SV_Target3;
            };

            // 一条 lane 的**物体侧**覆盖率 = Σ_l cov_l · (第 l 层的部件带不带这一位)。
            // 层身份与标签掩码在 Frag 里**只读一次**（4 次查表），8 条 lane 各自只做位测试，
            // 不要写成"每条 lane 重新解码四层"——那会变成 32 次表查询。
            float LaneCoverage(uint bit, uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                uint mask = 1u << bit;
                float total = 0.0;
                total += ((tags0 & mask) != 0u) ? coverage.r : 0.0;
                total += ((tags1 & mask) != 0u) ? coverage.g : 0.0;
                total += ((tags2 & mask) != 0u) ? coverage.b : 0.0;
                total += ((tags3 & mask) != 0u) ? coverage.a : 0.0;
                return saturate(total);
            }

            #if defined(_HO_SURFACE_SEMANTIC_MSAA_2) || defined(_HO_SURFACE_SEMANTIC_MSAA_4)
                /// <summary>按 lane 号选到它所在的那张 lane 图，Load 指定 sample。</summary>
                float4 LoadSurfaceLane(uint laneIndex, uint2 pixelCoord, uint sampleIndex)
                {
                    if (laneIndex < 2u)
                    {
                        return HO_AC_SURFACE_LOAD_MS(_HoSurfaceSemanticLane0Texture, pixelCoord, sampleIndex);
                    }

                    if (laneIndex < 4u)
                    {
                        return HO_AC_SURFACE_LOAD_MS(_HoSurfaceSemanticLane1Texture, pixelCoord, sampleIndex);
                    }

                    if (laneIndex < 6u)
                    {
                        return HO_AC_SURFACE_LOAD_MS(_HoSurfaceSemanticLane2Texture, pixelCoord, sampleIndex);
                    }

                    return HO_AC_SURFACE_LOAD_MS(_HoSurfaceSemanticLane3Texture, pixelCoord, sampleIndex);
                }

                /// <summary>
                /// 一条 lane 的 surface 侧：返回 `(写了几个 sample, 这些 sample 的 value 之和)`，
                /// **都是未除 N 的和**（除 N 与回落放在调用处一起做，省得两边各除一次）。
                /// `SemanticId = 0` 是未写；`SemanticId = 声明值` 才算写了（value = 0 也是"明确写 0"）。
                /// </summary>
                float2 ResolveSurfaceLane(uint laneIndex, uint declaredId, uint2 pixelCoord)
                {
                    float written = 0.0;
                    float sum = 0.0;
                    if (declaredId == 0u)
                    {
                        return float2(0.0, 0.0);
                    }

                    uint samples = (uint)max(1.0, _HoSurfaceSemanticSampleCount);
                    bool even = (laneIndex & 1u) == 0u;
                    [loop]
                    for (uint i = 0u; i < samples; i++)
                    {
                        uint idA;
                        float valueA;
                        uint idB;
                        float valueB;
                        HoObjectBufferUnpackSelection(LoadSurfaceLane(laneIndex, pixelCoord, i), idA, valueA, idB, valueB);
                        uint inImageId = even ? idA : idB;
                        float inImageValue = even ? valueA : valueB;
                        if (inImageId == declaredId)
                        {
                            written += 1.0;
                            sum += saturate(inImageValue);
                        }
                    }

                    return float2(written, sum);
                }
            #endif

            /// <summary>
            /// 按 catalog 里的 `sourceMode` 合成一条 lane（规划 §0.3.6 的五种），**先逐 sample 合成、再 resolve**：
            /// `o` = 物体侧（像素级：Σ 层覆盖率 · 该层带不带这一位），`s_i` / `written_i` = SB 第 i 个 sample。
            /// <list type="bullet">
            /// <item>0 `ObjectOnly`：`o`（没有 SB 语义 lane 时也是这条路径）；</item>
            /// <item>1 `SurfaceOnly`：`Σ (written ? s : 0) / N`；</item>
            /// <item>2 `Union`：`max(o, 上面那个)`；</item>
            /// <item>3 `SurfaceOverride`：`Σ (written ? s : o) / N` —— **材质写了就以材质为准，没写回落到物体位**；</item>
            /// <item>4 `Intersection`：`o · Σ (written ? s : 0) / N`。</item>
            /// </list>
            /// </summary>
            float ComposeLane(uint mode, float objectCoverage, float2 surface, float samples)
            {
                #if defined(_HO_SURFACE_SEMANTIC_MSAA_2) || defined(_HO_SURFACE_SEMANTIC_MSAA_4)
                    // ==== 临时诊断（查眼睛区拖影）：把合成强制成 SurfaceOnly ====
                    // 这样 Selection 池里显示的就是"SB 到底写了什么"（未写 = 0），不掺任何物体位。
                    // 眼睛/前发材质的「语义权重」设 0 时这里必须立刻变 0 —— 如果不变，说明权重根本没进到
                    // 这个 pass（材质没这个属性 / palette 的物体位门控没过 / lane↔id 对不上）。
                    // 定位完把下一行删掉即可恢复正常的逐 lane sourceMode。
                    mode = 1u;
                    float surfaceAll = saturate(surface.y / max(1.0, samples));
                    if (mode == 1u)
                    {
                        return surfaceAll;
                    }

                    if (mode == 2u)
                    {
                        return saturate(max(objectCoverage, surfaceAll));
                    }

                    if (mode == 3u)
                    {
                        float writtenFraction = saturate(surface.x / max(1.0, samples));
                        return saturate(surfaceAll + (1.0 - writtenFraction) * objectCoverage);
                    }

                    if (mode == 4u)
                    {
                        return saturate(objectCoverage * surfaceAll);
                    }

                    return objectCoverage;
                #else
                    return objectCoverage;
                #endif
            }

            /// <summary>
            /// 一条 lane 的最终覆盖率：物体侧一律先算（`o` 是所有模式的输入），再按 `sourceMode` 与 SB 合成。
            /// </summary>
            float LaneCoverageWithSurface(uint laneIndex, uint mode, uint objectBit, uint declaredId, uint2 pixelCoord,
                                          uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                float objectCoverage = LaneCoverage(objectBit, tags0, tags1, tags2, tags3, coverage);
                #if defined(_HO_SURFACE_SEMANTIC_MSAA_2) || defined(_HO_SURFACE_SEMANTIC_MSAA_4)
                    float samples = max(1.0, _HoSurfaceSemanticSampleCount);
                    float2 surface = ResolveSurfaceLane(laneIndex, declaredId, pixelCoord);
                    return ComposeLane(mode, objectCoverage, surface, samples);
                #else
                    return objectCoverage;
                #endif
            }

            float4 PackLanePair(uint laneA, uint laneB, uint laneCount, uint2 pixelCoord,
                                uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                if (laneA >= laneCount)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                uint idA = _HoACLanes[laneA].semanticId;
                float covA = LaneCoverageWithSurface(laneA, _HoACLanes[laneA].sourceMode, _HoACLanes[laneA].objectTagBit, idA, pixelCoord,
                    tags0, tags1, tags2, tags3, coverage);

                uint idB = 0u;
                float covB = 0.0;
                if (laneB < laneCount)
                {
                    idB = _HoACLanes[laneB].semanticId;
                    covB = LaneCoverageWithSurface(laneB, _HoACLanes[laneB].sourceMode, _HoACLanes[laneB].objectTagBit, idB, pixelCoord,
                        tags0, tags1, tags2, tags3, coverage);
                }

                return HoObjectBufferPackSelectionRow(idA, covA, idB, covB);
            }

            AcResolveOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                // MSAA 只能按像素 Load：SV_POSITION 就是渲染目标像素坐标（与 SB 的图同尺寸、同缩放假定）。
                uint2 pixelCoord = (uint2)input.positionCS.xy;

                float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                float4 id1 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture, sampler_PointClamp, uv);
                float4 coverage = SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv);

                uint layerIds[4] = { 0u, 0u, 0u, 0u };
                layerIds[0] = HoObjectBufferDecodeIdExact(id0.xy);
                layerIds[1] = HoObjectBufferDecodeIdExact(id0.zw);
                layerIds[2] = HoObjectBufferDecodeIdExact(id1.xy);
                layerIds[3] = HoObjectBufferDecodeIdExact(id1.zw);

                uint tags[4] = { 0u, 0u, 0u, 0u };
                [unroll]
                for (uint layer = 0u; layer < 4u; layer++)
                {
                    if (layerIds[layer] != 0u)
                    {
                        tags[layer] = HoObjectBufferLoadPart(layerIds[layer]).tags;
                    }
                }

                uint laneCount = (uint)max(0.0, _HoACLaneCount);
                AcResolveOutput output;
                output.lanes01 = (half4)PackLanePair(0u, 1u, laneCount, pixelCoord, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes23 = (half4)PackLanePair(2u, 3u, laneCount, pixelCoord, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes45 = (half4)PackLanePair(4u, 5u, laneCount, pixelCoord, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes67 = (half4)PackLanePair(6u, 7u, laneCount, pixelCoord, tags[0], tags[1], tags[2], tags[3], coverage);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
