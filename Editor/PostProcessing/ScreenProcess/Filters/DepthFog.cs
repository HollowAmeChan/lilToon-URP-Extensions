using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] DepthFogDepthModes = { "直线", "指数", "指数平方" };

        private static readonly string[] DepthFogHeightModes =
        {
            "高度窗（下方浓）", "高度窗（上方浓）", "指数衰减（下方浓）", "指数衰减（上方浓）"
        };

        private static readonly string[] DepthFogHeightReferences = { "世界高度", "相机相对" };

        private static readonly string[] DepthFogSkyModes = { "跳过天空", "一起上雾", "单独天空色" };

        /// <summary>
        /// Rows the fog UI draws after the layer core fields and the rule-mask header. The two slots
        /// have their own switch; a switched-off slot only draws its toggle row.
        /// </summary>
        private static int GetDepthFogLineCount(SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty color = element.FindPropertyRelative("color");
            if (parameters0 == null || parameters1 == null || parameters2 == null ||
                parameters3 == null || parameters4 == null || parameters5 == null || color == null)
            {
                // The draw function bails out on the same condition, so nothing beyond the layer core
                // fields and the rule-mask header is drawn.
                return 0;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p5 = parameters5.vector4Value;

            int count = 1; // depth slot toggle
            if (p0.x > 0.5f)
            {
                count += 8; // mode, start, far, density, max opacity, far colour, far mix, desaturate
            }

            count += 1; // height slot toggle
            if (p2.w > 0.5f)
            {
                count += 7; // mode, reference, A, B, hardness, max opacity, colour
            }

            count += 1; // sky mode
            int skyMode = Mathf.Clamp(Mathf.RoundToInt(p5.y), 0, DepthFogSkyModes.Length - 1);
            if (skyMode != 0)
            {
                count += 1; // sky strength
            }

            count += 1; // dither
            return count;
        }

        private void DrawDepthFogProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty color = element.FindPropertyRelative("color");
            if (parameters0 == null || parameters1 == null || parameters2 == null ||
                parameters3 == null || parameters4 == null || parameters5 == null || color == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            EnsureDepthFogDefaults(ref p0, ref p1, ref p2, ref p3, ref p4, ref p5, color);

            // ---- slot 1: depth (distance) fog ------------------------------------------------
            p0.x = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("深度雾", "按距离生成的雾层。颜色一栏是它的近色（雾薄处的颜色）。"),
                p0.x > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (p0.x > 0.5f)
            {
                int depthMode = Mathf.Clamp(Mathf.RoundToInt(p0.y), 0, DepthFogDepthModes.Length - 1);
                depthMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "距离曲线", depthMode, DepthFogDepthModes);
                p0.y = depthMode;
                y += LineHeight + LineSpacing;

                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "起始距离", p0.z, 0.0f, 500.0f);
                y += LineHeight + LineSpacing;
                p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "终点距离", p0.w, 1.0f, 2000.0f);
                y += LineHeight + LineSpacing;
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "密度", p1.x, 0.0f, 0.2f);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "浓度上限", p1.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;

                p2 = DrawDepthFogColorLine(rect, y, "远色", p2);
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "远色混合", p1.z, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "空气感（去饱和）", p1.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            // ---- slot 2: height fog -----------------------------------------------------------
            p2.w = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("高度雾", "按世界高度生成的雾层，可以和深度雾同时开。"),
                p2.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (p2.w > 0.5f)
            {
                int heightMode = Mathf.Clamp(Mathf.RoundToInt(p3.x), 0, DepthFogHeightModes.Length - 1);
                heightMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "高度曲线", heightMode, DepthFogHeightModes);
                p3.x = heightMode;
                y += LineHeight + LineSpacing;

                int heightReference = Mathf.Clamp(Mathf.RoundToInt(p3.y), 0, DepthFogHeightReferences.Length - 1);
                heightReference = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "高度参考", heightReference, DepthFogHeightReferences);
                p3.y = heightReference;
                y += LineHeight + LineSpacing;

                bool windowMode = heightMode < 2;
                p3.z = EditorGUI.Slider(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    windowMode ? "起点高度" : "基准高度",
                    p3.z,
                    -50.0f,
                    200.0f);
                y += LineHeight + LineSpacing;
                p3.w = EditorGUI.Slider(
                    new Rect(rect.x, y, rect.width, LineHeight),
                    windowMode ? "终点高度" : "衰减率",
                    p3.w,
                    windowMode ? -50.0f : 0.0f,
                    windowMode ? 200.0f : 0.5f);
                y += LineHeight + LineSpacing;
                p4.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "过渡硬度", p4.x, 0.1f, 8.0f);
                y += LineHeight + LineSpacing;
                p4.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "高度雾浓度", p4.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;

                DrawDepthFogHeightColorLine(rect, y, "高度雾颜色", ref p4, ref p5);
                y += LineHeight + LineSpacing;
            }

            // ---- shared ------------------------------------------------------------------------
            int skyMode = Mathf.Clamp(Mathf.RoundToInt(p5.y), 0, DepthFogSkyModes.Length - 1);
            skyMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "天空", skyMode, DepthFogSkyModes);
            p5.y = skyMode;
            y += LineHeight + LineSpacing;

            if (skyMode != 0)
            {
                p5.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "天空强度", p5.z, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            p5.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "抖动", p5.w, 0.0f, 4.0f);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
        }

        /// <summary>Fog colours live in Vector4 slots (rgb is used; w stays free), so they need their own row.</summary>
        private static Vector4 DrawDepthFogColorLine(Rect rect, float y, string label, Vector4 value)
        {
            Color color = new Color(value.x, value.y, value.z, 1.0f);
            color = EditorGUI.ColorField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent(label),
                color,
                false,
                true,
                false);
            return new Vector4(color.r, color.g, color.b, value.w);
        }

        /// <summary>The height colour is split across two Vector4 slots (p4.zw + p5.x).</summary>
        private static void DrawDepthFogHeightColorLine(Rect rect, float y, string label, ref Vector4 p4, ref Vector4 p5)
        {
            Color color = new Color(p4.z, p4.w, p5.x, 1.0f);
            color = EditorGUI.ColorField(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent(label),
                color,
                false,
                true,
                false);
            p4.z = color.r;
            p4.w = color.g;
            p5.x = color.b;
        }

        private static void EnsureDepthFogDefaults(
            ref Vector4 p0,
            ref Vector4 p1,
            ref Vector4 p2,
            ref Vector4 p3,
            ref Vector4 p4,
            ref Vector4 p5,
            SerializedProperty color)
        {
            bool untouched = p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero &&
                p3 == Vector4.zero && p4 == Vector4.zero && p5 == Vector4.zero;
            if (!untouched)
            {
                return;
            }

            p0 = new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 400.0f);
            p1 = new Vector4(0.01f, 0.6f, 1.0f, 0.3f);
            p2 = new Vector4(0.49f, 0.58f, 0.71f, 0.0f);
            p3 = new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 12.0f);
            p4 = new Vector4(1.0f, 0.5f, 0.85f, 0.88f);
            p5 = new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Skip, 0.4f, 0.5f);

            if (color != null)
            {
                color.colorValue = new Color(0.66f, 0.71f, 0.76f, 1.0f);
            }
        }
    }
}
