using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawRgbChannelSeparatorElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureRgbChannelSeparatorDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 separatorParams = parameters0.vector4Value;
            int channel = Mathf.Clamp(Mathf.RoundToInt(separatorParams.x), 0, 4);
            channel = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "通道", channel, new[] { "RGB", "红", "绿", "蓝", "Alpha" });
            separatorParams.x = channel;
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = separatorParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureRgbChannelSeparatorDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = Vector4.zero;
            }
        }
    }
}
