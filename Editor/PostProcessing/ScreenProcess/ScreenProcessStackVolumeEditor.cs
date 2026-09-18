using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ScreenProcessStackVolume))]
    internal sealed partial class ScreenProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;

        private readonly struct EffectToggleEntry
        {
            public readonly ScreenProcessEffect Effect;
            public readonly string Label;
            public readonly string IconName;

            public EffectToggleEntry(ScreenProcessEffect effect, string label, string iconName)
            {
                Effect = effect;
                Label = label;
                IconName = iconName;
            }
        }

        private static readonly EffectToggleEntry[] VisibleEffectOrder =
        {
            new EffectToggleEntry(ScreenProcessEffect.EdgeLight, "边缘光", "icon_RimLight_v1"),
            new EffectToggleEntry(ScreenProcessEffect.Outline, "轮廓", "icon_OutLine_v1"),
            new EffectToggleEntry(ScreenProcessEffect.PostLighting, "后期打光", "icon_RimLight_v1"),
            new EffectToggleEntry(ScreenProcessEffect.SkyTyndall, "天光丁达尔", "icon_Flare_Ray_v1"),
            new EffectToggleEntry(ScreenProcessEffect.DropShadow, "投影", "icon_DropShadow_v1"),
            new EffectToggleEntry(ScreenProcessEffect.DepthOfField, "景深", "icon_Effects_v1"),
            new EffectToggleEntry(ScreenProcessEffect.DepthFog, "深度雾", "icon_Weather_v1"),
            new EffectToggleEntry(ScreenProcessEffect.CustomMaterial, "自定义", "icon_Effects_v1")
        };

        /// <summary>Search text + sidebar style, remembered per editor type (see EffectBrowserState).</summary>
        private EffectBrowserState effectBrowserState;

        /// <summary>The palette handed to the shared effect browser (see EffectBrowserView).</summary>
        private EffectBrowserCatalog effectBrowserCatalog;

        /// <summary>Row highlight tint for layers that match the current search.</summary>
        private static readonly Color LayerHighlightColor = new Color(0.30f, 0.55f, 0.95f, 0.16f);

        /// <summary>Accent bar drawn on the left edge of a highlighted row.</summary>
        private static readonly Color LayerHighlightAccent = new Color(0.35f, 0.65f, 1.0f, 0.85f);

        // Every declared effect, ordered the way the enum declares them (= the order Unity's enum popup
        // uses, and therefore the meaning of SerializedProperty.enumValueIndex).
        private static readonly ScreenProcessEffect[] EffectValues = BuildEffectValues();

        private static ScreenProcessEffect[] BuildEffectValues()
        {
            var values = (ScreenProcessEffect[])System.Enum.GetValues(typeof(ScreenProcessEffect));
            System.Array.Sort(values);
            return values;
        }

        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<ScreenProcessStackVolume> fetcher = new PropertyFetcher<ScreenProcessStackVolume>(serializedObject);
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
            DisableScreenProcessLayerViewControlsForThisEditor();
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(showInSceneView, new GUIContent("Scene View"));
            EditorGUILayout.Space(4.0f);
            EnsureEffectBrowser();
            EffectBrowserView.Draw(effectBrowserCatalog, effectBrowserState, DrawLayerList);
            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ effect browser

        /// <summary>
        /// Builds the browser state/catalog once: the palette table below plus three callbacks into
        /// this editor (the browser itself is shared with the ImageProcess editor).
        /// </summary>
        private void EnsureEffectBrowser()
        {
            if (effectBrowserState == null)
            {
                effectBrowserState = EffectBrowserState.For("ScreenProcess");
            }

            if (effectBrowserCatalog != null)
            {
                return;
            }

            effectBrowserCatalog = new EffectBrowserCatalog(
                "ScreenProcess",
                BuildBrowserEntries(VisibleEffectOrder),
                effect => HasLayer((ScreenProcessEffect)effect),
                effect => ToggleEffect((ScreenProcessEffect)effect),
                ResetEffectToDefaults,
                CountLayersForEffect);
        }

        /// <summary>Turns the authored palette into browser entries; the enum name comes from the enum.</summary>
        private static EffectBrowserEntry[] BuildBrowserEntries(EffectToggleEntry[] source)
        {
            if (source == null || source.Length == 0)
            {
                return System.Array.Empty<EffectBrowserEntry>();
            }

            var entries = new EffectBrowserEntry[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                entries[i] = new EffectBrowserEntry(
                    (int)source[i].Effect,
                    source[i].Label,
                    System.Enum.GetName(typeof(ScreenProcessEffect), source[i].Effect) ?? string.Empty,
                    source[i].IconName);
            }

            return entries;
        }

        /// <summary>How many layers use that effect (right-click menu and the highlight count).</summary>
        private int CountLayersForEffect(int effectValue)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return 0;
            }

            int count = 0;
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if ((int)GetEffect(layerValues.GetArrayElementAtIndex(index)) == effectValue)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Adds the effect when missing, otherwise puts its layer back to the effect defaults.</summary>
        private void ResetEffectToDefaults(int effectValue)
        {
            ScreenProcessEffect effect = (ScreenProcessEffect)effectValue;
            if (!HasLayer(effect))
            {
                AddLayer(effect);
                ApplyLayerListChanges();
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Reset ScreenProcess Effect");
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                if ((int)GetEffect(element) != effectValue)
                {
                    continue;
                }

                ResetLayerDefaults(element, effect);
            }

            ApplyLayerListChanges();
        }

        /// <summary>Removes one specific row (the × button), unlike RemoveLayer which drops every match.</summary>
        private void RemoveLayerAt(int index)
        {
            if (layerValues == null || !layerValues.isArray || index < 0 || index >= layerValues.arraySize)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Remove ScreenProcess Layer");
            layerValues.DeleteArrayElementAtIndex(index);
            ApplyLayerListChanges();
        }

        /// <summary>Array index of a serialized layer element ("layers.m_Value.Array.data[3]").</summary>
        private static int GetLayerArrayIndex(SerializedProperty element)
        {
            if (element == null)
            {
                return -1;
            }

            string path = element.propertyPath;
            int open = path.LastIndexOf('[');
            int close = path.LastIndexOf(']');
            if (open < 0 || close <= open + 1)
            {
                return -1;
            }

            return int.TryParse(path.Substring(open + 1, close - open - 1), out int index) ? index : -1;
        }

        /// <summary>Background tint + accent bar for a layer whose effect matches the search.</summary>
        private void DrawLayerHighlight(Rect rect, SerializedProperty element)
        {
            string query = EffectBrowserView.CurrentQuery;
            if (string.IsNullOrEmpty(query) || effectBrowserCatalog == null)
            {
                return;
            }

            if (!effectBrowserCatalog.LayerEffectMatches((int)GetEffect(element), query))
            {
                return;
            }

            EditorGUI.DrawRect(rect, LayerHighlightColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2.0f, rect.height), LayerHighlightAccent);
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
            DrawLayerHighlight(rect, element);
            SerializedProperty enabledProperty = element.FindPropertyRelative("enabled");
            float y = DrawFoldoutLine(rect, rect.y, element, enabledProperty);
            if (!element.isExpanded)
            {
                if (IsScreenProcessDirectionDistanceViewControlActive(element))
                {
                    ScreenProcessDirectionDistanceViewControl.Stop();
                }

                if (IsScreenProcessCenterRadiusViewControlActive(element))
                {
                    ScreenProcessCenterRadiusViewControl.Stop();
                }

                return;
            }

            EditorGUI.indentLevel++;
            ScreenProcessEffect effect = GetEffect(element);
            DrawCoreFields(
                rect,
                ref y,
                element,
                includeColorBlend: effect != ScreenProcessEffect.DepthOfField,
                includeTexture: effect == ScreenProcessEffect.CustomMaterial,
                includePassIndex: effect == ScreenProcessEffect.CustomMaterial,
                includeMaterialOverride: effect == ScreenProcessEffect.CustomMaterial);
            DrawRuleMaskProperties(rect, ref y, element);

            switch (effect)
            {
                case ScreenProcessEffect.EdgeLight:
                    DrawEdgeLightProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.Outline:
                    DrawOutlineProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.DropShadow:
                    DrawDropShadowProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.DepthOfField:
                    DrawDepthOfFieldProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.PostLighting:
                    DrawPostLightingProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.SkyTyndall:
                    DrawSkyTyndallProperties(rect, ref y, element);
                    break;
                case ScreenProcessEffect.DepthFog:
                    DrawDepthFogProperties(rect, ref y, element);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        private static int GetElementLineCount(SerializedProperty element)
        {
            switch (GetEffect(element))
            {
                case ScreenProcessEffect.EdgeLight:
                    return 16 + GetRuleLineCount(element);
                case ScreenProcessEffect.Outline:
                    return 11 + GetRuleLineCount(element);
                case ScreenProcessEffect.DepthOfField:
                    return GetDepthOfFieldLineCount(element) + GetRuleLineCount(element);
                case ScreenProcessEffect.PostLighting:
                    return GetPostLightingLineCount(element) + GetRuleLineCount(element);
                case ScreenProcessEffect.SkyTyndall:
                    return GetSkyTyndallLineCount(element) + GetRuleLineCount(element);
                case ScreenProcessEffect.DepthFog:
                    // foldout + colour + blend mode + rule mask header, then the fog rows
                    return 4 + GetDepthFogLineCount(element) + GetRuleLineCount(element);
                case ScreenProcessEffect.CustomMaterial:
                    return 7 + GetRuleLineCount(element);
                case ScreenProcessEffect.DropShadow:
                default:
                    return 9 + GetRuleLineCount(element);
            }
        }

        private static int GetRuleLineCount(SerializedProperty element)
        {
            return ScreenProcessRuleMaskEditorUtility.GetLineCount(element);
        }

        private float DrawFoldoutLine(Rect rect, float y, SerializedProperty element, SerializedProperty enabled)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            float checkboxWidth = 18.0f;
            float presetWidth = LayerPresetButtonSize;
            float removeWidth = 18.0f;
            float intensityWidth = Mathf.Clamp(rect.width * 0.34f, 140.0f, 220.0f);
            float foldoutWidth = Mathf.Max(0.0f, rect.width - checkboxWidth - presetWidth - intensityWidth - removeWidth - 12.0f);

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

            Rect removeRect = new Rect(lineRect.xMax - removeWidth, lineRect.y + 1.0f, removeWidth, lineRect.height - 2.0f);
            if (EffectBrowserView.DrawChromeLessButton(removeRect, new GUIContent("×", "移除这一层")))
            {
                RemoveLayerAt(GetLayerArrayIndex(element));
                return y + LineHeight + LineSpacing;
            }

            Rect presetRect = new Rect(lineRect.xMax - removeWidth - intensityWidth - presetWidth - 6.0f, lineRect.y, presetWidth, lineRect.height);
            DrawLayerPresetButton(presetRect, element);

            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            if (intensity != null && intensity.propertyType == SerializedPropertyType.Float)
            {
                Rect intensityRect = new Rect(lineRect.xMax - removeWidth - intensityWidth - 2.0f, lineRect.y, intensityWidth, lineRect.height);
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

        private static void DrawRuleMaskProperties(Rect rect, ref float y, SerializedProperty element)
        {
            ScreenProcessRuleMaskEditorUtility.Draw(rect, ref y, element, LineHeight, LineSpacing);
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

        private void ToggleEffect(ScreenProcessEffect effect)
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

        private bool HasLayer(ScreenProcessEffect effect)
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

        private void AddLayer(ScreenProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray || HasLayer(effect))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add ScreenProcess Effect");
            int index = GetLayerInsertIndex(effect);
            layerValues.InsertArrayElementAtIndex(index);
            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element, effect);
            element.isExpanded = true;
        }

        private int GetLayerInsertIndex(ScreenProcessEffect effect)
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

        private static int GetPreferredLayerOrder(ScreenProcessEffect effect)
        {
            switch (effect)
            {
                case ScreenProcessEffect.EdgeLight:
                    return 10;
                case ScreenProcessEffect.Outline:
                    return 20;
                case ScreenProcessEffect.PostLighting:
                    return 30;
                case ScreenProcessEffect.SkyTyndall:
                    return 35;
                case ScreenProcessEffect.DropShadow:
                    return 40;
                case ScreenProcessEffect.DepthFog:
                    return 45;
                case ScreenProcessEffect.DepthOfField:
                    return 50;
                case ScreenProcessEffect.CustomMaterial:
                default:
                    return 100;
            }
        }

        private void RemoveLayer(ScreenProcessEffect effect)
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
                    Undo.RecordObject(serializedObject.targetObject, "Remove ScreenProcess Effect");
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

        private static ScreenProcessEffect GetEffect(SerializedProperty element)
        {
            SerializedProperty effect = element.FindPropertyRelative("effect");
            if (effect == null || EffectValues.Length == 0)
            {
                return ScreenProcessEffect.CustomMaterial;
            }

            // enumValueIndex is an index into the enum's name list, so the bound must come from the
            // enum itself - not a literal. The old `Mathf.Clamp(value, 0, 6)` pinned the maximum at
            // the then-last effect (SkyTyndall), so anything added after it read back as SkyTyndall:
            // DepthFog's icon button never recognised its own layer, so every click added another copy
            // (and the layer was labelled/drawn as 天光丁达尔). Same shape as ImageProcessStackVolumeEditor.
            int index = effect.enumValueIndex;
            if (index < 0 || index >= EffectValues.Length)
            {
                return ScreenProcessEffect.CustomMaterial;
            }

            return EffectValues[index];
        }

        private static string GetEffectDisplayName(ScreenProcessEffect effect)
        {
            switch (effect)
            {
                case ScreenProcessEffect.PostLighting:
                    return "后期打光";
                case ScreenProcessEffect.SkyTyndall:
                    return "天光丁达尔";
                case ScreenProcessEffect.EdgeLight:
                    return "边缘光";
                case ScreenProcessEffect.Outline:
                    return "轮廓";
                case ScreenProcessEffect.DropShadow:
                    return "投影";
                case ScreenProcessEffect.DepthOfField:
                    return "景深";
                case ScreenProcessEffect.DepthFog:
                    return "深度雾";
                case ScreenProcessEffect.CustomMaterial:
                default:
                    return "自定义";
            }
        }

        private static void ResetLayerDefaults(SerializedProperty element, ScreenProcessEffect effect)
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
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Add);
            SetVector4(element, "parameters0", Vector4.zero);
            SetVector4(element, "parameters1", Vector4.zero);
            SetVector4(element, "parameters2", Vector4.zero);
            SetVector4(element, "parameters3", Vector4.zero);
            SetVector4(element, "parameters4", Vector4.zero);
            SetVector4(element, "parameters5", Vector4.zero);
            SetObjectReference(element, "depthOfFieldFocusTarget", null);
            SetString(element, "depthOfFieldFocusTargetPath", string.Empty);
            SetFloat(element, "depthOfFieldFocusOffset", 0.0f);
            SetBool(element, "useRuleMask", false);
            SetEnum(element, "ruleSource", (int)ScreenProcessRuleSource.Mask);
            SetEnum(element, "ruleMaskMode", (int)ScreenProcessRuleMaskMode.Direct);
            SetFloat(element, "ruleThreshold", 0.5f);
            SetFloat(element, "ruleMatchValue", 0.0f);
            SetColor(element, "ruleMatchColor", Color.white);
            SetBool(element, "invertRuleMask", false);
            SetBool(element, "debugRuleMask", false);
            ScreenProcessRuleMaskEditorUtility.ResetRules(element);

            switch (effect)
            {
                case ScreenProcessEffect.EdgeLight:
                    SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Add);
                    SetVector4(element, "parameters0", new Vector4(0.45f, 2.0f, 0.35f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 0.65f, 0.45f, 1.0f));
                    break;
                case ScreenProcessEffect.Outline:
                    SetColor(element, "color", Color.black);
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(0.85f, 0.85f, 0.45f, 0.11f));
                    SetVector4(element, "parameters1", new Vector4(0.08f, 0.85f, 0.32f, 0.9f));
                    break;
                case ScreenProcessEffect.DropShadow:
                    SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.65f));
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Multiply);
                    SetVector4(element, "parameters0", new Vector4(0.35f, -45.0f, 0.85f, 6.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    SetBool(element, "useRuleMask", true);
                    break;
                case ScreenProcessEffect.DepthFog:
                    SetColor(element, "color", new Color(0.66f, 0.71f, 0.76f, 1.0f));
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
                    // Depth slot on (exponential, 5 m -> 400 m), height slot off: the usual "distance
                    // haze" starting point. Both slots and their switches are documented in
                    // Documentation~/PostProcessing/DepthFog.md.
                    SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 400.0f));
                    SetVector4(element, "parameters1", new Vector4(0.01f, 0.6f, 1.0f, 0.3f));
                    SetVector4(element, "parameters2", new Vector4(0.49f, 0.58f, 0.71f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 12.0f));
                    SetVector4(element, "parameters4", new Vector4(1.0f, 0.5f, 0.85f, 0.88f));
                    SetVector4(element, "parameters5", new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Skip, 0.4f, 0.5f));
                    break;
                case ScreenProcessEffect.DepthOfField:
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 10.0f, 50.0f, 5.6f));
                    SetVector4(element, "parameters1", new Vector4(10.0f, 30.0f, 18.0f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(5.0f, 1.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(3.0f, 1.35f, 1.0f, 1.0f));
                    break;
                case ScreenProcessEffect.PostLighting:
                    SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.55f, 0.18f, 0.38f));
                    SetVector4(element, "parameters1", new Vector4(90.0f, 1.15f, 0.06f, 0.55f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.58f, 0.62f, 0.28f));
                    SetVector4(element, "parameters3", new Vector4(1.0f, 0.84f, 0.62f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters5", new Vector4(0.35f, 0.28f, 0.0f, 0.45f));
                    break;
                case ScreenProcessEffect.SkyTyndall:
                    SetColor(element, "color", new Color(1.0f, 0.72f, 0.42f, 1.0f));
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Screen);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 8.0f, 1.52f, 1.15f));
                    SetVector4(element, "parameters1", new Vector4(0.65f, 1.0f, 0.70f, 2.0f));
                    SetVector4(element, "parameters2", new Vector4(0.72f, 0.45f, 1.15f, 0.75f));
                    SetVector4(element, "parameters3", new Vector4(0.68f, 0.0f, 1.0f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.0f, 1.0f, 90.0f, 0.0f));
                    SetVector4(element, "parameters5", new Vector4(0.06f, 1.2f, 0.0f, 0.99f));
                    break;
                case ScreenProcessEffect.CustomMaterial:
                    SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
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
