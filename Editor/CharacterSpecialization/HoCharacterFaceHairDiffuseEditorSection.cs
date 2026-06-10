using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterFaceHairDiffuseEditorSection
    {
        private static readonly Color SectionColor = new Color(0.86f, 0.58f, 0.56f);

        private static bool showSettings;
        private static bool showVolume;

        public static void DrawSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty enabled = Find(settingsProperty, "faceHairDiffuseEnabled");
            SerializedProperty strength = Find(settingsProperty, "faceHairDiffuseStrength");
            string summary = enabled != null && enabled.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "脸部发色漫反射", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("模糊 Face 的 SurfaceColor，并只对 FrontHair 接收物进行染色。模糊链会作为临时 RenderGraph 纹理分配。", MessageType.None);
                DrawProperty(enabled, "启用脸部发色漫反射");
                DrawProperty(strength, "强度");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseRadiusPixels"), "模糊半径像素");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseDepthTolerance"), "深度容差");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseLevelBlack"), "黑场阈值");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseLevelWhite"), "白场阈值");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseTintColor"), "染色倍率");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseBlendMode"), "混合模式");
            }
        }

        public static void DrawVolume(
            SerializedDataParameter enabled,
            SerializedDataParameter strength,
            SerializedDataParameter radiusPixels,
            SerializedDataParameter depthTolerance,
            SerializedDataParameter levelBlack,
            SerializedDataParameter levelWhite,
            SerializedDataParameter tintColor,
            SerializedDataParameter blendMode,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            string summary = enabled?.value != null && enabled.value.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "脸部发色漫反射", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("模糊 Face 的 SurfaceColor，并只对 FrontHair 接收物进行染色。模糊链会作为临时 RenderGraph 纹理分配。", MessageType.None);
                DrawParameter(enabled, "启用脸部发色漫反射", drawParameter);
                DrawParameter(strength, "强度", drawParameter);
                DrawParameter(radiusPixels, "模糊半径像素", drawParameter);
                DrawParameter(depthTolerance, "深度容差", drawParameter);
                DrawParameter(levelBlack, "黑场阈值", drawParameter);
                DrawParameter(levelWhite, "白场阈值", drawParameter);
                DrawParameter(tintColor, "染色倍率", drawParameter);
                DrawParameter(blendMode, "混合模式", drawParameter);
            }
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
