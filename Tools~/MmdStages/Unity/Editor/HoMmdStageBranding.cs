using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Targeted logo edits; never rebuild a scene or apply a lighting recipe.</summary>
public static class HoMmdStageBranding
{
    public const float DefaultY = .1f;
    public const float DefaultSize = .3f;

    public static string SetLogos(string root = "Assets/00动画/MMD场景", float y = DefaultY, float size = DefaultSize)
    {
        if (!AssetDatabase.IsValidFolder(root)) throw new ArgumentException("Asset folder not found: " + root);
        var changed = new List<string>();
        int logos = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null || !profile.TryGet<ImageProcessStackVolume>(out var image)) continue;
            bool dirty = false;
            foreach (var layer in image.layers.value)
            {
                if (layer.effect != ImageProcessEffect.LogoOverlay ||
                    !((layer.name ?? "").IndexOf("Hollow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      (layer.logoTexture0 != null && layer.logoTexture0.name.IndexOf("Hollow", StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                logos++;
                if (Mathf.Approximately(layer.parameters0.y, y) && Mathf.Approximately(layer.parameters0.z, size)) continue;
                if (!dirty) Undo.RecordObject(image, "Resize Hollow logo");
                var p = layer.parameters0; p.y = y; p.z = size; layer.parameters0 = p;
                dirty = true;
            }
            if (!dirty) continue;
            EditorUtility.SetDirty(image);
            AssetDatabase.SaveAssetIfDirty(image); // Save this profile only; leave other artist edits alone.
            changed.Add(path);
        }
        string result = JsonUtility.ToJson(new Result { logos = logos, changedProfiles = changed.ToArray(), y = y, size = size }, true);
        Directory.CreateDirectory(HoMmdStageWork.Work);
        File.WriteAllText(Path.Combine(HoMmdStageWork.Work, "logo-update.json"), result);
        return result;
    }

    [Serializable] sealed class Result { public int logos; public string[] changedProfiles; public float y, size; }
    [MenuItem("HoUnityTools/MMD/Set Hollow Logo Y 0.1 Size 0.3")]
    public static void ApplyDefaults() => Debug.Log(SetLogos());
}
