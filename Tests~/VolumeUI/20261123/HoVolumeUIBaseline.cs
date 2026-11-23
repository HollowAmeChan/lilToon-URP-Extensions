using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class HoVolumeUIBaseline
{
    [Serializable] public class Entry { public string type, defaults, seeded; }
    [Serializable] public class Report { public List<Entry> entries = new List<Entry>(); }
    public const string Output = "D:/Unity_Fork/lilToon-URP-Extensions/Temp~/VolumeUI-20261123";
    public const string ProfilePath = "Assets/HoVolumeUICompatibility.asset";

    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            var report = new Report();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<VolumeComponent>())
            {
                if (type.IsAbstract || type.Assembly.GetName().Name != "jp.lilxyzw.liltoon.urp.extensions.Runtime") continue;
                var component = (VolumeComponent)ScriptableObject.CreateInstance(type);
                var entry = new Entry { type = type.FullName, defaults = JsonUtility.ToJson(component) };
                int index = 0;
                foreach (var parameter in component.parameters)
                {
                    parameter.overrideState = index++ % 2 == 0;
                    if (parameter is BoolParameter value) value.value = !value.value;
                }
                entry.seeded = JsonUtility.ToJson(component);
                report.entries.Add(entry);
                profile.components.Add(component);
                AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(component);
            }
            if (report.entries.Count != 18) throw new Exception("Expected 18 baseline components, got " + report.entries.Count);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            File.WriteAllText(Output + "/baseline.json", JsonUtility.ToJson(report, true));
            Debug.Log("HO_VOLUME_UI_BASELINE_PASSED components=" + report.entries.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
