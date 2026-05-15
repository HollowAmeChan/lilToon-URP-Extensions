using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawChangeFrameRateElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureChangeFrameRateDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 frameRateParams = parameters0.vector4Value;
            int targetFrameRate = Mathf.Clamp(Mathf.RoundToInt(frameRateParams.x), 1, 60);
            targetFrameRate = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "目标帧率", targetFrameRate, 1, 60);
            frameRateParams.x = targetFrameRate;
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = frameRateParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureChangeFrameRateDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.x <= 0.0f)
            {
                value.x = 12.0f;
                parameters0.vector4Value = value;
            }
        }
    }
}
