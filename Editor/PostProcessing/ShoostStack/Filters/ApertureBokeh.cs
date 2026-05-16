using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] ApertureBokehQualityNames =
        {
            "快速",
            "平衡",
            "高质量",
        };

        private static readonly string[] ApertureBokehBlendModeNames =
        {
            "相加",
            "滤色",
            "叠加",
            "正常",
        };

        private void DrawApertureBokehElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureApertureBokehDefaults(parameters0, parameters1, parameters2, parameters3, color);

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
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;

            int quality = Mathf.Clamp(Mathf.RoundToInt(p1.w), 0, ApertureBokehQualityNames.Length - 1);
            quality = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "质量", quality, ApertureBokehQualityNames);
            p1.w = quality;
            y += LineHeight + LineSpacing;

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光圈大小", Mathf.Clamp01(p0.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光斑增益", Mathf.Clamp(p3.z, 0.0f, 8.0f), 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "亮度阈值", Mathf.Clamp(p0.y, 0.0f, 8.0f), 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阈值柔化", Mathf.Clamp(p0.z, 0.0f, 4.0f), 0.0f, 4.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "曝光", Mathf.Clamp(p0.w, 0.0f, 4.0f), 0.0f, 4.0f);
            y += LineHeight + LineSpacing;

            if (color != null && color.propertyType == SerializedPropertyType.Color)
            {
                color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("染色"), color.colorValue, true, true, true);
                y += LineHeight + LineSpacing;
            }

            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘提取", Mathf.Clamp01(p1.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光斑硬度", Mathf.Clamp01(p1.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            int blades = Mathf.Clamp(Mathf.RoundToInt(p2.x), 0, 12);
            blades = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "叶片数", blades, 0, 12);
            p2.x = blades;
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片曲率", Mathf.Clamp01(p2.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片旋转", Mathf.Clamp(p2.z, 0.0f, 360.0f), 0.0f, 360.0f);
            y += LineHeight + LineSpacing;
            p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色散", Mathf.Clamp(p2.w, 0.0f, 1.0f), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            int blendMode = Mathf.Clamp(Mathf.RoundToInt(p3.x), 0, ApertureBokehBlendModeNames.Length - 1);
            blendMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "叠加模式", blendMode, ApertureBokehBlendModeNames);
            p3.x = blendMode;
            y += LineHeight + LineSpacing;
            p3.y = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "只显示光斑层", p3.y > 0.5f) ? 1.0f : 0.0f;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            EditorGUI.indentLevel--;
        }

        private static void EnsureApertureBokehDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty color)
        {
            if (parameters0 != null &&
                parameters0.propertyType == SerializedPropertyType.Vector4 &&
                parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.0f, 0.4f, 0.2f, 1.0f);
            }

            if (parameters1 != null &&
                parameters1.propertyType == SerializedPropertyType.Vector4 &&
                parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.35f, 1.0f, 0.0f, 2.0f);
            }

            if (parameters2 != null &&
                parameters2.propertyType == SerializedPropertyType.Vector4 &&
                parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(0.0f, 1.0f, 0.0f, 0.35f);
            }

            if (parameters3 != null &&
                parameters3.propertyType == SerializedPropertyType.Vector4 &&
                parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.0f, 0.0f, 4.0f, 0.0f);
            }

            if (color != null && color.propertyType == SerializedPropertyType.Color && color.colorValue.maxColorComponent <= 0.0001f)
            {
                color.colorValue = Color.white;
            }
        }
    }
}
