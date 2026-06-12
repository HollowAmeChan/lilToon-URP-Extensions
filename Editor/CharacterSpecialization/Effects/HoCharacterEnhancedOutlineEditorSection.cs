using System;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterEnhancedOutlineEditorSection
    {
        private static readonly Color SectionColor = new Color(0.78f, 0.64f, 0.42f);

        private static bool showSettings;
        private static bool showVolume;

        public static void DrawSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty enabled = Find(settingsProperty, "enhancedOutlineEnabled");
            SerializedProperty strength = Find(settingsProperty, "enhancedOutlineStrength");
            string summary = enabled != null && enabled.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "增强轮廓", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("读取指定 ObjectCustom 分量，使用独立临时 RenderGraph 纹理生成柔化外扩雾气。", MessageType.None);
                DrawProperty(enabled, "启用增强轮廓");
                DrawProperty(Find(settingsProperty, "enhancedOutlineSourceChannel"), "来源通道");
                DrawProperty(strength, "雾气强度");
                DrawProperty(Find(settingsProperty, "enhancedOutlineRadiusPixels"), "外扩半径像素");
                DrawFogSettings(settingsProperty);
                DrawHeightFadeSettings(settingsProperty);
            }
        }

        public static void DrawVolume(
            SerializedDataParameter enabled,
            SerializedDataParameter sourceChannel,
            SerializedDataParameter strength,
            SerializedDataParameter radiusPixels,
            SerializedDataParameter fogColor,
            SerializedDataParameter fogHueShiftDegrees,
            SerializedDataParameter fogSaturation,
            SerializedDataParameter fogValue,
            SerializedDataParameter fogSoftness,
            SerializedDataParameter heightFadeMode,
            SerializedDataParameter heightFadeGroundY,
            SerializedDataParameter heightFadeStart,
            SerializedDataParameter heightFadeEnd,
            SerializedDataParameter heightFadeHardness,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            string summary = enabled?.value != null && enabled.value.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "增强轮廓", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("读取指定 ObjectCustom 分量，使用独立临时 RenderGraph 纹理生成柔化外扩雾气。", MessageType.None);
                DrawParameter(enabled, "启用增强轮廓", drawParameter);
                DrawParameter(sourceChannel, "来源通道", drawParameter);
                DrawParameter(strength, "雾气强度", drawParameter);
                DrawParameter(radiusPixels, "外扩半径像素", drawParameter);
                DrawFogParameters(
                    fogColor,
                    fogHueShiftDegrees,
                    fogSaturation,
                    fogValue,
                    fogSoftness,
                    drawParameter);
                DrawHeightFadeParameters(
                    heightFadeMode,
                    heightFadeGroundY,
                    heightFadeStart,
                    heightFadeEnd,
                    heightFadeHardness,
                    drawParameter);
            }
        }

        private static void DrawFogSettings(SerializedProperty settingsProperty)
        {
            DrawProperty(Find(settingsProperty, "enhancedOutlineFogColor"), "雾气颜色");
            DrawProperty(Find(settingsProperty, "enhancedOutlineFogHueShiftDegrees"), "雾气色相偏移");
            DrawProperty(Find(settingsProperty, "enhancedOutlineFogSaturation"), "雾气饱和度");
            DrawProperty(Find(settingsProperty, "enhancedOutlineFogValue"), "雾气亮度");
            DrawProperty(Find(settingsProperty, "enhancedOutlineFogSoftness"), "雾气柔化");
        }

        private static void DrawFogParameters(
            SerializedDataParameter fogColor,
            SerializedDataParameter fogHueShiftDegrees,
            SerializedDataParameter fogSaturation,
            SerializedDataParameter fogValue,
            SerializedDataParameter fogSoftness,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            DrawParameter(fogColor, "雾气颜色", drawParameter);
            DrawParameter(fogHueShiftDegrees, "雾气色相偏移", drawParameter);
            DrawParameter(fogSaturation, "雾气饱和度", drawParameter);
            DrawParameter(fogValue, "雾气亮度", drawParameter);
            DrawParameter(fogSoftness, "雾气柔化", drawParameter);
        }

        private static void DrawHeightFadeSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty mode = Find(settingsProperty, "enhancedOutlineHeightFadeMode");
            DrawProperty(mode, "高度渐隐");
            if (GetHeightFadeMode(mode) == HoCharacterSubjectOutlineHeightFadeMode.Off)
            {
                return;
            }

            DrawProperty(Find(settingsProperty, "enhancedOutlineHeightFadeGroundY"), "地面高度");
            DrawProperty(Find(settingsProperty, "enhancedOutlineHeightFadeStart"), "渐隐开始距离");
            DrawProperty(Find(settingsProperty, "enhancedOutlineHeightFadeEnd"), "渐隐结束距离");
            DrawProperty(Find(settingsProperty, "enhancedOutlineHeightFadeHardness"), "渐隐硬度");
        }

        private static void DrawHeightFadeParameters(
            SerializedDataParameter mode,
            SerializedDataParameter groundY,
            SerializedDataParameter start,
            SerializedDataParameter end,
            SerializedDataParameter hardness,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            DrawParameter(mode, "高度渐隐", drawParameter);
            if (GetHeightFadeMode(mode?.value) == HoCharacterSubjectOutlineHeightFadeMode.Off)
            {
                return;
            }

            DrawParameter(groundY, "地面高度", drawParameter);
            DrawParameter(start, "渐隐开始距离", drawParameter);
            DrawParameter(end, "渐隐结束距离", drawParameter);
            DrawParameter(hardness, "渐隐硬度", drawParameter);
        }

        private static HoCharacterSubjectOutlineHeightFadeMode GetHeightFadeMode(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return HoCharacterSubjectOutlineHeightFadeMode.Off;
            }

            int value = Mathf.Clamp(property.enumValueIndex, 0, 2);
            return (HoCharacterSubjectOutlineHeightFadeMode)value;
        }

        private static SerializedProperty Find(SerializedProperty settingsProperty, string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }

        private static void DrawParameter(SerializedDataParameter parameter, string label, Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            if (parameter != null)
            {
                drawParameter(parameter, new GUIContent(label));
            }
        }
    }
}
