using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawLevelAdjustmentElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureLevelAdjustmentDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 rgbParams = parameters0.vector4Value;
            Vector4 rgbModeParams = parameters1.vector4Value;
            Vector4 channelParams = parameters2.vector4Value;
            Vector4 channelOutputParams = parameters3.vector4Value;

            int channel = Mathf.Clamp(Mathf.RoundToInt(rgbModeParams.y), 0, 3);
            channel = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "调整范围", channel, new[] { "RGB", "红通道", "绿通道", "蓝通道" });
            rgbModeParams.y = channel;
            y += LineHeight + LineSpacing;

            if (channel == 0)
            {
                rgbParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入黑场", rgbParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入白场", rgbParams.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "伽马", rgbParams.z, 0.01f, 10.0f);
                y += LineHeight + LineSpacing;
                rgbParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出黑场", rgbParams.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbModeParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出白场", rgbModeParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }
            else
            {
                channelParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入黑场", channelParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入白场", channelParams.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "伽马", channelParams.z, 0.01f, 10.0f);
                y += LineHeight + LineSpacing;
                channelParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出黑场", channelParams.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelOutputParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出白场", channelOutputParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = rgbParams;
            parameters1.vector4Value = rgbModeParams;
            parameters2.vector4Value = channelParams;
            parameters3.vector4Value = channelOutputParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureLevelAdjustmentDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty parameters3)
        {
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null)
            {
                return;
            }

            if (parameters0.propertyType != SerializedPropertyType.Vector4
                || parameters1.propertyType != SerializedPropertyType.Vector4
                || parameters2.propertyType != SerializedPropertyType.Vector4
                || parameters3.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 rgbParams = parameters0.vector4Value;
            Vector4 rgbModeParams = parameters1.vector4Value;
            Vector4 channelParams = parameters2.vector4Value;
            Vector4 channelOutputParams = parameters3.vector4Value;

            bool needsReset = Mathf.Abs(channelOutputParams.w - LevelAdjustmentInitMarker) > 0.001f;
            if (needsReset)
            {
                rgbParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
                rgbModeParams = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                channelParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
                channelOutputParams = new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker);
            }

            parameters0.vector4Value = rgbParams;
            parameters1.vector4Value = rgbModeParams;
            parameters2.vector4Value = channelParams;
            parameters3.vector4Value = channelOutputParams;
        }
    }
}
