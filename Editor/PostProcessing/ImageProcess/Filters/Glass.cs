using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static readonly string[] GlassShapeNames =
        {
            "矩形",
            "圆角矩形",
            "多边形"
        };

        private static readonly string[] GlassQualityNames =
        {
            "低",
            "中",
            "高"
        };

        private void DrawGlassElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGlassDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsImageProcessGlassViewControlActive(element))
                {
                    StopImageProcessGlassViewControl();
                }

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
            y = DrawImageProcessGlassViewControlButton(rect, y, element);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 p3 = parameters3.vector4Value;

            int shape = Mathf.Clamp(Mathf.RoundToInt(p1.y), 0, GlassShapeNames.Length - 1);
            shape = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "形状", shape, GlassShapeNames);
            p1.y = shape;
            y += LineHeight + LineSpacing;

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 X", Mathf.Clamp01(p0.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 Y", Mathf.Clamp01(p0.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "宽度", Mathf.Clamp(p0.z, 0.02f, 2.0f), 0.02f, 2.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "高度", Mathf.Clamp(p0.w, 0.02f, 2.0f), 0.02f, 2.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转", Mathf.Clamp(p1.x, -180.0f, 180.0f), -180.0f, 180.0f);
            y += LineHeight + LineSpacing;

            EditorGUI.BeginDisabledGroup(shape != 1);
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "圆角半径", Mathf.Clamp(p1.z, 0.0f, 0.5f), 0.0f, 0.5f);
            EditorGUI.EndDisabledGroup();
            y += LineHeight + LineSpacing;

            EditorGUI.BeginDisabledGroup(shape != 2);
            p1.w = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "多边形边数", Mathf.Clamp(Mathf.RoundToInt(p1.w), 3, 12), 3, 12);
            EditorGUI.EndDisabledGroup();
            y += LineHeight + LineSpacing;

            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "模糊半径", Mathf.Clamp(p2.x, 0.0f, 32.0f), 0.0f, 32.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "采样质量", Mathf.Clamp(Mathf.RoundToInt(p2.y), 0, 2), GlassQualityNames);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘带宽", Mathf.Clamp(p2.z, 0.0f, 128.0f), 0.0f, 128.0f);
            y += LineHeight + LineSpacing;
            p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘柔化", Mathf.Clamp(p2.w, 0.0f, 64.0f), 0.0f, 64.0f);
            y += LineHeight + LineSpacing;

            p3.x = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "边缘位移", p3.x > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            EditorGUI.BeginDisabledGroup(p3.x <= 0.5f);
            p3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位移强度", Mathf.Clamp(p3.y, 0.0f, 32.0f), 0.0f, 32.0f);
            EditorGUI.EndDisabledGroup();
            y += LineHeight + LineSpacing;
            p3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘乘图像色程度", Mathf.Clamp01(p3.z), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "玻璃不透明度", Mathf.Clamp01(p3.w), 0.0f, 1.0f);

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = p3;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGlassDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.5f, 0.5f, 0.55f, 0.35f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.0f, 0.0f, 0.06f, 6.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(6.0f, 1.0f, 3.0f, 2.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.0f, 2.0f, 0.35f, 1.0f);
            }
        }
    }
}
