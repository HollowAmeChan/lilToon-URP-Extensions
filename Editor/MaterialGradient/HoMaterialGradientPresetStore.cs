using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MaterialGradient
{
    internal static class HoMaterialGradientPresetStore
    {
        internal const string FolderPath = "Assets/HoMaterialGradient/Editor/Presets";
        internal const string AssetPath = FolderPath + "/HoMaterialGradientPresetLibrary.asset";

        public static HoMaterialGradientPresetLibrary Load()
        {
            return AssetDatabase.LoadAssetAtPath<HoMaterialGradientPresetLibrary>(AssetPath);
        }

        public static HoMaterialGradientPresetLibrary LoadOrCreate()
        {
            HoMaterialGradientPresetLibrary library = Load();
            if (library != null)
            {
                return library;
            }

            EnsureFolder("Assets", "HoMaterialGradient");
            EnsureFolder("Assets/HoMaterialGradient", "Editor");
            EnsureFolder("Assets/HoMaterialGradient/Editor", "Presets");

            library = HoMaterialGradientPresetLibrary.CreateDefaultInstance();
            library.EnsureDefaults();
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
