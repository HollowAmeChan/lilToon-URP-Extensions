using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] GlowModeNames = { "正常", "条纹", "星芒" };

        private static int GetGlowLineCount(SerializedProperty element)
        {
            int count = 8;
            if (GetGlowMode(element) == 2)
            {
                count += 2;
            }

            return count;
        }

        private static int GetGlowMode(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.w), 0, 2);
        }

        private void DrawGlowElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGlowDefaults(parameters0, parameters1, parameters2);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            Vector4 threshold = parameters0.vector4Value;
            Vector4 look = parameters1.vector4Value;
            Vector4 star = parameters2.vector4Value;

            threshold.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阈值", Mathf.Clamp01(threshold.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            threshold.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阈值平滑", Mathf.Clamp01(threshold.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            threshold.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", Mathf.Clamp(threshold.z, 0.0f, 6.0f), 0.0f, 6.0f);
            y += LineHeight + LineSpacing;
            look.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "强度", Mathf.Clamp(look.x, 0.0f, 12.0f), 0.0f, 12.0f);
            y += LineHeight + LineSpacing;
            look.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "饱和度", Mathf.Clamp(look.y, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;
            y = DrawPropertyLine(rect.x, y, rect.width, color, "颜色");
            look.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", Mathf.Clamp01(look.w), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            int mode = Mathf.Clamp(Mathf.RoundToInt(threshold.w), 0, 2);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "发光类型", mode, GlowModeNames);
            threshold.w = mode;
            y += LineHeight + LineSpacing;

            if (mode == 2)
            {
                star.x = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "星芒数量", Mathf.Clamp(Mathf.RoundToInt(star.x), 1, 6), 1, 6);
                y += LineHeight + LineSpacing;
                star.y = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "星芒角度", Mathf.Clamp(Mathf.RoundToInt(star.y), 0, 360), 0, 360);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = threshold;
            parameters1.vector4Value = look;
            parameters2.vector4Value = star;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGlowDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.9f, 0.0f, 3.0f, 0.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (Mathf.Abs(value.w) <= 0.000001f && Mathf.Abs(value.x) <= 0.000001f && Mathf.Abs(value.y) <= 0.000001f && Mathf.Abs(value.z) <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(2.0f, 0.0f, 0.0f, 1.0f);
                }
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(3.0f, 180.0f, 0.0f, 0.0f);
            }

        }
    }
}
