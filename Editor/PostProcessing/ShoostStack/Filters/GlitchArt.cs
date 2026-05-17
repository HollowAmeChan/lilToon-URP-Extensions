using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawGlitchArtElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGlitchArtDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: false,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            y = DrawSliderLine(rect.x, y, rect.width, "屏幕抖动", Mathf.Clamp01(p0.x), 0.0f, 1.0f, value => p0.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "行撕裂", Mathf.Clamp01(p0.y), 0.0f, 1.0f, value => p0.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "块状错位", Mathf.Clamp01(p0.z), 0.0f, 1.0f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "闪动", Mathf.Clamp01(p0.w), 0.0f, 1.0f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "色散", Mathf.Clamp01(p1.x), 0.0f, 1.0f, value => p1.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "节奏", Mathf.Clamp(p1.y, 0.0f, 8.0f), 0.0f, 8.0f, value => p1.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "噪点", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "条带密度", Mathf.Clamp(p1.w, 1.0f, 32.0f), 1.0f, 32.0f, value => p1.w = value);
            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;

            EditorGUI.indentLevel--;
        }

        private static void EnsureGlitchArtDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.18f, 0.34f, 0.24f, 0.12f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.42f, 1.10f, 0.04f, 7.0f);
            }
        }
    }
}
