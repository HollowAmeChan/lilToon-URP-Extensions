using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawPrismFractureElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsurePrismFractureDefaults(parameters0, parameters1, parameters2);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsShoostCenterRadiusViewControlActive(element))
                {
                    ShoostCenterRadiusViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: false,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);
            y = DrawShoostCenterRadiusViewControlButton(rect, y, element);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            y = DrawSliderLine(rect.x, y, rect.width, "中心 X", Mathf.Clamp01(p0.x), 0.0f, 1.0f, value => p0.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "中心 Y", Mathf.Clamp01(p0.y), 0.0f, 1.0f, value => p0.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "半径", Mathf.Clamp(p0.z, 0.0f, 1.5f), 0.0f, 1.5f, value => p0.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "柔边", Mathf.Clamp(p0.w, 0.0f, 0.75f), 0.0f, 0.75f, value => p0.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "破碎程度", Mathf.Clamp01(p1.x), 0.0f, 1.0f, value => p1.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "焦散色散", Mathf.Clamp01(p1.y), 0.0f, 1.0f, value => p1.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "碎片数量", Mathf.Clamp(p1.z, 3.0f, 40.0f), 3.0f, 40.0f, value => p1.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "旋转", Mathf.Clamp(p1.w, -180.0f, 180.0f), -180.0f, 180.0f, value => p1.w = value);
            y = DrawSliderLine(rect.x, y, rect.width, "棱镜高光", Mathf.Clamp01(p2.x), 0.0f, 1.0f, value => p2.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "随机种子", Mathf.Clamp(p2.y, 0.0f, 99.0f), 0.0f, 99.0f, value => p2.y = value);
            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;

            EditorGUI.indentLevel--;
        }

        private static void EnsurePrismFractureDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.5f, 0.5f, 0.42f, 0.12f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(0.58f, 0.74f, 15.0f, 0.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(0.38f, 1.0f, 0.0f, 0.0f);
            }
        }
    }
}
