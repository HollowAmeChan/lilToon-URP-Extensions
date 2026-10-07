Shader "Hidden/lilToon/URP/Debug/GridOverlay"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _HoDebugTileGrid;
            float4 _HoDebugTileLabel0, _HoDebugTileLabel1, _HoDebugTileLabel2, _HoDebugTileLabel3;
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
                color.a = max(color.a, saturate(labelBackground + label));
                return color;
            }


            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return ApplyOverlay(half4(0, 0, 0, 0), input.texcoord);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
