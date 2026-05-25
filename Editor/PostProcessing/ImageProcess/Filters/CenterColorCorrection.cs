using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawCenterColorCorrectionElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureCenterColorCorrectionDefaults(parameters0, parameters1, parameters2);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsImageProcessCenterRadiusViewControlActive(element))
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);
            y = DrawImageProcessCenterRadiusViewControlButton(rect, y, element);

            Vector4 colorParams = parameters0.vector4Value;
            Vector4 maskParams = parameters1.vector4Value;
            Vector4 opacityParams = parameters2.vector4Value;

            colorParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "饱和度", Mathf.Clamp(colorParams.x, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            opacityParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色相", Mathf.Clamp(opacityParams.y, -180.0f, 180.0f), -180.0f, 180.0f);
            y += LineHeight + LineSpacing;

            colorParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "亮度", Mathf.Clamp(colorParams.y, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            colorParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度", Mathf.Clamp(colorParams.z, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            colorParams.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "反相", colorParams.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            maskParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", Mathf.Clamp(maskParams.x, 0.0f, 1.0f), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            maskParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", Mathf.Clamp(maskParams.y, 0.0f, 1.0f), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            maskParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心位置 X", Mathf.Clamp(maskParams.z, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            maskParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心位置 Y", Mathf.Clamp(maskParams.w, -1.0f, 1.0f), -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            opacityParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", Mathf.Clamp01(opacityParams.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = colorParams;
            parameters1.vector4Value = maskParams;
            parameters2.vector4Value = opacityParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureCenterColorCorrectionDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.18f, 0.0f, 0.0f, 0.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.5f, 0.5f, 0.0f, 0.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
