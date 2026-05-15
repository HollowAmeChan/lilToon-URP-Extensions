using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] GradientModeNames = { "单色", "线性", "圆形", "椭圆" };

        private static int GetGradientLineCount(SerializedProperty element)
        {
            int mode = GetGradientMode(element);
            int count = 6;
            if (mode != 0)
            {
                count += 4;
            }

            if (mode == 1)
            {
                count += 1;
            }

            if (mode == 3)
            {
                count += 2;
            }

            return count;
        }

        private static int GetGradientMode(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return 2;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                return 2;
            }

            return Mathf.Clamp(Mathf.RoundToInt(value.x), 0, 3);
        }

        private void DrawGradientElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGradientDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 background = parameters3.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, 3);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, GradientModeNames);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            y = DrawBlendModeLine(rect.x, y, rect.width, blendMode);
            y = DrawPropertyLine(rect.x, y, rect.width, color, "颜色 1");
            background = DrawVectorColorLine(rect.x, y, rect.width, "颜色 2", background);
            y += LineHeight + LineSpacing;

            p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "反转颜色", p1.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (mode != 0)
            {
                p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", p0.y, 0.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", p0.z, 0.0f, 10.0f);
                y += LineHeight + LineSpacing;
                p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "偏移 X", p1.x, -3.0f, 3.0f);
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "偏移 Y", p1.y, -3.0f, 3.0f);
                y += LineHeight + LineSpacing;
            }

            if (mode == 1)
            {
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p1.z, -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
            }

            if (mode == 3)
            {
                p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "横向缩放", p2.x, 0.1f, 3.0f);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "纵向缩放", p2.y, 0.1f, 3.0f);
                y += LineHeight + LineSpacing;
            }

            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = background;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGradientDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.0f, 1.0f, 5.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = Vector4.zero;
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(1.0f, 0.5f, 1.0f, 0.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            }
        }
    }
}
