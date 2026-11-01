using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.Editor.MmdMotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Add an action to an authored stage without regenerating the actor or rendering assets.</summary>
public static class HoMmdPerformanceVariants
{
    public static string ConfigurePreparation(HoMmdVideoWorkshop.Request request)
    {
        ValidateScene(request.scene);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != request.scene)
            scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequences = scene.GetRootGameObjects()
            .SelectMany(x => x.GetComponentsInChildren<HoVmdSequencePlayer>(true)).ToArray();
        if (sequences.Length != 1) throw new InvalidOperationException("Expected one VMD Sequence Player.");
        var sequence = sequences[0];
        sequence.InitialPoseTransitionSeconds = request.poseTransitionSeconds;
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save preparation setting.");
        return JsonUtility.ToJson(new PreparationReport
        {
            scene = scene.path, transitionSeconds = sequence.InitialPoseTransitionSeconds,
            settleSeconds = sequence.Preroll, totalSeconds = sequence.PreparationDuration,
            motionDuration = sequence.Duration, soundtrackTimeOffset = sequence.SoundtrackTimeOffset
        }, true);
    }

    [Serializable] sealed class PreparationReport
    {
        public string scene;
        public float transitionSeconds, settleSeconds, totalSeconds, motionDuration, soundtrackTimeOffset;
    }

    [Serializable] sealed class Shapes
    {
        public string renderer;
        public string[] names;
    }

    [Serializable] sealed class Report
    {
        public string scene, template, referenceRig, motion, facial, camera, audio;
        public float duration, soundtrackTimeOffset;
        public bool retargetCalibrationPreserved;
        public string framingFollowTarget;
        public Vector2 framingFollowWeight, framingFollowOffset;
        public HoVmdMorphAuditResult expressions;
        public Shapes[] shapes;
    }

    public static string Clone(HoMmdVideoWorkshop.Request request)
    {
        ValidateScene(request.sourceScene);
        ValidateScene(request.scene);
        if (request.scene == request.sourceScene || File.Exists(request.scene))
            throw new IOException("Choose a new action scene; existing scenes are never rebuilt.");
        if (!File.Exists(request.sourceScene))
            throw new FileNotFoundException("Authored template scene not found.", request.sourceScene);
        var motion = Load<HoVmdAnimation>(request.motion);
        var facial = Load<HoVmdAnimation>(request.facial);
        var camera = Load<HoVmdAnimation>(request.camera);
        var audio = Load<AudioClip>(request.audio);
        if (motion.boneFrames.Count == 0 || camera.cameraFrames.Count == 0)
            throw new InvalidDataException("The action must have body and camera tracks.");
        Directory.CreateDirectory(Path.GetDirectoryName(request.scene));
        if (!AssetDatabase.CopyAsset(request.sourceScene, request.scene))
            throw new IOException("Cannot copy authored action scene.");
        return Update(request);
    }

    public static string Update(HoMmdVideoWorkshop.Request request)
    {
        ValidateScene(request.scene);
        if (!File.Exists(request.scene)) throw new FileNotFoundException("Action scene not found.", request.scene);
        var motion = Load<HoVmdAnimation>(request.motion);
        var facial = Load<HoVmdAnimation>(request.facial);
        var camera = Load<HoVmdAnimation>(request.camera);
        var audio = Load<AudioClip>(request.audio);
        Scene scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequences = scene.GetRootGameObjects()
            .SelectMany(x => x.GetComponentsInChildren<HoVmdSequencePlayer>(true)).ToArray();
        if (sequences.Length != 1)
            throw new InvalidOperationException("Template must contain exactly one VMD Sequence Player.");
        var sequence = sequences[0];
        if (sequence.Retargeter == null || sequence.TargetAnimator == null || sequence.Body == null || sequence.Face == null)
            throw new InvalidOperationException("Template has no completed character adaptation.");
        string calibration = EditorJsonUtility.ToJson(sequence.Retargeter);
        string reference = AssetDatabase.GetAssetPath(sequence.ReferenceRigAsset);
        if (!string.IsNullOrEmpty(request.sourceRig) && request.sourceRig != reference)
            throw new InvalidOperationException("Changing the reference PMX needs an explicit re-adaptation; clone preserves the template rig.");
        sequence.MotionClip = motion;
        sequence.ExpressionClip = facial;
        sequence.CameraClip = camera;
        sequence.Soundtrack = audio;
        sequence.SoundtrackTimeOffset = request.soundtrackTimeOffset;
        sequence.Duration = request.durationSeconds;
        if (request.overrideCameraFov)
            sequence.CameraPlayer.minimumPerspectiveFov = Mathf.Clamp(request.minimumCameraFov, 0, 100);
        if (request.overrideFramingFollow)
        {
            Transform follow = null;
            if (request.framingFollow)
            {
                if (!Enum.TryParse(request.framingFollowBone, true, out HumanBodyBones bone) ||
                    bone < 0 || bone >= HumanBodyBones.LastBone)
                    throw new ArgumentException("Choose an explicit Humanoid framingFollowBone.");
                follow = sequence.TargetAnimator.GetBoneTransform(bone);
                if (follow == null) throw new InvalidOperationException("Framing bone not found: " + bone);
            }
            sequence.CameraPlayer.framingFollowTarget = follow;
            sequence.CameraPlayer.framingFollowWeight = new Vector2(
                Mathf.Clamp01(request.framingFollowWeight.x), Mathf.Clamp01(request.framingFollowWeight.y));
            sequence.CameraPlayer.framingFollowOffset = request.framingFollowOffset;
        }
        sequence.ApplySources();
        BindExactExpressions(sequence);
        sequence.Body.RebuildBindings();
        sequence.Face.RebuildBindings();
        sequence.CameraPlayer.ApplyAtFrame(0);
        SetParts(sequence.TargetAnimator.transform, request.disableParts, false);
        SetParts(sequence.TargetAnimator.transform, request.enableParts, true);
        bool preserved = calibration == EditorJsonUtility.ToJson(sequence.Retargeter);
        if (!preserved) throw new InvalidOperationException("Retarget calibration unexpectedly changed.");
        var active = ScriptableObject.CreateInstance<HoVmdAnimation>();
        HoVmdMorphAuditResult audit;
        try
        {
            active.morphFrames.AddRange(facial.morphFrames.Where(x => Mathf.Abs(x.weight) > .00001f));
            audit = HoVmdMorphAudit.Audit(active, sequence.TargetAnimator.gameObject, null, sequence.Face.MorphBindings);
        }
        finally { UnityEngine.Object.DestroyImmediate(active); }
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save new action scene.");
        var report = new Report
        {
            scene = scene.path, template = request.sourceScene, referenceRig = reference,
            motion = request.motion, facial = request.facial, camera = request.camera, audio = request.audio,
            duration = sequence.Duration, soundtrackTimeOffset = sequence.SoundtrackTimeOffset,
            retargetCalibrationPreserved = preserved, expressions = audit,
            framingFollowTarget = sequence.CameraPlayer.framingFollowTarget != null
                ? AnimationUtility.CalculateTransformPath(sequence.CameraPlayer.framingFollowTarget, sequence.TargetAnimator.transform) : "",
            framingFollowWeight = sequence.CameraPlayer.framingFollowWeight,
            framingFollowOffset = sequence.CameraPlayer.framingFollowOffset,
            shapes = sequence.TargetAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(x => x.sharedMesh != null && x.sharedMesh.blendShapeCount > 0)
                .Select(x => new Shapes
                {
                    renderer = AnimationUtility.CalculateTransformPath(x.transform, sequence.TargetAnimator.transform),
                    names = Enumerable.Range(0, x.sharedMesh.blendShapeCount).Select(x.sharedMesh.GetBlendShapeName).ToArray()
                }).ToArray()
        };
        string output = Path.GetFullPath("Library/HoMmdVideo/Variants/" + Path.GetFileNameWithoutExtension(scene.path) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        return output;
    }

    static void BindExactExpressions(HoVmdSequencePlayer sequence)
    {
        // Split face meshes intentionally repeat a shape across eyelids, brows,
        // iris and face. Save an explicit one-to-many mapping, exactly as the
        // runtime's name matching already applies it, instead of reporting those
        // separate mesh parts as ambiguous alternatives.
        var renderers = sequence.TargetAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (string source in sequence.ExpressionClip.morphFrames.Where(x => Mathf.Abs(x.weight) > .00001f)
                     .Select(x => x.morphName).Distinct(StringComparer.Ordinal))
        {
            if (sequence.Face.MorphBindings.Any(x => x != null && x.vmdName == source)) continue;
            foreach (var renderer in renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                int index = mesh.GetBlendShapeIndex(source);
                if (index < 0)
                {
                    string normalized = source.Normalize(System.Text.NormalizationForm.FormKC);
                    int[] matches = Enumerable.Range(0, mesh.blendShapeCount)
                        .Where(i => mesh.GetBlendShapeName(i).Normalize(System.Text.NormalizationForm.FormKC) == normalized).ToArray();
                    if (matches.Length == 1) index = matches[0];
                }
                if (index >= 0) sequence.Face.MorphBindings.Add(new HoVmdMotionPlayer.MorphBinding
                    { vmdName = source, renderer = renderer, blendShapeIndex = index });
            }
        }
    }

    static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new FileNotFoundException("Required " + typeof(T).Name + " asset not imported.", path);
        return asset;
    }

    static void ValidateScene(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path.Split('/').Contains(".."))
            throw new ArgumentException("Use an explicit Assets/...unity scene path.");
    }

    static void SetParts(Transform actor, string[] names, bool active)
    {
        if (names == null || names.Length == 0) return;
        Transform parts = null;
        for (Transform parent = actor; parent != null && parts == null; parent = parent.parent)
            parts = parent.Find("Parts");
        if (parts == null) throw new InvalidOperationException("Character Parts group not found.");
        foreach (string name in names)
        {
            Transform part = parts.Find(name);
            if (part == null) throw new InvalidOperationException("Character Parts child not found: " + name);
            part.gameObject.SetActive(active);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
        }
    }
}
