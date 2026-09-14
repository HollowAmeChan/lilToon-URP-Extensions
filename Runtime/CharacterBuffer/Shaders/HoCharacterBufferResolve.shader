Shader "Hidden/lilToon/URP/CharacterBuffer/Resolve"
{
    // MSAA → 4 层 (ID, 覆盖率) 的唯一归约点。
    // 规则（规划 §5.4 / §5.5）：
    //   * 逐样本 **Load**，绝不做硬件 resolve（resolve 是求平均，身份过不去）；
    //   * 每个样本的一张票投给它的部件 ID，按票数降序取前 4；**平票取更近的样本**；
    //   * 覆盖率 = 票数 / 采样数，**不归一化**，`1 - Σcov` 就是背景占比（ID 0 = 背景，不占层）；
    //   * 因为 K = N = 4，实际配置下**不存在尾部丢失**，所以"层里没有"就等于"没覆盖"。
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "CharacterBuffer Resolve"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ _HO_CHARACTER_BUFFER_MSAA_2 _HO_CHARACTER_BUFFER_MSAA_4
            #pragma multi_compile_local_fragment _ _HO_CHARACTER_BUFFER_ID_UNORM
            #pragma multi_compile_local_fragment _ _HO_CHARACTER_BUFFER_HAS_SELECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形的 Vert / Varyings 由 Blit.hlsl 提供（与 GeometryBuffer 的 resolve 同一做法）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterBuffer/Shaders/HoCharacterBufferIdPass.hlsl"

            #if defined(_HO_CHARACTER_BUFFER_MSAA_2)
                #define HO_CB_SAMPLES 2
            #else
                #define HO_CB_SAMPLES 4
            #endif

            #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                #define HO_CB_TEXTURE_MS(type, name) Texture2DMSArray<type, HO_CB_SAMPLES> name
                #define HO_CB_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_ARRAY_MSAA(name, coord, SLICE_ARRAY_INDEX, sampleIndex)
            #else
                #define HO_CB_TEXTURE_MS(type, name) Texture2DMS<type, HO_CB_SAMPLES> name
                #define HO_CB_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_MSAA(name, coord, sampleIndex)
            #endif

            #if defined(_HO_CHARACTER_BUFFER_ID_UNORM)
                HO_CB_TEXTURE_MS(float, _HoCharacterBufferResolveIdTextureMS);
                uint HoCharacterBufferLoadSampleId(uint2 coord, int sampleIndex)
                {
                    float encoded = HO_CB_LOAD_MS(_HoCharacterBufferResolveIdTextureMS, coord, sampleIndex);
                    return (uint)round(saturate(encoded) * 65535.0);
                }
            #else
                HO_CB_TEXTURE_MS(uint, _HoCharacterBufferResolveIdTextureMS);
                uint HoCharacterBufferLoadSampleId(uint2 coord, int sampleIndex)
                {
                    return HO_CB_LOAD_MS(_HoCharacterBufferResolveIdTextureMS, coord, sampleIndex);
                }
            #endif

            HO_CB_TEXTURE_MS(float, _HoCharacterBufferResolveDepthTextureMS);

            #if defined(_HO_CHARACTER_BUFFER_HAS_SELECTION)
                HO_CB_TEXTURE_MS(float4, _HoCharacterBufferResolveSelectionTextureMS);
            #endif

            struct LayerOutput
            {
                float4 id0 : SV_Target0;
                float4 id1 : SV_Target1;
                float4 coverage : SV_Target2;
                float4 selection0 : SV_Target3;
                float4 selection1 : SV_Target4;
            };

            // 数票 → 排序（票数降序，平票取更近的样本）。
            void HoCharacterBufferResolveLayers(uint2 coord, out uint ids[4], out float coverages[4])
            {
                uint distinct[4] = { 0u, 0u, 0u, 0u };
                float counts[4] = { 0.0, 0.0, 0.0, 0.0 };
                float nearest[4] = { 0.0, 0.0, 0.0, 0.0 };
                int distinctCount = 0;

                [unroll]
                for (int sampleIndex = 0; sampleIndex < HO_CB_SAMPLES; sampleIndex++)
                {
                    uint sampleId = HoCharacterBufferLoadSampleId(coord, sampleIndex);
                    if (sampleId == 0u)
                    {
                        continue;   // 背景不占层，只体现在残差里
                    }

                    float sampleDepth = HO_CB_LOAD_MS(_HoCharacterBufferResolveDepthTextureMS, coord, sampleIndex);

                    int found = -1;
                    [unroll]
                    for (int i = 0; i < 4; i++)
                    {
                        if (i < distinctCount && distinct[i] == sampleId)
                        {
                            found = i;
                            break;
                        }
                    }

                    if (found >= 0)
                    {
                        counts[found] += 1.0;
                        nearest[found] = min(nearest[found], sampleDepth);
                    }
                    else if (distinctCount < 4)
                    {
                        distinct[distinctCount] = sampleId;
                        counts[distinctCount] = 1.0;
                        nearest[distinctCount] = sampleDepth;
                        distinctCount++;
                    }
                }

                // 4 个元素的选择排序：把"票数最多、平票更近"的放到前面。
                [unroll]
                for (int i = 1; i < 4; i++)
                {
                    [unroll]
                    for (int j = i; j > 0; j--)
                    {
                        bool better = counts[j] > counts[j - 1] ||
                            (counts[j] == counts[j - 1] && nearest[j] < nearest[j - 1]);
                        if (!better)
                        {
                            break;
                        }

                        uint swapId = distinct[j];
                        distinct[j] = distinct[j - 1];
                        distinct[j - 1] = swapId;
                        float swapCount = counts[j];
                        counts[j] = counts[j - 1];
                        counts[j - 1] = swapCount;
                        float swapDepth = nearest[j];
                        nearest[j] = nearest[j - 1];
                        nearest[j - 1] = swapDepth;
                    }
                }

                float invSampleCount = rcp((float)HO_CB_SAMPLES);
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    ids[i] = i < distinctCount ? distinct[i] : 0u;
                    coverages[i] = i < distinctCount ? counts[i] * invSampleCount : 0.0;
                }
            }

            // 选择层归约：按选择 ID 累加覆盖率再排名次（同一条"按 ID 匹配加权"的规则）。
            // P1 只归约前两个选择层（一张选择图）；第二张（4 层配置）见规划 §5.11 的后续项。
            void HoCharacterBufferResolveSelections(uint2 coord, out uint selectionIds[2], out float selectionCoverages[2])
            {
                selectionIds[0] = 0u;
                selectionIds[1] = 0u;
                selectionCoverages[0] = 0.0;
                selectionCoverages[1] = 0.0;

                #if defined(_HO_CHARACTER_BUFFER_HAS_SELECTION)
                uint candidates[4] = { 0u, 0u, 0u, 0u };
                float candidateCoverages[4] = { 0.0, 0.0, 0.0, 0.0 };
                int candidateCount = 0;
                // 每像素最多 4 个候选；被丢掉的候选目前只在 debug 视图里看得出来（规划 §5.11 的"溢出可见性"）。
                int dropped = 0;

                [unroll]
                for (int sampleIndex = 0; sampleIndex < HO_CB_SAMPLES; sampleIndex++)
                {
                    float4 packed = HO_CB_LOAD_MS(_HoCharacterBufferResolveSelectionTextureMS, coord, sampleIndex);
                    uint selectionIdA;
                    float coverageA;
                    uint selectionIdB;
                    float coverageB;
                    HoCharacterBufferUnpackSelection(packed, selectionIdA, coverageA, selectionIdB, coverageB);

                    [unroll]
                    for (int pair = 0; pair < 2; pair++)
                    {
                        uint selectionId = pair == 0 ? selectionIdA : selectionIdB;
                        float coverage = pair == 0 ? coverageA : coverageB;
                        if (selectionId == 0u || coverage <= 0.0)
                        {
                            continue;
                        }

                        int found = -1;
                        [unroll]
                        for (int i = 0; i < 4; i++)
                        {
                            if (i < candidateCount && candidates[i] == selectionId)
                            {
                                found = i;
                                break;
                            }
                        }

                        if (found >= 0)
                        {
                            candidateCoverages[found] += coverage;
                        }
                        else if (candidateCount < 4)
                        {
                            candidates[candidateCount] = selectionId;
                            candidateCoverages[candidateCount] = coverage;
                            candidateCount++;
                        }
                        else
                        {
                            dropped++;
                        }
                    }
                }

                float invSampleCount = rcp((float)HO_CB_SAMPLES);
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i < candidateCount)
                    {
                        candidateCoverages[i] *= invSampleCount;
                    }
                }

                // 取覆盖率最高的两个（简单选择排序）。
                [unroll]
                for (int i = 1; i < 4; i++)
                {
                    [unroll]
                    for (int j = i; j > 0; j--)
                    {
                        if (candidateCoverages[j] <= candidateCoverages[j - 1])
                        {
                            break;
                        }

                        uint swapId = candidates[j];
                        candidates[j] = candidates[j - 1];
                        candidates[j - 1] = swapId;
                        float swapCoverage = candidateCoverages[j];
                        candidateCoverages[j] = candidateCoverages[j - 1];
                        candidateCoverages[j - 1] = swapCoverage;
                    }
                }

                selectionIds[0] = candidates[0];
                selectionCoverages[0] = candidateCoverages[0];
                selectionIds[1] = candidates[1];
                selectionCoverages[1] = candidateCoverages[1];
                #endif
            }

            LayerOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                uint2 coord = (uint2)input.positionCS.xy;

                uint ids[4];
                float coverages[4];
                HoCharacterBufferResolveLayers(coord, ids, coverages);

                uint selectionIds[2];
                float selectionCoverages[2];
                HoCharacterBufferResolveSelections(coord, selectionIds, selectionCoverages);

                LayerOutput output;
                output.id0 = HoCharacterBufferPackIdRow(ids[0], ids[1]);
                output.id1 = HoCharacterBufferPackIdRow(ids[2], ids[3]);
                output.coverage = float4(coverages[0], coverages[1], coverages[2], coverages[3]);
                output.selection0 = HoCharacterBufferPackSelectionRow(
                    selectionIds[0], selectionCoverages[0], selectionIds[1], selectionCoverages[1]);
                output.selection1 = float4(0.0, 0.0, 0.0, 0.0);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
