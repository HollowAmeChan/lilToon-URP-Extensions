using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using HoShader.ClothURP.Editor;
using HoShader.EffectURP.Editor;
using HoShader.HairURP.Editor;
using HoShader.SkinStylizedURP.Editor;
using lilToon.URP.Extensions.Shadows;
using Microsoft.Win32.SafeHandles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Scratch Windows batch entry. Copy this source into the specified project's Assets/Editor only when authorized.
// Set HO_SHADOWS_APPLY_PROJECT and HO_SHADOWS_APPLY_PLAN to absolute paths before executing Run.
// JSON input requires scenePath, sceneSha256, cameraGlobalObjectId, candidateRendererIndex and a nonempty slots array.
// Each slot requires rendererGlobalObjectId, slotIndex, sourceMaterialGuid and sourceMaterialSha256.
// Optional scopeMode 1 means Main, 2 means Full, and omission preserves the source.
public static class HoShadowsApplyAcceptedScene
{
    const string ScratchPrefix = @"D:\Temp\HoShadows-P5-";
    const string Preview = "_HO_SHADOWS_SCREEN_PREVIEW";
    const string Scope = "_HoRPShadowMode";
    const string Generated = "Assets/HoShadowsAccepted";

    [DataContract]
    sealed class Plan
    {
        [DataMember(IsRequired = true)] public string scenePath { get; set; }
        [DataMember(IsRequired = true)] public string sceneSha256 { get; set; }
        [DataMember(IsRequired = true)] public string cameraGlobalObjectId { get; set; }
        [DataMember(IsRequired = true)] public int candidateRendererIndex { get; set; }
        [DataMember(IsRequired = true)] public Slot[] slots { get; set; }
    }

    [DataContract]
    sealed class Slot
    {
        [DataMember(IsRequired = true)] public string rendererGlobalObjectId { get; set; }
        [DataMember(IsRequired = true)] public int slotIndex { get; set; }
        [DataMember(IsRequired = true)] public string sourceMaterialGuid { get; set; }
        [DataMember(IsRequired = true)] public string sourceMaterialSha256 { get; set; }
        [DataMember] public int? scopeMode { get; set; }
    }

    sealed class Binding
    {
        public Slot slot;
        public Renderer renderer;
        public Material source;
        public string assetPath, sourceFile, metaHash, target;
    }

    [Serializable] sealed class CopyProof
    {
        public string sourceGuid, sourcePath, sourceSha256, sourceMetaSha256;
        public string copyPath, copyGuid, copySha256;
    }

    [Serializable] sealed class Receipt
    {
        public string planSha256, scenePath, sceneGuid, beforeSha256, afterSha256, backupPath, backupSha256;
        public string cameraGlobalObjectId, rendererAssetPath, sceneMetaSha256;
        public int beforeRenderer, afterRenderer, changedSlots;
        public CopyProof[] copies;
    }

    public static void Run()
    {
        Require(Application.isBatchMode && Application.platform == RuntimePlatform.WindowsEditor,
            "Use a Windows batch Editor.");
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Require(project.StartsWith(ScratchPrefix, StringComparison.OrdinalIgnoreCase)
                && Same(project, EnvironmentPath("HO_SHADOWS_APPLY_PROJECT")), "Explicitly select the physical HoShadows-P5 scratch project.");
        Physical(project);
        string input = EnvironmentPath("HO_SHADOWS_APPLY_PLAN");
        Require(input.StartsWith(ScratchPrefix, StringComparison.OrdinalIgnoreCase), "Keep the accepted plan beneath the scratch directory.");
        Physical(input);
        byte[] json = File.ReadAllBytes(input);
        string planHash = Hash(json);
        Plan plan;
        using (var bytes = new MemoryStream(json))
            plan = (Plan)new DataContractJsonSerializer(typeof(Plan)).ReadObject(bytes);
        Require(plan?.slots != null && plan.slots.Length != 0 && IsHex(plan.sceneSha256, 64), "Expected one saved scene and explicit slots.");

        string sceneFile = AssetFile(project, plan.scenePath, ".unity");
        string sceneGuid = AssetDatabase.AssetPathToGUID(plan.scenePath);
        Require(IsHex(sceneGuid, 32) && File.Exists(sceneFile), "The scene must be an imported scratch asset.");
        string sceneMetaHash = Hash(sceneFile + ".meta");
        string artifactDir = Path.Combine(project, "HoShadowsAccepted", sceneGuid, plan.sceneSha256.ToLowerInvariant());
        string receiptPath = Path.Combine(artifactDir, "report.json");
        string backup = Path.Combine(artifactDir, "original.unity");
        Physical(receiptPath);
        Receipt existing = File.Exists(receiptPath) ? JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath)) : null;
        if (File.Exists(receiptPath))
            Require(existing != null && Same(existing.planSha256, planHash) && Same(existing.scenePath, plan.scenePath)
                && Same(existing.sceneGuid, sceneGuid) && Same(existing.beforeSha256, plan.sceneSha256)
                && Same(existing.backupPath, backup) && Same(existing.backupSha256, Hash(backup))
                && Same(existing.sceneMetaSha256, sceneMetaHash) && Same(sceneMetaHash, Hash(backup + ".meta"))
                && existing.afterRenderer == plan.candidateRendererIndex && existing.changedSlots == plan.slots.Length,
                "An earlier application receipt does not match this plan or backup.");
        Require(Same(Hash(sceneFile), existing?.afterSha256 ?? plan.sceneSha256), "The saved scene hash does not match the plan or receipt.");

        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "The scratch Editor has an unsaved scene.");
        Scene scene = EditorSceneManager.OpenScene(plan.scenePath, OpenSceneMode.Single);
        Require(scene.isLoaded && !scene.isDirty, "The target scene must open cleanly.");
        var camera = Resolve<Camera>(plan.cameraGlobalObjectId, scene);
        camera.TryGetComponent(out UniversalAdditionalCameraData cameraData);
        Require(cameraData == null || cameraData.renderType == CameraRenderType.Base, "The selected camera must be a Base camera.");
        var pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        Require(pipeline != null, "The scratch scene must use a URP asset.");
        var pipelineState = new SerializedObject(pipeline);
        var renderers = pipelineState.FindProperty("m_RendererDataList");
        int index = plan.candidateRendererIndex;
        Require(renderers != null && index >= 0 && index < renderers.arraySize
            && index != pipelineState.FindProperty("m_DefaultRendererIndex").intValue,
            "The candidate must be a registered renderer separate from the pipeline default.");
        var rendererData = renderers.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
        var shadows = rendererData?.rendererFeatures.OfType<HoShadowsRendererFeature>().SingleOrDefault(feature => feature.isActive);
        Require(shadows != null, "The selected renderer needs its active Ho Shadows feature.");
        int beforeIndex = RendererIndex(cameraData);

        string destinationFolder = Generated + "/" + sceneGuid;
        var uniqueSlots = new HashSet<(Renderer, int)>();
        var bindings = new List<Binding>(plan.slots.Length);
        foreach (var slot in plan.slots)
        {
            Require(slot != null && IsHex(slot.sourceMaterialGuid, 32) && IsHex(slot.sourceMaterialSha256, 64)
                && (!slot.scopeMode.HasValue || slot.scopeMode == 1 || slot.scopeMode == 2), "Malformed explicit slot.");
            var renderer = Resolve<Renderer>(slot.rendererGlobalObjectId, scene);
            var materials = renderer.sharedMaterials;
            Require(slot.slotIndex >= 0 && slot.slotIndex < materials.Length && uniqueSlots.Add((renderer, slot.slotIndex)),
                "Duplicate or missing renderer slot " + slot.rendererGlobalObjectId);
            string sourcePath = AssetDatabase.GUIDToAssetPath(slot.sourceMaterialGuid);
            string sourceFile = AssetFile(project, sourcePath, ".mat");
            Require(Same(Hash(sourceFile), slot.sourceMaterialSha256), "The named source material changed: " + sourcePath);
            string sourceMeta = Hash(sourceFile + ".meta");
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            Require(source != null && source.shader != null && !EditorUtility.IsDirty(source), "The source material must be saved: " + sourcePath);
            string suffix = slot.scopeMode.HasValue ? "-scope" + slot.scopeMode.Value : "-keep";
            string target = destinationFolder + "/" + slot.sourceMaterialGuid.ToLowerInvariant() + suffix + ".mat";
            AssetFile(project, target, ".mat");
            if (existing == null)
                Require(materials[slot.slotIndex] == source, "The current slot does not reference the named source: " + sourcePath);
            else
                Require(materials[slot.slotIndex] == AssetDatabase.LoadAssetAtPath<Material>(target) && materials[slot.slotIndex] != null,
                    "An applied slot no longer references its recorded scene-local copy.");
            bindings.Add(new Binding { slot = slot, renderer = renderer, source = source, assetPath = sourcePath,
                sourceFile = sourceFile, metaHash = sourceMeta, target = target });
        }

        var uniqueCopies = bindings.GroupBy(binding => binding.target).Select(group => group.First()).ToArray();
        if (existing != null)
        {
            Require(beforeIndex == index && existing.copies != null && existing.copies.Length == uniqueCopies.Length,
                "The applied camera index or receipt copy count changed.");
            foreach (var binding in uniqueCopies)
            {
                var proof = existing.copies.Single(item => Same(item.copyPath, binding.target));
                Require(Same(proof.sourceGuid, binding.slot.sourceMaterialGuid) && Same(proof.sourceSha256, binding.slot.sourceMaterialSha256)
                    && Same(proof.sourceMetaSha256, binding.metaHash) && Same(proof.copyGuid, AssetDatabase.AssetPathToGUID(binding.target))
                    && Same(proof.copySha256, Hash(AssetFile(project, binding.target, ".mat"))), "An applied material or its source changed.");
            }
            Debug.Log("HO_SHADOWS_ACCEPTED_SCENE_NOOP " + receiptPath);
            return;
        }

        // Configure only the scene-local asset copies. Copy construction can mark Unity's loaded source material dirty.
        foreach (var binding in uniqueCopies)
        {
            string targetFile = AssetFile(project, binding.target, ".mat");
            Require(!File.Exists(targetFile) && !File.Exists(targetFile + ".meta"), "A scene-local copy already exists without its receipt.");
        }
        CheckSources(uniqueCopies);
        CheckSceneTargets(sceneFile, plan, scene, camera, cameraData, beforeIndex, bindings);

        Directory.CreateDirectory(artifactDir);
        Physical(artifactDir);
        if (!File.Exists(backup)) File.Copy(sceneFile, backup);
        if (!File.Exists(backup + ".meta")) File.Copy(sceneFile + ".meta", backup + ".meta");
        Require(Same(Hash(backup), plan.sceneSha256) && Same(Hash(backup + ".meta"), sceneMetaHash),
            "The scratch backup does not match the planned original.");
        var createdAssets = new List<string>();
        var createdFolders = new List<string>();
        var copies = new Dictionary<string, Material>();
        string temporaryReceipt = receiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
        bool saveAttempted = false;
        try
        {
            EnsureFolder(Generated, createdFolders);
            EnsureFolder(destinationFolder, createdFolders);
            var proofs = new List<CopyProof>();
            foreach (var binding in uniqueCopies)
            {
                string targetFile = AssetFile(project, binding.target, ".mat");
                Require(!File.Exists(targetFile) && !File.Exists(targetFile + ".meta"), "The output path is no longer empty.");
                Require(AssetDatabase.CopyAsset(binding.assetPath, binding.target), "Could not copy " + binding.assetPath);
                createdAssets.Add(binding.target);
                var copy = AssetDatabase.LoadAssetAtPath<Material>(binding.target);
                Require(copy != null, "The copied material was not imported.");
                Configure(copy, binding, shadows);
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
                copies.Add(binding.target, copy);
                proofs.Add(new CopyProof { sourceGuid = binding.slot.sourceMaterialGuid, sourcePath = binding.assetPath,
                    sourceSha256 = binding.slot.sourceMaterialSha256, sourceMetaSha256 = binding.metaHash, copyPath = binding.target,
                    copyGuid = AssetDatabase.AssetPathToGUID(binding.target), copySha256 = Hash(AssetFile(project, binding.target, ".mat")) });
            }

            CheckSources(uniqueCopies);
            CheckSceneTargets(sceneFile, plan, scene, camera, cameraData, beforeIndex, bindings);
            foreach (var binding in bindings)
            {
                var state = new SerializedObject(binding.renderer);
                var slot = state.FindProperty("m_Materials").GetArrayElementAtIndex(binding.slot.slotIndex);
                slot.objectReferenceValue = copies[binding.target];
                state.ApplyModifiedPropertiesWithoutUndo();
                RecordOverride(binding.renderer);
            }
            if (cameraData == null) cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.SetRenderer(index);
            RecordOverride(cameraData);
            EditorSceneManager.MarkSceneDirty(scene);
            saveAttempted = true;
            Require(EditorSceneManager.SaveScene(scene), "Could not save the accepted scratch scene.");
            var saved = EditorSceneManager.OpenScene(plan.scenePath, OpenSceneMode.Single);
            var savedCamera = Resolve<Camera>(plan.cameraGlobalObjectId, saved);
            Require(savedCamera.TryGetComponent(out UniversalAdditionalCameraData savedData)
                && savedData.renderType == CameraRenderType.Base && RendererIndex(savedData) == index,
                "The saved Base camera did not retain the candidate renderer.");
            foreach (var binding in bindings)
            {
                var target = Resolve<Renderer>(binding.slot.rendererGlobalObjectId, saved).sharedMaterials;
                Require(binding.slot.slotIndex < target.Length && target[binding.slot.slotIndex] == copies[binding.target],
                    "A saved renderer slot did not retain its scene-local copy.");
            }
            CheckSources(uniqueCopies);
            string after = Hash(sceneFile);
            Require(!Same(after, plan.sceneSha256) && Same(sceneMetaHash, Hash(sceneFile + ".meta")), "Unexpected scene or metadata write.");
            var report = new Receipt { planSha256 = planHash, scenePath = plan.scenePath, sceneGuid = sceneGuid,
                beforeSha256 = plan.sceneSha256, afterSha256 = after, backupPath = backup, backupSha256 = Hash(backup),
                cameraGlobalObjectId = plan.cameraGlobalObjectId, rendererAssetPath = AssetDatabase.GetAssetPath(rendererData), sceneMetaSha256 = sceneMetaHash,
                beforeRenderer = beforeIndex, afterRenderer = index, changedSlots = bindings.Count, copies = proofs.ToArray() };
            File.WriteAllText(temporaryReceipt, JsonUtility.ToJson(report, true));
            File.Move(temporaryReceipt, receiptPath);
        }
        catch
        {
            if (saveAttempted)
            {
                Physical(sceneFile);
                Physical(sceneFile + ".meta");
                File.Copy(backup, sceneFile, true);
                if (!File.Exists(sceneFile + ".meta") || !Same(sceneMetaHash, Hash(sceneFile + ".meta")))
                    File.Copy(backup + ".meta", sceneFile + ".meta", true);
            }
            EditorSceneManager.OpenScene(plan.scenePath, OpenSceneMode.Single);
            foreach (string path in createdAssets) AssetDatabase.DeleteAsset(path);
            for (int i = createdFolders.Count - 1; i >= 0; i--)
            {
                string folder = Path.GetFullPath(Path.Combine(project, createdFolders[i]));
                Physical(folder);
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(createdFolders[i]);
            }
            if (File.Exists(temporaryReceipt)) File.Delete(temporaryReceipt);
            throw;
        }
        Debug.Log("HO_SHADOWS_ACCEPTED_SCENE_OK " + receiptPath);
    }

    static void Configure(Material material, Binding binding, HoShadowsRendererFeature shadows)
    {
        string shader = material.shader.name;
        bool early = material.renderQueue <= (int)RenderQueue.GeometryLast;
        bool transparent = material.HasProperty("_SurfaceMode") && material.GetFloat("_SurfaceMode") >= 1.5f;
        bool weightedOit = false;
        Require(material.shader.keywordSpace.FindKeyword(Preview).isValid, "No screen preview keyword on " + shader);
        switch (shader)
        {
            case "Ho/Hair/URP/Hair":
                HairMaterialUtility.SetScreenShadowPreview(material, true);
                if (transparent) Require(material.IsKeywordEnabled(early ? "_HAIR_EARLY_TRANSPARENT_SCREEN_SHADOW_SURFACE"
                    : "_HAIR_TRANSPARENT_SCREEN_SHADOW_SURFACE"), "Ineligible transparent Hair.");
                break;
            case "Ho/Cloth/URP/Cloth":
                ClothMaterialUtility.SetScreenShadowPreview(material, true);
                if (transparent && material.GetFloat("_UseHoOIT") > .5f)
                {
                    Require(!early && shadows.WeightedOitShadowsEnabled && material.GetShaderPassEnabled("HoOIT"), "Ineligible Cloth OIT.");
                    weightedOit = true;
                }
                else if (transparent)
                    Require(material.IsKeywordEnabled(early ? "_CLOTH_EARLY_TRANSPARENT_SCREEN_SHADOW_SURFACE"
                        : "_CLOTH_TRANSPARENT_SCREEN_SHADOW_SURFACE"), "Ineligible ordinary transparent Cloth.");
                break;
            case EyeMaterialUtility.ShaderName:
                EyeMaterialUtility.SetScreenShadowPreview(material, true);
                if (transparent && early) Require(material.IsKeywordEnabled("_EYE_EARLY_TRANSPARENT_SCREEN_SHADOW_SURFACE"), "Ineligible early Eye.");
                break;
            case EffectMaterialUtility.ShaderName:
                EffectMaterialUtility.SetScreenShadowPreview(material, true);
                Require(material.IsKeywordEnabled("_EFFECT_SCREEN_SHADOW_SURFACE"), "Ineligible Effect surface.");
                break;
            case "Ho/Environment/URP/Lit":
                Require(material.GetFloat("_Surface") < .5f, "Transparent Environment does not have an accepted screen path.");
                material.EnableKeyword(Preview);
                break;
            case "Ho/Skin/URP/Stylized":
                material.EnableKeyword(Preview);
                break;
            default:
                throw new InvalidOperationException("Unsupported preview shader " + shader);
        }
        bool supported = weightedOit || (transparent
            ? early ? shadows.EarlyTransparentShadowsEnabled : shadows.TransparentEyeShadowsEnabled
            : early);
        Require(material.IsKeywordEnabled(Preview) && supported, "The candidate renderer does not support the selected material's draw phase.");
        Require(material.HasProperty(Scope), "The selected shader has no approved shadow scope property.");
        float expectedScope = binding.slot.scopeMode ?? binding.source.GetFloat(Scope);
        if (binding.slot.scopeMode.HasValue) material.SetFloat(Scope, expectedScope);
        Require(material.GetFloat(Scope) == expectedScope && material.renderQueue == binding.source.renderQueue
            && material.rawRenderQueue == binding.source.rawRenderQueue, "Preview changed the authored queue or scope.");
        foreach (string key in new[] { "_ZWrite", "_DepthWrite", "_TransparentZWrite", "_SrcBlend", "_DstBlend", "_StencilComp" })
            if (binding.source.HasProperty(key)) Require(material.GetFloat(key) == binding.source.GetFloat(key), "Preview changed " + key);
    }

    static void CheckSources(IEnumerable<Binding> bindings)
    {
        foreach (var binding in bindings)
        {
            Require(Same(Hash(binding.sourceFile), binding.slot.sourceMaterialSha256) && Same(Hash(binding.sourceFile + ".meta"), binding.metaHash),
                "Original material or metadata bytes changed: " + binding.assetPath);
            Require(!EditorUtility.IsDirty(binding.source), "Original material was marked dirty: " + binding.assetPath);
        }
    }

    static void CheckSceneTargets(string sceneFile, Plan plan, Scene scene, Camera camera,
        UniversalAdditionalCameraData cameraData, int beforeIndex, List<Binding> bindings)
    {
        Require(Same(Hash(sceneFile), plan.sceneSha256) && !scene.isDirty, "The scratch scene changed during preflight.");
        camera.TryGetComponent(out UniversalAdditionalCameraData current);
        Require(current == cameraData && RendererIndex(current) == beforeIndex, "The camera data changed during preflight.");
        foreach (var binding in bindings)
        {
            var slots = new SerializedObject(binding.renderer).FindProperty("m_Materials");
            Require(slots != null && binding.slot.slotIndex < slots.arraySize
                && slots.GetArrayElementAtIndex(binding.slot.slotIndex).objectReferenceValue == binding.source,
                "A named renderer slot changed during preflight.");
        }
    }

    static T Resolve<T>(string serializedId, Scene scene) where T : Component
    {
        Require(GlobalObjectId.TryParse(serializedId, out var id), "Malformed saved GlobalObjectId.");
        var component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T;
        Require(component != null && component.gameObject.scene == scene && GlobalObjectId.GetGlobalObjectIdSlow(component).Equals(id),
            "GlobalObjectId must resolve to the requested component in the saved scene: " + serializedId);
        return component;
    }

    static void EnsureFolder(string path, List<string> created)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string guid = AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        Require(AssetDatabase.GUIDToAssetPath(guid) == path, "Could not create the exact scene-local asset folder.");
        created.Add(path);
    }

    static void RecordOverride(Component component)
    {
        EditorUtility.SetDirty(component);
        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    static int RendererIndex(UniversalAdditionalCameraData data) => data == null
        ? -1 : new SerializedObject(data).FindProperty("m_RendererIndex").intValue;

    static string AssetFile(string project, string path, string extension)
    {
        Require(!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) && path.IndexOf('\\') < 0
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            && !path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."), "Expected an exact scratch asset path.");
        string full = Path.GetFullPath(Path.Combine(project, path));
        Require(full.StartsWith(Path.Combine(project, "Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "The asset leaves the scratch project.");
        Physical(full);
        Physical(full + ".meta");
        return full;
    }

    static void Physical(string path)
    {
        for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current))
                Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "A planned path is a link: " + current);
    }

    static string EnvironmentPath(string key)
    {
        string value = Environment.GetEnvironmentVariable(key);
        Require(!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value), "Set an absolute " + key + ".");
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    static string Hash(string path)
    {
        Physical(path);
        using var file = File.OpenRead(path);
        Require(GetFileInformationByHandle(file.SafeFileHandle, out var information) && information.LinkCount == 1,
            "The asset must have exactly one filesystem link: " + path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(file));
    }

    static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(bytes)); }
    static string Hex(byte[] digest) => BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool IsHex(string text, int length) => text != null && text.Length == length && text.All(Uri.IsHexDigit);
    static void Require(bool value, string problem) { if (!value) throw new InvalidOperationException(problem); }

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
