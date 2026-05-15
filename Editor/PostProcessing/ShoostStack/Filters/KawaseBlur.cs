using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static bool GetKawaseBlurUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawKawaseBlurElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureKawaseBlurDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 blurParams0 = parameters0.vector4Value;
            Vector4 blurParams1 = parameters1.vector4Value;

            int resolutionMode = Mathf.Clamp(Mathf.RoundToInt(blurParams0.x), 0, 1);
            resolutionMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率模式", resolutionMode, new[] { "游戏视图", "自定义尺寸" });
            blurParams0.x = resolutionMode;
            y += LineHeight + LineSpacing;

            if (resolutionMode == 1)
            {
                blurParams0.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(blurParams0.y), 1, 8192));
                y += LineHeight + LineSpacing;
                blurParams0.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(blurParams0.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            blurParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", blurParams1.x, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;

            blurParams1.y = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "降采样", Mathf.RoundToInt(blurParams1.y), 1, 8);
            y += LineHeight + LineSpacing;

            blurParams1.z = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "迭代次数", Mathf.RoundToInt(blurParams1.z), 1, 10);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = blurParams0;
            parameters1.vector4Value = blurParams1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureKawaseBlurDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters0.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(0.5f, 2.0f, 6.0f, 0.0f);
                }
            }
        }
    }
}
