using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private static readonly string[] PostLightingModes =
        {
            "屏幕直线渐变",
            "中心渐变",
            "MatCap 渐变"
        };

        private static int GetPostLightingLineCount(SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            int mode = parameters0 != null ? Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, PostLightingModes.Length - 1) : 0;
            if (mode == 1)
            {
                return 16;
            }

            return mode == 2 ? 14 : 15;
        }

        private void DrawPostLightingProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null || parameters4 == null || parameters5 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            Vector4 p4 = parameters4.vector4Value;
            Vector4 p5 = parameters5.vector4Value;
            EnsurePostLightingDefaults(ref p0, ref p1, ref p2, ref p3, ref p4, ref p5);

            int mode = EditorGUI.Popup(
                new Rect(rect.x, y, rect.width, LineHeight),
                "打光模式",
                Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, PostLightingModes.Length - 1),
                PostLightingModes);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            if (mode == 1)
            {
                if (IsScreenProcessDirectionDistanceViewControlActive(element))
                {
                    ScreenProcessDirectionDistanceViewControl.Stop();
                }

                y = DrawScreenProcessCenterRadiusViewControlButton(rect, y, element);
            }
            else
            {
                if (IsScreenProcessCenterRadiusViewControlActive(element))
                {
                    ScreenProcessCenterRadiusViewControl.Stop();
                }

                y = DrawScreenProcessDirectionDistanceViewControlButton(rect, y, element);
            }

            Color colorA = new Color(p3.x, p3.y, p3.z, p3.w);
            Color colorB = new Color(p4.x, p4.y, p4.z, p4.w);
            colorA = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), "亮部颜色", colorA);
            y += LineHeight + LineSpacing;
            colorB = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), "未照射补色", colorB);
            y += LineHeight + LineSpacing;
            p3 = new Vector4(colorA.r, colorA.g, colorA.b, colorA.a);
            p4 = new Vector4(colorB.r, colorB.g, colorB.b, colorB.a);

            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "HDR 亮度", p0.y, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线影响", p1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            if (mode == 1)
            {
                p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 X", p2.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 Y", p2.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", p2.z, 0.01f, 1.5f);
                y += LineHeight + LineSpacing;
                p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔边", p2.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }
            else
            {
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向角度", p1.x, -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
                if (mode == 0)
                {
                    p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐变宽度", p1.y, 0.02f, 2.0f);
                    y += LineHeight + LineSpacing;
                    p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "渐变偏移", p1.z, -1.0f, 1.0f);
                    y += LineHeight + LineSpacing;
                }
                else
                {
                    p5.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "MatCap 聚焦", p5.w, 0.0f, 4.0f);
                    y += LineHeight + LineSpacing;
                    p5.z = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "反转 MatCap", p5.z > 0.5f) ? 1.0f : 0.0f;
                    y += LineHeight + LineSpacing;
                }
            }

            p5.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "暗部压低", p5.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            parameters4.vector4Value = p4;
            parameters5.vector4Value = p5;
        }

        private static void EnsurePostLightingDefaults(ref Vector4 p0, ref Vector4 p1, ref Vector4 p2, ref Vector4 p3, ref Vector4 p4, ref Vector4 p5)
        {
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero && p3 == Vector4.zero && p4 == Vector4.zero && p5 == Vector4.zero)
            {
                p0 = new Vector4(0.0f, 0.55f, 0.18f, 0.38f);
                p1 = new Vector4(90.0f, 1.15f, 0.06f, 0.55f);
                p2 = new Vector4(0.5f, 0.58f, 0.62f, 0.28f);
                p3 = new Vector4(1.0f, 0.84f, 0.62f, 1.0f);
                p4 = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                p5 = new Vector4(0.35f, 0.28f, 0.0f, 0.45f);
            }
        }
    }
}
