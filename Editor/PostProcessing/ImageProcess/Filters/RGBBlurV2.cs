using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawRgbBlurV2Element(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            EnsureRgbBlurV2Defaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 blurParams = parameters0.vector4Value;
            blurParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "红通道模糊度", Mathf.Clamp01(blurParams.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            blurParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "绿通道模糊度", Mathf.Clamp01(blurParams.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            blurParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "蓝通道模糊度", Mathf.Clamp01(blurParams.z), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = blurParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureRgbBlurV2Defaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            if (parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = Vector4.zero;
            }
        }
    }
}
