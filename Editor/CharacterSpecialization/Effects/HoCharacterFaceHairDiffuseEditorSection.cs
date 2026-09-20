using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterFaceHairDiffuseEditorSection
    {

        /// <summary>只画参数行：标题、启用开关、折叠状态由 Volume 编辑器（效果浏览器）负责。</summary>
        public static void DrawEffects(SerializedProperty effects)
        {
            DrawProperty(Find(effects, "faceHairDiffuseStrength"), "强度");
            DrawProperty(Find(effects, "faceHairDiffuseRadiusPixels"), "模糊半径像素");
            DrawProperty(Find(effects, "faceHairDiffuseDepthTolerance"), "深度容差");
            DrawProperty(Find(effects, "faceHairDiffuseLevelBlack"), "黑场阈值");
            DrawProperty(Find(effects, "faceHairDiffuseLevelWhite"), "白场阈值");
            DrawProperty(Find(effects, "faceHairDiffuseTintColor"), "染色倍率");
            DrawProperty(Find(effects, "faceHairDiffuseBlendMode"), "混合模式");
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
