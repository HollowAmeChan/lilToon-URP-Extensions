using System;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterSubjectOutlineEditorSection
    {
        private static readonly Color SectionColor = new Color(0.74f, 0.74f, 0.92f);

        private static bool showSettings;
        private static bool showVolume;

        public static void DrawSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty enabled = Find(settingsProperty, "subjectOutlineEnabled");
            SerializedProperty strength = Find(settingsProperty, "subjectOutlineStrength");
            SerializedProperty fillMode = Find(settingsProperty, "subjectOutlineFillMode");
            string summary = enabled != null && enabled.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "主体轮廓", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("读取 ObjectCustom0.r 的主体遮罩，使用临时 RenderGraph 纹理生成连续外扩场。", MessageType.None);
                DrawProperty(enabled, "启用主体轮廓");
                DrawProperty(strength, "强度");
                DrawProperty(Find(settingsProperty, "subjectOutlineRadiusPixels"), "外扩半径像素");
                DrawProperty(fillMode, "风格模式");
                DrawModeSettings(GetFillMode(fillMode), settingsProperty);
                DrawHeightFadeSettings(settingsProperty);
            }
        }

        public static void DrawVolume(
            SerializedDataParameter enabled,
            SerializedDataParameter strength,
            SerializedDataParameter radiusPixels,
            SerializedDataParameter levelBlack,
            SerializedDataParameter levelWhite,
            SerializedDataParameter color,
            SerializedDataParameter fillMode,
            SerializedDataParameter normalRotationDegrees,
            SerializedDataParameter normalFlowDegreesPerSecond,
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

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "主体轮廓", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("读取 ObjectCustom0.r 的主体遮罩，使用临时 RenderGraph 纹理生成连续外扩场。", MessageType.None);
                DrawParameter(enabled, "启用主体轮廓", drawParameter);
                DrawParameter(strength, "强度", drawParameter);
                DrawParameter(radiusPixels, "外扩半径像素", drawParameter);
                DrawParameter(fillMode, "风格模式", drawParameter);
                DrawModeParameters(
                    GetFillMode(fillMode?.value),
                    levelBlack,
                    levelWhite,
                    color,
                    normalRotationDegrees,
                    normalFlowDegreesPerSecond,
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

        private static void DrawModeSettings(HoCharacterSubjectOutlineFillMode fillMode, SerializedProperty settingsProperty)
        {
            switch (fillMode)
            {
                case HoCharacterSubjectOutlineFillMode.NormalColor:
                    DrawProperty(Find(settingsProperty, "subjectOutlineLevelBlack"), "边缘黑场");
                    DrawProperty(Find(settingsProperty, "subjectOutlineLevelWhite"), "边缘白场");
                    DrawProperty(Find(settingsProperty, "subjectOutlineNormalRotationDegrees"), "法线旋转");
                    DrawProperty(Find(settingsProperty, "subjectOutlineNormalFlowDegreesPerSecond"), "法线流动速度");
                    break;
                case HoCharacterSubjectOutlineFillMode.SoftFog:
                    DrawProperty(Find(settingsProperty, "subjectOutlineFogColor"), "雾气颜色");
                    DrawProperty(Find(settingsProperty, "subjectOutlineFogHueShiftDegrees"), "雾气色相偏移");
                    DrawProperty(Find(settingsProperty, "subjectOutlineFogSaturation"), "雾气饱和度");
                    DrawProperty(Find(settingsProperty, "subjectOutlineFogValue"), "雾气亮度");
                    DrawProperty(Find(settingsProperty, "subjectOutlineFogSoftness"), "雾气柔化");
                    break;
                default:
                    DrawProperty(Find(settingsProperty, "subjectOutlineLevelBlack"), "边缘黑场");
                    DrawProperty(Find(settingsProperty, "subjectOutlineLevelWhite"), "边缘白场");
                    DrawProperty(Find(settingsProperty, "subjectOutlineColor"), "轮廓颜色");
                    break;
            }
        }

        private static void DrawModeParameters(
            HoCharacterSubjectOutlineFillMode fillMode,
            SerializedDataParameter levelBlack,
            SerializedDataParameter levelWhite,
            SerializedDataParameter color,
            SerializedDataParameter normalRotationDegrees,
            SerializedDataParameter normalFlowDegreesPerSecond,
            SerializedDataParameter fogColor,
            SerializedDataParameter fogHueShiftDegrees,
            SerializedDataParameter fogSaturation,
            SerializedDataParameter fogValue,
            SerializedDataParameter fogSoftness,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            switch (fillMode)
            {
                case HoCharacterSubjectOutlineFillMode.NormalColor:
                    DrawParameter(levelBlack, "边缘黑场", drawParameter);
                    DrawParameter(levelWhite, "边缘白场", drawParameter);
                    DrawParameter(normalRotationDegrees, "法线旋转", drawParameter);
                    DrawParameter(normalFlowDegreesPerSecond, "法线流动速度", drawParameter);
                    break;
                case HoCharacterSubjectOutlineFillMode.SoftFog:
                    DrawParameter(fogColor, "雾气颜色", drawParameter);
                    DrawParameter(fogHueShiftDegrees, "雾气色相偏移", drawParameter);
                    DrawParameter(fogSaturation, "雾气饱和度", drawParameter);
                    DrawParameter(fogValue, "雾气亮度", drawParameter);
                    DrawParameter(fogSoftness, "雾气柔化", drawParameter);
                    break;
                default:
                    DrawParameter(levelBlack, "边缘黑场", drawParameter);
                    DrawParameter(levelWhite, "边缘白场", drawParameter);
                    DrawParameter(color, "轮廓颜色", drawParameter);
                    break;
            }
        }

        private static void DrawHeightFadeSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty mode = Find(settingsProperty, "subjectOutlineHeightFadeMode");
            DrawProperty(mode, "高度渐隐");
            if (GetHeightFadeMode(mode) == HoCharacterSubjectOutlineHeightFadeMode.Off)
            {
                return;
            }

            DrawProperty(Find(settingsProperty, "subjectOutlineHeightFadeGroundY"), "地面高度");
            DrawProperty(Find(settingsProperty, "subjectOutlineHeightFadeStart"), "渐隐开始距离");
            DrawProperty(Find(settingsProperty, "subjectOutlineHeightFadeEnd"), "渐隐结束距离");
            DrawProperty(Find(settingsProperty, "subjectOutlineHeightFadeHardness"), "渐隐硬度");
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

        private static HoCharacterSubjectOutlineFillMode GetFillMode(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return HoCharacterSubjectOutlineFillMode.SolidColor;
            }

            int value = Mathf.Clamp(property.enumValueIndex, 0, 2);
            return (HoCharacterSubjectOutlineFillMode)value;
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
