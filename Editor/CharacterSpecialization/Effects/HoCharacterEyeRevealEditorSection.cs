using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterEyeRevealEditorSection
    {

        /// <summary>只画参数行：标题、启用开关、折叠状态由 Volume 编辑器（效果浏览器）负责。</summary>
        public static void DrawEffects(SerializedProperty effects)
        {
            DrawProperty(Find(effects, "eyeRevealStrength"), "透过强度");
            DrawProperty(Find(effects, "eyeRevealFeatherPixels"), "羽化像素");
            DrawProperty(Find(effects, "eyeRevealDilationPixels"), "扩张像素");
            DrawProperty(Find(effects, "eyeRevealDepthBias"), "深度偏移");
            DrawProperty(Find(effects, "useEyeRevealArea"), "使用眼透区域");
            DrawProperty(Find(effects, "sameCharacterOnly"), "仅同角色");
            DrawProperty(Find(effects, "semanticMaskBlurEyeReveal"), "读取抗锯齿掩码");
            EditorGUILayout.Space(4.0f);
            DrawProperty(Find(effects, "eyeRevealAngleEnabled"), "启用相机角度修正");
            DrawProperty(Find(effects, "eyeRevealAngleStrength"), "角度修正强度");
            DrawProperty(Find(effects, "eyeRevealAngleYawRangeDegrees"), "平转半角范围");
            DrawProperty(Find(effects, "eyeRevealAnglePitchRangeDegrees"), "俯仰半角范围");
            DrawProperty(Find(effects, "eyeRevealAngleSoftnessDegrees"), "角度柔化");
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
    }
}
