using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawAutoWhiteBalanceElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureAutoWhiteBalanceDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: true, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 whiteBalanceParams = parameters0.vector4Value;
            whiteBalanceParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色温", whiteBalanceParams.x, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            whiteBalanceParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色调", whiteBalanceParams.y, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            bool preserveLuminance = whiteBalanceParams.z > 0.5f;
            preserveLuminance = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "保持亮度", preserveLuminance);
            whiteBalanceParams.z = preserveLuminance ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = whiteBalanceParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureAutoWhiteBalanceDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            }
        }
    }
}
