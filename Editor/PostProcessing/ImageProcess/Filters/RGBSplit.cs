using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static bool GetRgbSplitUsesAngle(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 0;
        }

        private void DrawRgbSplitElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            EnsureRgbSplitDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 splitParams = parameters0.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(splitParams.x), 0, 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, new[] { "RGB 分离", "径向色差" });
            splitParams.x = mode;
            y += LineHeight + LineSpacing;

            splitParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分离强度", splitParams.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            if (mode == 0)
            {
                splitParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", splitParams.z, 0.0f, 360.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = splitParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureRgbSplitDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (Mathf.Approximately(value.w, RgbSplitInitMarker))
            {
                return;
            }

            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.35f, 0.0f, RgbSplitInitMarker);
                return;
            }

            value.w = RgbSplitInitMarker;
            parameters0.vector4Value = value;
        }
    }
}
