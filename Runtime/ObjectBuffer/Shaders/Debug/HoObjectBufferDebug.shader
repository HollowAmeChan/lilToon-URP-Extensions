Shader "Hidden/lilToon/URP/ObjectBuffer/DebugView"
{
    Properties
    {
        [HideInInspector] _HoObjectBufferDebugMode ("ObjectBuffer Debug Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ObjectBuffer DebugView"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferPalette.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

            TEXTURE2D_X(_HoObjectBufferId0Texture);
            TEXTURE2D_X(_HoObjectBufferId1Texture);
            TEXTURE2D_X(_HoObjectBufferCoverageTexture);
            TEXTURE2D_X(_HoObjectBufferSelectionTexture);
            float _HoObjectBufferDebugMode;
            float _HoObjectBufferValid;
            float _HoObjectBufferRequestedSamples;
            float _HoObjectBufferActualSamples;

            float4 SampleId0(float2 uv) { return SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv); }
            float4 SampleId1(float2 uv) { return SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture, sampler_PointClamp, uv); }
            float4 SampleCoverage(float2 uv) { return SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv); }

            // 层 ID 的解码：UNORM8 → round(v*255)，绝不在插值后的值上比（规划 §6 第 1 条）。
            uint DecodeLayerId(float4 id0, float4 id1, int layer)
            {
                float4 packed = layer < 2 ? id0 : id1;
                float2 pair = layer % 2 == 0 ? packed.xy : packed.zw;
                return HoObjectBufferDecodeIdExact(pair);
            }

            float LayerCoverage(float4 coverage, int layer)
            {
                return layer == 0 ? coverage.r : (layer == 1 ? coverage.g : (layer == 2 ? coverage.b : coverage.a));
            }

            float3 LayerColor(uint partId, float coverage)
            {
                HoObjectPartData part = HoObjectBufferLoadPart(partId);
                // 用覆盖率调制亮度：这样"半覆盖的像素"看得见，而不是只有二值的硬边。
                return part.displayColor.rgb * saturate(coverage) + 0.06;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                uint mode = (uint)round(_HoObjectBufferDebugMode);

                if (_HoObjectBufferValid < 0.5)
                {
                    // 没产出时给一个明确的信号色，而不是静默黑屏（黑屏分不清"没跑"和"全背景"）。
                    return float4(0.35, 0.0, 0.0, 1.0);
                }

                if (mode == 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float4 id0 = SampleId0(uv);
                float4 id1 = SampleId1(uv);
                float4 coverage = SampleCoverage(uv);

                if (mode >= 1 && mode <= 4)
                {
                    int layer = (int)mode - 1;
                    uint partId = DecodeLayerId(id0, id1, layer);
                    return float4(LayerColor(partId, LayerCoverage(coverage, layer)), 1.0);
                }

                if (mode == 5)
                {
                    float total = saturate(coverage.r + coverage.g + coverage.b + coverage.a);
                    return float4(total, total, total, 1.0);
                }

                if (mode == 6)
                {
                    // 四层覆盖率直接铺到 RGB：哪一层占了这笔像素一目了然。
                    return float4(coverage.r, coverage.g, coverage.b, 1.0);
                }

                if (mode == 7)
                {
                    float4 packed = SAMPLE_TEXTURE2D_X(_HoObjectBufferSelectionTexture, sampler_PointClamp, uv);
                    uint selectionIdA;
                    float coverageA;
                    uint selectionIdB;
                    float coverageB;
                    HoObjectBufferUnpackSelection(packed, selectionIdA, coverageA, selectionIdB, coverageB);
                    if (selectionIdA == 0u)
                    {
                        return float4(0.0, 0.0, 0.0, 1.0);
                    }

                    HoObjectSelectionData selection = HoObjectBufferLoadSelection(selectionIdA);
                    return float4(selection.displayColor.rgb * saturate(coverageA) + 0.06, 1.0);
                }

                if (mode == 8)
                {
                    // palette 行视图：层0 的部件属性（厚度 / 曲率 / 材质分类）——查表对不对一眼可见。
                    uint partId = DecodeLayerId(id0, id1, 0);
                    HoObjectPartData part = HoObjectBufferLoadPart(partId);
                    return float4(saturate(part.thickness), saturate(part.curvature), saturate((float)part.materialClass * 0.25), 1.0);
                }

                // 10 = Sample Count：0.3.2 要求的"实际协商采样数必须看得见"。
                // 绿 = 4x（请求值）、橙 = 2x、红 = 1x（覆盖率退化成 0/1）；请求值写在 alpha 里备用。
                if (mode == 10)
                {
                    float actual = _HoObjectBufferActualSamples;
                    float3 sampleColor = actual >= 3.5 ? float3(0.1, 0.8, 0.2)
                        : (actual >= 1.5 ? float3(0.95, 0.6, 0.1) : float3(0.85, 0.15, 0.15));
                    return float4(sampleColor, saturate(_HoObjectBufferRequestedSamples / 4.0));
                }

                // 9 = Valid：能走到这里就说明"表在、图在、pass 跑了"三件事都成立
                // （没产出时上面 `_HoObjectBufferValid < 0.5` 已经返回暗红，两者必须能一眼分开）。
                // 想直接读像素里的原始字节时不要改这里——那是临时探针，改完必须撤；
                // 需要读字节就用 Id0..Id3 视图配合 palette 表，或走 CPU 回读。
                return float4(0.0, 0.6, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
