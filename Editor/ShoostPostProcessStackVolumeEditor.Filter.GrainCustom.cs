using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static bool GetGrainCustomUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawGrainCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureGrainCustomDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 grainParams0 = parameters0.vector4Value;
            Vector4 grainParams1 = parameters1.vector4Value;

            int resolutionType = Mathf.Clamp(Mathf.RoundToInt(grainParams0.x), 0, 1);
            resolutionType = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率模式", resolutionType, new[] { "游戏视图", "自定义尺寸" });
            grainParams0.x = resolutionType;
            y += LineHeight + LineSpacing;
            if (resolutionType == 1)
            {
                grainParams0.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(grainParams0.y), 1, 8192));
                y += LineHeight + LineSpacing;
                grainParams0.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(grainParams0.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            grainParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "降采样", grainParams0.w, 1.0f, 4.0f);
            y += LineHeight + LineSpacing;
            grainParams1.x = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "彩色颗粒", grainParams1.x > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            grainParams1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "强度", grainParams1.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            grainParams1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "尺寸", grainParams1.z, 0.3f, 3.0f);
            y += LineHeight + LineSpacing;
            grainParams1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "亮度贡献", grainParams1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = grainParams0;
            parameters1.vector4Value = grainParams1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureGrainCustomDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(1.0f, 1920.0f, 1080.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.0f, 0.5f, 2.0f, 0.9f);
            }
        }
    }
}
