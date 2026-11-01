using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>One-time composition of an authored stage and an already adapted performance.</summary>
public static class HoMmdStagePerformance
{
    public static string Compose(HoMmdVideoWorkshop.Request request)
    {
        Validate(request);
        Scene scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        if (All<HoVmdSequencePlayer>(scene).Any())
            throw new InvalidOperationException("The destination already has a performance; edit it directly.");

        string sourceFolder = Path.GetDirectoryName(request.stageScene).Replace('\\', '/');
        var copies = new Dictionary<Object, Object>();
        foreach (string folder in new[] { "Rendering", "Materials" })
            CopyAssets(sourceFolder + "/" + folder, request.folder + "/" + folder, copies);
        foreach (Object copied in copies.Values.Distinct()) Remap(copied, copies);
        foreach (Component component in All<Component>(scene).Where(x => x != null)) Remap(component, copies);
        if (RenderSettings.skybox != null && copies.TryGetValue(RenderSettings.skybox, out Object sky))
            RenderSettings.skybox = (Material)sky;
        if (Lightmapping.TryGetLightingSettings(out LightingSettings lightingSettings))
        {
            string path = request.folder + "/Rendering/LightingSettings.asset";
            CopyAsset(AssetDatabase.GetAssetPath(lightingSettings), path, copies);
            Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        }
        foreach (Volume volume in All<Volume>(scene))
        {
            if (volume.sharedProfile == null) continue;
            string path = AssetDatabase.GetAssetPath(volume.sharedProfile);
            if (!path.StartsWith(request.folder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Stage Volume was not isolated: " + path);
            if (volume.sharedProfile.TryGet(out ImageProcessStackVolume image))
            {
                foreach (var layer in image.layers.value)
                    if (layer.effect == ImageProcessEffect.CinematicBars ||
                        layer.effect == ImageProcessEffect.LogoOverlay)
                        layer.enabled = false;
                EditorUtility.SetDirty(image);
            }
        }
        // Keep the established shared pipeline/feature asset. The scene owns
        // its Volume, lights, sky and environment materials.
        foreach (Camera camera in All<Camera>(scene))
        {
            camera.enabled = false;
            camera.tag = "Untagged";
        }
        foreach (AudioListener listener in All<AudioListener>(scene)) listener.enabled = false;

        Scene animation = EditorSceneManager.OpenScene(request.animationScene, OpenSceneMode.Additive);
        HoVmdSequencePlayer sequence = All<HoVmdSequencePlayer>(animation).Single();
        if (sequence.TargetAnimator == null || sequence.CameraPlayer == null ||
            sequence.Retargeter == null || sequence.Body == null)
            throw new InvalidOperationException("Use a configured, working VMD performance as the source.");
        var transfer = new HashSet<GameObject>
        {
            sequence.transform.root.gameObject,
            sequence.TargetAnimator.transform.root.gameObject,
            sequence.CameraPlayer.transform.root.gameObject
        };
        if (sequence.Music != null) transfer.Add(sequence.Music.transform.root.gameObject);
        foreach (GameObject root in transfer) SceneManager.MoveGameObjectToScene(root, scene);
        EditorSceneManager.CloseScene(animation, true);
        SceneManager.SetActiveScene(scene);
        Transform origin = sequence.transform;
        origin.name = "20 演出 · EX-HAPPY（整体位置与朝向）";
        foreach (GameObject root in transfer.Where(x => x != origin.gameObject))
            root.transform.SetParent(origin, true);
        origin.position = request.performancePosition;
        origin.rotation = Quaternion.Euler(0, request.performanceYaw, 0);
        sequence.CameraPlayer.motionOrigin = sequence.Retargeter.TargetSpace;
        sequence.CameraPlayer.enabled = true;
        Camera main = sequence.CameraPlayer.TargetCamera;
        main.name = "演出相机 · VMD";
        main.tag = "MainCamera";
        main.enabled = true;
        main.allowHDR = true;
        main.nearClipPlane = .03f;
        main.farClipPlane = 200;
        main.aspect = 9f / 16f;
        var cameraData = main.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.volumeLayerMask = ~0;
        cameraData.SetRenderer(-1);
        var audio = main.GetComponent<AudioListener>();
        if (audio == null) audio = main.gameObject.AddComponent<AudioListener>();
        audio.enabled = true;
        var serializedSequence = new SerializedObject(sequence);
        serializedSequence.FindProperty("loop").boolValue = true;
        serializedSequence.FindProperty("playOnEnable").boolValue = true;
        serializedSequence.FindProperty("currentTime").floatValue = 0;
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();
        sequence.ApplySources();
        ConfigureFocus(scene, sequence);

        // Position only the camera for the saved edit-mode view. Never save a
        // sampled body pose or apply changes back to the shared character prefab.
        sequence.CameraPlayer.ApplyAtFrame(0);
        foreach (var behaviour in All<MonoBehaviour>(scene).Where(x => x != null &&
                     x.GetType().Name == "HoMmdStageEnvironment"))
            behaviour.SendMessage("ApplyNow", SendMessageOptions.DontRequireReceiver);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sequence.TargetAnimator.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        HoMmdRTShadowDiagnostics.ConfigureStageReceivers(request);
        AssetDatabase.SaveAssets();
        return Report(scene, request.folder, "composition");
    }

    public static string Inspect(HoMmdVideoWorkshop.Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        return Report(scene, request.folder, "inspection");
    }

    public static string Place(HoMmdVideoWorkshop.Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequence = All<HoVmdSequencePlayer>(scene).Single();
        sequence.transform.SetPositionAndRotation(request.performancePosition,
            Quaternion.Euler(0, request.performanceYaw, 0));
        sequence.CameraPlayer.ApplyAtFrame(0);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Report(scene, request.folder, "placement");
    }

    public static string Focus(HoMmdVideoWorkshop.Request request)
    {
        var scene = SceneManager.GetSceneByPath(request.scene);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        ConfigureFocus(scene, All<HoVmdSequencePlayer>(scene).Single());
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return Report(scene, request.folder, "focus");
    }

    static void ConfigureFocus(Scene scene, HoVmdSequencePlayer sequence)
    {
        Transform head = sequence.TargetAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) throw new InvalidOperationException("The character has no Humanoid head for focus.");
        var names = new Stack<string>();
        for (Transform bone = head; bone != null; bone = bone.parent) names.Push(bone.name);
        string path = "/" + string.Join("/", names);
        foreach (var screen in All<Volume>(scene).Where(x => x.sharedProfile != null)
                     .SelectMany(x => x.sharedProfile.components.OfType<ScreenProcessStackVolume>()))
        {
            foreach (var layer in screen.layers.value.Where(x => x.effect == ScreenProcessEffect.DepthOfField))
            {
                layer.name = "演出 · 角色头部跟焦";
                layer.parameters0 = new Vector4(2, 3.4f, 70, 5.6f);
                layer.depthOfFieldFocusTarget = null;
                layer.depthOfFieldFocusTargetPath = path;
                layer.depthOfFieldFocusOffset = 0;
            }
            EditorUtility.SetDirty(screen);
        }
    }

    public static void Render(HoMmdVideoWorkshop.Request request, Action<string> completed,
        Action<Exception> failed)
    {
        HoMmdStageCapture.Begin(request.scene, request.output, request.width, request.height, completed, failed,
            camera =>
            {
                var sequence = All<HoVmdSequencePlayer>(camera.gameObject.scene).Single();
                float frame = request.seconds * sequence.FrameRate;
                sequence.Body.RebuildBindings();
                sequence.Body.ApplyAtFrame(frame);
                sequence.Body.SolveIkNow();
                if (sequence.Face != null)
                {
                    sequence.Face.RebuildBindings();
                    sequence.Face.ApplyAtFrame(frame);
                }
                sequence.Retargeter.ApplyPose();
                sequence.CameraPlayer.ApplyAtFrame(frame);
            }, 32);
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true));

    static void Validate(HoMmdVideoWorkshop.Request request)
    {
        if (string.IsNullOrEmpty(request.folder) || !request.folder.StartsWith("Assets/") ||
            request.folder.Split('/').Contains("..") ||
            !request.scene.StartsWith(request.folder + "/", StringComparison.Ordinal) ||
            request.scene == request.stageScene || request.scene == request.animationScene)
            throw new ArgumentException("Use a separate Assets output folder and scene.");
        foreach (string path in new[] { request.stageScene, request.animationScene })
            if (!File.Exists(path)) throw new FileNotFoundException("Scene not found", path);
        if (!File.Exists(request.scene))
        {
            foreach (string folder in new[] { "Scenes", "Rendering", "Materials", "Renders" })
                Directory.CreateDirectory(request.folder + "/" + folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.CopyAsset(request.stageScene, request.scene))
                throw new IOException("Cannot create the stage scene copy.");
        }
    }

    static void CopyAssets(string source, string target, Dictionary<Object, Object> copies)
    {
        if (!AssetDatabase.IsValidFolder(source)) return;
        Directory.CreateDirectory(target);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in AssetDatabase.FindAssets("", new[] { source })
                     .Select(AssetDatabase.GUIDToAssetPath).Where(x => !AssetDatabase.IsValidFolder(x)))
        {
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
            string destination = target + path.Substring(source.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            CopyAsset(path, destination, copies);
        }
    }

    static void CopyAsset(string source, string destination, Dictionary<Object, Object> copies)
    {
        // A failed composition may already have copied this asset. Reuse its
        // contents without overwriting; the scene itself still must have no sequence.
        if (!File.Exists(destination) && !AssetDatabase.CopyAsset(source, destination))
            throw new IOException("Cannot copy asset: " + source);
        var targets = AssetDatabase.LoadAllAssetsAtPath(destination);
        foreach (Object value in AssetDatabase.LoadAllAssetsAtPath(source))
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string ignored, out long localId);
            var match = targets.FirstOrDefault(x => x != null && x.GetType() == value.GetType() &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x, out string guid, out long id) && id == localId);
            if (match == null) throw new IOException("Missing copied subasset: " + value.name);
            copies.Add(value, match);
        }
    }

    static void Remap(Object target, Dictionary<Object, Object> copies)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.GetIterator();
        bool changed = false;
        while (property.Next(true))
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue != null &&
                copies.TryGetValue(property.objectReferenceValue, out Object replacement))
            {
                property.objectReferenceValue = replacement;
                changed = true;
            }
        if (!changed) return;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        if (target is Component component && PrefabUtility.IsPartOfPrefabInstance(component))
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    [Serializable] sealed class ReportData
    {
        public string scene, characterPrefab;
        public string[] roots, volumes, branding;
        public Vector3 performancePosition;
        public RendererData[] stageMeshes;
        public CameraData[] cameras;
    }
    [Serializable] sealed class RendererData { public string name; public Vector3 center, size; }
    [Serializable] sealed class CameraData { public string name; public bool enabled; public Vector3 position, euler; }
    static string Report(Scene scene, string folder, string name)
    {
        var sequence = All<HoVmdSequencePlayer>(scene).SingleOrDefault();
        var report = new ReportData
        {
            scene = scene.path,
            characterPrefab = sequence != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                sequence.TargetAnimator.gameObject) : null,
            roots = scene.GetRootGameObjects().Select(x => x.name).ToArray(),
            performancePosition = sequence != null ? sequence.transform.position : Vector3.zero,
            volumes = All<Volume>(scene).Select(x => x.name + " -> " +
                AssetDatabase.GetAssetPath(x.sharedProfile)).ToArray(),
            branding = All<Volume>(scene).Where(x => x.sharedProfile != null)
                .SelectMany(x => x.sharedProfile.components.OfType<ImageProcessStackVolume>())
                .SelectMany(x => x.layers.value).Where(x => x.effect == ImageProcessEffect.CinematicBars ||
                    x.effect == ImageProcessEffect.LogoOverlay)
                .Select(x => x.effect + " enabled=" + x.enabled).ToArray(),
            stageMeshes = All<MeshRenderer>(scene).Where(x =>
                    sequence == null || !x.transform.IsChildOf(sequence.transform))
                .Select(x => new RendererData { name = x.name, center = x.bounds.center, size = x.bounds.size }).ToArray(),
            cameras = All<Camera>(scene).Select(x => new CameraData { name = x.name, enabled = x.enabled,
                position = x.transform.position, euler = x.transform.eulerAngles }).ToArray()
        };
        string path = Path.Combine("Library/HoMmdVideo/StagePerformance", name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        return Path.GetFullPath(path);
    }
}
