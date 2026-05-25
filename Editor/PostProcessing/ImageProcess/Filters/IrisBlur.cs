using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static bool GetIrisBlurUsesRgbBlur(SerializedProperty element)
        {
            SerializedProperty parameters3 = element?.FindPropertyRelative("parameters3");
            if (parameters3 == null || parameters3.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return parameters3.vector4Value.x > 0.5f;
        }

        private static bool GetIrisBlurUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawIrisBlurElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureIrisBlurDefaults(parameters0, parameters1, parameters2, parameters3);

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

            Vector4 irisParams0 = parameters0.vector4Value;
            Vector4 irisParams1 = parameters1.vector4Value;
            Vector4 irisParams2 = parameters2.vector4Value;
            Vector4 irisParams3 = parameters3.vector4Value;

            irisParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "模糊大小", irisParams1.x, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            irisParams2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心半径", irisParams2.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            irisParams2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", irisParams2.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            float centerX = Mathf.Clamp((irisParams2.x * 2.0f) - 1.0f, -1.0f, 1.0f);
            float centerY = Mathf.Clamp((irisParams2.y * 2.0f) - 1.0f, -1.0f, 1.0f);
            centerX = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心位置 X", centerX, -1.0f, 1.0f);
            y += LineHeight + LineSpacing;
            centerY = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心位置 Y", centerY, -1.0f, 1.0f);
            y += LineHeight + LineSpacing;

            irisParams0.x = 0.0f;
            irisParams0.y = 1.0f;
            irisParams0.z = 1.0f;
            irisParams0.w = 0.0f;
            irisParams1.y = 2.0f;
            irisParams1.z = 3.0f;
            irisParams1.w = 0.0f;
            irisParams2.x = (centerX + 1.0f) * 0.5f;
            irisParams2.y = (centerY + 1.0f) * 0.5f;
            irisParams3 = Vector4.zero;

            parameters0.vector4Value = irisParams0;
            parameters1.vector4Value = irisParams1;
            parameters2.vector4Value = irisParams2;
            parameters3.vector4Value = irisParams3;
            EditorGUI.indentLevel--;
        }

        private static void EnsureIrisBlurDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters0.vector4Value = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(1.0f, 2.0f, 3.0f, 0.0f);
                }
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters2.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters2.vector4Value = new Vector4(0.5f, 0.5f, 0.8f, 0.1f);
                }
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters3.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters3.vector4Value = Vector4.zero;
                }
            }
        }
    }
}
