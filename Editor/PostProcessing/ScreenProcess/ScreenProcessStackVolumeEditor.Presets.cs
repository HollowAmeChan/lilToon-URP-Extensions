using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        private const float LayerPresetButtonSize = 18.0f;
        private const string LayerPresetIconName = "icon_Settings_v1";
        private const int MaxRuleMaskCount = 4;
        private static GUIContent layerPresetIconContent;

        private void DrawLayerPresetButton(Rect rect, SerializedProperty element)
        {
            if (element == null)
            {
                return;
            }

            GUIContent content = GetLayerPresetIconContent();
            Texture icon = content.image;
            if (icon != null)
            {
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            }
            else
            {
                EditorGUI.LabelField(rect, content);
            }

            GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                ShowLayerPresetMenu(element);
                Event.current.Use();
            }
        }

        private static GUIContent GetLayerPresetIconContent()
        {
            if (layerPresetIconContent != null)
            {
                return layerPresetIconContent;
            }

            Texture2D icon = LoadEffectIcon(LayerPresetIconName);
            layerPresetIconContent = icon != null
                ? new GUIContent(icon, "预设")
                : new GUIContent("P", "预设");
            return layerPresetIconContent;
        }

        private void ShowLayerPresetMenu(SerializedProperty element)
        {
            ScreenProcessEffect effect = GetEffect(element);
            string propertyPath = element.propertyPath;
            GenericMenu menu = new GenericMenu();
            AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "默认", ApplyScreenProcessDefaultPreset);
            AddScreenProcessSpecificPresetMenuItems(menu, propertyPath, effect);
            menu.ShowAsContext();
        }

        private void AddScreenProcessPresetMenuItem(
            GenericMenu menu,
            string propertyPath,
            ScreenProcessEffect effect,
            string label,
            Action<SerializedProperty, ScreenProcessEffect> apply)
        {
            menu.AddItem(new GUIContent(label), false, () => ApplyScreenProcessPreset(propertyPath, effect, apply));
        }

        private void ApplyScreenProcessPreset(
            string propertyPath,
            ScreenProcessEffect effect,
            Action<SerializedProperty, ScreenProcessEffect> apply)
        {
            serializedObject.Update();
            SerializedProperty element = serializedObject.FindProperty(propertyPath);
            if (element == null)
            {
                return;
            }

            bool wasExpanded = element.isExpanded;
            bool wasEnabled = GetBoolValue(element, "enabled", true);
            RuleMaskState ruleMaskState = CaptureRuleMaskState(element);
            Undo.RecordObject(serializedObject.targetObject, "Apply ScreenProcess Preset");
            apply(element, effect);
            SetEnum(element, "effect", (int)effect);
            SetBool(element, "enabled", wasEnabled);
            RestoreRuleMaskState(element, ruleMaskState);
            element.isExpanded = wasExpanded;
            ApplyLayerListChanges();
        }

        private void AddScreenProcessSpecificPresetMenuItems(GenericMenu menu, string propertyPath, ScreenProcessEffect effect)
        {
            switch (effect)
            {
                case ScreenProcessEffect.PostLighting:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "暖色顶光", ApplyScreenProcessWarmTopPostLightingPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "中心聚光", ApplyScreenProcessCenterPostLightingPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "冷暖 MatCap", ApplyScreenProcessMatcapPostLightingPreset);
                    break;
                case ScreenProcessEffect.SkyTyndall:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "Soft Sky Shafts", ApplyScreenProcessSoftSkyTyndallPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "Foreground Rays", ApplyScreenProcessForegroundSkyTyndallPreset);
                    break;
                case ScreenProcessEffect.EdgeLight:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "柔和边缘光", ApplyScreenProcessSoftEdgeLightPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "锐利轮廓光", ApplyScreenProcessSharpEdgeLightPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "外扩高亮", ApplyScreenProcessOuterEdgeLightPreset);
                    break;
                case ScreenProcessEffect.Outline:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "细轮廓", ApplyScreenProcessThinOutlinePreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "粗轮廓", ApplyScreenProcessThickOutlinePreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "柔和描边", ApplyScreenProcessSoftOutlinePreset);
                    break;
                case ScreenProcessEffect.DropShadow:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "近距离柔影", ApplyScreenProcessSoftDropShadowPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "硬投影", ApplyScreenProcessHardDropShadowPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "长投影", ApplyScreenProcessLongDropShadowPreset);
                    break;
                case ScreenProcessEffect.DepthOfField:
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "Gaussian 远景虚化", ApplyScreenProcessGaussianDepthOfFieldPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "Bokeh 人像虚化", ApplyScreenProcessBokehDepthOfFieldPreset);
                    AddScreenProcessPresetMenuItem(menu, propertyPath, effect, "目标跟焦强景深", ApplyScreenProcessTargetDepthOfFieldPreset);
                    break;
            }
        }

        private static void ApplyScreenProcessDefaultPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ResetLayerDefaults(element, effect);
        }

        private static void ApplyScreenProcessSoftEdgeLightPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.88f, 0.62f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Add);
            SetVector4(element, "parameters0", new Vector4(0.35f, 1.2f, 0.25f, 0.65f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(1.0f, 0.35f, 0.35f, 0.55f));
        }

        private static void ApplyScreenProcessSharpEdgeLightPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.72f, 0.9f, 1.0f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Add);
            SetVector4(element, "parameters0", new Vector4(0.55f, 3.5f, 0.55f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(18.0f, 3.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(1.15f, 0.9f, 0.6f, 1.0f));
        }

        private static void ApplyScreenProcessOuterEdgeLightPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.78f, 0.42f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
            SetVector4(element, "parameters0", new Vector4(0.45f, 2.4f, 0.35f, 0.9f));
            SetVector4(element, "parameters1", new Vector4(-20.0f, 1.0f, 3.0f, 0.55f));
            SetVector4(element, "parameters2", new Vector4(0.75f, 1.0f, 0.5f, 0.85f));
        }

        private static void ApplyScreenProcessThinOutlinePreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(0.55f, 0.8f, 0.35f, 0.10f));
            SetVector4(element, "parameters1", new Vector4(0.04f, 0.75f, 0.28f, 0.85f));
        }

        private static void ApplyScreenProcessThickOutlinePreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.35f, 0.95f, 0.48f, 0.10f));
            SetVector4(element, "parameters1", new Vector4(0.06f, 0.85f, 0.42f, 0.95f));
        }

        private static void ApplyScreenProcessSoftOutlinePreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.04f, 0.06f, 0.1f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(0.95f, 0.65f, 0.35f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.18f, 0.70f, 0.34f, 0.65f));
        }

        private static void ApplyScreenProcessSoftDropShadowPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.55f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.25f, -45.0f, 0.55f, 10.0f));
            SetVector4(element, "parameters1", new Vector4(2.0f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyScreenProcessHardDropShadowPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.78f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.18f, -35.0f, 0.8f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.4f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyScreenProcessLongDropShadowPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.68f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.6f, -38.0f, 0.75f, 14.0f));
            SetVector4(element, "parameters1", new Vector4(2.5f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyScreenProcessGaussianDepthOfFieldPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(0.0f, 10.0f, 50.0f, 5.6f));
            SetVector4(element, "parameters1", new Vector4(8.0f, 28.0f, 20.0f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(5.0f, 1.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(2.4f, 1.0f, 1.0f, 1.0f));
        }

        private static void ApplyScreenProcessBokehDepthOfFieldPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, 6.0f, 70.0f, 2.8f));
            SetVector4(element, "parameters1", new Vector4(6.0f, 20.0f, 34.0f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.85f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(4.0f, 1.45f, 1.2f, 0.9f));
        }

        private static void ApplyScreenProcessTargetDepthOfFieldPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(2.0f, 4.0f, 95.0f, 1.8f));
            SetVector4(element, "parameters1", new Vector4(4.0f, 18.0f, 56.0f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(7.0f, 0.78f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(6.0f, 1.8f, 1.35f, 0.75f));
        }

        private static void ApplyScreenProcessWarmTopPostLightingPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.55f, 0.18f, 0.38f));
            SetVector4(element, "parameters1", new Vector4(90.0f, 1.15f, 0.06f, 0.55f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.58f, 0.62f, 0.28f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.84f, 0.62f, 1.0f));
            SetVector4(element, "parameters4", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            SetVector4(element, "parameters5", new Vector4(0.35f, 0.28f, 0.0f, 0.45f));
        }

        private static void ApplyScreenProcessCenterPostLightingPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.94f, 0.82f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.70f, 0.20f, 0.42f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.85f, 0.0f, 0.50f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.52f, 0.48f, 0.34f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.94f, 0.82f, 1.0f));
            SetVector4(element, "parameters4", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            SetVector4(element, "parameters5", new Vector4(0.35f, 0.32f, 0.0f, 0.45f));
        }

        private static void ApplyScreenProcessMatcapPostLightingPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.72f, 0.86f, 1.0f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
            SetVector4(element, "parameters0", new Vector4(2.0f, 0.48f, 0.24f, 0.34f));
            SetVector4(element, "parameters1", new Vector4(135.0f, 0.9f, 0.0f, 0.72f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.5f, 0.2f));
            SetVector4(element, "parameters3", new Vector4(0.72f, 0.86f, 1.0f, 1.0f));
            SetVector4(element, "parameters4", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            SetVector4(element, "parameters5", new Vector4(0.45f, 0.24f, 1.0f, 1.25f));
        }

        private static void ApplyScreenProcessSoftSkyTyndallPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.88f, 0.68f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Add);
            SetVector4(element, "parameters0", new Vector4(0.58f, 0.85f, 0.55f, 1.15f));
            SetVector4(element, "parameters1", new Vector4(0.5f, 0.14f, 1.8f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(0.20f, 0.25f, 1.0f, 0.75f));
            SetVector4(element, "parameters3", new Vector4(0.45f, 0.0f, 1.0f, 1.0f));
        }

        private static void ApplyScreenProcessForegroundSkyTyndallPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.78f, 0.48f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
            SetVector4(element, "parameters0", new Vector4(0.74f, 1.1f, 0.38f, 1.35f));
            SetVector4(element, "parameters1", new Vector4(0.48f, 0.12f, 1.35f, 2.0f));
            SetVector4(element, "parameters2", new Vector4(0.68f, 0.70f, 1.6f, 0.55f));
            SetVector4(element, "parameters3", new Vector4(0.72f, 0.0f, 1.0f, 1.0f));
        }

        private readonly struct RuleMaskState
        {
            public readonly bool UseRuleMask;
            public readonly bool UseRuleMaskExpanded;
            public readonly int RuleSource;
            public readonly int RuleMaskMode;
            public readonly float RuleThreshold;
            public readonly float RuleMatchValue;
            public readonly Color RuleMatchColor;
            public readonly bool InvertRuleMask;
            public readonly bool DebugRuleMask;
            public readonly RuleMaskRuleState[] Rules;

            public RuleMaskState(SerializedProperty element)
            {
                SerializedProperty useRuleMask = element.FindPropertyRelative("useRuleMask");
                UseRuleMask = GetBoolValue(element, "useRuleMask", false);
                UseRuleMaskExpanded = useRuleMask != null && useRuleMask.isExpanded;
                RuleSource = GetEnumValue(element, "ruleSource", (int)ScreenProcessRuleSource.Mask);
                RuleMaskMode = GetEnumValue(element, "ruleMaskMode", (int)ScreenProcessRuleMaskMode.Direct);
                RuleThreshold = GetFloatValue(element, "ruleThreshold", 0.5f);
                RuleMatchValue = GetFloatValue(element, "ruleMatchValue", 0.0f);
                RuleMatchColor = GetColorValue(element, "ruleMatchColor", Color.white);
                InvertRuleMask = GetBoolValue(element, "invertRuleMask", false);
                DebugRuleMask = GetBoolValue(element, "debugRuleMask", false);
                Rules = CaptureRuleMasks(element);
            }
        }

        private readonly struct RuleMaskRuleState
        {
            public readonly bool Enabled;
            public readonly string Name;
            public readonly int Source;
            public readonly int MatchOperator;
            public readonly float Value;
            public readonly float MinValue;
            public readonly float MaxValue;
            public readonly float Tolerance;
            public readonly Color MatchColor;
            public readonly int Combine;
            public readonly bool Invert;
            public readonly bool Expanded;

            public RuleMaskRuleState(SerializedProperty rule)
            {
                Enabled = GetBoolValue(rule, "enabled", true);
                Name = GetStringValue(rule, "name", "ScreenProcess Rule");
                Source = GetEnumValue(rule, "source", (int)ScreenProcessRuleSource.Mask);
                MatchOperator = GetEnumValue(rule, "matchOperator", (int)ScreenProcessRuleMaskOperator.Direct);
                Value = GetFloatValue(rule, "value", 0.5f);
                MinValue = GetFloatValue(rule, "minValue", 0.0f);
                MaxValue = GetFloatValue(rule, "maxValue", 1.0f);
                Tolerance = GetFloatValue(rule, "tolerance", 0.02f);
                MatchColor = GetColorValue(rule, "matchColor", Color.white);
                Combine = GetEnumValue(rule, "combine", (int)ScreenProcessRuleMaskCombine.Replace);
                Invert = GetBoolValue(rule, "invert", false);
                Expanded = rule != null && rule.isExpanded;
            }
        }

        private static RuleMaskState CaptureRuleMaskState(SerializedProperty element)
        {
            return new RuleMaskState(element);
        }

        private static void RestoreRuleMaskState(SerializedProperty element, RuleMaskState state)
        {
            SetBool(element, "useRuleMask", state.UseRuleMask);
            SerializedProperty useRuleMask = element.FindPropertyRelative("useRuleMask");
            if (useRuleMask != null)
            {
                useRuleMask.isExpanded = state.UseRuleMaskExpanded;
            }

            SetEnum(element, "ruleSource", state.RuleSource);
            SetEnum(element, "ruleMaskMode", state.RuleMaskMode);
            SetFloat(element, "ruleThreshold", state.RuleThreshold);
            SetFloat(element, "ruleMatchValue", state.RuleMatchValue);
            SetColor(element, "ruleMatchColor", state.RuleMatchColor);
            SetBool(element, "invertRuleMask", state.InvertRuleMask);
            SetBool(element, "debugRuleMask", state.DebugRuleMask);
            RestoreRuleMasks(element, state.Rules);
        }

        private static RuleMaskRuleState[] CaptureRuleMasks(SerializedProperty element)
        {
            SerializedProperty rules = element.FindPropertyRelative("ruleMasks");
            if (rules == null || !rules.isArray || rules.arraySize == 0)
            {
                return Array.Empty<RuleMaskRuleState>();
            }

            int ruleCount = Mathf.Min(rules.arraySize, MaxRuleMaskCount);
            RuleMaskRuleState[] states = new RuleMaskRuleState[ruleCount];
            for (int i = 0; i < ruleCount; i++)
            {
                states[i] = new RuleMaskRuleState(rules.GetArrayElementAtIndex(i));
            }

            return states;
        }

        private static void RestoreRuleMasks(SerializedProperty element, RuleMaskRuleState[] states)
        {
            SerializedProperty rules = element.FindPropertyRelative("ruleMasks");
            if (rules == null || !rules.isArray || states == null)
            {
                return;
            }

            rules.ClearArray();
            int ruleCount = Mathf.Min(states.Length, MaxRuleMaskCount);
            for (int i = 0; i < ruleCount; i++)
            {
                rules.InsertArrayElementAtIndex(i);
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                SetBool(rule, "enabled", states[i].Enabled);
                SetString(rule, "name", states[i].Name);
                SetEnum(rule, "source", states[i].Source);
                SetEnum(rule, "matchOperator", states[i].MatchOperator);
                SetFloat(rule, "value", states[i].Value);
                SetFloat(rule, "minValue", states[i].MinValue);
                SetFloat(rule, "maxValue", states[i].MaxValue);
                SetFloat(rule, "tolerance", states[i].Tolerance);
                SetColor(rule, "matchColor", states[i].MatchColor);
                SetEnum(rule, "combine", states[i].Combine);
                SetBool(rule, "invert", states[i].Invert);
                rule.isExpanded = states[i].Expanded;
            }
        }

        private static bool GetBoolValue(SerializedProperty element, string name, bool fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue
                : fallback;
        }

        private static int GetEnumValue(SerializedProperty element, string name, int fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Enum
                ? property.enumValueIndex
                : fallback;
        }

        private static float GetFloatValue(SerializedProperty element, string name, float fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Float
                ? property.floatValue
                : fallback;
        }

        private static Color GetColorValue(SerializedProperty element, string name, Color fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Color
                ? property.colorValue
                : fallback;
        }

        private static string GetStringValue(SerializedProperty element, string name, string fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.String
                ? property.stringValue
                : fallback;
        }
    }
}
