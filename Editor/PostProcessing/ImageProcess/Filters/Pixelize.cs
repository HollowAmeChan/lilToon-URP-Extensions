using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private void DrawPixelizeElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");

            EnsurePixelizeDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 pixelParams0 = parameters0.vector4Value;
            pixelParams0.x = 0.0f;
            pixelParams0.y = 1920.0f;
            pixelParams0.z = 1080.0f;
            pixelParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分辨率缩放", Mathf.Clamp01(pixelParams0.w), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = pixelParams0;
            EditorGUI.indentLevel--;
        }

        private static void EnsurePixelizeDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 1920.0f, 1080.0f, 1.0f);
            }
            else if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (Mathf.Approximately(value.x, 0.0f) && Mathf.Approximately(value.y, 320.0f) && Mathf.Approximately(value.z, 240.0f) && Mathf.Approximately(value.w, 1.0f))
                {
                    parameters0.vector4Value = new Vector4(0.0f, 1920.0f, 1080.0f, 1.0f);
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            }
        }
    }
}
