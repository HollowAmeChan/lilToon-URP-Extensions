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

        /// <summary>只画参数行：标题、启用开关、折叠状态由 Volume 编辑器（效果浏览器）负责。</summary>
        public static void DrawEffects(SerializedProperty effects, HoCharacterEffectLayout layout)
        {
            layout.Property(Find(effects, "subjectOutlineStrength"), "强度");
            layout.Property(Find(effects, "subjectOutlineRadiusPixels"), "外扩半径像素");
            layout.Property(Find(effects, "subjectOutlineFillMode"), "风格模式");
            DrawModeProperties(
                effects,
                GetFillMode(Find(effects, "subjectOutlineFillMode")), layout);
            DrawHeightFadeProperties(effects, layout);
        }

        private static void DrawModeProperties(
            SerializedProperty effects,
            HoCharacterSubjectOutlineFillMode fillMode, HoCharacterEffectLayout layout)
        {
            switch (fillMode)
            {
                case HoCharacterSubjectOutlineFillMode.NormalColor:
                    layout.Property(Find(effects, "subjectOutlineLevelBlack"), "边缘黑场");
                    layout.Property(Find(effects, "subjectOutlineLevelWhite"), "边缘白场");
                    layout.Property(Find(effects, "subjectOutlineNormalRotationDegrees"), "法线旋转");
                    layout.Property(Find(effects, "subjectOutlineNormalFlowDegreesPerSecond"), "法线流动速度");
                    break;
                case HoCharacterSubjectOutlineFillMode.SoftFog:
                    layout.Property(Find(effects, "subjectOutlineFogColor"), "雾气颜色");
                    layout.Property(Find(effects, "subjectOutlineFogHueShiftDegrees"), "雾气色相偏移");
                    layout.Property(Find(effects, "subjectOutlineFogSaturation"), "雾气饱和度");
                    layout.Property(Find(effects, "subjectOutlineFogValue"), "雾气亮度");
                    layout.Property(Find(effects, "subjectOutlineFogSoftness"), "雾气柔化");
                    break;
                default:
                    layout.Property(Find(effects, "subjectOutlineLevelBlack"), "边缘黑场");
                    layout.Property(Find(effects, "subjectOutlineLevelWhite"), "边缘白场");
                    layout.Property(Find(effects, "subjectOutlineColor"), "轮廓颜色");
                    break;
            }
        }

        private static void DrawHeightFadeProperties(
            SerializedProperty effects, HoCharacterEffectLayout layout)
        {
            layout.Property(Find(effects, "subjectOutlineHeightFadeMode"), "高度渐隐");
            if (GetHeightFadeMode(Find(effects, "subjectOutlineHeightFadeMode")) == HoCharacterSubjectOutlineHeightFadeMode.Off)
            {
                return;
            }

            layout.Property(Find(effects, "subjectOutlineHeightFadeGroundY"), "地面高度");
            layout.Property(Find(effects, "outlineHeightFadeReferenceDistance"), "渐隐参考距离（共用）");
            layout.Property(Find(effects, "subjectOutlineHeightFadeStart"), "渐隐开始距离");
            layout.Property(Find(effects, "subjectOutlineHeightFadeEnd"), "渐隐结束距离");
            layout.Property(Find(effects, "subjectOutlineHeightFadeHardness"), "渐隐硬度");
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


    }
}
