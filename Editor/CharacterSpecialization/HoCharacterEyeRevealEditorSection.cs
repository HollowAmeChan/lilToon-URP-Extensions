using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterEyeRevealEditorSection
    {
        private static readonly Color EyeRevealColor = new Color(0.48f, 0.70f, 0.92f);

        private static bool showSettings;
        private static bool showVolume;

        public static void DrawSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty enabled = Find(settingsProperty, "eyeRevealEnabled");
            SerializedProperty strength = Find(settingsProperty, "eyeRevealStrength");
            string summary = enabled != null && enabled.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "眼透", summary, EyeRevealColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Eye 标记提供眼睛颜色、深度与 Alpha；FrontHair 标记作为遮挡物；EyeRevealArea 可选，用来限制透出区域。", MessageType.None);
                DrawProperty(enabled, "启用眼睛透过");
                DrawProperty(strength, "透过强度");
                DrawProperty(Find(settingsProperty, "eyeRevealFeatherPixels"), "羽化像素");
                DrawProperty(Find(settingsProperty, "eyeRevealDilationPixels"), "扩张像素");
                DrawProperty(Find(settingsProperty, "eyeRevealDepthBias"), "深度偏移");
                DrawProperty(Find(settingsProperty, "useEyeRevealArea"), "使用眼透区域");
                DrawProperty(Find(settingsProperty, "sameCharacterOnly"), "仅同角色");
            }
        }

        public static void DrawVolume(
            SerializedDataParameter enabled,
            SerializedDataParameter strength,
            SerializedDataParameter featherPixels,
            SerializedDataParameter dilationPixels,
            SerializedDataParameter depthBias,
            SerializedDataParameter useRevealArea,
            SerializedDataParameter sameCharacterOnly,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            string summary = enabled?.value != null && enabled.value.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(strength)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "眼透", summary, EyeRevealColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Eye 标记提供眼睛颜色、深度与 Alpha；FrontHair 标记作为遮挡物；EyeRevealArea 可选，用来限制透出区域。", MessageType.None);
                DrawParameter(enabled, "启用眼睛透过", drawParameter);
                DrawParameter(strength, "透过强度", drawParameter);
                DrawParameter(featherPixels, "羽化像素", drawParameter);
                DrawParameter(dilationPixels, "扩张像素", drawParameter);
                DrawParameter(depthBias, "深度偏移", drawParameter);
                DrawParameter(useRevealArea, "使用眼透区域", drawParameter);
                DrawParameter(sameCharacterOnly, "仅同角色", drawParameter);
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
