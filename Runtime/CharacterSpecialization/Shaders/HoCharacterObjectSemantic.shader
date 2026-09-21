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
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AttributeComposite/Shaders/HoACQuery.hlsl"

            // 角色语义位平面：**从 AC 的 Selection 池转置而来**，不再自己解码 OB 的身份池与部件表。
            // AC 的池是 `(SemanticId, coverage)` 的固定 lane（每张 RGBA8 两条），这里是"每通道一个语义"的
            // 位平面 —— 因为下游（前发投影的半影滤波、眼透的羽化）要在同一张图上按 texel 抽很多次，
            // 位平面布局更便宜。这就是AC 架构 §9.2 的"消费者自己用 API 烤一张、图记在自己名下"。
            //
            // 通道布局沿用历史上的 objectCustom 布局，消费端不用动：
            //   low  = (0 全角色, 1 脸, 2 前发, 3 眼睛)
            //   high = (4 眼透区, 5 配件, 6 人体, 7 预留)
            struct ObjectSemanticOutput
            {
                half4 low : SV_Target0;
                half4 high : SV_Target1;
            };

            // lane 号 = 物体位序（HAc schema 的默认保证）；图内 SemanticId 与声明不符时按"未写"处理。
            float LaneCoverage(uint laneIndex, uint inImageId, float coverage)
            {
                uint laneCount = (uint)max(0.0, _HoACLaneCount);
                if (laneIndex >= laneCount)
                {
                    return 0.0;
                }

                return inImageId == _HoACLanes[laneIndex].semanticId ? coverage : 0.0;
            }

            ObjectSemanticOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 packed01 = SAMPLE_TEXTURE2D_X(_HoACSelection0Texture, sampler_PointClamp, uv);
                float4 packed23 = SAMPLE_TEXTURE2D_X(_HoACSelection1Texture, sampler_PointClamp, uv);
                float4 packed45 = SAMPLE_TEXTURE2D_X(_HoACSelection2Texture, sampler_PointClamp, uv);
                float4 packed67 = SAMPLE_TEXTURE2D_X(_HoACSelection3Texture, sampler_PointClamp, uv);

                uint id0;
                float cov0;
                uint id1;
                float cov1;
                uint id2;
                float cov2;
                uint id3;
                float cov3;
                uint id4;
                float cov4;
                uint id5;
                float cov5;
                uint id6;
                float cov6;
                uint id7;
                float cov7;
                HoAC_UnpackSelection(packed01, id0, cov0, id1, cov1);
                HoAC_UnpackSelection(packed23, id2, cov2, id3, cov3);
                HoAC_UnpackSelection(packed45, id4, cov4, id5, cov5);
                HoAC_UnpackSelection(packed67, id6, cov6, id7, cov7);

                ObjectSemanticOutput output;
                output.low = (half4)float4(
                    LaneCoverage(0u, id0, cov0),
                    LaneCoverage(1u, id1, cov1),
                    LaneCoverage(2u, id2, cov2),
                    LaneCoverage(3u, id3, cov3));
                output.high = (half4)float4(
                    LaneCoverage(4u, id4, cov4),
                    LaneCoverage(5u, id5, cov5),
                    LaneCoverage(6u, id6, cov6),
                    LaneCoverage(7u, id7, cov7));
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
