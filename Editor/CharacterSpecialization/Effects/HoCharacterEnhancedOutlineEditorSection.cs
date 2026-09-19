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

        /// <summary>只画参数行：标题、启用开关、折叠状态由 Volume 编辑器（效果浏览器）负责。</summary>
        public static void DrawEffects(SerializedProperty effects)
        {
            DrawProperty(Find(effects, "enhancedOutlineSourceChannel"), "来源通道");
            DrawProperty(Find(effects, "enhancedOutlineStrength"), "雾气强度");
            DrawProperty(Find(effects, "enhancedOutlineRadiusPixels"), "外扩半径像素");
            DrawFogProperties(effects);
            DrawHeightFadeProperties(effects);
            DrawProperty(Find(effects, "semanticMaskBlurEnhancedOutline"), "读取抗锯齿掩码");
        }

        private static void DrawFogProperties(
            SerializedProperty effects)
        {
            DrawProperty(Find(effects, "enhancedOutlineFogColor"), "雾气颜色");
            DrawProperty(Find(effects, "enhancedOutlineFogHueShiftDegrees"), "雾气色相偏移");
            DrawProperty(Find(effects, "enhancedOutlineFogSaturation"), "雾气饱和度");
            DrawProperty(Find(effects, "enhancedOutlineFogValue"), "雾气亮度");
            DrawProperty(Find(effects, "enhancedOutlineFogSoftness"), "雾气柔化");
        }

        private static void DrawHeightFadeProperties(
            SerializedProperty effects)
        {
            DrawProperty(Find(effects, "enhancedOutlineHeightFadeMode"), "高度渐隐");
            if (GetHeightFadeMode(Find(effects, "enhancedOutlineHeightFadeMode")) == HoCharacterSubjectOutlineHeightFadeMode.Off)
            {
                return;
            }

            DrawProperty(Find(effects, "enhancedOutlineHeightFadeGroundY"), "地面高度");
            DrawProperty(Find(effects, "enhancedOutlineHeightFadeStart"), "渐隐开始距离");
            DrawProperty(Find(effects, "enhancedOutlineHeightFadeEnd"), "渐隐结束距离");
            DrawProperty(Find(effects, "enhancedOutlineHeightFadeHardness"), "渐隐硬度");
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
    }
}
