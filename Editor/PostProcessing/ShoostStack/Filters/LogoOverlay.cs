using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private void DrawLogoOverlayElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty parameters7 = element.FindPropertyRelative("parameters7");
            SerializedProperty parameters8 = element.FindPropertyRelative("parameters8");
            SerializedProperty parameters9 = element.FindPropertyRelative("parameters9");
            SerializedProperty parameters10 = element.FindPropertyRelative("parameters10");
            SerializedProperty parameters11 = element.FindPropertyRelative("parameters11");
            SerializedProperty parameters12 = element.FindPropertyRelative("parameters12");

            EnsureLogoOverlayDefaults(
                parameters0,
                parameters1,
                parameters2,
                parameters3,
                parameters4,
                parameters5,
                parameters6,
                parameters7,
                parameters8,
                parameters9,
                parameters10,
                parameters11,
                parameters12);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: true,
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

            SerializedProperty[] slotTextures =
            {
                element.FindPropertyRelative("logoTexture0"),
                element.FindPropertyRelative("logoTexture1"),
                element.FindPropertyRelative("logoTexture2"),
                element.FindPropertyRelative("logoTexture3"),
                element.FindPropertyRelative("logoTexture4"),
                element.FindPropertyRelative("logoTexture5"),
                element.FindPropertyRelative("logoTexture6"),
                element.FindPropertyRelative("logoTexture7")
            };

            SerializedProperty[] slotParameters =
            {
                parameters0,
                parameters1,
                parameters2,
                parameters3,
                parameters4,
                parameters5,
                parameters6,
                parameters7
            };

            Vector4 orderA = parameters8.vector4Value;
            Vector4 orderB = parameters9.vector4Value;
            Vector4 autoA = parameters10.vector4Value;
            Vector4 autoB = parameters11.vector4Value;

            for (int i = 0; i < 8; i++)
            {
                y = DrawPropertyLine(rect.x, y, rect.width, slotTextures[i], $"输入 {i + 1}");
                Vector4 slot = slotParameters[i].vector4Value;
                float order = GetPackedLogoValue(i, orderA, orderB);
                float autoAspect = GetPackedLogoValue(i, autoA, autoB);
                y = DrawLogoOverlayTransformLine(rect.x, y, rect.width, i, ref slot);
                y = DrawLogoOverlayOrderLine(rect.x, y, rect.width, ref order, ref autoAspect);
                slotParameters[i].vector4Value = slot;
                SetPackedLogoValue(i, order, ref orderA, ref orderB);
                SetPackedLogoValue(i, autoAspect, ref autoA, ref autoB);
            }

            parameters8.vector4Value = orderA;
            parameters9.vector4Value = orderB;
            parameters10.vector4Value = autoA;
            parameters11.vector4Value = autoB;

            EditorGUI.indentLevel--;
        }

        private static float DrawLogoOverlayTransformLine(
            float x,
            float y,
            float width,
            int index,
            ref Vector4 slot)
        {
            Rect lineRect = new Rect(x, y, width, LineHeight);
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            float labelWidth = Mathf.Clamp(width * 0.22f, 72.0f, 110.0f);
            float gap = 6.0f;
            float fieldWidth = Mathf.Max(52.0f, (width - labelWidth - gap * 3.0f) / 4.0f);

            EditorGUI.LabelField(new Rect(lineRect.x, lineRect.y, labelWidth, lineRect.height), $"变换 {index + 1}");
            float fieldX = lineRect.x + labelWidth;

            EditorGUIUtility.labelWidth = 36.0f;
            slot.x = EditorGUI.FloatField(new Rect(fieldX, lineRect.y, fieldWidth, lineRect.height), "X", Mathf.Clamp(slot.x, -1.0f, 2.0f));
            fieldX += fieldWidth + gap;
            slot.y = EditorGUI.FloatField(new Rect(fieldX, lineRect.y, fieldWidth, lineRect.height), "Y", Mathf.Clamp(slot.y, -1.0f, 2.0f));
            fieldX += fieldWidth + gap;
            slot.z = EditorGUI.FloatField(new Rect(fieldX, lineRect.y, fieldWidth, lineRect.height), "大小", Mathf.Clamp(slot.z, 0.001f, 2.0f));
            fieldX += fieldWidth + gap;
            slot.w = EditorGUI.FloatField(new Rect(fieldX, lineRect.y, fieldWidth, lineRect.height), "透明", Mathf.Clamp01(slot.w));
            EditorGUIUtility.labelWidth = oldLabelWidth;

            return y + LineHeight + LineSpacing;
        }

        private static float DrawLogoOverlayOrderLine(
            float x,
            float y,
            float width,
            ref float order,
            ref float autoAspect)
        {
            Rect lineRect = new Rect(x, y, width, LineHeight);
            float labelWidth = Mathf.Clamp(width * 0.22f, 72.0f, 110.0f);
            float toggleWidth = 92.0f;
            float orderWidth = Mathf.Max(120.0f, width - labelWidth - toggleWidth - 8.0f);
            float oldLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUI.LabelField(new Rect(lineRect.x, lineRect.y, labelWidth, lineRect.height), "叠放");
            EditorGUIUtility.labelWidth = 54.0f;
            order = EditorGUI.IntSlider(
                new Rect(lineRect.x + labelWidth, lineRect.y, orderWidth, lineRect.height),
                "顺序",
                Mathf.Clamp(Mathf.RoundToInt(order), 0, 7),
                0,
                7);
            autoAspect = EditorGUI.ToggleLeft(
                new Rect(lineRect.xMax - toggleWidth, lineRect.y, toggleWidth, lineRect.height),
                "自动宽高",
                autoAspect > 0.5f) ? 1.0f : 0.0f;
            EditorGUIUtility.labelWidth = oldLabelWidth;

            return y + LineHeight + LineSpacing;
        }

        private static void EnsureLogoOverlayDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5,
            SerializedProperty parameters6,
            SerializedProperty parameters7,
            SerializedProperty parameters8,
            SerializedProperty parameters9,
            SerializedProperty parameters10,
            SerializedProperty parameters11,
            SerializedProperty parameters12)
        {
            if (parameters12 != null &&
                parameters12.propertyType == SerializedPropertyType.Vector4 &&
                Mathf.Approximately(parameters12.vector4Value.x, LogoOverlayInitMarker))
            {
                return;
            }

            SetVector4Property(parameters0, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters1, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters2, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters3, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters4, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters5, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters6, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters7, new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
            SetVector4Property(parameters8, new Vector4(0.0f, 1.0f, 2.0f, 3.0f));
            SetVector4Property(parameters9, new Vector4(4.0f, 5.0f, 6.0f, 7.0f));
            SetVector4Property(parameters10, Vector4.one);
            SetVector4Property(parameters11, Vector4.one);
            SetVector4Property(parameters12, new Vector4(LogoOverlayInitMarker, 0.0f, 0.0f, 0.0f));
        }

        private static void SetVector4Property(SerializedProperty property, Vector4 value)
        {
            if (property != null && property.propertyType == SerializedPropertyType.Vector4)
            {
                property.vector4Value = value;
            }
        }

        private static float GetPackedLogoValue(int index, Vector4 first, Vector4 second)
        {
            Vector4 source = index < 4 ? first : second;
            int component = index % 4;
            if (component == 0) return source.x;
            if (component == 1) return source.y;
            if (component == 2) return source.z;
            return source.w;
        }

        private static void SetPackedLogoValue(int index, float value, ref Vector4 first, ref Vector4 second)
        {
            int component = index % 4;
            if (index < 4)
            {
                if (component == 0) first.x = value;
                else if (component == 1) first.y = value;
                else if (component == 2) first.z = value;
                else first.w = value;
                return;
            }

            if (component == 0) second.x = value;
            else if (component == 1) second.y = value;
            else if (component == 2) second.z = value;
            else second.w = value;
        }
    }
}
