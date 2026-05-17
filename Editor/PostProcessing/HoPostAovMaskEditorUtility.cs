using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal static class HoPostAovMaskEditorUtility
    {
        private const int MaxRuleCount = 4;
        private const float RuleGroupPadding = 6.0f;

        private static readonly string[] AovSources =
        {
            "遮罩",
            "角色组 ID",
            "部件 ID",
            "标记",
            "厚度",
            "曲率",
            "材质分类",
            "预留值",
            "材质自定义通道 0",
            "材质自定义通道 1",
            "材质自定义通道 2",
            "材质自定义通道 3",
            "主体",
            "脸",
            "前发",
            "眼睛",
            "眼透区域",
            "配件",
            "预留 6",
            "预留 7"
        };

        private static readonly string[] AovRuleOperators =
        {
            "直接灰度",
            "阈值",
            "大于",
            "大于等于",
            "小于",
            "小于等于",
            "等于",
            "不等于",
            "范围",
            "匹配颜色",
            "包含任意标记 bit",
            "包含全部标记 bit"
        };

        private static readonly string[] AovRuleCombines =
        {
            "替换",
            "或",
            "且",
            "减去",
            "相加",
            "相乘"
        };

        public static int GetLineCount(SerializedProperty element)
        {
            SerializedProperty useAovMask = element.FindPropertyRelative("useAovMask");
            if (useAovMask == null || !useAovMask.boolValue || !useAovMask.isExpanded)
            {
                return 1;
            }

            SerializedProperty rules = element.FindPropertyRelative("aovRules");
            if (rules == null || !rules.isArray)
            {
                return 1;
            }

            int ruleCount = Mathf.Clamp(rules.arraySize, 1, MaxRuleCount);
            int lineCount = 6;
            for (int i = 0; i < ruleCount; i++)
            {
                SerializedProperty rule = i < rules.arraySize ? rules.GetArrayElementAtIndex(i) : null;
                lineCount += GetRuleLineCount(rule);
            }

            return lineCount;
        }

        public static void Draw(Rect rect, ref float y, SerializedProperty element, float lineHeight, float lineSpacing)
        {
            y = Draw(rect.x, y, rect.width, element, lineHeight, lineSpacing);
        }

        public static float Draw(float x, float y, float width, SerializedProperty element, float lineHeight, float lineSpacing)
        {
            SerializedProperty useAovMask = element.FindPropertyRelative("useAovMask");
            SerializedProperty invertAovMask = element.FindPropertyRelative("invertAovMask");
            SerializedProperty debugAovMask = element.FindPropertyRelative("debugAovMask");
            SerializedProperty rules = element.FindPropertyRelative("aovRules");
            if (useAovMask == null || invertAovMask == null || debugAovMask == null || rules == null || !rules.isArray)
            {
                return y;
            }

            Rect headerRect = new Rect(x, y, width, lineHeight);
            float toggleWidth = 58.0f;
            Rect foldoutRect = new Rect(headerRect.x, headerRect.y, Mathf.Max(0.0f, headerRect.width - toggleWidth), headerRect.height);
            Rect toggleRect = new Rect(headerRect.xMax - toggleWidth, headerRect.y, toggleWidth, headerRect.height);
            useAovMask.isExpanded = EditorGUI.Foldout(foldoutRect, useAovMask.isExpanded, "AOV 遮罩", true);
            useAovMask.boolValue = EditorGUI.ToggleLeft(toggleRect, "启用", useAovMask.boolValue);
            y += lineHeight + lineSpacing;

            if (!useAovMask.boolValue || !useAovMask.isExpanded)
            {
                return y;
            }

            EnsureRuleListFromLegacy(element, rules);
            TrimRulesToMax(rules);

            invertAovMask.boolValue = EditorGUI.Toggle(
                new Rect(x, y, width, lineHeight),
                new GUIContent("最终反转", "只在当前 HoAOV 覆盖范围内反转最终规则组结果。"),
                invertAovMask.boolValue);
            y += lineHeight + lineSpacing;

            debugAovMask.boolValue = EditorGUI.Toggle(
                new Rect(x, y, width, lineHeight),
                new GUIContent("输出匹配结果", "直接输出当前 AOV 规则组解析出的 mask，用于调试。"),
                debugAovMask.boolValue);
            y += lineHeight + lineSpacing;

            int visibleRuleCount = Mathf.Min(rules.arraySize, MaxRuleCount);
            float ruleGroupHeight = GetRuleGroupHeight(rules, lineHeight, lineSpacing);
            Rect ruleGroupRect = new Rect(x, y, width, ruleGroupHeight);
            GUI.Box(ruleGroupRect, GUIContent.none, EditorStyles.helpBox);

            float innerX = x + RuleGroupPadding;
            float innerWidth = Mathf.Max(0.0f, width - RuleGroupPadding * 2.0f);
            y += RuleGroupPadding;

            EditorGUI.LabelField(
                new Rect(innerX, y, innerWidth, lineHeight),
                $"AOV 规则 ({rules.arraySize}/{MaxRuleCount})",
                EditorStyles.boldLabel);
            y += lineHeight + lineSpacing;

            for (int i = 0; i < visibleRuleCount; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                if (DrawRule(innerX, ref y, innerWidth, lineHeight, lineSpacing, rules, rule, i))
                {
                    break;
                }
            }

            Rect addRect = new Rect(innerX, y, innerWidth, lineHeight);
            using (new EditorGUI.DisabledScope(rules.arraySize >= MaxRuleCount))
            {
                if (GUI.Button(addRect, $"添加 AOV 规则 ({rules.arraySize}/{MaxRuleCount})"))
                {
                    int index = rules.arraySize;
                    rules.InsertArrayElementAtIndex(index);
                    SerializedProperty rule = rules.GetArrayElementAtIndex(index);
                    SetDefaultRule(rule, index);
                    rule.isExpanded = true;
                }
            }

            return ruleGroupRect.yMax + lineSpacing;
        }

        public static void ResetRules(SerializedProperty element)
        {
            SerializedProperty rules = element.FindPropertyRelative("aovRules");
            if (rules == null || !rules.isArray)
            {
                return;
            }

            rules.ClearArray();
            rules.InsertArrayElementAtIndex(0);
            SetDefaultRule(rules.GetArrayElementAtIndex(0), 0);
        }

        private static bool DrawRule(
            float x,
            ref float y,
            float width,
            float lineHeight,
            float lineSpacing,
            SerializedProperty rules,
            SerializedProperty rule,
            int index)
        {
            SerializedProperty enabled = rule.FindPropertyRelative("enabled");
            SerializedProperty name = rule.FindPropertyRelative("name");
            SerializedProperty source = rule.FindPropertyRelative("source");
            SerializedProperty matchOperator = rule.FindPropertyRelative("matchOperator");
            SerializedProperty value = rule.FindPropertyRelative("value");
            SerializedProperty minValue = rule.FindPropertyRelative("minValue");
            SerializedProperty maxValue = rule.FindPropertyRelative("maxValue");
            SerializedProperty tolerance = rule.FindPropertyRelative("tolerance");
            SerializedProperty matchColor = rule.FindPropertyRelative("matchColor");
            SerializedProperty combine = rule.FindPropertyRelative("combine");
            SerializedProperty invert = rule.FindPropertyRelative("invert");
            if (enabled == null || name == null || source == null || matchOperator == null || value == null || minValue == null || maxValue == null || tolerance == null || matchColor == null || combine == null || invert == null)
            {
                return false;
            }

            Rect headerRect = new Rect(x, y, width, lineHeight);
            float removeWidth = 22.0f;
            float toggleWidth = 48.0f;
            Rect foldoutRect = new Rect(headerRect.x + 12.0f, headerRect.y, Mathf.Max(0.0f, headerRect.width - removeWidth - toggleWidth - 18.0f), headerRect.height);
            Rect toggleRect = new Rect(headerRect.xMax - removeWidth - toggleWidth, headerRect.y, toggleWidth, headerRect.height);
            Rect removeRect = new Rect(headerRect.xMax - removeWidth, headerRect.y, removeWidth, headerRect.height);

            string label = string.IsNullOrEmpty(name.stringValue) ? $"规则 {index + 1}" : name.stringValue;
            rule.isExpanded = EditorGUI.Foldout(foldoutRect, rule.isExpanded, label, true);
            enabled.boolValue = EditorGUI.ToggleLeft(toggleRect, "启用", enabled.boolValue);

            EditorGUI.BeginDisabledGroup(rules.arraySize <= 1);
            bool remove = GUI.Button(removeRect, "-");
            EditorGUI.EndDisabledGroup();
            y += lineHeight + lineSpacing;
            if (remove && rules.arraySize > 1)
            {
                rules.DeleteArrayElementAtIndex(index);
                return true;
            }

            if (!rule.isExpanded)
            {
                return false;
            }

            name.stringValue = EditorGUI.TextField(new Rect(x, y, width, lineHeight), "名称", name.stringValue);
            y += lineHeight + lineSpacing;

            source.enumValueIndex = EditorGUI.Popup(
                new Rect(x, y, width, lineHeight),
                "AOV 源",
                Mathf.Clamp(source.enumValueIndex, 0, AovSources.Length - 1),
                AovSources);
            y += lineHeight + lineSpacing;

            matchOperator.enumValueIndex = EditorGUI.Popup(
                new Rect(x, y, width, lineHeight),
                "匹配方式",
                Mathf.Clamp(matchOperator.enumValueIndex, 0, AovRuleOperators.Length - 1),
                AovRuleOperators);
            y += lineHeight + lineSpacing;

            DrawRuleParameters(x, ref y, width, lineHeight, lineSpacing, matchOperator, value, minValue, maxValue, tolerance, matchColor);

            combine.enumValueIndex = EditorGUI.Popup(
                new Rect(x, y, width, lineHeight),
                "混合方式",
                Mathf.Clamp(combine.enumValueIndex, 0, AovRuleCombines.Length - 1),
                AovRuleCombines);
            y += lineHeight + lineSpacing;

            invert.boolValue = EditorGUI.Toggle(
                new Rect(x, y, width, lineHeight),
                new GUIContent("反转本规则", "在 HoAOV 覆盖范围内反转当前规则结果，再与规则组累计结果混合。"),
                invert.boolValue);
            y += lineHeight + lineSpacing;
            return false;
        }

        private static void DrawRuleParameters(
            float x,
            ref float y,
            float width,
            float lineHeight,
            float lineSpacing,
            SerializedProperty matchOperator,
            SerializedProperty value,
            SerializedProperty minValue,
            SerializedProperty maxValue,
            SerializedProperty tolerance,
            SerializedProperty matchColor)
        {
            HoPostAovMaskOperator op = (HoPostAovMaskOperator)Mathf.Clamp(matchOperator.enumValueIndex, 0, AovRuleOperators.Length - 1);
            switch (op)
            {
                case HoPostAovMaskOperator.Threshold:
                case HoPostAovMaskOperator.Greater:
                case HoPostAovMaskOperator.GreaterOrEqual:
                case HoPostAovMaskOperator.Less:
                case HoPostAovMaskOperator.LessOrEqual:
                    value.floatValue = EditorGUI.FloatField(new Rect(x, y, width, lineHeight), "比较值", value.floatValue);
                    y += lineHeight + lineSpacing;
                    break;
                case HoPostAovMaskOperator.Equal:
                case HoPostAovMaskOperator.NotEqual:
                    value.floatValue = EditorGUI.FloatField(new Rect(x, y, width, lineHeight), "匹配值 / ID", value.floatValue);
                    y += lineHeight + lineSpacing;
                    DrawTolerance(x, ref y, width, lineHeight, lineSpacing, tolerance);
                    break;
                case HoPostAovMaskOperator.Range:
                    minValue.floatValue = EditorGUI.FloatField(new Rect(x, y, width, lineHeight), "最小值 / ID", minValue.floatValue);
                    y += lineHeight + lineSpacing;
                    maxValue.floatValue = EditorGUI.FloatField(new Rect(x, y, width, lineHeight), "最大值 / ID", maxValue.floatValue);
                    y += lineHeight + lineSpacing;
                    break;
                case HoPostAovMaskOperator.MatchColor:
                    matchColor.colorValue = EditorGUI.ColorField(new Rect(x, y, width, lineHeight), "匹配颜色", matchColor.colorValue);
                    y += lineHeight + lineSpacing;
                    DrawTolerance(x, ref y, width, lineHeight, lineSpacing, tolerance);
                    break;
                case HoPostAovMaskOperator.FlagsAny:
                case HoPostAovMaskOperator.FlagsAll:
                    value.floatValue = Mathf.Clamp(EditorGUI.IntField(new Rect(x, y, width, lineHeight), "标记 bit mask", Mathf.RoundToInt(value.floatValue)), 0, 255);
                    y += lineHeight + lineSpacing;
                    break;
            }
        }

        private static void DrawTolerance(float x, ref float y, float width, float lineHeight, float lineSpacing, SerializedProperty tolerance)
        {
            tolerance.floatValue = EditorGUI.Slider(new Rect(x, y, width, lineHeight), "容差", Mathf.Max(0.0f, tolerance.floatValue), 0.0f, 1.0f);
            y += lineHeight + lineSpacing;
        }

        private static int GetRuleLineCount(SerializedProperty rule)
        {
            if (rule == null || !rule.isExpanded)
            {
                return 1;
            }

            SerializedProperty matchOperator = rule.FindPropertyRelative("matchOperator");
            HoPostAovMaskOperator op = matchOperator != null
                ? (HoPostAovMaskOperator)Mathf.Clamp(matchOperator.enumValueIndex, 0, AovRuleOperators.Length - 1)
                : HoPostAovMaskOperator.Direct;

            int parameterLines = 0;
            switch (op)
            {
                case HoPostAovMaskOperator.Threshold:
                case HoPostAovMaskOperator.Greater:
                case HoPostAovMaskOperator.GreaterOrEqual:
                case HoPostAovMaskOperator.Less:
                case HoPostAovMaskOperator.LessOrEqual:
                    parameterLines = 1;
                    break;
                case HoPostAovMaskOperator.Equal:
                case HoPostAovMaskOperator.NotEqual:
                case HoPostAovMaskOperator.Range:
                case HoPostAovMaskOperator.MatchColor:
                    parameterLines = 2;
                    break;
                case HoPostAovMaskOperator.FlagsAny:
                case HoPostAovMaskOperator.FlagsAll:
                    parameterLines = 1;
                    break;
            }

            return 6 + parameterLines;
        }

        private static float GetRuleGroupHeight(SerializedProperty rules, float lineHeight, float lineSpacing)
        {
            float rowHeight = lineHeight + lineSpacing;
            float height = RuleGroupPadding * 2.0f + rowHeight * 2.0f;
            int visibleRuleCount = Mathf.Min(rules.arraySize, MaxRuleCount);
            for (int i = 0; i < visibleRuleCount; i++)
            {
                height += GetRuleLineCount(rules.GetArrayElementAtIndex(i)) * rowHeight;
            }

            return height;
        }

        private static void EnsureRuleListFromLegacy(SerializedProperty element, SerializedProperty rules)
        {
            if (rules.arraySize > 0)
            {
                return;
            }

            rules.InsertArrayElementAtIndex(0);
            SerializedProperty rule = rules.GetArrayElementAtIndex(0);
            SetRuleFromLegacy(element, rule);
            rule.isExpanded = true;
        }

        private static void TrimRulesToMax(SerializedProperty rules)
        {
            while (rules.arraySize > MaxRuleCount)
            {
                rules.DeleteArrayElementAtIndex(rules.arraySize - 1);
            }
        }

        private static void SetDefaultRule(SerializedProperty rule, int index)
        {
            SetBool(rule, "enabled", true);
            SetString(rule, "name", $"规则 {index + 1}");
            SetEnum(rule, "source", (int)HoPostAovSource.Mask);
            SetEnum(rule, "matchOperator", (int)HoPostAovMaskOperator.Direct);
            SetFloat(rule, "value", 0.5f);
            SetFloat(rule, "minValue", 0.0f);
            SetFloat(rule, "maxValue", 1.0f);
            SetFloat(rule, "tolerance", 0.02f);
            SetColor(rule, "matchColor", Color.white);
            SetEnum(rule, "combine", index == 0 ? (int)HoPostAovMaskCombine.Replace : (int)HoPostAovMaskCombine.Or);
            SetBool(rule, "invert", false);
        }

        private static void SetRuleFromLegacy(SerializedProperty element, SerializedProperty rule)
        {
            SetDefaultRule(rule, 0);
            SetEnum(rule, "source", GetEnum(element, "aovSource", (int)HoPostAovSource.Mask));
            HoPostAovMaskMode mode = (HoPostAovMaskMode)GetEnum(element, "aovMaskMode", (int)HoPostAovMaskMode.Direct);
            float threshold = GetFloat(element, "aovThreshold", 0.5f);
            float matchValue = GetFloat(element, "aovMatchValue", 0.0f);
            Color matchColor = GetColor(element, "aovMatchColor", Color.white);

            switch (mode)
            {
                case HoPostAovMaskMode.Threshold:
                    SetEnum(rule, "matchOperator", (int)HoPostAovMaskOperator.Threshold);
                    SetFloat(rule, "value", threshold);
                    break;
                case HoPostAovMaskMode.MatchValue:
                    SetEnum(rule, "matchOperator", (int)HoPostAovMaskOperator.Equal);
                    SetFloat(rule, "value", matchValue);
                    SetFloat(rule, "tolerance", threshold);
                    break;
                case HoPostAovMaskMode.MatchColor:
                    SetEnum(rule, "matchOperator", (int)HoPostAovMaskOperator.MatchColor);
                    SetColor(rule, "matchColor", matchColor);
                    SetFloat(rule, "tolerance", threshold);
                    break;
            }

        }

        private static bool GetBool(SerializedProperty element, string name, bool fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Boolean ? property.boolValue : fallback;
        }

        private static int GetEnum(SerializedProperty element, string name, int fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Enum ? property.enumValueIndex : fallback;
        }

        private static float GetFloat(SerializedProperty element, string name, float fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Float ? property.floatValue : fallback;
        }

        private static Color GetColor(SerializedProperty element, string name, Color fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Color ? property.colorValue : fallback;
        }

        private static void SetBool(SerializedProperty element, string name, bool value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean)
            {
                property.boolValue = value;
            }
        }

        private static void SetString(SerializedProperty element, string name, string value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = value;
            }
        }

        private static void SetEnum(SerializedProperty element, string name, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Enum)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetFloat(SerializedProperty element, string name, float value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value;
            }
        }

        private static void SetColor(SerializedProperty element, string name, Color value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Color)
            {
                property.colorValue = value;
            }
        }
    }
}
