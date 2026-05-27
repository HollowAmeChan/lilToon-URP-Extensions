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
                ? "On " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "Off";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "Face Hair Diffuse", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Blurs Face SurfaceColor and tints only FrontHair receivers. The blur chain is allocated as temporary RenderGraph textures.", MessageType.None);
                DrawProperty(enabled, "Enable");
                DrawProperty(strength, "Strength");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseRadiusPixels"), "Blur Radius Pixels");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseDepthTolerance"), "Depth Tolerance");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseLevelBlack"), "Level Black");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseLevelWhite"), "Level White");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseTintColor"), "Tint Multiplier");
                DrawProperty(Find(settingsProperty, "faceHairDiffuseBlendMode"), "Blend Mode");
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
                ? "On " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "Off";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "Face Hair Diffuse", summary, SectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Blurs Face SurfaceColor and tints only FrontHair receivers. The blur chain is allocated as temporary RenderGraph textures.", MessageType.None);
                DrawParameter(enabled, "Enable", drawParameter);
                DrawParameter(strength, "Strength", drawParameter);
                DrawParameter(radiusPixels, "Blur Radius Pixels", drawParameter);
                DrawParameter(depthTolerance, "Depth Tolerance", drawParameter);
                DrawParameter(levelBlack, "Level Black", drawParameter);
                DrawParameter(levelWhite, "Level White", drawParameter);
                DrawParameter(tintColor, "Tint Multiplier", drawParameter);
                DrawParameter(blendMode, "Blend Mode", drawParameter);
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
