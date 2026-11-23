using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Debugging
{
    /// <summary>Small final-color text HUD used by Ho-DebugPerformance.</summary>
    internal static class HoDebugTextOverlay
    {
        private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int TextChars = Shader.PropertyToID("_HoDebugTextChars");
        private static readonly int TextLength = Shader.PropertyToID("_HoDebugTextLength");
        private static readonly int TextCell = Shader.PropertyToID("_HoDebugTextCell");
        private static readonly int TextColor = Shader.PropertyToID("_HoDebugTextColor");
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static Material material;

        public static void DrawLines(RasterCommandBuffer cmd, IReadOnlyList<string> lines, Vector2Int size, Rect? viewport = null)
        {
            EnsureMaterial();
            if (material == null || lines == null) return;

            Rect targetViewport = viewport ?? new Rect(0, 0, size.x, size.y);
            int viewportWidth = Mathf.Max(1, Mathf.RoundToInt(targetViewport.width));
            int viewportHeight = Mathf.Max(1, Mathf.RoundToInt(targetViewport.height));
            int viewportX = Mathf.RoundToInt(targetViewport.x);
            int viewportY = Mathf.RoundToInt(targetViewport.y);
            int maxLength = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                maxLength = Mathf.Max(maxLength, Mathf.Clamp((lines[i] ?? string.Empty).Length, 0, 40));
            }

            int glyphScale = viewportWidth >= 640 && maxLength * 12 <= viewportWidth - 16 ? 2 : 1;
            int characterWidth = glyphScale * 6;
            int fontHeight = glyphScale * 7;
            int lineHeight = fontHeight + glyphScale * 2;
            Vector4[] chars = new Vector4[12];
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = (lines[lineIndex] ?? string.Empty).ToUpperInvariant();
                int length = Mathf.Clamp(line.Length, 0, 40);
                if (length == 0) continue;

                Array.Clear(chars, 0, chars.Length);
                for (int i = 0; i < length; i++)
                {
                    int chunk = i / 4;
                    int component = i % 4;
                    chars[chunk][component] = line[i];
                }

                int width = Mathf.Min(Mathf.Max(characterWidth, viewportWidth - 16), length * characterWidth);
                int height = fontHeight;
                int x = viewportX + 8;
                int y = viewportY + viewportHeight - 8 - (lineIndex + 1) * lineHeight;
                if (y < viewportY) break;

                Properties.Clear();
                Properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                Properties.SetVectorArray(TextChars, chars);
                Properties.SetFloat(TextLength, length);
                Properties.SetVector(TextCell, new Vector4(characterWidth, fontHeight, glyphScale, 0));
                Properties.SetColor(TextColor, new Color(0.96f, 0.96f, 0.90f, 0.96f));
                cmd.SetViewport(new Rect(x, y, width, height));
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, Properties);
            }

            cmd.SetViewport(targetViewport);
        }

        private static void EnsureMaterial()
        {
            if (material != null) return;
            Shader shader = Shader.Find("Hidden/lilToon/URP/Debug/StatsOverlay");
            if (shader != null) material = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
