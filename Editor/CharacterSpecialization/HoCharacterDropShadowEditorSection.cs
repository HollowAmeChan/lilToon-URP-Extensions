using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterDropShadowEditorSection
    {
        private static readonly Color DropShadowColor = new Color(0.76f, 0.58f, 0.84f);

        private static bool showSettings;
        private static bool showVolume;

        public static void DrawSettings(SerializedProperty settingsProperty)
        {
            SerializedProperty enabled = Find(settingsProperty, "hairDropShadowEnabled");
            SerializedProperty opacity = Find(settingsProperty, "hairShadowOpacity");
            string summary = enabled != null && enabled.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(opacity)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "DropShadow", summary, DropShadowColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("FrontHair 标记作为投影源，Face 标记作为接收面。这里的参数只影响屏幕空间 DropShadow。", MessageType.None);
                DrawProperty(enabled, "启用前发投影");
                DrawProperty(Find(settingsProperty, "hairShadowColor"), "投影颜色");
                DrawProperty(opacity, "投影不透明度");
                DrawProperty(Find(settingsProperty, "hairShadowDistancePixels"), "投影距离像素");
                DrawProperty(Find(settingsProperty, "hairShadowDistancePerspectiveStrength"), "投影距离透视衰减");
                DrawProperty(Find(settingsProperty, "hairShadowDistanceReferenceDepth"), "投影距离参考深度");
                DrawProperty(Find(settingsProperty, "hairShadowDistanceMinScale"), "投影距离最小倍率");
                DrawProperty(Find(settingsProperty, "hairShadowAngleDegrees"), "投影角度");
                DrawProperty(Find(settingsProperty, "hairShadowSoftnessPixels"), "柔化像素");
                DrawProperty(Find(settingsProperty, "hairShadowSpreadPixels"), "扩散像素");
                DrawProperty(Find(settingsProperty, "hairShadowKeepOffHair"), "避开前发");
                DrawProperty(Find(settingsProperty, "hairShadowBlendMode"), "混合模式");
            }
        }

        public static void DrawVolume(
            SerializedDataParameter enabled,
            SerializedDataParameter color,
            SerializedDataParameter opacity,
            SerializedDataParameter distancePixels,
            SerializedDataParameter distancePerspectiveStrength,
            SerializedDataParameter distanceReferenceDepth,
            SerializedDataParameter distanceMinScale,
            SerializedDataParameter angleDegrees,
            SerializedDataParameter softnessPixels,
            SerializedDataParameter spreadPixels,
            SerializedDataParameter keepOffHair,
            SerializedDataParameter blendMode,
            Action<SerializedDataParameter, GUIContent> drawParameter)
        {
            string summary = enabled?.value != null && enabled.value.boolValue
                ? "开 " + LilUrpEditorSectionGui.FloatSummary(opacity)
                : "关";

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showVolume, "DropShadow", summary, DropShadowColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("FrontHair 标记作为投影源，Face 标记作为接收面。这里的参数只影响屏幕空间 DropShadow。", MessageType.None);
                DrawParameter(enabled, "启用前发投影", drawParameter);
                DrawParameter(color, "投影颜色", drawParameter);
                DrawParameter(opacity, "投影不透明度", drawParameter);
                DrawParameter(distancePixels, "投影距离像素", drawParameter);
                DrawParameter(distancePerspectiveStrength, "投影距离透视衰减", drawParameter);
                DrawParameter(distanceReferenceDepth, "投影距离参考深度", drawParameter);
                DrawParameter(distanceMinScale, "投影距离最小倍率", drawParameter);
                DrawParameter(angleDegrees, "投影角度", drawParameter);
                DrawParameter(softnessPixels, "柔化像素", drawParameter);
                DrawParameter(spreadPixels, "扩散像素", drawParameter);
                DrawParameter(keepOffHair, "避开前发", drawParameter);
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
