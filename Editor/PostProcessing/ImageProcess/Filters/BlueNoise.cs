using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static readonly string[] BlueNoiseModeNames = { "颗粒", "色阶抖动", "网点", "边缘噪声" };

        private void DrawBlueNoiseElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureBlueNoiseDefaults(parameters0, parameters1, color);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: true, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, BlueNoiseModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, BlueNoiseModeNames);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            y = DrawSliderLine(rect.x, y, rect.width, "局部强度", Mathf.Clamp01(p0.y), 0.0f, 1.0f, value => p0.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "噪声尺寸", Mathf.Clamp(p0.z, 0.5f, 8.0f), 0.5f, 8.0f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "动画速度", Mathf.Clamp(p0.w, 0.0f, 60.0f), 0.0f, 60.0f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "对比", Mathf.Clamp(p1.x, 0.25f, 3.0f), 0.25f, 3.0f, value => p1.x = value);

            switch (mode)
            {
                case 1:
                    p1.y = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "色阶数", Mathf.RoundToInt(Mathf.Clamp(p1.y, 2.0f, 32.0f)), 2, 32);
                    y += LineHeight + LineSpacing;
                    y = DrawSliderLine(rect.x, y, rect.width, "彩色量", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "HDR 保留", Mathf.Clamp01(p1.w), 0.0f, 1.0f, value => p1.w = value);
                    break;
                case 2:
                    y = DrawSliderLine(rect.x, y, rect.width, "墨点密度", Mathf.Clamp(p1.y, 0.05f, 2.0f), 0.05f, 2.0f, value => p1.y = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "墨点透明", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "底色保留", Mathf.Clamp01(p1.w), 0.0f, 1.0f, value => p1.w = value);
                    break;
                case 3:
                    y = DrawSliderLine(rect.x, y, rect.width, "边缘强度", Mathf.Clamp(p1.y, 0.0f, 12.0f), 0.0f, 12.0f, value => p1.y = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "边缘染色", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "原色保留", Mathf.Clamp01(p1.w), 0.0f, 1.0f, value => p1.w = value);
                    break;
                default:
                    y = DrawSliderLine(rect.x, y, rect.width, "颗粒强度", Mathf.Clamp(p1.y, 0.0f, 3.0f), 0.0f, 3.0f, value => p1.y = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "彩色颗粒", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);
                    y = DrawSliderLine(rect.x, y, rect.width, "亮部保护", Mathf.Clamp01(p1.w), 0.0f, 1.0f, value => p1.w = value);
                    break;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureBlueNoiseDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty color)
        {
            bool assignedDefaults = false;
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.45f, 1.0f, 0.0f);
                assignedDefaults = true;
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(1.0f, 0.85f, 0.25f, 0.75f);
            }

            if (assignedDefaults && color != null && color.propertyType == SerializedPropertyType.Color && color.colorValue == Color.white)
            {
                color.colorValue = Color.black;
            }
        }
    }
}
