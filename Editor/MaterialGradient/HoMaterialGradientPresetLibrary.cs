using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    public sealed class HoMaterialGradientPresetLibrary : ScriptableObject
    {
        [SerializeField] private List<Preset> presets = new();

        public IReadOnlyList<Preset> Presets => presets;

        public int Count => presets.Count;

        public string GetName(int index)
        {
            return IsValidIndex(index) ? presets[index].name : string.Empty;
        }

        public Gradient GetGradient(int index)
        {
            return IsValidIndex(index) ? presets[index].gradient : null;
        }

        public void SavePreset(string presetName, Gradient gradient)
        {
            string cleanName = string.IsNullOrWhiteSpace(presetName) ? "Gradient Preset" : presetName.Trim();
            int index = presets.FindIndex(preset => string.Equals(preset.name, cleanName, StringComparison.OrdinalIgnoreCase));
            Gradient copy = HoMaterialGradientTextureBaker.CloneGradient(gradient);

            if (index >= 0)
            {
                presets[index].name = cleanName;
                presets[index].gradient = copy;
                return;
            }

            presets.Add(new Preset
            {
                name = cleanName,
                gradient = copy
            });
        }

        public void DeletePreset(int index)
        {
            if (IsValidIndex(index))
            {
                presets.RemoveAt(index);
            }
        }

        public string CreateUniqueName(string baseName)
        {
            string cleanName = string.IsNullOrWhiteSpace(baseName) ? "Gradient Preset" : baseName.Trim();
            if (!ContainsName(cleanName))
            {
                return cleanName;
            }

            int suffix = 1;
            string candidate;
            do
            {
                candidate = cleanName + " " + suffix;
                suffix++;
            }
            while (ContainsName(candidate));

            return candidate;
        }

        public void EnsureDefaults()
        {
            if (presets.Count > 0)
            {
                return;
            }

            SavePreset("Black to White", HoMaterialGradientTextureBaker.CreateDefaultGradient());
            SavePreset("Cool Ramp", CreateGradient(
                new Color(0.05f, 0.09f, 0.65f),
                new Color(0.0f, 0.75f, 0.9f),
                new Color(0.95f, 1.0f, 1.0f)));
            SavePreset("Warm Ramp", CreateGradient(
                new Color(0.18f, 0.02f, 0.0f),
                new Color(1.0f, 0.42f, 0.08f),
                new Color(1.0f, 0.92f, 0.42f)));
        }

        public static HoMaterialGradientPresetLibrary CreateDefaultInstance()
        {
            HoMaterialGradientPresetLibrary library = CreateInstance<HoMaterialGradientPresetLibrary>();
            library.EnsureDefaults();
            return library;
        }

        private bool ContainsName(string presetName)
        {
            return presets.Exists(preset => string.Equals(preset.name, presetName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsValidIndex(int index)
        {
            return index >= 0 && index < presets.Count;
        }

        private static Gradient CreateGradient(Color left, Color middle, Color right)
        {
            return new Gradient
            {
                mode = GradientMode.Blend,
                colorKeys = new[]
                {
                    new GradientColorKey(left, 0.0f),
                    new GradientColorKey(middle, 0.5f),
                    new GradientColorKey(right, 1.0f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                }
            };
        }

        [Serializable]
        public sealed class Preset
        {
            public string name;
            public Gradient gradient;
        }
    }
}
