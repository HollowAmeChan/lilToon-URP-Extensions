using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] LensFlareBlendModeNames =
        {
            "相加",
            "滤色",
            "正常",
        };

        private void DrawLensFlareElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureLensFlareDefaults(parameters0, parameters1, parameters2, parameters3, color);

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

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "太阳位置 X", Mathf.Clamp(p0.x, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "太阳位置 Y", Mathf.Clamp(p0.y, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光晕方向", Mathf.Clamp(p0.z, -180.0f, 180.0f), -180.0f, 180.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "轴向长度", Mathf.Clamp(p0.w, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;

            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "太阳核心", Mathf.Clamp(p1.x, 0.0f, 0.25f), 0.0f, 0.25f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "外层光晕", Mathf.Clamp(p1.y, 0.0f, 1.0f), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "星芒长度", Mathf.Clamp(p1.z, 0.0f, 1.5f), 0.0f, 1.5f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "星芒层数", Mathf.Clamp(Mathf.RoundToInt(p1.w), 2, 8), 2, 8);
            y += LineHeight + LineSpacing;

            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "鬼影强度", Mathf.Clamp(p2.x, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "鬼影间距", Mathf.Clamp(p2.y, 0.0f, 1.6f), 0.0f, 1.6f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光环强度", Mathf.Clamp(p2.z, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "横向光带", Mathf.Clamp(p3.x, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;

            p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色散", Mathf.Clamp01(p2.w), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "曝光", Mathf.Clamp(p3.y, 0.0f, 6.0f), 0.0f, 6.0f);
            y += LineHeight + LineSpacing;

            if (color != null && color.propertyType == SerializedPropertyType.Color)
            {
                color.colorValue = EditorGUI.ColorField(new Rect(rect.x, y, rect.width, LineHeight), new GUIContent("颜色"), color.colorValue, true, true, true);
                y += LineHeight + LineSpacing;
            }

            int blendMode = Mathf.Clamp(Mathf.RoundToInt(p3.z), 0, LensFlareBlendModeNames.Length - 1);
            blendMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "叠加模式", blendMode, LensFlareBlendModeNames);
            p3.z = blendMode;
            y += LineHeight + LineSpacing;
            p3.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "只显示光晕层", p3.w > 0.5f) ? 1.0f : 0.0f;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            EditorGUI.indentLevel--;
        }

        private static void EnsureLensFlareDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty color)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(-0.38f, 0.32f, -18.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.065f, 0.34f, 0.92f, 6.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(0.78f, 1.0f, 0.55f, 0.55f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.85f, 2.4f, 0.0f, 0.0f);
            }

            if (color != null && color.propertyType == SerializedPropertyType.Color && color.colorValue.maxColorComponent <= 0.0001f)
            {
                color.colorValue = new Color(1.0f, 0.86f, 0.55f, 1.0f);
            }
        }
    }
}
