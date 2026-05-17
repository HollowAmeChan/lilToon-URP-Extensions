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

        private readonly struct AovMaskState
        {
            public readonly bool UseAovMask;
            public readonly bool UseAovMaskExpanded;
            public readonly int AovSource;
            public readonly int AovMaskMode;
            public readonly float AovThreshold;
            public readonly float AovSoftness;
            public readonly float AovMatchValue;
            public readonly Color AovMatchColor;
            public readonly bool InvertAovMask;
            public readonly bool DebugAovMask;

            public AovMaskState(SerializedProperty element)
            {
                SerializedProperty useAovMask = element.FindPropertyRelative("useAovMask");
                UseAovMask = GetBoolValue(element, "useAovMask", false);
                UseAovMaskExpanded = useAovMask != null && useAovMask.isExpanded;
                AovSource = GetEnumValue(element, "aovSource", (int)HoPostAovSource.Mask);
                AovMaskMode = GetEnumValue(element, "aovMaskMode", (int)HoPostAovMaskMode.Direct);
                AovThreshold = GetFloatValue(element, "aovThreshold", 0.5f);
                AovSoftness = GetFloatValue(element, "aovSoftness", 0.02f);
                AovMatchValue = GetFloatValue(element, "aovMatchValue", 0.0f);
                AovMatchColor = GetColorValue(element, "aovMatchColor", Color.white);
                InvertAovMask = GetBoolValue(element, "invertAovMask", false);
                DebugAovMask = GetBoolValue(element, "debugAovMask", false);
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
            SetFloat(element, "aovSoftness", state.AovSoftness);
            SetFloat(element, "aovMatchValue", state.AovMatchValue);
            SetColor(element, "aovMatchColor", state.AovMatchColor);
            SetBool(element, "invertAovMask", state.InvertAovMask);
            SetBool(element, "debugAovMask", state.DebugAovMask);
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
    }
}
