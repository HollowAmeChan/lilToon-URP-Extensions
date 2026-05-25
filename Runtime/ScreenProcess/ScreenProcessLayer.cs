using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    public enum ScreenProcessRuleSource
    {
        [InspectorName("遮罩")]
        Mask = 0,
        [InspectorName("角色组 ID")]
        GroupId = 1,
        [InspectorName("部件 ID")]
        ObjectId = 2,
        [InspectorName("标记")]
        Flags = 3,
        [InspectorName("厚度")]
        Thickness = 4,
        [InspectorName("曲率")]
        Curvature = 5,
        [InspectorName("材质分类")]
        Material = 6,
        [InspectorName("透射提示")]
        TransmittanceHint = 7,
        [InspectorName("材质自定义通道 0")]
        Custom0 = 8,
        [InspectorName("材质自定义通道 1")]
        Custom1 = 9,
        [InspectorName("材质自定义通道 2")]
        Custom2 = 10,
        [InspectorName("材质自定义通道 3")]
        Custom3 = 11,
        [InspectorName("主体")]
        ObjectCustom0 = 12,
        [InspectorName("脸")]
        ObjectCustom1 = 13,
        [InspectorName("前发")]
        ObjectCustom2 = 14,
        [InspectorName("眼睛")]
        ObjectCustom3 = 15,
        [InspectorName("眼透区域")]
        ObjectCustom4 = 16,
        [InspectorName("配件")]
        ObjectCustom5 = 17,
        [InspectorName("预留 6")]
        ObjectCustom6 = 18,
        [InspectorName("预留 7")]
        ObjectCustom7 = 19
    }

    public enum ScreenProcessRuleMaskMode
    {
        Direct = 0,
        Threshold = 1,
        MatchValue = 2,
        MatchColor = 3
    }

    public enum ScreenProcessRuleMaskOperator
    {
        [InspectorName("直接灰度")]
        Direct = 0,
        [InspectorName("阈值")]
        Threshold = 1,
        [InspectorName("大于")]
        Greater = 2,
        [InspectorName("大于等于")]
        GreaterOrEqual = 3,
        [InspectorName("小于")]
        Less = 4,
        [InspectorName("小于等于")]
        LessOrEqual = 5,
        [InspectorName("等于")]
        Equal = 6,
        [InspectorName("不等于")]
        NotEqual = 7,
        [InspectorName("范围")]
        Range = 8,
        [InspectorName("匹配颜色")]
        MatchColor = 9,
        [InspectorName("包含任意标记 bit")]
        FlagsAny = 10,
        [InspectorName("包含全部标记 bit")]
        FlagsAll = 11
    }

    public enum ScreenProcessRuleMaskCombine
    {
        [InspectorName("替换")]
        Replace = 0,
        [InspectorName("或")]
        Or = 1,
        [InspectorName("且")]
        And = 2,
        [InspectorName("减去")]
        Subtract = 3,
        [InspectorName("相加")]
        Add = 4,
        [InspectorName("相乘")]
        Multiply = 5
    }

    [Serializable]
    public sealed class ScreenProcessRuleMaskRule
    {
        [Tooltip("Skip only this ScreenProcess rule mask rule.")]
        public bool enabled = true;

        [Tooltip("Display name in the ScreenProcess rule mask list.")]
        public string name = "ScreenProcess Rule";

        [Tooltip("MetadataBuffer channel sampled by this rule.")]
        public ScreenProcessRuleSource source = ScreenProcessRuleSource.Mask;

        [Tooltip("How the sampled MetadataBuffer data is converted into a rule mask.")]
        public ScreenProcessRuleMaskOperator matchOperator = ScreenProcessRuleMaskOperator.Direct;

        [Tooltip("Primary numeric value used by threshold, compare, equality, and flags modes.")]
        public float value = 0.5f;

        [Tooltip("Lower bound used by Range mode.")]
        public float minValue;

        [Tooltip("Upper bound used by Range mode.")]
        public float maxValue = 1.0f;

        [Tooltip("Tolerance used by equality and color matching.")]
        [Min(0.0f)]
        public float tolerance = 0.02f;

        [Tooltip("Color used by Match Color mode.")]
        public Color matchColor = Color.white;

        [Tooltip("How this rule is combined with the accumulated ScreenProcess mask.")]
        public ScreenProcessRuleMaskCombine combine = ScreenProcessRuleMaskCombine.Replace;

        [Tooltip("Invert this rule within covered MetadataBuffer pixels before combining it.")]
        public bool invert;
    }

    [Serializable]
    public sealed class ScreenProcessLayer
    {
        [Tooltip("Display name in the ScreenProcess stack.")]
        public string name = "ScreenProcess Layer";

        [Tooltip("Keep the layer in the stack, but skip it at runtime.")]
        public bool enabled = true;

        [Tooltip("The ScreenProcess effect slot represented by this layer.")]
        public ScreenProcessEffect effect = ScreenProcessEffect.EdgeLight;

        [Tooltip("Optional material override. Used for experiments or custom passes.")]
        public Material materialOverride;

        [Tooltip("Optional shader override. Runtime creates and caches a material for it.")]
        public Shader shaderOverride;

        [Tooltip("Shader pass index used by this layer.")]
        [Min(0)]
        public int passIndex;

        [Tooltip("Layer intensity. The shader reads _Intensity.")]
        [Range(0.0f, 1.0f)]
        public float intensity = 1.0f;

        [Tooltip("Blend mode hint for ScreenProcess effects. The shader reads _LayerBlendMode.")]
        public ScreenProcessBlendMode blendMode = ScreenProcessBlendMode.Add;

        [Tooltip("Primary color. EdgeLight and other HDR subject effects should treat this as HDR.")]
        public Color color = Color.white;

        [Tooltip("Optional layer texture. The shader reads _LayerTexture and _LayerTextureEnabled.")]
        public Texture texture;

        public Vector4 parameters0;
        public Vector4 parameters1;
        public Vector4 parameters2;
        public Vector4 parameters3;
        public Vector4 parameters4;
        public Vector4 parameters5;

        [Tooltip("Scene object used by ScreenProcess Depth Of Field target focus mode.")]
        public Transform depthOfFieldFocusTarget;

        [Tooltip("Fallback scene hierarchy path used when a Volume Profile asset cannot keep a scene object reference.")]
        public string depthOfFieldFocusTargetPath;

        [Tooltip("Additional camera-space distance offset added to the Depth Of Field focus target.")]
        public float depthOfFieldFocusOffset;

        [Tooltip("Use MetadataBuffer data as a per-layer ScreenProcess mask.")]
        public bool useRuleMask;

        [Tooltip("MetadataBuffer channel sampled by this layer mask.")]
        public ScreenProcessRuleSource ruleSource = ScreenProcessRuleSource.Mask;

        [Tooltip("How the sampled MetadataBuffer data is converted into a mask.")]
        public ScreenProcessRuleMaskMode ruleMaskMode = ScreenProcessRuleMaskMode.Direct;

        [Tooltip("Threshold, tolerance, or match width used by the ScreenProcess mask.")]
        [Min(0.0f)]
        public float ruleThreshold = 0.5f;

        [Tooltip("Numeric value used by Match Value mode. ID sources encode this value before comparison.")]
        public float ruleMatchValue;

        [Tooltip("Color used by Match Color mode.")]
        public Color ruleMatchColor = Color.white;

        [Tooltip("Invert the resolved ScreenProcess mask within covered MetadataBuffer pixels.")]
        public bool invertRuleMask;

        [Tooltip("Replace this layer output with the resolved ScreenProcess mask for debugging.")]
        public bool debugRuleMask;

        [Tooltip("Fine-grained ScreenProcess rule mask list. The runtime evaluates at most four rules. Empty lists fall back to the single-rule fields above.")]
        public List<ScreenProcessRuleMaskRule> ruleMasks = new List<ScreenProcessRuleMaskRule>();

        public bool IsActive => enabled && intensity > 0.0001f;
    }

    internal static class ScreenProcessRuleMaskRuntime
    {
        public const int MaxRuleCount = 4;

        private static readonly Vector4[] RuleData0 = new Vector4[MaxRuleCount];
        private static readonly Vector4[] RuleData1 = new Vector4[MaxRuleCount];
        private static readonly Vector4[] RuleData2 = new Vector4[MaxRuleCount];
        private static readonly Vector4[] RuleColors = new Vector4[MaxRuleCount];

        public static void ApplyToMaterial(
            ScreenProcessLayer layer,
            Material material,
            int ruleCountId,
            int ruleData0Id,
            int ruleData1Id,
            int ruleData2Id,
            int ruleColorId)
        {
            if (layer == null || material == null)
            {
                return;
            }

            ApplyToMaterial(
                material,
                ruleCountId,
                ruleData0Id,
                ruleData1Id,
                ruleData2Id,
                ruleColorId,
                layer.ruleMasks,
                layer.ruleSource,
                layer.ruleMaskMode,
                layer.ruleThreshold,
                layer.ruleMatchValue,
                layer.ruleMatchColor);
        }

        private static void ApplyToMaterial(
            Material material,
            int ruleCountId,
            int ruleData0Id,
            int ruleData1Id,
            int ruleData2Id,
            int ruleColorId,
            List<ScreenProcessRuleMaskRule> rules,
            ScreenProcessRuleSource legacySource,
            ScreenProcessRuleMaskMode legacyMode,
            float legacyThreshold,
            float legacyMatchValue,
            Color legacyMatchColor)
        {
            ClearRuleArrays();
            int ruleCount = FillRuleArrays(
                rules,
                legacySource,
                legacyMode,
                legacyThreshold,
                legacyMatchValue,
                legacyMatchColor);

            material.SetFloat(ruleCountId, ruleCount);
            material.SetVectorArray(ruleData0Id, RuleData0);
            material.SetVectorArray(ruleData1Id, RuleData1);
            material.SetVectorArray(ruleData2Id, RuleData2);
            material.SetVectorArray(ruleColorId, RuleColors);
        }

        private static int FillRuleArrays(
            List<ScreenProcessRuleMaskRule> rules,
            ScreenProcessRuleSource legacySource,
            ScreenProcessRuleMaskMode legacyMode,
            float legacyThreshold,
            float legacyMatchValue,
            Color legacyMatchColor)
        {
            if (rules == null || rules.Count == 0)
            {
                WriteLegacyRule(0, legacySource, legacyMode, legacyThreshold, legacyMatchValue, legacyMatchColor);
                return 1;
            }

            int ruleCount = Mathf.Min(rules.Count, MaxRuleCount);
            for (int i = 0; i < ruleCount; i++)
            {
                WriteRule(i, rules[i]);
            }

            return ruleCount;
        }

        private static void WriteLegacyRule(
            int index,
            ScreenProcessRuleSource source,
            ScreenProcessRuleMaskMode mode,
            float threshold,
            float matchValue,
            Color matchColor)
        {
            ScreenProcessRuleMaskOperator matchOperator = ScreenProcessRuleMaskOperator.Direct;
            float value = 0.0f;
            float tolerance = 0.02f;

            switch (mode)
            {
                case ScreenProcessRuleMaskMode.Threshold:
                    matchOperator = ScreenProcessRuleMaskOperator.Threshold;
                    value = threshold;
                    break;
                case ScreenProcessRuleMaskMode.MatchValue:
                    matchOperator = ScreenProcessRuleMaskOperator.Equal;
                    value = matchValue;
                    tolerance = threshold;
                    break;
                case ScreenProcessRuleMaskMode.MatchColor:
                    matchOperator = ScreenProcessRuleMaskOperator.MatchColor;
                    tolerance = threshold;
                    break;
            }

            RuleData0[index] = new Vector4(
                1.0f,
                ClampEnumValue((int)source, (int)ScreenProcessRuleSource.Mask, (int)ScreenProcessRuleSource.ObjectCustom7),
                (int)matchOperator,
                (int)ScreenProcessRuleMaskCombine.Replace);
            RuleData1[index] = new Vector4(
                value,
                0.0f,
                1.0f,
                Mathf.Max(0.0f, tolerance));
            RuleData2[index] = new Vector4(
                0.0f,
                0.0f,
                0.0f,
                0.0f);
            RuleColors[index] = ColorToVector(matchColor);
        }

        private static void WriteRule(int index, ScreenProcessRuleMaskRule rule)
        {
            if (rule == null)
            {
                return;
            }

            RuleData0[index] = new Vector4(
                rule.enabled ? 1.0f : 0.0f,
                ClampEnumValue((int)rule.source, (int)ScreenProcessRuleSource.Mask, (int)ScreenProcessRuleSource.ObjectCustom7),
                ClampEnumValue((int)rule.matchOperator, (int)ScreenProcessRuleMaskOperator.Direct, (int)ScreenProcessRuleMaskOperator.FlagsAll),
                ClampEnumValue((int)rule.combine, (int)ScreenProcessRuleMaskCombine.Replace, (int)ScreenProcessRuleMaskCombine.Multiply));
            RuleData1[index] = new Vector4(
                rule.value,
                rule.minValue,
                rule.maxValue,
                Mathf.Max(0.0f, rule.tolerance));
            RuleData2[index] = new Vector4(
                0.0f,
                rule.invert ? 1.0f : 0.0f,
                0.0f,
                0.0f);
            RuleColors[index] = ColorToVector(rule.matchColor);
        }

        private static void ClearRuleArrays()
        {
            for (int i = 0; i < MaxRuleCount; i++)
            {
                RuleData0[i] = Vector4.zero;
                RuleData1[i] = Vector4.zero;
                RuleData2[i] = Vector4.zero;
                RuleColors[i] = Vector4.zero;
            }
        }

        private static int ClampEnumValue(int value, int min, int max)
        {
            return Mathf.Clamp(value, min, max);
        }

        private static Vector4 ColorToVector(Color color)
        {
            return new Vector4(color.r, color.g, color.b, color.a);
        }
    }
}
