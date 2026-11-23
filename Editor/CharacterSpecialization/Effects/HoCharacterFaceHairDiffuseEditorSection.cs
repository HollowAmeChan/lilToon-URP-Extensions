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
        public static void DrawEffects(SerializedProperty effects, HoCharacterEffectLayout layout)
        {
            layout.Property(Find(effects, "faceHairDiffuseStrength"), "强度");
            layout.Property(Find(effects, "faceHairDiffuseRadiusPixels"), "模糊半径像素");
            layout.Property(Find(effects, "faceHairDiffuseDepthTolerance"), "深度容差");
            layout.Property(Find(effects, "faceHairDiffuseLevelBlack"), "黑场阈值");
            layout.Property(Find(effects, "faceHairDiffuseLevelWhite"), "白场阈值");
            layout.Property(Find(effects, "faceHairDiffuseTintColor"), "染色倍率");
            layout.Property(Find(effects, "faceHairDiffuseBlendMode"), "混合模式");
        }

        private static SerializedProperty Find(SerializedProperty settingsProperty, string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }


    }
}
