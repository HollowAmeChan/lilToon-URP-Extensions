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
using lilToon.URP.Extensions.RTShadow;
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
// Run applies a resolved-screen-v1 plan to the scratch scene. VerifySaved checks an existing accepted scene without writing it.
// Each slot names its approved phase, queue, source hash and optional scopeMode. A saved check also requires its copy GUID and hash.
public static class HoShadowsApplyAcceptedScene
{
    const string ScratchPrefix = @"D:\Temp\HoShadows-P5-";
    const string Abi = "resolved-screen-v1";
    const string Scope = "_HoRPShadowMode";
    const string Generated = "Assets/HoShadowsAccepted";

    enum ShadowPhase { Opaque, EarlyTransparent, LateTransparent, WeightedOit }

    [DataContract]
    sealed class Plan
    {
        [DataMember(IsRequired = true)] public string abi { get; set; }
        [DataMember(IsRequired = true)] public string scenePath { get; set; }
        [DataMember(IsRequired = true)] public string sceneSha256 { get; set; }
        [DataMember(IsRequired = true)] public string cameraGlobalObjectId { get; set; }
        [DataMember] public int? sourceRendererIndex { get; set; }
        [DataMember(IsRequired = true)] public int candidateRendererIndex { get; set; }
        [DataMember(IsRequired = true)] public string candidateRendererPath { get; set; }
        [DataMember(IsRequired = true)] public string candidateRendererGuid { get; set; }
        [DataMember(IsRequired = true)] public string candidateRendererSha256 { get; set; }
        [DataMember(IsRequired = true)] public bool rayTraced { get; set; }
        [DataMember] public RayTracedVolume requiredRtVolume { get; set; }
        [DataMember(IsRequired = true)] public Slot[] slots { get; set; }
    }

    [DataContract]
    sealed class RayTracedVolume
    {
        [DataMember(IsRequired = true)] public string globalObjectId { get; set; }
        [DataMember(IsRequired = true)] public string profilePath { get; set; }
        [DataMember(IsRequired = true)] public string profileSha256 { get; set; }
    }

    [DataContract]
    sealed class Slot
    {
        [DataMember(IsRequired = true)] public string rendererGlobalObjectId { get; set; }
        [DataMember(IsRequired = true)] public int slotIndex { get; set; }
        [DataMember(IsRequired = true)] public string sourceMaterialGuid { get; set; }
        [DataMember(IsRequired = true)] public string sourceMaterialSha256 { get; set; }
        [DataMember] public int? scopeMode { get; set; }
        [DataMember(IsRequired = true)] public string phase { get; set; }
        [DataMember(IsRequired = true)] public int rawRenderQueue { get; set; }
        [DataMember] public string savedMaterialGuid { get; set; }
        [DataMember] public string savedMaterialSha256 { get; set; }
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
        public string copyPath, copyGuid, copySha256, phase;
        public int rawRenderQueue, renderQueue;
        public float scope;
    }

    [Serializable] sealed class Receipt
    {
        public string planSha256, scenePath, sceneGuid, beforeSha256, afterSha256, backupPath, backupSha256;
        public string abi, cameraGlobalObjectId, rendererAssetPath, rendererGuid, rendererSha256, sceneMetaSha256;
        public bool rayTraced;
        public int beforeRenderer, afterRenderer, changedSlots;
        public CopyProof[] copies;
    }

    public static void Run() => Execute(verifySaved: false);
    public static void VerifySaved() => Execute(verifySaved: true);

    static void Execute(bool verifySaved)
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
        Require(plan?.abi == Abi && plan.slots != null && plan.slots.Length != 0 && IsHex(plan.sceneSha256, 64),
            "Expected a resolved-screen-v1 plan for one saved scene and explicit slots.");

        string sceneFile = AssetFile(project, plan.scenePath, ".unity");
        string sceneGuid = AssetDatabase.AssetPathToGUID(plan.scenePath);
        Require(IsHex(sceneGuid, 32) && File.Exists(sceneFile), "The scene must be an imported scratch asset.");
        string sceneMetaHash = Hash(sceneFile + ".meta");
        string artifactDir = Path.Combine(project, "HoShadowsAccepted", sceneGuid, plan.sceneSha256.ToLowerInvariant());
        string receiptPath = Path.Combine(artifactDir, "report.json");
        string backup = Path.Combine(artifactDir, "original.unity");
        Physical(receiptPath);
        bool hasReceipt = !verifySaved && File.Exists(receiptPath);
        Receipt existing = hasReceipt ? JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath)) : null;
        if (hasReceipt)
            Require(existing != null && existing.abi == Abi && Same(existing.planSha256, planHash) && Same(existing.scenePath, plan.scenePath)
                && Same(existing.sceneGuid, sceneGuid) && Same(existing.beforeSha256, plan.sceneSha256)
                && Same(existing.backupPath, backup) && Same(existing.backupSha256, Hash(backup))
                && Same(existing.sceneMetaSha256, sceneMetaHash) && Same(sceneMetaHash, Hash(backup + ".meta"))
                && existing.afterRenderer == plan.candidateRendererIndex && existing.changedSlots == plan.slots.Length
                && Same(existing.cameraGlobalObjectId, plan.cameraGlobalObjectId)
                && Same(existing.rendererAssetPath, plan.candidateRendererPath) && Same(existing.rendererGuid, plan.candidateRendererGuid)
                && Same(existing.rendererSha256, plan.candidateRendererSha256) && existing.rayTraced == plan.rayTraced,
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
        string rendererPath = AssetDatabase.GetAssetPath(rendererData);
        Require(Same(rendererPath, plan.candidateRendererPath) && IsHex(plan.candidateRendererGuid, 32)
            && Same(AssetDatabase.AssetPathToGUID(rendererPath), plan.candidateRendererGuid)
            && IsHex(plan.candidateRendererSha256, 64)
            && Same(Hash(AssetFile(project, rendererPath, ".asset")), plan.candidateRendererSha256),
            "The selected Renderer asset does not match the plan.");
        CheckRayTracedOptIn(project, scene, plan, shadows);
        int beforeIndex = RendererIndex(cameraData);
        if (verifySaved || existing != null)
            Require(beforeIndex == index, "The saved camera must already select the planned Renderer.");
        else if (plan.sourceRendererIndex.HasValue)
            Require(beforeIndex == plan.sourceRendererIndex.Value, "The camera's original Renderer index changed.");

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
            if (existing == null && !verifySaved)
                Require(materials[slot.slotIndex] == source, "The current slot does not reference the named source: " + sourcePath);
            else
                Require(materials[slot.slotIndex] == AssetDatabase.LoadAssetAtPath<Material>(target) && materials[slot.slotIndex] != null,
                    "An applied slot no longer references its recorded scene-local copy.");
            var binding = new Binding { slot = slot, renderer = renderer, source = source, assetPath = sourcePath,
                sourceFile = sourceFile, metaHash = sourceMeta, target = target };
            if (existing != null || verifySaved)
            {
                var savedMaterial = materials[slot.slotIndex];
                CheckResolvedMaterial(savedMaterial, binding, shadows, synchronize: false);
                if (verifySaved)
                    Require(IsHex(slot.savedMaterialGuid, 32) && IsHex(slot.savedMaterialSha256, 64)
                        && Same(AssetDatabase.AssetPathToGUID(target), slot.savedMaterialGuid)
                        && Same(Hash(AssetFile(project, target, ".mat")), slot.savedMaterialSha256),
                        "The saved local material does not match the read-only plan.");
            }
            bindings.Add(binding);
        }

        if (verifySaved)
        {
            Debug.Log("HO_SHADOWS_ACCEPTED_SCENE_VERIFIED " + plan.scenePath);
            return;
        }

        var copyGroups = bindings.GroupBy(binding => binding.target).ToArray();
        Require(copyGroups.All(group => group.All(binding => binding.slot.phase == group.First().slot.phase
            && binding.slot.rawRenderQueue == group.First().slot.rawRenderQueue)), "Shared copy slots disagree on phase or queue.");
        var uniqueCopies = copyGroups.Select(group => group.First()).ToArray();
        if (existing != null)
        {
            Require(beforeIndex == index && existing.copies != null && existing.copies.Length == uniqueCopies.Length,
                "The applied camera index or receipt copy count changed.");
            foreach (var binding in uniqueCopies)
            {
                var proof = existing.copies.Single(item => Same(item.copyPath, binding.target));
                Require(Same(proof.sourceGuid, binding.slot.sourceMaterialGuid) && Same(proof.sourceSha256, binding.slot.sourceMaterialSha256)
                    && Same(proof.sourceMetaSha256, binding.metaHash) && Same(proof.copyGuid, AssetDatabase.AssetPathToGUID(binding.target))
                    && Same(proof.copySha256, Hash(AssetFile(project, binding.target, ".mat")))
                    && proof.phase == binding.slot.phase && proof.rawRenderQueue == binding.slot.rawRenderQueue
                    && proof.renderQueue == binding.source.renderQueue && proof.scope == ExpectedScope(binding),
                    "An applied material, its source or its resolved phase changed.");
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
                if (binding.slot.scopeMode.HasValue)
                    copy.SetFloat(Scope, binding.slot.scopeMode.Value);
                CheckResolvedMaterial(copy, binding, shadows, synchronize: true);
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
                copies.Add(binding.target, copy);
                proofs.Add(new CopyProof { sourceGuid = binding.slot.sourceMaterialGuid, sourcePath = binding.assetPath,
                    sourceSha256 = binding.slot.sourceMaterialSha256, sourceMetaSha256 = binding.metaHash, copyPath = binding.target,
                    copyGuid = AssetDatabase.AssetPathToGUID(binding.target), copySha256 = Hash(AssetFile(project, binding.target, ".mat")),
                    phase = binding.slot.phase, rawRenderQueue = copy.rawRenderQueue, renderQueue = copy.renderQueue, scope = copy.GetFloat(Scope) });
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
                CheckResolvedMaterial(target[binding.slot.slotIndex], binding, shadows, synchronize: false);
            }
            CheckSources(uniqueCopies);
            CheckRayTracedOptIn(project, saved, plan, shadows);
            string after = Hash(sceneFile);
            Require(!Same(after, plan.sceneSha256) && Same(sceneMetaHash, Hash(sceneFile + ".meta")), "Unexpected scene or metadata write.");
            var report = new Receipt { abi = Abi, planSha256 = planHash, scenePath = plan.scenePath, sceneGuid = sceneGuid,
                beforeSha256 = plan.sceneSha256, afterSha256 = after, backupPath = backup, backupSha256 = Hash(backup),
                cameraGlobalObjectId = plan.cameraGlobalObjectId, rendererAssetPath = rendererPath,
                rendererGuid = plan.candidateRendererGuid, rendererSha256 = plan.candidateRendererSha256, rayTraced = plan.rayTraced, sceneMetaSha256 = sceneMetaHash,
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

    static void CheckRayTracedOptIn(string project, Scene scene, Plan plan, HoShadowsRendererFeature shadows)
    {
        Require(shadows.RayTracedShadowsEnabled == plan.rayTraced
            && (plan.requiredRtVolume != null) == plan.rayTraced, "The Renderer RT opt-in does not match the plan.");
        if (!plan.rayTraced) return;

        var expected = plan.requiredRtVolume;
        var volume = Resolve<Volume>(expected.globalObjectId, scene);
        Require(shadows.RayTracedShadowSettings.enabled && volume.isActiveAndEnabled && volume.isGlobal && volume.weight > 0f
            && volume.sharedProfile != null && Same(AssetDatabase.GetAssetPath(volume.sharedProfile), expected.profilePath)
            && volume.sharedProfile.TryGet<HoRTShadowVolume>(out var rt) && rt.active && rt.enable.overrideState && rt.enable.value
            && IsHex(expected.profileSha256, 64) && Same(Hash(AssetFile(project, expected.profilePath, ".asset")), expected.profileSha256),
            "The named RT Volume profile or its local opt-in no longer matches the plan.");
    }

    static float ExpectedScope(Binding binding) =>
        binding.slot.scopeMode.HasValue ? binding.slot.scopeMode.Value : binding.source.GetFloat(Scope);

    static void CheckResolvedMaterial(Material material, Binding binding, HoShadowsRendererFeature shadows, bool synchronize)
    {
        Require(material != null && material.shader == binding.source.shader && material.HasProperty(Scope)
            && binding.source.HasProperty(Scope), "The selected copy needs the original shader and approved scope property.");
        var actual = ResolvePhase(material, synchronize);
        Require(actual.ToString() == binding.slot.phase, "The selected surface does not match its approved phase: " + binding.assetPath);
        bool supported = actual switch
        {
            ShadowPhase.EarlyTransparent => shadows.EarlyTransparentShadowsEnabled,
            ShadowPhase.LateTransparent => shadows.TransparentEyeShadowsEnabled,
            ShadowPhase.WeightedOit => shadows.WeightedOitShadowsEnabled,
            _ => true,
        };
        Require(supported, "The candidate Renderer does not publish the selected shadow phase.");
        Require(material.GetFloat(Scope) == ExpectedScope(binding) && material.renderQueue == binding.source.renderQueue
            && material.rawRenderQueue == binding.source.rawRenderQueue && material.rawRenderQueue == binding.slot.rawRenderQueue,
            "The scene-local copy changed the authored queue or approved shadow scope.");
        foreach (string property in new[] { "_SurfaceMode", "_Surface", "_RenderMode", "_UseHoOIT", "_ZWrite",
            "_DepthWrite", "_TransparentZWrite", "_SrcBlend", "_DstBlend", "_BlendOp", "_StencilComp" })
            if (binding.source.HasProperty(property))
                Require(material.GetFloat(property) == binding.source.GetFloat(property), "The copy changed " + property);
    }

    static ShadowPhase ResolvePhase(Material material, bool synchronize)
    {
        string shader = material.shader.name;
        int queue = material.renderQueue;
        var earlyRange = RenderQueueRange.opaque;
        var lateRange = RenderQueueRange.transparent;
        bool early = queue >= earlyRange.lowerBound && queue <= earlyRange.upperBound;
        bool late = queue >= lateRange.lowerBound && queue <= lateRange.upperBound;
        bool transparent = material.HasProperty("_SurfaceMode") && material.GetFloat("_SurfaceMode") >= 1.5f;
        string prefix;
        switch (shader)
        {
            case "Ho/Hair/URP/Hair":
                if (synchronize) HairMaterialUtility.SyncScreenShadowSurface(material);
                prefix = "_HAIR_";
                break;
            case "Ho/Cloth/URP/Cloth":
                if (synchronize) ClothMaterialUtility.SyncScreenShadowSurface(material);
                if (transparent && material.GetFloat("_UseHoOIT") > .5f)
                {
                    Require(late && material.GetShaderPassEnabled("HoOIT"), "Ineligible weighted OIT Cloth.");
                    return ShadowPhase.WeightedOit;
                }
                prefix = "_CLOTH_";
                break;
            case EyeMaterialUtility.ShaderName:
                if (synchronize) EyeMaterialUtility.SyncScreenShadowSurface(material);
                prefix = "_EYE_";
                break;
            case EffectMaterialUtility.ShaderName:
                if (synchronize) EffectMaterialUtility.SyncScreenShadowSurface(material);
                Require(early && material.IsKeywordEnabled("_EFFECT_SCREEN_SHADOW_SURFACE"), "Ineligible Effect surface.");
                return ShadowPhase.Opaque;
            case "Ho/Environment/URP/Lit":
                Require(material.HasProperty("_Surface") && material.GetFloat("_Surface") < .5f,
                    "Transparent Environment has no approved screen-shadow phase.");
                goto case "Ho/Skin/URP/Stylized";
            case "Ho/Skin/URP/Stylized":
                Require(early && !transparent, "Ineligible opaque surface.");
                return ShadowPhase.Opaque;
            default:
                throw new InvalidOperationException("Unsupported resolved-screen shader " + shader);
        }

        bool earlyKeyword = material.IsKeywordEnabled(prefix + "EARLY_TRANSPARENT_SCREEN_SHADOW_SURFACE");
        bool lateKeyword = material.IsKeywordEnabled(prefix + "TRANSPARENT_SCREEN_SHADOW_SURFACE");
        Require(earlyKeyword == (transparent && early) && lateKeyword == (transparent && late)
            && (early || (transparent && late)), "The material has no consistent resolved screen-shadow phase: " + shader);
        return !transparent ? ShadowPhase.Opaque : early ? ShadowPhase.EarlyTransparent : ShadowPhase.LateTransparent;
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
