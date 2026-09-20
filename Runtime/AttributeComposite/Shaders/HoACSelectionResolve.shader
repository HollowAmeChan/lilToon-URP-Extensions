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
            // 有 SB 语义 lane 时按 catalog 的 sourceMode 与它合成；关掉 = 纯物体位（ObjectOnly）。
            // **单采样**：lane 是逐像素的，普通采样即可 —— 不读 MSAA（读端按 Texture2DMS + Load 时，
            // 坐标 / 采样数 / bindMS 任何一处对不上都会静默读出邻域或旧 sample）。
            #pragma multi_compile_local_fragment _ _HO_SURFACE_SEMANTIC

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形的 Vert / Varyings 由 Blit.hlsl 提供（与 OB 的 resolve 同一做法）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

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

            #if defined(_HO_SURFACE_SEMANTIC)
                // SB 的语义 lane（**单采样、逐像素**）。配对与 SB 写出时同一套：
                // 一张 RGBA8 装两条 lane（R/G = A，B/A = B）。
                TEXTURE2D_X(_HoSurfaceSemanticLane0Texture);
                TEXTURE2D_X(_HoSurfaceSemanticLane1Texture);
                TEXTURE2D_X(_HoSurfaceSemanticLane2Texture);
                TEXTURE2D_X(_HoSurfaceSemanticLane3Texture);
            #endif

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

            #if defined(_HO_SURFACE_SEMANTIC)
                /// <summary>按 lane 号选到它所在的那张 lane 图（单采样，普通采样）。</summary>
                float4 SampleSurfaceLane(uint laneIndex, float2 uv)
                {
                    if (laneIndex < 2u)
                    {
                        return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane0Texture, sampler_PointClamp, uv);
                    }

                    if (laneIndex < 4u)
                    {
                        return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane1Texture, sampler_PointClamp, uv);
                    }

                    if (laneIndex < 6u)
                    {
                        return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane2Texture, sampler_PointClamp, uv);
                    }

                    return SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane3Texture, sampler_PointClamp, uv);
                }

                /// <summary>
                /// 一条 lane 的 surface 侧：`(写了没有 0/1, value)`。
                /// `SemanticId = 0` 是未写；`SemanticId = 声明值` 才算写了（`value = 0` 也是"明确写 0"）。
                /// </summary>
                float2 ResolveSurfaceLane(uint laneIndex, uint declaredId, float2 uv)
                {
                    if (declaredId == 0u)
                    {
                        return float2(0.0, 0.0);
                    }

                    uint idA;
                    float valueA;
                    uint idB;
                    float valueB;
                    HoObjectBufferUnpackSelection(SampleSurfaceLane(laneIndex, uv), idA, valueA, idB, valueB);
                    bool even = (laneIndex & 1u) == 0u;
                    uint inImageId = even ? idA : idB;
                    float inImageValue = even ? valueA : valueB;
                    return inImageId == declaredId ? float2(1.0, saturate(inImageValue)) : float2(0.0, 0.0);
                }
            #endif

            /// <summary>
            /// 按 catalog 里的 `sourceMode` 合成一条 lane（规划 §0.3.6 的五种）：
            /// `o` = 物体侧（像素级：Σ 层覆盖率 · 该层带不带这一位），`(written, s)` = SB 在**同一像素**写的 lane 值。
            /// <list type="bullet">
            /// <item>0 `ObjectOnly`：`o`（没有 SB 语义 lane 时也是这条路径）；</item>
            /// <item>1 `SurfaceOnly`：`written ? s : 0`；</item>
            /// <item>2 `Union`：`max(o, written ? s : 0)`；</item>
            /// <item>3 `SurfaceOverride`：`written ? s : o` —— **材质写了就以材质为准，没写回落到物体位**；</item>
            /// <item>4 `Intersection`：`o · (written ? s : 0)`。</item>
            /// </list>
            /// </summary>
            float ComposeLane(uint mode, float objectCoverage, float2 surface)
            {
                #if defined(_HO_SURFACE_SEMANTIC)
                    float surfaceAll = saturate(surface.y);
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
                        return saturate(surfaceAll + (1.0 - saturate(surface.x)) * objectCoverage);
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
            float LaneCoverageWithSurface(uint laneIndex, uint mode, uint objectBit, uint declaredId, float2 uv,
                                          uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                float objectCoverage = LaneCoverage(objectBit, tags0, tags1, tags2, tags3, coverage);
                #if defined(_HO_SURFACE_SEMANTIC)
                    float2 surface = ResolveSurfaceLane(laneIndex, declaredId, uv);
                    return ComposeLane(mode, objectCoverage, surface);
                #else
                    return objectCoverage;
                #endif
            }

            float4 PackLanePair(uint laneA, uint laneB, uint laneCount, float2 uv,
                                uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                if (laneA >= laneCount)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                uint idA = _HoACLanes[laneA].semanticId;
                float covA = LaneCoverageWithSurface(laneA, _HoACLanes[laneA].sourceMode, _HoACLanes[laneA].objectTagBit, idA, uv,
                    tags0, tags1, tags2, tags3, coverage);

                uint idB = 0u;
                float covB = 0.0;
                if (laneB < laneCount)
                {
                    idB = _HoACLanes[laneB].semanticId;
                    covB = LaneCoverageWithSurface(laneB, _HoACLanes[laneB].sourceMode, _HoACLanes[laneB].objectTagBit, idB, uv,
                        tags0, tags1, tags2, tags3, coverage);
                }

                return HoObjectBufferPackSelectionRow(idA, covA, idB, covB);
            }

            AcResolveOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

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
                output.lanes01 = (half4)PackLanePair(0u, 1u, laneCount, uv, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes23 = (half4)PackLanePair(2u, 3u, laneCount, uv, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes45 = (half4)PackLanePair(4u, 5u, laneCount, uv, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes67 = (half4)PackLanePair(6u, 7u, laneCount, uv, tags[0], tags[1], tags[2], tags[3], coverage);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
