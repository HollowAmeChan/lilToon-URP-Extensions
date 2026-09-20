Shader "Hidden/lilToon-HoCharacterSpecialization/URP/ObjectSemantic"
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
            Name "HoCharacter ObjectSemantic"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

            // 角色语义位平面：OB 身份池（最多 4 层）+ 覆盖率 → 两张 RGBA8，一位一个通道。
            // 通道布局与从前的 objectCustom0_3 / 4_7 完全一致，所以消费端的"频道号"不用动：
            //   low  = (0 全角色, 1 脸, 2 前发, 3 眼睛)
            //   high = (4 眼透区, 5 配件, 6 人体, 7 预留)
            // 值 = 该位在该像素上的覆盖率之和（一个像素可同时属于多层，
            // "整角色 + 脸"这种多归属会直接累加）。**这已经是 MSAA 抗锯齿过的连续场**，
            // 所以消费端不再需要给二值位伪造抗锯齿（规划 §0.3.x：不再把 bit 通道当 UNORM 过滤）。
            TEXTURE2D_X(_HoObjectBufferId0Texture);
            TEXTURE2D_X(_HoObjectBufferId1Texture);
            TEXTURE2D_X(_HoObjectBufferCoverageTexture);

            struct ObjectSemanticOutput
            {
                half4 low : SV_Target0;
                half4 high : SV_Target1;
            };

            float BitAt(uint tags, uint index)
            {
                return (float)((tags >> index) & 1u);
            }

            // 一层：身份 → 标签位；这个像素在该层上占多少覆盖率，就给那几位加多少。
            void AccumulateLayer(uint partId, float coverage, inout float4 low, inout float4 high)
            {
                if (partId == 0u || coverage <= 0.0)
                {
                    return;
                }

                uint tags = HoObjectBufferLoadPart(partId).tags;
                low += coverage * float4(BitAt(tags, 0u), BitAt(tags, 1u), BitAt(tags, 2u), BitAt(tags, 3u));
                high += coverage * float4(BitAt(tags, 4u), BitAt(tags, 5u), BitAt(tags, 6u), BitAt(tags, 7u));
            }

            ObjectSemanticOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                float4 id1 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId1Texture, sampler_PointClamp, uv);
                float4 coverage = SAMPLE_TEXTURE2D_X(_HoObjectBufferCoverageTexture, sampler_PointClamp, uv);

                float4 low = 0.0;
                float4 high = 0.0;
                AccumulateLayer(HoObjectBufferDecodeIdExact(id0.xy), coverage.r, low, high);
                AccumulateLayer(HoObjectBufferDecodeIdExact(id0.zw), coverage.g, low, high);
                AccumulateLayer(HoObjectBufferDecodeIdExact(id1.xy), coverage.b, low, high);
                AccumulateLayer(HoObjectBufferDecodeIdExact(id1.zw), coverage.a, low, high);

                ObjectSemanticOutput output;
                output.low = (half4)saturate(low);
                output.high = (half4)saturate(high);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
