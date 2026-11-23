using System;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    internal static class HoCharacterDropShadowEditorSection
    {

        /// <summary>只画参数行：标题、启用开关、折叠状态由 Volume 编辑器（效果浏览器）负责。</summary>
        public static void DrawEffects(SerializedProperty effects, HoCharacterEffectLayout layout)
        {
            layout.Property(Find(effects, "hairShadowColor"), "投影颜色");
            layout.Property(Find(effects, "hairShadowOpacity"), "投影不透明度");
            layout.Property(Find(effects, "hairShadowDistancePixels"), "投影距离像素");
            layout.Property(Find(effects, "hairShadowDistancePerspectiveStrength"), "投影距离透视衰减");
            layout.Property(Find(effects, "hairShadowDistanceReferenceDepth"), "投影距离参考深度");
            layout.Property(Find(effects, "hairShadowDistanceMinScale"), "投影距离最小倍率");
            layout.Property(Find(effects, "hairShadowAngleDegrees"), "投影角度");
            layout.Property(Find(effects, "hairShadowSoftnessPixels"), "柔化像素");
            layout.Property(Find(effects, "hairShadowSpreadPixels"), "扩散像素");
            layout.Property(Find(effects, "hairShadowBlendMode"), "混合模式");
        }

        private static SerializedProperty Find(SerializedProperty settingsProperty, string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }


    }
}
