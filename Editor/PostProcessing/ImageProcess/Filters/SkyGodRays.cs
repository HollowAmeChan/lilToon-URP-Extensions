using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawSkyGodRaysElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureSkyGodRaysDefaults(parameters0, parameters1, parameters2, parameters3);

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
            y = DrawSkyGodRaysSection(rect.x, y, rect.width, "分形噪声");
            y = DrawSliderLine(rect.x, y, rect.width, "光斑宽度", Mathf.Clamp(p1.x, 40.0f, 600.0f), 40.0f, 600.0f, value => p1.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "光斑高度", Mathf.Clamp(p1.y, 30.0f, 420.0f), 30.0f, 420.0f, value => p1.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "对比度", Mathf.Clamp(p1.z, 50.0f, 600.0f), 50.0f, 600.0f, value => p1.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "亮度", Mathf.Clamp(p1.w, -150.0f, 50.0f), -150.0f, 50.0f, value => p1.w = value);

            y = DrawSkyGodRaysSection(rect.x, y, rect.width, "VR 色差");
            y = DrawSliderLine(rect.x, y, rect.width, "分离强度", Mathf.Clamp(p2.x, 0.0f, 2.0f), 0.0f, 2.0f, value => p2.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "衰减距离", Mathf.Clamp(p2.y, 10.0f, 240.0f), 10.0f, 240.0f, value => p2.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "演化速度", Mathf.Clamp(p2.z, 0.0f, 3.0f), 0.0f, 3.0f, value => p2.z = value);

            y = DrawSkyGodRaysSection(rect.x, y, rect.width, "径向模糊（缩放）");
            y = DrawSliderLine(rect.x, y, rect.width, "中心 X", Mathf.Clamp(p0.x, -0.25f, 1.25f), -0.25f, 1.25f, value => p0.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "中心 Y", Mathf.Clamp(p0.y, -0.25f, 1.25f), -0.25f, 1.25f, value => p0.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "模糊量", Mathf.Clamp(p0.z, 0.0f, 200.0f), 0.0f, 200.0f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "采样数", Mathf.Clamp(p3.x, 6.0f, 40.0f), 6.0f, 40.0f, value => p3.x = value);

            y = DrawSkyGodRaysSection(rect.x, y, rect.width, "颜色减淡合成");
            y = DrawSliderLine(rect.x, y, rect.width, "图层曝光", Mathf.Clamp(p0.w, 0.0f, 2.5f), 0.0f, 2.5f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "遮罩柔和", Mathf.Clamp01(p3.y), 0.0f, 1.0f, value => p3.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "单层预览", Mathf.Clamp01(p3.z), 0.0f, 1.0f, value => p3.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "颜色减淡", Mathf.Clamp01(p3.w), 0.0f, 1.0f, value => p3.w = value);
            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;

            EditorGUI.indentLevel--;
        }

        private static void EnsureSkyGodRaysDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.22f, 0.99f, 181.0f, 1.08f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(130.0f, 85.0f, 234.0f, -53.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(1.04f, 146.0f, 3.0f, 3.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(32.0f, 0.36f, 0.0f, 0.21f);
            }
        }

        private static float DrawSkyGodRaysSection(float x, float y, float width, string label)
        {
            Rect lineRect = new Rect(x, y, width, LineHeight);
            EditorGUI.LabelField(lineRect, label, EditorStyles.miniBoldLabel);
            return y + LineHeight + LineSpacing;
        }
    }
}
