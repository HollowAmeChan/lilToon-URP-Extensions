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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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

            // 输出：4 张 RGBA8，每张两条 lane 的 `(SemanticId, coverage)` ——
            // 布局与 OB 的 Selection 行一致（R=ID0, G=覆盖率0, B=ID1, A=覆盖率1）。
            struct AcResolveOutput
            {
                half4 lanes01 : SV_Target0;
                half4 lanes23 : SV_Target1;
                half4 lanes45 : SV_Target2;
                half4 lanes67 : SV_Target3;
            };

            // 一条 lane 的覆盖率 = Σ_l cov_l · (第 l 层的部件带不带这一位)。
            // 层身份与标签掩码在这里**只读一次**（4 次查表），8 条 lane 各自只做位测试，
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

            float4 PackLanePair(uint laneA, uint laneB, uint laneCount, uint tags0, uint tags1, uint tags2, uint tags3, float4 coverage)
            {
                if (laneA >= laneCount)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                uint idA = _HoACLanes[laneA].semanticId;
                float covA = LaneCoverage(_HoACLanes[laneA].objectTagBit, tags0, tags1, tags2, tags3, coverage);
                uint idB = 0u;
                float covB = 0.0;
                if (laneB < laneCount)
                {
                    idB = _HoACLanes[laneB].semanticId;
                    covB = LaneCoverage(_HoACLanes[laneB].objectTagBit, tags0, tags1, tags2, tags3, coverage);
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
                output.lanes01 = (half4)PackLanePair(0u, 1u, laneCount, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes23 = (half4)PackLanePair(2u, 3u, laneCount, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes45 = (half4)PackLanePair(4u, 5u, laneCount, tags[0], tags[1], tags[2], tags[3], coverage);
                output.lanes67 = (half4)PackLanePair(6u, 7u, laneCount, tags[0], tags[1], tags[2], tags[3], coverage);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
