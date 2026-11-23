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
        private static readonly int TextFlipY = Shader.PropertyToID("_HoDebugTextFlipY");
        private static readonly int TextColor = Shader.PropertyToID("_HoDebugTextColor");
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static Material material;

        public static void DrawLines(RasterCommandBuffer cmd, IReadOnlyList<HoDebugPerformanceLine> lines, Vector2Int size,
            Rect? viewport = null, bool flipY = false)
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
                HoDebugPerformanceSegment[] segments = lines[i].Segments;
                int lineLength = 0;
                if (segments != null)
                {
                    for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                    {
                        lineLength = Mathf.Min(40, lineLength + (segments[segmentIndex].Text ?? string.Empty).Length);
                        if (lineLength == 40) break;
                    }
                }
                maxLength = Mathf.Max(maxLength, lineLength);
            }

            int glyphScale = viewportWidth >= 640 && maxLength * 12 <= viewportWidth - 16 ? 2 : 1;
            int characterWidth = glyphScale * 6;
            int fontHeight = glyphScale * 7;
            int lineHeight = fontHeight + glyphScale * 2;
            Vector4[] chars = new Vector4[12];
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                HoDebugPerformanceLine lineData = lines[lineIndex];
                int height = fontHeight;
                int y = viewportY + viewportHeight - 8 - (lineIndex + 1) * lineHeight;
                if (y < viewportY) break;

                HoDebugPerformanceSegment[] segments = lineData.Segments;
                if (segments == null) continue;

                int x = viewportX + 8;
                int remaining = 40;
                int viewportRight = viewportX + viewportWidth - 8;
                for (int segmentIndex = 0; segmentIndex < segments.Length && remaining > 0; segmentIndex++)
                {
                    HoDebugPerformanceSegment segment = segments[segmentIndex];
                    string text = (segment.Text ?? string.Empty).ToUpperInvariant();
                    int length = Mathf.Min(text.Length, remaining);
                    if (length == 0) continue;
                    if (x >= viewportRight) break;

                    int visibleLength = Mathf.Min(length, Mathf.Max(0, (viewportRight - x) / characterWidth));
                    if (visibleLength == 0) break;

                    Array.Clear(chars, 0, chars.Length);
                    for (int i = 0; i < visibleLength; i++)
                    {
                        int chunk = i / 4;
                        int component = i % 4;
                        chars[chunk][component] = text[i];
                    }

                    int width = visibleLength * characterWidth;
                    Properties.Clear();
                    Properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                    Properties.SetVectorArray(TextChars, chars);
                    Properties.SetFloat(TextLength, visibleLength);
                    Properties.SetVector(TextCell, new Vector4(characterWidth, fontHeight, glyphScale, 0));
                    Properties.SetFloat(TextFlipY, flipY ? 1.0f : 0.0f);
                    Properties.SetColor(TextColor, segment.Color);
                    cmd.SetViewport(new Rect(x, y, width, height));
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, Properties);

                    x += width;
                    remaining -= visibleLength;
                    if (visibleLength < length) break;
                }
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
