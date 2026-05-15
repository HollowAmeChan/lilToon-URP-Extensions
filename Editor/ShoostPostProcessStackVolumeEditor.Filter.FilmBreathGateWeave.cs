using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawFilmBreathGateWeaveElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureFilmBreathGateWeaveDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 filmParams0 = parameters0.vector4Value;
            Vector4 filmParams1 = parameters1.vector4Value;
            Vector4 filmParams2 = parameters2.vector4Value;
            Vector4 filmParams3 = parameters3.vector4Value;

            filmParams0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置幅度", filmParams0.x, 0.0f, 0.1f);
            y += LineHeight + LineSpacing;
            filmParams0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "位置频率", filmParams0.y, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;
            filmParams0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转幅度", filmParams0.z, 0.0f, 0.1f);
            y += LineHeight + LineSpacing;
            filmParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "旋转频率", filmParams0.w, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;

            filmParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "缩放呼吸幅度", filmParams1.x, 0.0f, 0.1f);
            y += LineHeight + LineSpacing;
            filmParams1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "缩放呼吸频率", filmParams1.y, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;
            filmParams1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "曝光变化幅度", filmParams1.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            filmParams1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "曝光变化频率", filmParams1.w, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;

            filmParams2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度变化幅度", filmParams2.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            filmParams2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度变化频率", filmParams2.y, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;
            filmParams2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "颜色变化幅度", filmParams2.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            filmParams2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "颜色变化频率", filmParams2.w, 0.0f, 50.0f);
            y += LineHeight + LineSpacing;

            filmParams3.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不规则度", filmParams3.x, 0.0f, 2.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = filmParams0;
            parameters1.vector4Value = filmParams1;
            parameters2.vector4Value = filmParams2;
            parameters3.vector4Value = filmParams3;
            EditorGUI.indentLevel--;
        }

        private static void EnsureFilmBreathGateWeaveDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.02f, 20.0f, 0.05f, 15.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.005f, 5.0f, 0.2f, 15.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(0.1f, 12.0f, 0.1f, 16.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
