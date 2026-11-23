using System;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HoVolumeUICompatibility
{
    public static void Run()
    {
        try
        {
            HoVolumeUIValidation.Validate();
            HoVolumeUILayerValidation.Validate();
            var report = JsonUtility.FromJson<HoVolumeUIBaseline.Report>(File.ReadAllText(HoVolumeUIBaseline.Output + "/baseline.json"));
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(HoVolumeUIBaseline.ProfilePath);
            if (profile == null || profile.components.Count != 18) throw new Exception("Existing profile lost components");
            foreach (var entry in report.entries)
            {
                Type type = typeof(HoVolumeNames).Assembly.GetType(entry.type, true);
                var fresh = (VolumeComponent)ScriptableObject.CreateInstance(type);
                try { if (JsonUtility.ToJson(fresh) != entry.defaults) throw new Exception("Default values changed: " + entry.type); }
                finally { Object.DestroyImmediate(fresh); }
                var existing = profile.components.Single(c => c.GetType() == type);
                if (JsonUtility.ToJson(existing) != entry.seeded) throw new Exception("Existing profile values/override flags changed: " + entry.type);
            }
            Debug.Log("HO_VOLUME_UI_COMPATIBILITY_PASSED defaults=18 existingProfile=18 mixedOverrides=preserved");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
