using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.OIT;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.Shadows;
using lilToon.URP.Extensions.SurfaceBuffer;
using Microsoft.Win32.SafeHandles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Installs a Ho Shadows pilot into a physically copied Windows Unity project.</summary>
public static class HoShadowsPilotCutover
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string CandidateRendererPath = "Assets/Settings/PC_HoShadowsPilotRenderer.asset";
    const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
    const string VolumePath = "Assets/Settings/DefaultVolumeProfile.asset";
    const string Production = "D:/Unity_Project/BREAK_URP";
    const string PilotRoot = "D:/Temp";

    [Serializable]
    sealed class FeatureState
    {
        public int index;
        public long fileId;
        public string type;
        public string name;
        public bool active;
    }

    [Serializable]
    sealed class Report
    {
        public string project;
        public string renderer;
        public string rendererGuid;
        public string rendererBefore;
        public string rendererAfter;
        public string volume;
        public string volumeSha256;
        public string pipelineBefore;
        public string pipelineAfter;
        public int defaultRendererIndex = -1;
        public int candidateRendererIndex = -1;
        public bool managedCharacter;
        public bool transparentEye;
        public bool weightedOit;
        public bool captureShadowInputs;
        public bool rayTraced;
        public int rayTracedResources;
        public FeatureState[] before;
        public FeatureState[] after;
    }

    public static void Run() => Run(false);

    public static void RunWithSeparateRenderer() => Run(true);

    static void Run(bool separateRenderer)
    {
        Require(Application.isBatchMode && Application.platform == RuntimePlatform.WindowsEditor,
            "Run this helper in a Windows batch Editor.");
        string project = Canonical(Path.Combine(Application.dataPath, ".."));
        string expected = DirectoryFromEnvironment("HO_SHADOWS_P5_PILOT_PROJECT");
        string output = DirectoryFromEnvironment("HO_SHADOWS_P5_PILOT_OUTPUT");
        Require(Same(project, expected), "The current Unity project does not match the explicitly named pilot.");
        Require(!Inside(project, Production) && !Inside(output, Production) && UnderTemp(project) && UnderTemp(output)
            && !Inside(output, project) && !Inside(project, output),
            "Use a physical project and a separate output directory under D:/Temp.");

        RequirePhysicalPath(project);
        RequirePhysicalPath(output);
        Require(!Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty pilot output directory.");
        string legacyRendererFile = Path.Combine(project, RendererPath);
        string volumeFile = Path.Combine(project, VolumePath);
        foreach (string file in new[] { legacyRendererFile, legacyRendererFile + ".meta", volumeFile, volumeFile + ".meta" })
            RequirePhysicalFile(file);

        string legacyRendererHash = Hash(legacyRendererFile);
        string chosenRendererPath = separateRenderer ? CandidateRendererPath : RendererPath;
        string rendererFile = Path.Combine(project, chosenRendererPath);
        string pipelineFile = Path.Combine(project, PipelinePath);
        var legacyRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        Require(legacyRenderer != null, "The legacy renderer is missing.");
        UniversalRenderPipelineAsset pipeline = null;
        if (separateRenderer)
        {
            RequirePhysicalFile(pipelineFile);
            RequirePhysicalFile(pipelineFile + ".meta");
            Require(!File.Exists(rendererFile) && !File.Exists(rendererFile + ".meta"), "Use a fresh candidate renderer path.");
            pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            Require(pipeline != null, "The pilot pipeline asset is missing.");
            var settings = new SerializedObject(pipeline);
            var renderers = settings.FindProperty("m_RendererDataList");
            var index = settings.FindProperty("m_DefaultRendererIndex").intValue;
            Require(index >= 0 && index < renderers.arraySize && renderers.GetArrayElementAtIndex(index).objectReferenceValue == legacyRenderer,
                "The default pipeline renderer must still point at the legacy asset.");
            Backup(pipelineFile, output);
            Backup(pipelineFile + ".meta", output);
            Require(AssetDatabase.CopyAsset(RendererPath, chosenRendererPath), "Could not copy the legacy renderer.");
        }
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(chosenRendererPath);
        Require(renderer != null, "The pilot renderer is missing.");
        var features = new List<ScriptableRendererFeature>(renderer.rendererFeatures);
        var cast = Find<HoShadowCastRendererFeature>(features);
        var oldCharacter = Find<HoCharacterShadowRendererFeature>(features);
        var oldRt = Find<HoRTShadowRendererFeature>(features);
        var capture = Find<HoCharacterSpecializationRendererFeature>(features);
        var oit = Find<WeightedOITRendererFeature>(features);
        var unified = Find<HoShadowsRendererFeature>(features, required: false);
        var report = new Report
        {
            project = project,
            renderer = chosenRendererPath,
            rendererGuid = AssetDatabase.AssetPathToGUID(chosenRendererPath),
            rendererBefore = Hash(rendererFile),
            volume = VolumePath,
            volumeSha256 = Hash(volumeFile),
            before = Snapshot(features, chosenRendererPath),
            pipelineBefore = separateRenderer ? Hash(pipelineFile) : null,
        };

        if (unified != null)
            features.Remove(unified);
        int insertion = Math.Min(features.IndexOf(capture), features.IndexOf(oit));
        foreach (var input in new ScriptableRendererFeature[] {
            Find<HoRTRendererFeature>(features), Find<HoGeometryBufferRendererFeature>(features),
            Find<HoObjectBufferRendererFeature>(features), Find<HoSurfaceBufferRendererFeature>(features) })
            Require(features.IndexOf(input) < insertion, input.name + " must precede the pilot's capture/OIT insertion point.");

        Backup(legacyRendererFile, output);
        Backup(legacyRendererFile + ".meta", output);
        Backup(volumeFile, output);
        Backup(volumeFile + ".meta", output);
        File.WriteAllText(Path.Combine(output, "pilot-before.json"), JsonUtility.ToJson(report, true));

        if (unified == null)
        {
            unified = ScriptableObject.CreateInstance<HoShadowsRendererFeature>();
            unified.name = "Ho Shadows (isolated pilot)";
            AssetDatabase.AddObjectToAsset(unified, renderer);
        }
        unified.SetActive(true);
        unified.ManagedCharacterShadowsEnabled = true;
        unified.TransparentEyeShadowsEnabled = true;
        unified.WeightedOitShadowsEnabled = true;
        unified.RayTracedShadowsEnabled = false;
        var previous = oldCharacter.Settings;
        var current = unified.CharacterSettings;
        current.resolution = previous.resolution;
        current.maxCharacters = previous.maxCharacters;
        current.maxAtlasSize = previous.maxAtlasSize;
        current.depthBias = previous.depthBias;
        current.normalBias = previous.normalBias;
        report.rayTracedResources = CopyRayPrograms(oldRt, unified);

        capture.CaptureShadowInputsEnabled = true;
        foreach (var retired in new ScriptableRendererFeature[] { cast, oldCharacter, oldRt })
            retired.SetActive(false);

        features.Insert(insertion, unified);
        renderer.rendererFeatures.Clear();
        renderer.rendererFeatures.AddRange(features);
        report.after = Snapshot(features, chosenRendererPath);
        var serialized = new SerializedObject(renderer);
        var map = serialized.FindProperty("m_RendererFeatureMap");
        Require(map != null, "The renderer has no serialized feature map.");
        map.arraySize = report.after.Length;
        for (int i = 0; i < report.after.Length; i++)
            map.GetArrayElementAtIndex(i).longValue = report.after[i].fileId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach (var changed in new ScriptableRendererFeature[] { unified, cast, oldCharacter, oldRt, capture })
            EditorUtility.SetDirty(changed);
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);

        Require(Hash(volumeFile) == report.volumeSha256, "The pilot unexpectedly changed the Volume profile.");
        if (separateRenderer)
        {
            Require(Hash(legacyRendererFile) == legacyRendererHash, "The separate pilot modified the legacy renderer.");
            var settings = new SerializedObject(pipeline);
            var renderers = settings.FindProperty("m_RendererDataList");
            report.candidateRendererIndex = renderers.arraySize;
            renderers.arraySize++;
            renderers.GetArrayElementAtIndex(report.candidateRendererIndex).objectReferenceValue = renderer;
            report.defaultRendererIndex = settings.FindProperty("m_DefaultRendererIndex").intValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(pipeline);
            report.pipelineAfter = Hash(pipelineFile);
        }
        report.rendererAfter = Hash(rendererFile);
        report.managedCharacter = unified.ManagedCharacterShadowsEnabled;
        report.transparentEye = unified.TransparentEyeShadowsEnabled;
        report.weightedOit = unified.WeightedOitShadowsEnabled;
        report.rayTraced = unified.RayTracedShadowsEnabled;
        report.captureShadowInputs = capture.CaptureShadowInputsEnabled;
        File.WriteAllText(Path.Combine(output, "pilot-applied.json"), JsonUtility.ToJson(report, true));
        Debug.Log("HO_SHADOWS_P5_PILOT_OK " + Path.Combine(output, "pilot-applied.json"));
    }

    static int CopyRayPrograms(HoRTShadowRendererFeature sourceFeature, HoShadowsRendererFeature destinationFeature)
    {
        var source = new SerializedObject(sourceFeature);
        var destination = new SerializedObject(destinationFeature);
        int resourceCount = 0;
        foreach (var (from, to) in new[]
        {
            ("shader", "rayTracedShadowProgram"),
            ("visibilityMaterialShader", "rayVisibilityMaterial"),
            ("sigmaShader", "rayShadowReconstruction"),
            ("debugShader", "rayShadowDebugShader")
        })
        {
            var oldReference = source.FindProperty(from).objectReferenceValue;
            var newProperty = destination.FindProperty(to);
            if (oldReference != null) newProperty.objectReferenceValue = oldReference;
            if (newProperty.objectReferenceValue != null) resourceCount++;
        }

        var oldPasses = source.FindProperty("sigmaPipelines");
        var newPasses = destination.FindProperty("rayShadowReconstructionPasses");
        if (oldPasses.arraySize != 0)
        {
            newPasses.arraySize = oldPasses.arraySize;
            for (int index = 0; index < oldPasses.arraySize; index++)
                newPasses.GetArrayElementAtIndex(index).objectReferenceValue = oldPasses.GetArrayElementAtIndex(index).objectReferenceValue;
        }
        for (int index = 0; index < newPasses.arraySize; index++)
            if (newPasses.GetArrayElementAtIndex(index).objectReferenceValue != null) resourceCount++;
        destination.ApplyModifiedPropertiesWithoutUndo();
        return resourceCount;
    }

    static T Find<T>(List<ScriptableRendererFeature> features, bool required = true) where T : ScriptableRendererFeature
    {
        T[] matches = features.OfType<T>().ToArray();
        Require(matches.Length <= 1 && (!required || matches.Length == 1), "Unexpected renderer feature count for " + typeof(T).Name);
        return matches.SingleOrDefault();
    }

    static FeatureState[] Snapshot(List<ScriptableRendererFeature> features, string rendererPath)
    {
        return features.Select((feature, index) =>
        {
            Require(feature != null, "A missing renderer Feature cannot be migrated safely.");
            bool hasId = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long fileId);
            Require(AssetDatabase.GetAssetPath(feature) == rendererPath && hasId && fileId != 0,
                "A feature does not have a local subasset ID on the isolated renderer.");
            return new FeatureState { index = index, fileId = fileId, type = feature.GetType().FullName, name = feature.name, active = feature.isActive };
        }).ToArray();
    }

    static string DirectoryFromEnvironment(string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        Require(!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value), "Set the absolute directory in " + variable);
        return Canonical(value);
    }

    static string Canonical(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    static bool Same(string a, string b) => string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);
    static bool Inside(string path, string root) => Same(path, root) ||
        Canonical(path).StartsWith(Canonical(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static bool UnderTemp(string path) => Inside(path, PilotRoot) && !Same(path, PilotRoot);

    static void RequirePhysicalPath(string path)
    {
        Require(Directory.Exists(path), "Directory does not exist: " + path);
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Junction or symbolic link in path: " + directory.FullName);
    }

    static void RequirePhysicalFile(string path)
    {
        RequirePhysicalPath(Path.GetDirectoryName(path));
        Require(File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
            "Asset must be a physical file: " + path);
        using var file = File.OpenRead(path);
        Require(GetFileInformationByHandle(file.SafeFileHandle, out var information) && information.LinkCount == 1,
            "Asset must have exactly one filesystem link: " + path);
    }

    static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    static void Backup(string path, string output) => File.Copy(path, Path.Combine(output, Path.GetFileName(path)));

    static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
