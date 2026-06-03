using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal sealed class HoMaterialGradientPropertyGui
    {
        private static readonly GradientMode[] InterpolationModes = Enum.GetValues(typeof(GradientMode)).Cast<GradientMode>().ToArray();
        private static readonly GUIContent[] InterpolationLabels = InterpolationModes.Select(mode => new GUIContent(GetInterpolationLabel(mode))).ToArray();
        private static readonly Dictionary<string, Texture2D> LastKnownTextures = new();
        private static readonly Dictionary<string, Gradient> GradientCache = new();
        private static readonly Dictionary<string, int> PresetIndexCache = new();
        private static readonly Dictionary<string, string> PresetNameCache = new();
        private static HoMaterialGradientPresetLibrary defaultPresetLibrary;

        public static float PropertyHeight => EditorGUIUtility.singleLineHeight * 3.0f + EditorGUIUtility.standardVerticalSpacing * 2.0f;

        public void OnGUI(Rect rect, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            if (property.targets.Length != 1)
            {
                EditorGUI.LabelField(rect, label, new GUIContent("Gradient editing supports one material at a time."));
                return;
            }

            Material material = (Material)property.targets[0];
            string materialKey = material.GetInstanceID() + "_" + property.name;
            Texture2D currentTexture = GetCurrentTexture(material, property.name);
            bool textureChanged = UpdateTextureChangeState(materialKey, currentTexture);
            Gradient gradient = ResolveGradient(materialKey, currentTexture, material, property.name);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float verticalSpacing = EditorGUIUtility.standardVerticalSpacing;
            Rect gradientLine = new Rect(rect.x, rect.y, rect.width, lineHeight);
            Rect presetLine = new Rect(rect.x, gradientLine.yMax + verticalSpacing, rect.width, lineHeight);
            Rect actionLine = new Rect(rect.x, presetLine.yMax + verticalSpacing, rect.width, lineHeight);
            Rect valueRect = EditorGUI.PrefixLabel(gradientLine, label);

            EditorGUI.BeginChangeCheck();
            gradient = EditorGUI.GradientField(valueRect, gradient);
            bool gradientChanged = EditorGUI.EndChangeCheck();

            bool presetChanged = DrawPresetControls(
                presetLine,
                actionLine,
                valueRect.x,
                materialKey,
                property.displayName,
                ref gradient,
                material,
                property,
                editor,
                out bool cleaned);

            if (cleaned)
            {
                return;
            }

            if (textureChanged)
            {
                editor.Repaint();
            }

            if (!gradientChanged && !presetChanged)
            {
                return;
            }

            GradientCache[materialKey] = gradient;
            bool commit = Event.current.type == EventType.MouseUp
                || Event.current.type == EventType.KeyUp
                || Event.current.type == EventType.ExecuteCommand;

            HoMaterialGradientDebounce.Request(
                materialKey,
                0.1,
                () => HoMaterialGradientTextureBaker.ApplyGradient(material, property.name, gradient),
                commit);
        }

        private static bool DrawPresetControls(
            Rect presetLine,
            Rect actionLine,
            float valueX,
            string materialKey,
            string defaultPresetName,
            ref Gradient gradient,
            Material material,
            MaterialProperty property,
            MaterialEditor editor,
            out bool cleaned)
        {
            cleaned = false;
            HoMaterialGradientPresetLibrary library = HoMaterialGradientPresetStore.Load() ?? GetDefaultPresetLibrary();
            Rect presetValueRect = OffsetToValueRect(presetLine, valueX);
            Rect actionValueRect = OffsetToValueRect(actionLine, valueX);
            const float spacing = 4.0f;
            const float interpolationLabelWidth = 82.0f;
            const float interpolationWidth = 132.0f;
            const float saveWidth = 48.0f;
            const float deleteWidth = 56.0f;
            const float cleanWidth = 54.0f;
            const float nameLabelWidth = 82.0f;

            Rect interpolationLabelRect = new Rect(presetValueRect.x, presetValueRect.y, interpolationLabelWidth, presetValueRect.height);
            Rect interpolationRect = new Rect(interpolationLabelRect.xMax + spacing, presetValueRect.y, interpolationWidth, presetValueRect.height);
            Rect popupRect = new Rect(interpolationRect.xMax + spacing, presetValueRect.y, Mathf.Max(32.0f, presetValueRect.xMax - interpolationRect.xMax - spacing), presetValueRect.height);

            bool changed = false;
            EditorGUI.LabelField(interpolationLabelRect, "Interpolation");
            int currentModeIndex = Math.Max(0, Array.IndexOf(InterpolationModes, gradient.mode));
            int newModeIndex = EditorGUI.Popup(interpolationRect, currentModeIndex, InterpolationLabels);
            if (newModeIndex != currentModeIndex)
            {
                gradient.mode = InterpolationModes[newModeIndex];
                changed = true;
            }

            string[] presetNames = BuildPresetOptions(library);
            int selectedPreset = Mathf.Clamp(GetPresetIndex(materialKey), 0, presetNames.Length - 1);
            int newPreset = EditorGUI.Popup(popupRect, selectedPreset, presetNames);
            if (newPreset != selectedPreset)
            {
                PresetIndexCache[materialKey] = newPreset;
                if (newPreset > 0)
                {
                    Gradient presetGradient = library.GetGradient(newPreset - 1);
                    gradient = HoMaterialGradientTextureBaker.CloneGradient(presetGradient);
                    PresetNameCache[materialKey] = library.GetName(newPreset - 1);
                    changed = true;
                }
            }

            Rect nameLabelRect = new Rect(actionValueRect.x, actionValueRect.y, nameLabelWidth, actionValueRect.height);
            Rect cleanRect = new Rect(actionValueRect.xMax - cleanWidth, actionValueRect.y, cleanWidth, actionValueRect.height);
            Rect deleteRect = new Rect(cleanRect.x - spacing - deleteWidth, actionValueRect.y, deleteWidth, actionValueRect.height);
            Rect saveRect = new Rect(deleteRect.x - spacing - saveWidth, actionValueRect.y, saveWidth, actionValueRect.height);
            Rect nameRect = new Rect(nameLabelRect.xMax + spacing, actionValueRect.y, Mathf.Max(32.0f, saveRect.x - nameLabelRect.xMax - spacing * 2.0f), actionValueRect.height);

            EditorGUI.LabelField(nameLabelRect, "Preset Name");
            string presetName = GetPresetName(materialKey, defaultPresetName);
            PresetNameCache[materialKey] = EditorGUI.TextField(nameRect, presetName);

            if (GUI.Button(saveRect, "Save"))
            {
                library = HoMaterialGradientPresetStore.LoadOrCreate();
                Undo.RecordObject(library, "Save Gradient Preset");
                string cleanName = string.IsNullOrWhiteSpace(PresetNameCache[materialKey])
                    ? library.CreateUniqueName(defaultPresetName)
                    : PresetNameCache[materialKey].Trim();
                library.SavePreset(cleanName, gradient);
                PresetNameCache[materialKey] = cleanName;
                PresetIndexCache[materialKey] = FindPresetOptionIndex(library, cleanName);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }

            using (new EditorGUI.DisabledScope(selectedPreset <= 0))
            {
                if (GUI.Button(deleteRect, "Delete"))
                {
                    Undo.RecordObject(library, "Delete Gradient Preset");
                    library.DeletePreset(selectedPreset - 1);
                    PresetIndexCache[materialKey] = 0;
                    EditorUtility.SetDirty(library);
                    AssetDatabase.SaveAssets();
                }
            }

            if (GUI.Button(cleanRect, "Clean"))
            {
                HoMaterialGradientTextureBaker.CleanUnused(material, MaterialEditor.GetMaterialProperties(property.targets));
                GradientCache.Remove(materialKey);
                HoMaterialGradientDebounce.Cancel(materialKey);
                editor.Repaint();
                cleaned = true;
            }

            return changed;
        }

        private static Rect OffsetToValueRect(Rect source, float valueX)
        {
            return new Rect(valueX, source.y, Mathf.Max(0.0f, source.xMax - valueX), source.height);
        }

        private static int GetPresetIndex(string materialKey)
        {
            return PresetIndexCache.TryGetValue(materialKey, out int index) ? index : 0;
        }

        private static string GetPresetName(string materialKey, string defaultPresetName)
        {
            if (PresetNameCache.TryGetValue(materialKey, out string name))
            {
                return name;
            }

            name = string.IsNullOrWhiteSpace(defaultPresetName) ? "Gradient Preset" : defaultPresetName.Trim();
            PresetNameCache[materialKey] = name;
            return name;
        }

        private static string[] BuildPresetOptions(HoMaterialGradientPresetLibrary library)
        {
            string[] options = new string[library.Count + 1];
            options[0] = "Project Preset";
            for (int i = 0; i < library.Count; i++)
            {
                options[i + 1] = library.GetName(i);
            }

            return options;
        }

        private static int FindPresetOptionIndex(HoMaterialGradientPresetLibrary library, string presetName)
        {
            for (int i = 0; i < library.Count; i++)
            {
                if (string.Equals(library.GetName(i), presetName, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static HoMaterialGradientPresetLibrary GetDefaultPresetLibrary()
        {
            if (defaultPresetLibrary == null)
            {
                defaultPresetLibrary = HoMaterialGradientPresetLibrary.CreateDefaultInstance();
                defaultPresetLibrary.hideFlags = HideFlags.HideAndDontSave;
            }

            return defaultPresetLibrary;
        }

        private static string GetInterpolationLabel(GradientMode mode)
        {
            return mode.ToString() switch
            {
                "Blend" => "Blend",
                "Fixed" => "Fixed",
                "PerceptualBlend" => "Perceptual Blend",
                _ => ObjectNames.NicifyVariableName(mode.ToString())
            };
        }

        private static Texture2D GetCurrentTexture(Material material, string propertyName)
        {
#if UNITY_2021_1_OR_NEWER
            return material.HasTexture(propertyName) ? material.GetTexture(propertyName) as Texture2D : null;
#else
            return material.GetTexture(propertyName) as Texture2D;
#endif
        }

        private static bool UpdateTextureChangeState(string materialKey, Texture2D currentTexture)
        {
            if (LastKnownTextures.TryGetValue(materialKey, out Texture2D lastTexture) && lastTexture == currentTexture)
            {
                return false;
            }

            LastKnownTextures[materialKey] = currentTexture;
            GradientCache.Remove(materialKey);
            return true;
        }

        private static Gradient ResolveGradient(string materialKey, Texture2D currentTexture, Material material, string propertyName)
        {
            if (GradientCache.TryGetValue(materialKey, out Gradient cached))
            {
                return cached;
            }

            if (HoMaterialGradientTextureBaker.TryDecode(currentTexture, propertyName, out Gradient gradient))
            {
                GradientCache[materialKey] = gradient;
                return gradient;
            }

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(materialPath))
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(materialPath))
                {
                    if (asset is Texture2D texture && HoMaterialGradientTextureBaker.TryDecode(texture, propertyName, out gradient))
                    {
                        GradientCache[materialKey] = gradient;
                        return gradient;
                    }
                }
            }

            gradient = HoMaterialGradientTextureBaker.CreateDefaultGradient();
            GradientCache[materialKey] = gradient;
            return gradient;
        }
    }
}
