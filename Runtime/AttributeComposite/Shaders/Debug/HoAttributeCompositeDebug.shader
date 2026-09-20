Shader "Hidden/lilToon/URP/AttributeComposite/DebugView"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HoAC DebugView"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AttributeComposite/Shaders/HoACQuery.hlsl"

            float _HoACDebugMode;

            // 一组的四个通道 = 连续四条 lane 的 coverage。
            half4 LaneCoverageRow(float2 uv, uint firstLane)
            {
                return half4(
                    HoAC_Selection(uv, firstLane + 0u),
                    HoAC_Selection(uv, firstLane + 1u),
                    HoAC_Selection(uv, firstLane + 2u),
                    HoAC_Selection(uv, firstLane + 3u));
            }

            // 一组的四个通道 = 连续四条 lane 的 SemanticId（/255 归一化显示）。
            half4 LaneSemanticIdRow(float2 uv, uint firstLane)
            {
                return half4(
                    _HoACLanes[firstLane + 0u].semanticId / 255.0,
                    _HoACLanes[firstLane + 1u].semanticId / 255.0,
                    _HoACLanes[firstLane + 2u].semanticId / 255.0,
                    _HoACLanes[firstLane + 3u].semanticId / 255.0);
            }

            // catalog 直出：R = 每条 lane 的 object 位（0..7 → 0/32..224），G = sourceMode，B = lane 是否在产出范围内。
            half4 LaneCatalogRow(uint firstLane)
            {
                uint laneCount = (uint)max(0.0, _HoACLaneCount);
                float inRange = (firstLane < laneCount) ? 1.0 : 0.0;
                return half4(
                    _HoACLanes[firstLane].objectTagBit / 8.0,
                    _HoACLanes[firstLane].sourceMode / 4.0,
                    inRange,
                    1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                uint mode = (uint)round(_HoACDebugMode);
                if (_HoACActive <= 0.5)
                {
                    // 没产出时给一个明确的信号色，而不是静默黑屏（与 OB 的 Valid 视图同一个约定）。
                    return half4(0.35, 0.0, 0.0, 1.0);
                }

                if (mode == 1)
                {
                    // 上下半屏各铺一组，一次能看 8 条 lane 的覆盖率。
                    return uv.y < 0.5 ? LaneCoverageRow(uv, 0u) : LaneCoverageRow(uv, 4u);
                }

                if (mode == 2)
                {
                    return uv.y < 0.5 ? LaneSemanticIdRow(uv, 0u) : LaneSemanticIdRow(uv, 4u);
                }

                if (mode == 3)
                {
                    return LaneCatalogRow(uv.x < 0.5 ? 0u : 1u);
                }

                return half4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
