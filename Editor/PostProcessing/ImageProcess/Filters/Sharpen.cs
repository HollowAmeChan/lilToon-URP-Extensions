using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawSharpenElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            EnsureSharpenDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 sharpenParams = parameters0.vector4Value;
            sharpenParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化强度", sharpenParams.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = sharpenParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureSharpenDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.2f, 0.0f, 0.0f, 0.0f);
            }
        }
    }
}
