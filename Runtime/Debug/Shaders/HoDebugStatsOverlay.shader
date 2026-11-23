Shader "Hidden/lilToon/URP/Debug/StatsOverlay"
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
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HoDebugTextChars[12];
            float _HoDebugTextLength;
            float4 _HoDebugTextCell;
            float _HoDebugTextFlipY;
            float4 _HoDebugTextColor;

            uint TextChar(int index)
            {
                float4 chars = _HoDebugTextChars[index / 4];
                int component = index % 4;
                if (component == 0) return (uint)round(chars.x);
                if (component == 1) return (uint)round(chars.y);
                if (component == 2) return (uint)round(chars.z);
                return (uint)round(chars.w);
            }

            uint GlyphRow(uint c, uint row)
            {
                if (c == 32u) return 0u;
                if (c == 45u) return row == 3u ? 30u : 0u;
                if (c == 46u) return row == 6u ? 4u : 0u;
                if (c == 58u) return (row == 2u || row == 5u) ? 4u : 0u;
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
                if (c >= 65u && c <= 90u)
                {
                    uint glyph = c - 65u;
                    if (glyph == 0u) return row == 0u ? 14u : row == 3u ? 31u : 17u;
                    if (glyph == 1u) return row == 0u || row == 3u || row == 6u ? 30u : 17u;
                    if (glyph == 2u) return row == 0u || row == 6u ? 15u : 16u;
                    if (glyph == 3u) return row == 0u || row == 6u ? 30u : 17u;
                    if (glyph == 4u) return row == 0u || row == 6u ? 31u : row == 3u ? 30u : 16u;
                    if (glyph == 5u) return row == 0u ? 31u : row == 3u ? 30u : 16u;
                    if (glyph == 6u) return row == 0u || row == 6u ? 15u : row == 3u ? 23u : 17u;
                    if (glyph == 7u) return row == 3u ? 31u : 17u;
                    if (glyph == 8u) return row == 0u || row == 6u ? 31u : 4u;
                    if (glyph == 9u) return row == 0u ? 7u : row == 5u ? 18u : row == 6u ? 12u : 2u;
                    if (glyph == 10u) return row == 0u || row == 6u ? 17u : row == 1u || row == 5u ? 18u : row == 2u || row == 4u ? 20u : 24u;
                    if (glyph == 11u) return row == 6u ? 31u : 16u;
                    if (glyph == 12u) return row == 1u ? 27u : row == 2u || row == 3u ? 21u : 17u;
                    if (glyph == 13u) return row == 1u ? 25u : row == 2u ? 21u : row == 3u ? 19u : 17u;
                    if (glyph == 14u) return row == 0u || row == 6u ? 14u : 17u;
                    if (glyph == 15u) return row == 0u || row == 3u ? 30u : 17u;
                    if (glyph == 16u) return row == 0u || row == 6u ? 14u : row == 3u ? 23u : 17u;
                    if (glyph == 17u) return row == 0u || row == 3u ? 30u : row == 4u ? 20u : row == 5u ? 18u : 17u;
                    if (glyph == 18u) return row == 0u ? 15u : row == 1u || row == 2u ? 16u : row == 3u ? 14u : row == 4u || row == 5u ? 1u : 30u;
                    if (glyph == 19u) return row == 0u ? 31u : 4u;
                    if (glyph == 20u) return row == 6u ? 14u : 17u;
                    if (glyph == 21u) return row <= 4u ? 17u : row == 5u ? 10u : 4u;
                    if (glyph == 22u) return row == 6u ? 10u : row >= 3u ? 21u : 17u;
                    if (glyph == 23u) return row == 0u || row == 6u ? 17u : row == 1u || row == 5u ? 10u : 4u;
                    if (glyph == 24u) return row <= 2u ? 17u : row == 3u ? 10u : 4u;
                    return row == 0u ? 31u : row == 1u || row == 5u ? 2u : row == 2u || row == 4u ? 4u : 8u;
                }
                return 0u;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The CPU supplies an integer cell size: 5x7 glyph plus one spacing column.
                float cellWidth = max(_HoDebugTextCell.x, 6.0);
                float fontHeight = max(_HoDebugTextCell.y, 7.0);
                float glyphScale = max(_HoDebugTextCell.z, 1.0);
                float2 uv = input.texcoord;
                if (_HoDebugTextFlipY > 0.5) uv.y = 1.0 - uv.y;
                float2 pixel = uv * float2(max(_HoDebugTextLength, 1.0) * cellWidth, fontHeight);
                int charIndex = (int)floor(pixel.x / cellWidth);
                int col = (int)floor(fmod(pixel.x, cellWidth) / glyphScale);
                int row = 6 - (int)floor(pixel.y / glyphScale);
                bool inGlyph = col >= 0 && col < 5 && row >= 0 && row < 7;
                uint bits = inGlyph ? GlyphRow(TextChar(clamp(charIndex, 0, 39)), (uint)row) : 0u;
                half glyph = inGlyph ? (half)((bits >> (uint)(4 - col)) & 1u) : 0.0h;
                half3 background = half3(0.015h, 0.018h, 0.022h);
                half3 color = lerp(background, _HoDebugTextColor.rgb, glyph);
                return half4(color, glyph > 0.0h ? 0.98h : 0.84h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
