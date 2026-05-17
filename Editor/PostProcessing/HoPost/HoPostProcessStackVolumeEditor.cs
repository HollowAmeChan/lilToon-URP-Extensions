using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(HoPostProcessStackVolume))]
    internal sealed partial class HoPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float EffectIconSize = 22.0f;
        private const float EffectIconSpacing = 2.0f;
        private const string PackageAssetRoot = "Packages/jp.lilxyzw.liltoon.urp.extensions";

        private readonly struct EffectToggleEntry
        {
            public readonly HoPostProcessEffect Effect;
            public readonly string Label;
            public readonly string IconName;

            public EffectToggleEntry(HoPostProcessEffect effect, string label, string iconName)
            {
                Effect = effect;
                Label = label;
                IconName = iconName;
            }
        }

        private static readonly EffectToggleEntry[] VisibleEffectOrder =
        {
            new EffectToggleEntry(HoPostProcessEffect.EdgeLight, "边缘光", "icon_RimLight_v1"),
            new EffectToggleEntry(HoPostProcessEffect.Outline, "轮廓", "icon_OutLine_v1"),
            new EffectToggleEntry(HoPostProcessEffect.PostLighting, "后期打光", "icon_RimLight_v1"),
            new EffectToggleEntry(HoPostProcessEffect.DropShadow, "投影", "icon_DropShadow_v1"),
            new EffectToggleEntry(HoPostProcessEffect.DepthOfField, "景深", "icon_Effects_v1"),
            new EffectToggleEntry(HoPostProcessEffect.CustomMaterial, "自定义", "icon_Effects_v1")
        };

        private static readonly Dictionary<HoPostProcessEffect, GUIContent> EffectIconContents = new Dictionary<HoPostProcessEffect, GUIContent>();

        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<HoPostProcessStackVolume> fetcher = new PropertyFetcher<HoPostProcessStackVolume>(serializedObject);
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, true, false, false, false);
            layerList.drawHeaderCallback = null;
            layerList.headerHeight = 0.0f;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnDisable()
        {
            DisableHoPostLayerViewControlsForThisEditor();
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(showInSceneView, new GUIContent("Scene View"));
            EditorGUILayout.Space(4.0f);

            DrawEffectIconToggles();
            EditorGUILayout.Space(4.0f);

            DrawLayerList();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayerList()
        {
            if (layers == null)
            {
                return;
            }

            if (layerList != null)
            {
                layerList.DoLayoutList();
            }
            else
            {
                EditorGUILayout.PropertyField(layers, true);
            }
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return LineHeight + 6.0f;
            }

            if (!element.isExpanded)
            {
                return LineHeight + 6.0f;
            }

            int lineCount = GetElementLineCount(element);
            return (LineHeight + LineSpacing) * lineCount + 12.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            SerializedProperty enabledProperty = element.FindPropertyRelative("enabled");
            float y = DrawFoldoutLine(rect, rect.y, element, enabledProperty);
            if (!element.isExpanded)
            {
                if (IsHoPostDirectionDistanceViewControlActive(element))
                {
                    HoPostDirectionDistanceViewControl.Stop();
                }

                if (IsHoPostCenterRadiusViewControlActive(element))
                {
                    HoPostCenterRadiusViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            HoPostProcessEffect effect = GetEffect(element);
            DrawCoreFields(
                rect,
                ref y,
                element,
                includeColorBlend: effect != HoPostProcessEffect.DepthOfField,
                includeTexture: effect == HoPostProcessEffect.CustomMaterial,
                includePassIndex: effect == HoPostProcessEffect.CustomMaterial,
                includeMaterialOverride: effect == HoPostProcessEffect.CustomMaterial);
            DrawAovMaskProperties(rect, ref y, element);

            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    DrawEdgeLightProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.Outline:
                    DrawOutlineProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.DropShadow:
                    DrawDropShadowProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.DepthOfField:
                    DrawDepthOfFieldProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.PostLighting:
                    DrawPostLightingProperties(rect, ref y, element);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        private static int GetElementLineCount(SerializedProperty element)
        {
            switch (GetEffect(element))
            {
                case HoPostProcessEffect.EdgeLight:
                    return 16 + GetAovLineCount(element);
                case HoPostProcessEffect.Outline:
                    return 11 + GetAovLineCount(element);
                case HoPostProcessEffect.DepthOfField:
                    return GetDepthOfFieldLineCount(element) + GetAovLineCount(element);
                case HoPostProcessEffect.PostLighting:
                    return GetPostLightingLineCount(element) + GetAovLineCount(element);
                case HoPostProcessEffect.CustomMaterial:
                    return 7 + GetAovLineCount(element);
                case HoPostProcessEffect.DropShadow:
                default:
                    return 9 + GetAovLineCount(element);
            }
        }

        private static int GetAovLineCount(SerializedProperty element)
        {
            return HoPostAovMaskEditorUtility.GetLineCount(element);
        }

        private float DrawFoldoutLine(Rect rect, float y, SerializedProperty element, SerializedProperty enabled)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            float checkboxWidth = 18.0f;
            float presetWidth = LayerPresetButtonSize;
            float intensityWidth = Mathf.Clamp(rect.width * 0.34f, 140.0f, 220.0f);
            float foldoutWidth = Mathf.Max(0.0f, rect.width - checkboxWidth - presetWidth - intensityWidth - 10.0f);

            if (enabled != null && enabled.propertyType == SerializedPropertyType.Boolean)
            {
                Rect enabledRect = new Rect(lineRect.x, lineRect.y, checkboxWidth, lineRect.height);
                EditorGUI.BeginChangeCheck();
                bool enabledValue = EditorGUI.Toggle(enabledRect, enabled.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    enabled.boolValue = enabledValue;
                    ApplyLayerListChanges();
                }
            }

            Rect foldoutRect = new Rect(lineRect.x + checkboxWidth, lineRect.y, foldoutWidth, lineRect.height);
            element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GetLayerLabel(element), true);

            Rect presetRect = new Rect(lineRect.xMax - intensityWidth - presetWidth - 4.0f, lineRect.y, presetWidth, lineRect.height);
            DrawLayerPresetButton(presetRect, element);

            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            if (intensity != null && intensity.propertyType == SerializedPropertyType.Float)
            {
                Rect intensityRect = new Rect(lineRect.xMax - intensityWidth, lineRect.y, intensityWidth, lineRect.height);
                Rect sliderRect = new Rect(intensityRect.x, intensityRect.y + 2.0f, intensityRect.width, intensityRect.height - 4.0f);
                EditorGUI.BeginChangeCheck();
                float intensityValue = GUI.HorizontalSlider(sliderRect, intensity.floatValue, 0.0f, 1.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    intensity.floatValue = intensityValue;
                    ApplyLayerListChanges();
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private static void DrawCoreFields(Rect rect, ref float y, SerializedProperty element, bool includeColorBlend, bool includeTexture, bool includePassIndex, bool includeMaterialOverride)
        {
            if (includeColorBlend)
            {
                DrawPropertyLine(rect, ref y, element, "color", "颜色");
                DrawPropertyLine(rect, ref y, element, "blendMode", "混合模式");
            }

            if (includeTexture)
            {
                DrawPropertyLine(rect, ref y, element, "texture", "纹理");
            }

            if (includeMaterialOverride)
            {
                DrawPropertyLine(rect, ref y, element, "materialOverride", "材质覆盖");
                DrawPropertyLine(rect, ref y, element, "shaderOverride", "Shader 覆盖");
            }

            if (includePassIndex)
            {
                DrawPropertyLine(rect, ref y, element, "passIndex", "Pass 索引");
            }
        }

        private static void DrawAovMaskProperties(Rect rect, ref float y, SerializedProperty element)
        {
            HoPostAovMaskEditorUtility.Draw(rect, ref y, element, LineHeight, LineSpacing);
        }

        private static void DrawPropertyLine(Rect rect, ref float y, SerializedProperty element, string propertyName, string label)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, LineHeight), property, new GUIContent(label));
            y += LineHeight + LineSpacing;
        }

        private void DrawEffectIconToggles()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            DrawEffectIconRow(VisibleEffectOrder);
        }

        private void DrawEffectIconRow(EffectToggleEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            float width = Mathf.Max(160.0f, EditorGUIUtility.currentViewWidth - 40.0f);
            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((width + EffectIconSpacing) / (EffectIconSize + EffectIconSpacing)));
            int rowCount = Mathf.CeilToInt(entries.Length / (float)buttonsPerRow);
            float height = rowCount * EffectIconSize + Mathf.Max(0, rowCount - 1) * EffectIconSpacing;

            Rect rect = GUILayoutUtility.GetRect(0.0f, height, GUILayout.ExpandWidth(true));
            float x = rect.x;
            float y = rect.y;
            int column = 0;

            foreach (EffectToggleEntry entry in entries)
            {
                if (column >= buttonsPerRow)
                {
                    column = 0;
                    x = rect.x;
                    y += EffectIconSize + EffectIconSpacing;
                }

                DrawEffectIconButton(new Rect(x, y, EffectIconSize, EffectIconSize), entry);
                x += EffectIconSize + EffectIconSpacing;
                column++;
            }
        }

        private void DrawEffectIconButton(Rect rect, EffectToggleEntry entry)
        {
            bool active = HasLayer(entry.Effect);
            GUIContent content = GetEffectIconContent(entry);
            Texture icon = content.image;

            if (icon != null)
            {
                Color oldColor = GUI.color;
                GUI.color = active ? new Color(0.35f, 1.0f, 0.35f, 1.0f) : Color.white;
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = oldColor;
            }
            else
            {
                EditorGUI.LabelField(rect, content);
            }

            GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                ToggleEffect(entry.Effect);
                Event.current.Use();
            }
        }

        private static GUIContent GetEffectIconContent(EffectToggleEntry entry)
        {
            if (EffectIconContents.TryGetValue(entry.Effect, out GUIContent cached))
            {
                return cached;
            }

            Texture2D icon = LoadEffectIcon(entry.IconName);
            GUIContent content = icon != null ? new GUIContent(icon, entry.Label) : new GUIContent(entry.Label);
            EffectIconContents[entry.Effect] = content;
            return content;
        }

        private static Texture2D LoadEffectIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            string[] candidatePaths =
            {
                $"{PackageAssetRoot}/Editor/ShoostIcons/{iconName}.png",
                $"Assets/Editor/ShoostIcons/{iconName}.png"
            };

            foreach (string path in candidatePaths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private void ToggleEffect(HoPostProcessEffect effect)
        {
            if (HasLayer(effect))
            {
                RemoveLayer(effect);
            }
            else
            {
                AddLayer(effect);
            }

            ApplyLayerListChanges();
        }

        private bool HasLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return false;
            }

            int effectIndex = (int)effect;
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if ((int)GetEffect(layerValues.GetArrayElementAtIndex(index)) == effectIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray || HasLayer(effect))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add HoPost Effect");
            int index = GetLayerInsertIndex(effect);
            layerValues.InsertArrayElementAtIndex(index);
            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element, effect);
            element.isExpanded = true;
        }

        private int GetLayerInsertIndex(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return 0;
            }

            int newOrder = GetPreferredLayerOrder(effect);
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if (GetPreferredLayerOrder(GetEffect(layerValues.GetArrayElementAtIndex(index))) > newOrder)
                {
                    return index;
                }
            }

            return layerValues.arraySize;
        }

        private static int GetPreferredLayerOrder(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    return 10;
                case HoPostProcessEffect.Outline:
                    return 20;
                case HoPostProcessEffect.PostLighting:
                    return 30;
                case HoPostProcessEffect.DropShadow:
                    return 40;
                case HoPostProcessEffect.DepthOfField:
                    return 50;
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return 100;
            }
        }

        private void RemoveLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            int effectIndex = (int)effect;
            bool recordedUndo = false;
            for (int index = layerValues.arraySize - 1; index >= 0; index--)
            {
                if ((int)GetEffect(layerValues.GetArrayElementAtIndex(index)) != effectIndex)
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(serializedObject.targetObject, "Remove HoPost Effect");
                    recordedUndo = true;
                }

                layerValues.DeleteArrayElementAtIndex(index);
            }
        }

        private void ApplyLayerListChanges()
        {
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerValues == null || index < 0 || index >= layerValues.arraySize)
            {
                return null;
            }

            return layerValues.GetArrayElementAtIndex(index);
        }

        private static GUIContent GetLayerLabel(SerializedProperty element)
        {
            string effectName = GetEffectDisplayName(GetEffect(element));
            return new GUIContent(effectName, $"效果类型: {effectName}");
        }

        private static HoPostProcessEffect GetEffect(SerializedProperty element)
        {
            SerializedProperty effect = element.FindPropertyRelative("effect");
            int value = effect != null ? effect.enumValueIndex : 0;
            return (HoPostProcessEffect)Mathf.Clamp(value, 0, 5);
        }

        private static string GetEffectDisplayName(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.PostLighting:
                    return "后期打光";
                case HoPostProcessEffect.EdgeLight:
                    return "边缘光";
                case HoPostProcessEffect.Outline:
                    return "轮廓";
                case HoPostProcessEffect.DropShadow:
                    return "投影";
                case HoPostProcessEffect.DepthOfField:
                    return "景深";
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return "自定义";
            }
        }

        private static void ResetLayerDefaults(SerializedProperty element, HoPostProcessEffect effect)
        {
            SetBool(element, "enabled", true);
            SetEnum(element, "effect", (int)effect);
            SetString(element, "name", GetEffectDisplayName(effect));
            SetObjectReference(element, "materialOverride", null);
            SetObjectReference(element, "shaderOverride", null);
            SetObjectReference(element, "texture", null);
            SetInt(element, "passIndex", 0);
            SetFloat(element, "intensity", 1.0f);
            SetColor(element, "color", Color.white);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
            SetVector4(element, "parameters0", Vector4.zero);
            SetVector4(element, "parameters1", Vector4.zero);
            SetVector4(element, "parameters2", Vector4.zero);
            SetVector4(element, "parameters3", Vector4.zero);
            SetVector4(element, "parameters4", Vector4.zero);
            SetVector4(element, "parameters5", Vector4.zero);
            SetObjectReference(element, "depthOfFieldFocusTarget", null);
            SetString(element, "depthOfFieldFocusTargetPath", string.Empty);
            SetFloat(element, "depthOfFieldFocusOffset", 0.0f);
            SetBool(element, "useAovMask", false);
            SetEnum(element, "aovSource", (int)HoPostAovSource.Mask);
            SetEnum(element, "aovMaskMode", (int)HoPostAovMaskMode.Direct);
            SetFloat(element, "aovThreshold", 0.5f);
            SetFloat(element, "aovMatchValue", 0.0f);
            SetColor(element, "aovMatchColor", Color.white);
            SetBool(element, "invertAovMask", false);
            SetBool(element, "debugAovMask", false);
            HoPostAovMaskEditorUtility.ResetRules(element);

            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
                    SetVector4(element, "parameters0", new Vector4(0.45f, 2.0f, 0.35f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 0.65f, 0.45f, 1.0f));
                    break;
                case HoPostProcessEffect.Outline:
                    SetColor(element, "color", Color.black);
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(0.85f, 0.85f, 0.45f, 0.11f));
                    SetVector4(element, "parameters1", new Vector4(0.08f, 0.85f, 0.32f, 0.9f));
                    break;
                case HoPostProcessEffect.DropShadow:
                    SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.65f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
                    SetVector4(element, "parameters0", new Vector4(0.35f, -45.0f, 0.85f, 6.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    SetBool(element, "useAovMask", true);
                    break;
                case HoPostProcessEffect.DepthOfField:
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 10.0f, 50.0f, 5.6f));
                    SetVector4(element, "parameters1", new Vector4(10.0f, 30.0f, 18.0f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(5.0f, 1.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(3.0f, 1.35f, 1.0f, 1.0f));
                    break;
                case HoPostProcessEffect.PostLighting:
                    SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Screen);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.55f, 0.18f, 0.38f));
                    SetVector4(element, "parameters1", new Vector4(90.0f, 1.15f, 0.06f, 0.55f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.58f, 0.62f, 0.28f));
                    SetVector4(element, "parameters3", new Vector4(1.0f, 0.84f, 0.62f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters5", new Vector4(0.35f, 0.28f, 0.0f, 0.45f));
                    break;
                case HoPostProcessEffect.CustomMaterial:
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    break;
            }
        }

        private static void SetBool(SerializedProperty element, string propertyName, bool value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetEnum(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetString(SerializedProperty element, string propertyName, string value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetInt(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedProperty element, string propertyName, float value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetColor(SerializedProperty element, string propertyName, Color value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.colorValue = value;
            }
        }

        private static void SetVector4(SerializedProperty element, string propertyName, Vector4 value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.vector4Value = value;
            }
        }

        private static void SetObjectReference(SerializedProperty element, string propertyName, Object value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }
    }
}
