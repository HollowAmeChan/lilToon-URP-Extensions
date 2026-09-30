using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private static bool GetVignetteCustomUsesTintMode(SerializedProperty element)
        {
            SerializedProperty passIndex = element?.FindPropertyRelative("passIndex");
            return passIndex != null && passIndex.propertyType == SerializedPropertyType.Integer && passIndex.intValue == 1;
        }

        private void DrawVignetteCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty passIndex = element.FindPropertyRelative("passIndex");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureVignetteCustomDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                if (IsImageProcessCenterRadiusViewControlActive(element))
                {
                    ImageProcessCenterRadiusViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            y = DrawPopupLine(rect.x, y, rect.width, passIndex, "模式", new[] { "压暗", "染色" });
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: GetVignetteCustomUsesTintMode(element),
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);
            y = DrawImageProcessCenterRadiusViewControlButton(rect, y, element);

            Vector4 vignetteParams = parameters0.vector4Value;
            y = DrawSliderLine(rect.x, y, rect.width, "中心 X", vignetteParams.x, 0.0f, 1.0f, value => vignetteParams.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "中心 Y", vignetteParams.y, 0.0f, 1.0f, value => vignetteParams.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "半径", vignetteParams.z, 0.0f, 2.0f, value => vignetteParams.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "柔和度", vignetteParams.w, 0.0f, 1.0f, value => vignetteParams.w = value);
            parameters0.vector4Value = vignetteParams;

            // 色散：x 是开关、y 是强度。强度只在开关打开时被 shader 读，所以默认值不会改变旧观感。
            Vector4 dispersionParams = parameters1.vector4Value;
            dispersionParams.x = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "色散", dispersionParams.x > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            y = DrawSliderLine(rect.x, y, rect.width, "色散强度", dispersionParams.y, 0.0f, 1.0f, value => dispersionParams.y = value);
            parameters1.vector4Value = dispersionParams;

            EditorGUI.indentLevel--;
        }

        private static void EnsureVignetteCustomDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters0.vector4Value = new Vector4(0.5f, 0.5f, 1.0f, 0.5f);
                }
            }

            // parameters1.x 是色散开关（默认关，旧观感不变），.y 是强度。整条为零时给强度一个中间值。
            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(0.0f, 0.5f, 0.0f, 0.0f);
                }
            }
        }
    }
}
