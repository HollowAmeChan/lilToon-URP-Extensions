using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class HoPostProcessStackVolumeEditor
    {
        private const float LayerPresetButtonSize = 18.0f;
        private const string LayerPresetIconName = "icon_Settings_v1";
        private const int MaxAovRuleCount = 4;
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
            HoPostProcessEffect effect = GetEffect(element);
            string propertyPath = element.propertyPath;
            GenericMenu menu = new GenericMenu();
            AddHoPostPresetMenuItem(menu, propertyPath, effect, "默认", ApplyHoPostDefaultPreset);
            AddHoPostSpecificPresetMenuItems(menu, propertyPath, effect);
            menu.ShowAsContext();
        }

        private void AddHoPostPresetMenuItem(
            GenericMenu menu,
            string propertyPath,
            HoPostProcessEffect effect,
            string label,
            Action<SerializedProperty, HoPostProcessEffect> apply)
        {
            menu.AddItem(new GUIContent(label), false, () => ApplyHoPostPreset(propertyPath, effect, apply));
        }

        private void ApplyHoPostPreset(
            string propertyPath,
            HoPostProcessEffect effect,
            Action<SerializedProperty, HoPostProcessEffect> apply)
        {
            serializedObject.Update();
            SerializedProperty element = serializedObject.FindProperty(propertyPath);
            if (element == null)
            {
                return;
            }

            bool wasExpanded = element.isExpanded;
            bool wasEnabled = GetBoolValue(element, "enabled", true);
            AovMaskState aovMaskState = CaptureAovMaskState(element);
            Undo.RecordObject(serializedObject.targetObject, "Apply HoPost Preset");
            apply(element, effect);
            SetEnum(element, "effect", (int)effect);
            SetBool(element, "enabled", wasEnabled);
            RestoreAovMaskState(element, aovMaskState);
            element.isExpanded = wasExpanded;
            ApplyLayerListChanges();
        }

        private void AddHoPostSpecificPresetMenuItems(GenericMenu menu, string propertyPath, HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "柔和边缘光", ApplyHoPostSoftEdgeLightPreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "锐利轮廓光", ApplyHoPostSharpEdgeLightPreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "外扩高亮", ApplyHoPostOuterEdgeLightPreset);
                    break;
                case HoPostProcessEffect.Outline:
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "细轮廓", ApplyHoPostThinOutlinePreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "粗轮廓", ApplyHoPostThickOutlinePreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "柔和描边", ApplyHoPostSoftOutlinePreset);
                    break;
                case HoPostProcessEffect.DropShadow:
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "近距离柔影", ApplyHoPostSoftDropShadowPreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "硬投影", ApplyHoPostHardDropShadowPreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "长投影", ApplyHoPostLongDropShadowPreset);
                    break;
                case HoPostProcessEffect.DepthOfField:
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "Gaussian 远景虚化", ApplyHoPostGaussianDepthOfFieldPreset);
                    AddHoPostPresetMenuItem(menu, propertyPath, effect, "Bokeh 人像虚化", ApplyHoPostBokehDepthOfFieldPreset);
                    break;
            }
        }

        private static void ApplyHoPostDefaultPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ResetLayerDefaults(element, effect);
        }

        private static void ApplyHoPostSoftEdgeLightPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.88f, 0.62f, 1.0f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
            SetVector4(element, "parameters0", new Vector4(0.35f, 1.2f, 0.25f, 0.65f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(1.0f, 0.35f, 0.35f, 0.55f));
        }

        private static void ApplyHoPostSharpEdgeLightPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.72f, 0.9f, 1.0f, 1.0f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
            SetVector4(element, "parameters0", new Vector4(0.55f, 3.5f, 0.55f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(18.0f, 3.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(1.15f, 0.9f, 0.6f, 1.0f));
        }

        private static void ApplyHoPostOuterEdgeLightPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.78f, 0.42f, 1.0f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Screen);
            SetVector4(element, "parameters0", new Vector4(0.45f, 2.4f, 0.35f, 0.9f));
            SetVector4(element, "parameters1", new Vector4(-20.0f, 1.0f, 3.0f, 0.55f));
            SetVector4(element, "parameters2", new Vector4(0.75f, 1.0f, 0.5f, 0.85f));
        }

        private static void ApplyHoPostThinOutlinePreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 0.7f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.06f, 1.0f, 1.0f, 0.95f));
        }

        private static void ApplyHoPostThickOutlinePreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(3.0f, 1.2f, 0.9f, 0.07f));
            SetVector4(element, "parameters1", new Vector4(0.1f, 1.1f, 1.0f, 1.0f));
        }

        private static void ApplyHoPostSoftOutlinePreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.04f, 0.06f, 0.1f, 1.0f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(2.0f, 0.75f, 0.7f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.22f, 0.8f, 0.8f, 0.72f));
        }

        private static void ApplyHoPostSoftDropShadowPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.55f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.25f, -45.0f, 0.55f, 10.0f));
            SetVector4(element, "parameters1", new Vector4(2.0f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyHoPostHardDropShadowPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.78f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.18f, -35.0f, 0.8f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.4f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyHoPostLongDropShadowPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.68f));
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
            SetVector4(element, "parameters0", new Vector4(0.6f, -38.0f, 0.75f, 14.0f));
            SetVector4(element, "parameters1", new Vector4(2.5f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyHoPostGaussianDepthOfFieldPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(0.0f, 10.0f, 50.0f, 5.6f));
            SetVector4(element, "parameters1", new Vector4(8.0f, 28.0f, 7.0f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(5.0f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyHoPostBokehDepthOfFieldPreset(SerializedProperty element, HoPostProcessEffect effect)
        {
            ApplyHoPostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, 6.0f, 70.0f, 2.8f));
            SetVector4(element, "parameters1", new Vector4(6.0f, 20.0f, 10.0f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.85f, 0.0f, 0.0f));
        }

        private readonly struct AovMaskState
        {
            public readonly bool UseAovMask;
            public readonly bool UseAovMaskExpanded;
            public readonly int AovSource;
            public readonly int AovMaskMode;
            public readonly float AovThreshold;
            public readonly float AovMatchValue;
            public readonly Color AovMatchColor;
            public readonly bool InvertAovMask;
            public readonly bool DebugAovMask;
            public readonly AovRuleState[] Rules;

            public AovMaskState(SerializedProperty element)
            {
                SerializedProperty useAovMask = element.FindPropertyRelative("useAovMask");
                UseAovMask = GetBoolValue(element, "useAovMask", false);
                UseAovMaskExpanded = useAovMask != null && useAovMask.isExpanded;
                AovSource = GetEnumValue(element, "aovSource", (int)HoPostAovSource.Mask);
                AovMaskMode = GetEnumValue(element, "aovMaskMode", (int)HoPostAovMaskMode.Direct);
                AovThreshold = GetFloatValue(element, "aovThreshold", 0.5f);
                AovMatchValue = GetFloatValue(element, "aovMatchValue", 0.0f);
                AovMatchColor = GetColorValue(element, "aovMatchColor", Color.white);
                InvertAovMask = GetBoolValue(element, "invertAovMask", false);
                DebugAovMask = GetBoolValue(element, "debugAovMask", false);
                Rules = CaptureAovRules(element);
            }
        }

        private readonly struct AovRuleState
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

            public AovRuleState(SerializedProperty rule)
            {
                Enabled = GetBoolValue(rule, "enabled", true);
                Name = GetStringValue(rule, "name", "AOV Rule");
                Source = GetEnumValue(rule, "source", (int)HoPostAovSource.Mask);
                MatchOperator = GetEnumValue(rule, "matchOperator", (int)HoPostAovMaskOperator.Direct);
                Value = GetFloatValue(rule, "value", 0.5f);
                MinValue = GetFloatValue(rule, "minValue", 0.0f);
                MaxValue = GetFloatValue(rule, "maxValue", 1.0f);
                Tolerance = GetFloatValue(rule, "tolerance", 0.02f);
                MatchColor = GetColorValue(rule, "matchColor", Color.white);
                Combine = GetEnumValue(rule, "combine", (int)HoPostAovMaskCombine.Replace);
                Invert = GetBoolValue(rule, "invert", false);
                Expanded = rule != null && rule.isExpanded;
            }
        }

        private static AovMaskState CaptureAovMaskState(SerializedProperty element)
        {
            return new AovMaskState(element);
        }

        private static void RestoreAovMaskState(SerializedProperty element, AovMaskState state)
        {
            SetBool(element, "useAovMask", state.UseAovMask);
            SerializedProperty useAovMask = element.FindPropertyRelative("useAovMask");
            if (useAovMask != null)
            {
                useAovMask.isExpanded = state.UseAovMaskExpanded;
            }

            SetEnum(element, "aovSource", state.AovSource);
            SetEnum(element, "aovMaskMode", state.AovMaskMode);
            SetFloat(element, "aovThreshold", state.AovThreshold);
            SetFloat(element, "aovMatchValue", state.AovMatchValue);
            SetColor(element, "aovMatchColor", state.AovMatchColor);
            SetBool(element, "invertAovMask", state.InvertAovMask);
            SetBool(element, "debugAovMask", state.DebugAovMask);
            RestoreAovRules(element, state.Rules);
        }

        private static AovRuleState[] CaptureAovRules(SerializedProperty element)
        {
            SerializedProperty rules = element.FindPropertyRelative("aovRules");
            if (rules == null || !rules.isArray || rules.arraySize == 0)
            {
                return Array.Empty<AovRuleState>();
            }

            int ruleCount = Mathf.Min(rules.arraySize, MaxAovRuleCount);
            AovRuleState[] states = new AovRuleState[ruleCount];
            for (int i = 0; i < ruleCount; i++)
            {
                states[i] = new AovRuleState(rules.GetArrayElementAtIndex(i));
            }

            return states;
        }

        private static void RestoreAovRules(SerializedProperty element, AovRuleState[] states)
        {
            SerializedProperty rules = element.FindPropertyRelative("aovRules");
            if (rules == null || !rules.isArray || states == null)
            {
                return;
            }

            rules.ClearArray();
            int ruleCount = Mathf.Min(states.Length, MaxAovRuleCount);
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
