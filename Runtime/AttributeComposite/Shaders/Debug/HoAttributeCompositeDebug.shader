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
            #include "../HoACSemanticCompose.hlsl"

            float _HoACDebugMode;
            float4 _HoACDebugLane; // lane, semantic ID, object bit, source mode
            float _HoACDebugLaneValid;
            float _HoACDebugSurfaceAvailable;

            half4 SemanticView(float2 uv, uint view)
            {
                if (_HoACDebugLaneValid < 0.5) return half4(1.0, 0.0, 1.0, 1.0);
                uint lane = (uint)round(_HoACDebugLane.x);
                uint id = (uint)round(_HoACDebugLane.y);
                bool surfaceAvailable = _HoACDebugSurfaceAvailable > 0.5;
                float objectCoverage = _HoACInputFlags.x > 0.5 ? HoAC_Predicate(uv, (uint)_HoACDebugLane.z) : 0.0;
                float2 surface = HoAC_ReadSurfaceLane(uv, lane, id, surfaceAvailable);
                float final = _HoACInputFlags.y > 0.5 ? HoAC_SelectionExact(uv, lane, id) : 0.0;
                if (view == 10u)
                {
                    if (!surfaceAvailable || _HoACInputFlags.x < 0.5) return half4(0.35, 0.0, 0.0, 1.0);
                    uint owner = HoAC_ReadSemanticOwner(uv);
                    bool writtenOwner = owner != 0u;
                    return half4(writtenOwner && HoAC_Identity(uv, owner) > 0.0 ? 1.0 : 0.0,
                        writtenOwner && owner == HoAC_Layer0Identity(uv) ? 1.0 : 0.0,
                        writtenOwner ? 1.0 : 0.0, 1.0);
                }
                if ((view == 7u && _HoACInputFlags.x < 0.5) ||
                    ((view == 8u || view == 9u) && !surfaceAvailable) ||
                    ((view == 11u || view == 13u) && _HoACInputFlags.y < 0.5))
                    return half4(0.35, 0.0, 0.0, 1.0);
                float value = objectCoverage;
                if (view == 8u) value = surface.x;
                if (view == 9u) value = surface.y;
                if (view == 11u) value = final;
                if (view == 13u)
                {
                    float recomposed = HoAC_ComposeSemantic((uint)round(_HoACDebugLane.w), objectCoverage, surface, surfaceAvailable);
                    // Ignore one UNORM8 LSB before amplification: storage quantization is not a composition fault.
                    value = saturate(64.0 * max(0.0, abs(final - recomposed) - 1.0 / 255.0));
                }
                return half4(value, value, value, 1.0);
            }

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
                if (mode >= 7u && mode <= 11u) return SemanticView(uv, mode);
                if (mode == 12u)
                {
                    uint tile = (uint)min(floor(uv.x * 3.0), 2.0) + 3u * (uint)min(floor(uv.y * 2.0), 1.0);
                    float2 sceneUv = frac(uv * float2(3.0, 2.0));
                    // UV row 0: object / written / value. UV row 1: owner / final / recomposition delta.
                    uint view = tile < 5u ? 7u + tile : 13u;
                    return SemanticView(sceneUv, view);
                }
                if (mode == 6u) return half4(_HoACInputFlags.xyz, 1.0);
                if (mode == 4u || mode == 5u)
                {
                    float4 query = float4(mode, 0.0, 0.0, 1.0);
                    if (!HoAC_QueryValid(query)) return half4(0.35, 0.0, 0.0, 1.0);
                    float coverage = HoAC_QueryCoverage(uv, query);
                    return half4(coverage, coverage, coverage, 1.0);
                }
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
