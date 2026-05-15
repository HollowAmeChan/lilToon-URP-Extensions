using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static bool GetDownScaleUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawDownScaleResolutionElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureDownScaleResolutionDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 resolutionParams = parameters0.vector4Value;
            int resolutionType = Mathf.Clamp(Mathf.RoundToInt(resolutionParams.x), 0, 6);
            resolutionType = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率", resolutionType, new[] { "游戏视图", "自定义", "QVGA 320x240", "SDTV 640x480", "EDTV 854x480", "HD 1280x720", "FHD 1920x1080" });
            resolutionParams.x = resolutionType;
            y += LineHeight + LineSpacing;

            if (resolutionType == 1)
            {
                resolutionParams.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(resolutionParams.y), 1, 8192));
                y += LineHeight + LineSpacing;
                resolutionParams.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(resolutionParams.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            resolutionParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "降采样倍率", resolutionParams.w, 1.0f, 10.0f);
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = resolutionParams;
            EditorGUI.indentLevel--;
        }

        private static void EnsureDownScaleResolutionDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            }
        }
    }
}
