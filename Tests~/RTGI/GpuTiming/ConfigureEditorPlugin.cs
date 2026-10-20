// Copied only to the isolated project's Assets/Editor before invoking Configure.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class HoGIGpuPluginConfiguration
{
    public static void Configure()
    {
        try
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
            var importer=(PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/x86_64/HoGIGpuProbe.dll");if(importer==null)throw new Exception("Test timestamp DLL importer missing");
            importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(true);importer.SetEditorData("OS","Windows");importer.SetEditorData("CPU","x86_64");foreach(var target in new[]{BuildTarget.StandaloneWindows,BuildTarget.StandaloneWindows64,BuildTarget.StandaloneLinux64,BuildTarget.StandaloneOSX})importer.SetCompatibleWithPlatform(target,false);importer.SaveAndReimport();
            if(importer.GetCompatibleWithAnyPlatform()||!importer.GetCompatibleWithEditor()||new[]{BuildTarget.StandaloneWindows,BuildTarget.StandaloneWindows64,BuildTarget.StandaloneLinux64,BuildTarget.StandaloneOSX}.Any(importer.GetCompatibleWithPlatform))throw new Exception("Timestamp bridge is not strictly Editor-only");
            Debug.Log("[HoGpuPluginConfiguration] PASS Editor-only; excluded from standalone Player");EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
