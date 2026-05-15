using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] ToonMapModeNames =
        {
            "None",
            "Neutral",
            "ACES",
        };

        private void DrawToonMapElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element,
                includeBlendMode: false,
                includeColor: false,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, ToonMapModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, ToonMapModeNames);
            p0.x = mode;
            parameters0.vector4Value = p0;
            EditorGUI.indentLevel--;
        }
    }
}
