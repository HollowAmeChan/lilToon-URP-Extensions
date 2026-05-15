using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static bool GetTubeUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawTubeElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureTubeDefaults(parameters0, parameters1, parameters2);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 tubeParams0 = parameters0.vector4Value;
            Vector4 tubeParams1 = parameters1.vector4Value;
            Vector4 tubeParams2 = parameters2.vector4Value;

            int resolutionType = Mathf.Clamp(Mathf.RoundToInt(tubeParams0.x), 0, 1);
            resolutionType = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率模式", resolutionType, new[] { "游戏视图", "自定义尺寸" });
            tubeParams0.x = resolutionType;
            y += LineHeight + LineSpacing;
            if (resolutionType == 1)
            {
                tubeParams0.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(tubeParams0.y), 1, 8192));
                y += LineHeight + LineSpacing;
                tubeParams0.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(tubeParams0.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            tubeParams0.w = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "降采样", Mathf.RoundToInt(tubeParams0.w), 1, 4));
            y += LineHeight + LineSpacing;
            tubeParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "拖影", tubeParams1.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            tubeParams1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色边", tubeParams1.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            tubeParams1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", tubeParams1.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            tubeParams1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "扫描线", tubeParams1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            tubeParams2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "扫描线宽度", tubeParams2.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = tubeParams0;
            parameters1.vector4Value = tubeParams1;
            parameters2.vector4Value = tubeParams2;
            EditorGUI.indentLevel--;
        }

        private static void EnsureTubeDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.0f, 1920.0f, 1080.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.5f, 0.0f, 1.0f, 0.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = Vector4.zero;
            }
        }
    }
}
