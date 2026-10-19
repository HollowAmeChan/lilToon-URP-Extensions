using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace lilToon.URP.Extensions.Editor.Atmosphere
{
    // Maintenance entry point: bake only in an isolated batch project, never on editor load.
    public static class HoAtmosphereDensityPresetBake
    {
        public static void Run()
        {
            try
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project to bake bundled density assets.");
                const string folder = "Assets/GeneratedHoAtmosphereDensity";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "GeneratedHoAtmosphereDensity");
                foreach (var preset in HoAtmosphereDensityPresets.All)
                {
                    string path = folder + "/" + preset.file + ".asset";
                    if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing baked asset: " + path);
                    byte[] bytes = File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "AtmospherePresetSource", preset.file + ".raw"));
                    if (bytes.Length != 64 * 64 * 64) throw new InvalidDataException("Invalid density data size: " + preset.file);
                    var texture = new Texture3D(64, 64, 64, GraphicsFormat.R8_UNorm, TextureCreationFlags.None)
                    {
                        name = preset.file, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 1
                    };
                    texture.SetPixelData(bytes, 0); texture.Apply(false, false);
                    AssetDatabase.CreateAsset(texture, path);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[HoAtmosphereDensityPresetBake] PASS: four native Texture3D assets baked");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
