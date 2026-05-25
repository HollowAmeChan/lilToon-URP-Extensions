using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawSpeedLinesElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureSpeedLinesDefaults(parameters0, parameters1, parameters2, parameters3);

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
                includeColor: true,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;
            p0.x = 0.5f;
            p0.y = 0.5f;
            p2.y = 0.0f;
            y = DrawSliderLine(rect.x, y, rect.width, "中心留白", Mathf.Clamp01(p0.z), 0.0f, 1.0f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "覆盖量", Mathf.Clamp01(p0.w), 0.0f, 1.0f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "线条密度", Mathf.Clamp(p1.x, 8.0f, 180.0f), 8.0f, 180.0f, value => p1.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "线宽", Mathf.Clamp01(p1.y), 0.0f, 1.0f, value => p1.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "长度变化", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "柔边", Mathf.Clamp01(p1.w), 0.0f, 1.0f, value => p1.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "跳动速度", Mathf.Clamp(p2.x, 0.0f, 12.0f), 0.0f, 12.0f, value => p2.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "闪动", Mathf.Clamp01(p2.z), 0.0f, 1.0f, value => p2.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "层次", Mathf.Clamp01(p3.x), 0.0f, 1.0f, value => p3.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "背景压暗", Mathf.Clamp01(p3.y), 0.0f, 1.0f, value => p3.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "线条残缺", Mathf.Clamp01(p3.w), 0.0f, 1.0f, value => p3.w = value);
            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;

            EditorGUI.indentLevel--;
        }

        private static void EnsureSpeedLinesDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.5f, 0.5f, 0.34f, 0.82f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(88.0f, 0.24f, 0.72f, 0.05f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(2.0f, 0.0f, 0.16f, 2.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.55f, 0.10f, 10.0f, 0.10f);
            }
        }
    }
}
