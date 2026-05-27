using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static readonly string[] BlueNoiseModeNames = { "Voronoi 色块", "柔和马赛克", "彩窗边线", "海报色块" };

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

            y = DrawSliderLine(rect.x, y, rect.width, "混合强度", Mathf.Clamp01(p0.y), 0.0f, 1.0f, value => p0.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "色块尺寸", Mathf.Clamp(p0.z, 4.0f, 96.0f), 4.0f, 96.0f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "点位扰动", Mathf.Clamp01(p0.w), 0.0f, 1.0f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "颜色平均", Mathf.Clamp(p1.x, 0.0f, 2.0f), 0.0f, 2.0f, value => p1.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "边线宽度", Mathf.Clamp(p1.y, 0.0f, 6.0f), 0.0f, 6.0f, value => p1.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "边线透明", Mathf.Clamp01(p1.z), 0.0f, 1.0f, value => p1.z = value);

            if (mode == 3)
            {
                p1.w = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "色阶数量", Mathf.RoundToInt(Mathf.Clamp(p1.w, 2.0f, 32.0f)), 2, 32);
                y += LineHeight + LineSpacing;
            }
            else
            {
                y = DrawSliderLine(rect.x, y, rect.width, "色阶数量", Mathf.Clamp(p1.w, 2.0f, 32.0f), 2.0f, 32.0f, value => p1.w = value);
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
                parameters0.vector4Value = new Vector4(0.0f, 1.0f, 18.0f, 0.78f);
                assignedDefaults = true;
            }
            else if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.z > 0.0f && value.z < 4.0f)
                {
                    value.z = 18.0f;
                    parameters0.vector4Value = value;
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.35f, 0.75f, 0.18f, 12.0f);
            }

            if (assignedDefaults && color != null && color.propertyType == SerializedPropertyType.Color && color.colorValue == Color.white)
            {
                color.colorValue = Color.black;
            }
        }
    }
}
