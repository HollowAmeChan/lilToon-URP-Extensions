using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawFisheyeElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureFisheyeDefaults(parameters0);
            if (color != null && color.propertyType == SerializedPropertyType.Color)
            {
                color.colorValue = Color.black;
            }

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 fisheyeParams = parameters0.vector4Value;
            fisheyeParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "强度", fisheyeParams.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            fisheyeParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "缩放", fisheyeParams.y, 0.01f, 2.0f);
            y += LineHeight + LineSpacing;
            fisheyeParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", fisheyeParams.z, 0.01f, 0.5f);
            y += LineHeight + LineSpacing;
            bool isCircular = fisheyeParams.w > 0.5f;
            isCircular = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "是否圆形", isCircular);
            fisheyeParams.w = isCircular ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = fisheyeParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureFisheyeDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.2f, 1.0f, 0.1f, 0.0f);
                return;
            }

            if (IsLegacyFisheyeParams(value))
            {
                parameters0.vector4Value = new Vector4(0.2f, Mathf.Clamp(value.x, 0.01f, 2.0f), Mathf.Clamp(value.y, 0.01f, 0.5f), value.z > 0.5f ? 1.0f : 0.0f);
            }
        }

        private static bool IsLegacyFisheyeParams(Vector4 value)
        {
            bool legacyCircularSlot = Mathf.Abs(value.z) <= 0.0001f || Mathf.Abs(value.z - 1.0f) <= 0.0001f;
            return Mathf.Abs(value.w) <= 0.0001f && legacyCircularSlot && value.x >= 0.01f && value.y >= 0.01f && value.y <= 0.5f;
        }
    }
}
