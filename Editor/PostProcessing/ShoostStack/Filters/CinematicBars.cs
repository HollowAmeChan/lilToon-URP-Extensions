using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawCinematicBarsElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureCinematicBarsDefaults(parameters0);

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

            Vector4 barsParams = parameters0.vector4Value;
            y = DrawSliderLine(rect.x, y, rect.width, "目标宽高比", Mathf.Max(1.0f, barsParams.x), 1.0f, 4.0f, value => barsParams.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "额外裁切", Mathf.Clamp(barsParams.y, 0.0f, 0.35f), 0.0f, 0.35f, value => barsParams.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "柔边像素", Mathf.Clamp(barsParams.z, 0.0f, 64.0f), 0.0f, 64.0f, value => barsParams.z = value);
            parameters0.vector4Value = barsParams;

            EditorGUI.indentLevel--;
        }

        private static void EnsureCinematicBarsDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude > 0.000001f)
            {
                return;
            }

            parameters0.vector4Value = new Vector4(2.39f, 0.0f, 0.0f, 0.0f);
        }
    }
}
