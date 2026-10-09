using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Debugging
{
    /// <summary>Only rectangles and draw properties; view formulas and input bindings stay feature-local.</summary>
    internal static class HoDebugGrid
    {
        public const int All = 255;
        private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        private static readonly MaterialPropertyBlock Properties = new();
        private static Material overlay;

        public static void EnsureOverlay(Shader preferredShader = null)
        {
            if (overlay != null) return;
            Shader shader = preferredShader != null ? preferredShader : Shader.Find("Hidden/lilToon/URP/Debug/GridOverlay");
            if (shader != null) overlay = CoreUtils.CreateEngineMaterial(shader);
        }

        public static Vector4 Rect(int index, int count)
        {
            int side = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count)));
            float size = 1f / side;
            return new Vector4((index % side) * size, (index / side) * size, size, size);
        }

        public static void Draw(RasterCommandBuffer cmd, Material material, int selectedMode, int modeId,
            HoDebugViewInfo[] views, Vector2Int size, Vector4? modeVector = null, int shaderPass = 0, bool integerMode = false, int modeComponent = 0)
        {
            int count = selectedMode == All ? views.Length : 1;
            for (int i = 0; i < count; i++)
            {
                int mode = selectedMode == All ? views[i].ModeValue : selectedMode;
                Vector4 rect = Rect(i, count);
                cmd.SetViewport(new UnityEngine.Rect(rect.x * size.x, (1f - rect.y - rect.w) * size.y, rect.z * size.x, rect.w * size.y));
                Properties.Clear();
                Properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                if (modeVector.HasValue)
                {
                    Vector4 value = modeVector.Value;
                    value[modeComponent] = mode;
                    Properties.SetVector(modeId, value);
                }
                else if (integerMode) Properties.SetInteger(modeId, mode);
                else Properties.SetFloat(modeId, mode);
                cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 3, 1, Properties);
                if (selectedMode == All && overlay != null)
                {
                    DrawLabel(cmd, views[i].ShortName, count);
                }
            }
            cmd.SetViewport(new UnityEngine.Rect(0, 0, size.x, size.y));
        }

        public static void DrawLabel(RasterCommandBuffer cmd, string label, int count)
        {
            if (overlay == null) return;
            Properties.Clear();
            Properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
            float side = Mathf.Ceil(Mathf.Sqrt(count));
            Properties.SetVector("_HoDebugTileGrid", new Vector4(side, side, count, 0));
            label = label.ToUpperInvariant();
            for (int chunk = 0; chunk < 4; chunk++)
            {
                Vector4 chars = Vector4.zero;
                for (int c = 0; c < 4; c++)
                {
                    int index = chunk * 4 + c;
                    chars[c] = index < label.Length ? label[index] : ' ';
                }
                Properties.SetVector("_HoDebugTileLabel" + chunk, chars);
            }
            cmd.DrawProcedural(Matrix4x4.identity, overlay, 0, MeshTopology.Triangles, 3, 1, Properties);
        }

        public static HoDebugViewInfo[] EnumViews<T>(string feature, string shaderName, string shaderPath, params string[] labels) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var views = new System.Collections.Generic.List<HoDebugViewInfo>();
            foreach (T value in values)
            {
                int mode = Convert.ToInt32(value);
                if (mode == 0 || mode == All) continue;
                string label = views.Count < labels.Length ? labels[views.Count] : value.ToString();
                views.Add(new HoDebugViewInfo(feature, feature + "." + value, label, mode,
                    shaderName, shaderPath, true, "Feature input unavailable."));
            }
            return views.ToArray();
        }
    }
}
