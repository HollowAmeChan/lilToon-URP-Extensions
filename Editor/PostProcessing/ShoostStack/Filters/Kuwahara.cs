using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] KuwaharaQualityNames =
        {
            "基础",
            "平衡",
            "高质量",
        };

        private void DrawKuwaharaElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureKuwaharaDefaults(parameters0, parameters1, color);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element,
                includeBlendMode: false,
                includeColor: false,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;

            int quality = Mathf.Clamp(Mathf.RoundToInt(p0.y), 0, KuwaharaQualityNames.Length - 1);
            quality = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "质量", quality, KuwaharaQualityNames);
            p0.y = quality;
            y += LineHeight + LineSpacing;

            int maxRadius = quality == 2 ? 10 : 6;
            int radius = Mathf.Clamp(Mathf.RoundToInt(p0.x), 1, maxRadius);
            radius = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "半径", radius, 1, maxRadius);
            p0.x = radius;
            y += LineHeight + LineSpacing;

            p0.z = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "色阶", Mathf.Clamp(Mathf.RoundToInt(p0.z), 0, 32), 0, 32);
            y += LineHeight + LineSpacing;

            if (color != null && color.propertyType == SerializedPropertyType.Color)
            {
                color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("线稿颜色"), color.colorValue, true, false, false);
                y += LineHeight + LineSpacing;
            }

            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "线稿强度", Mathf.Clamp01(p1.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "线稿阈值", Mathf.Clamp01(p0.w), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "噪声强度", Mathf.Clamp(p1.y, 0.0f, 0.5f), 0.0f, 0.5f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureKuwaharaDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty color)
        {
            if (parameters0 != null &&
                parameters0.propertyType == SerializedPropertyType.Vector4 &&
                parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(3.0f, 1.0f, 0.0f, 0.1f);
            }

            if (parameters1 != null &&
                parameters1.propertyType == SerializedPropertyType.Vector4 &&
                parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.25f, 0.05f, 0.0f, 0.0f);
            }

            if (color != null && color.propertyType == SerializedPropertyType.Color && color.colorValue.maxColorComponent <= 0.0001f && color.colorValue.a <= 0.0001f)
            {
                color.colorValue = Color.black;
            }
        }
    }
}
