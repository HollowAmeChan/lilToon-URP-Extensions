using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.Editor.Pmx;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Explicit file-queue operations for an editable MMD video scene.</summary>
[InitializeOnLoad]
public static class HoMmdVideoWorkshop
{
    [Serializable] public sealed class Request
    {
        public string id, action, sourceScene, scene, folder, output, sourceRig, motion, facial, camera, audio;
        public string stageScene, animationScene;
        public string antialiasing;
        public string qualityPreset, aspectPreset, postStack, cameraName;
        public string[] enableParts, disableParts;
        public Vector3 performancePosition;
        public float performanceYaw;
        public float seconds, distanceMultiplier = 1;
        public float durationSeconds, soundtrackTimeOffset;
        public bool overrideCameraFov;
        public float minimumCameraFov;
        public bool overrideFramingFollow, framingFollow;
        public string framingFollowBone = "Hips";
        public Vector2 framingFollowWeight = Vector2.right, framingFollowOffset;
        public float poseTransitionSeconds;
        public int width = 720, height = 1280, fps = 60;
        public bool fullBody, disableRtgi;
        public bool handStudy;
    }
    [Serializable] public sealed class Response { public string id, status, result; }
    [Serializable] sealed class BoneInfo
    {
        public string human, path;
        public Vector3 localPosition, localEuler, position, euler, right, up, forward, scale;
    }
    [Serializable] sealed class ObjectInfo
    {
        public string path;
        public bool active;
        public string[] components;
    }
    [Serializable] sealed class MeshInfo
    {
        public string path, rootBone;
        public string[] shapes;
        public Bounds bounds;
    }
    [Serializable] sealed class AnimatorInfo
    {
        public string path, avatar, controller;
        public bool enabled, human;
        public BoneInfo[] bones;
    }
    [Serializable] sealed class Inspection
    {
        public string scene;
        public ObjectInfo[] objects;
        public AnimatorInfo[] animators;
        public MeshInfo[] meshes;
        public string[] ik, cameras;
    }
    static string Work => Path.GetFullPath("Library/HoMmdVideo");
    static double next;

    static HoMmdVideoWorkshop() { EditorApplication.update += Tick; }
    static void Tick()
    {
        string cancel = Path.Combine(Work, "cancel.txt");
        if (File.Exists(cancel) && EditorApplication.isPlaying)
        {
            File.Delete(cancel);
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            File.Exists("Library/HoMmdStages/running.json")) return;
        next = EditorApplication.timeSinceStartup + 1;
        string path = Path.Combine(Work, "request.json");
        if (!File.Exists(path)) return;
        Request request = null;
        try
        {
            request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
            File.Delete(path);
            bool resumeComposition = (request.action == "resume-stage" || request.action == "focus-stage" ||
                request.action == "diagnose-render" || request.action == "proof-post" || request.action == "proof-glow" ||
                request.action == "enable-scene-post" || request.action == "proof-ho-only" ||
                request.action == "strip-native-post" || request.action == "diagnose-rtshadow" ||
                request.action == "configure-stage-rtshadow") &&
                Enumerable.Range(0, SceneManager.sceneCount).All(i =>
                    !SceneManager.GetSceneAt(i).isDirty || SceneManager.GetSceneAt(i).path == request.scene);
            if (!resumeComposition && Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("A loaded scene has unsaved changes; the video queue leaves it intact.");
            string result;
            switch (request.action)
            {
                case "compose-stage": result = HoMmdStagePerformance.Compose(request); break;
                case "resume-stage": result = HoMmdStagePerformance.Compose(request); break;
                case "inspect-stage": result = HoMmdStagePerformance.Inspect(request); break;
                case "diagnose-render": result = HoMmdRenderDiagnostics.Inspect(request); break;
                case "configure-camera-post": result = HoMmdRenderDiagnostics.ConfigureCameraPost(request); break;
                case "enable-scene-post": result = HoMmdRenderDiagnostics.EnableScenePost(request); break;
                case "test-render-presets": result = HoMmdRenderDiagnostics.ValidatePresets(); break;
                case "proof-post": result = HoMmdRenderDiagnostics.Proof(request); break;
                case "proof-glow": result = HoMmdRenderDiagnostics.Proof(request); break;
                case "proof-ho-only": result = HoMmdRenderDiagnostics.Proof(request); break;
                case "strip-native-post": result = HoMmdRenderDiagnostics.RemoveBuiltinFilters(request); break;
                case "diagnose-rtshadow": result = HoMmdRTShadowDiagnostics.Run(request); break;
                case "configure-stage-rtshadow": result = HoMmdRTShadowDiagnostics.ConfigureStageReceivers(request); break;
                case "place-stage": result = HoMmdStagePerformance.Place(request); break;
                case "focus-stage": result = HoMmdStagePerformance.Focus(request); break;
                case "render-performance":
                    HoMmdStagePerformance.Render(request,
                        path => File.WriteAllText(Path.Combine(Work, "result.json"), JsonUtility.ToJson(
                            new Response { id = request.id, status = "ok", result = path }, true)),
                        error => File.WriteAllText(Path.Combine(Work, "result.json"), JsonUtility.ToJson(
                            new Response { id = request.id, status = "error", result = error.ToString() }, true)));
                    return;
                case "inspect": result = Inspect(); break;
                case "create": result = Create(request); break;
                case "clone-performance": result = HoMmdPerformanceVariants.Clone(request); break;
                case "update-performance": result = HoMmdPerformanceVariants.Update(request); break;
                case "configure-preparation": result = HoMmdPerformanceVariants.ConfigurePreparation(request); break;
                case "source-rig": result = InspectSourceRig(request); break;
                case "setup": result = Setup(request); break;
                case "sample": result = Sample(request); break;
                case "adjust": result = Adjust(request); break;
                case "validate-rotation": result = ValidateRotation(request); break;
                case "sour-compare": result = SourCompare(request); break;
                case "production-tests":
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VmdProductionValidation")).First(x => x != null);
                    result = (string)type.GetMethod("Run").Invoke(null, new object[] { request.motion, request.sourceRig,
                        "Assets/00\u52A8\u753B/\u89D2\u8272/WTD/WTD-BUILD.prefab", Path.Combine(Work, "production-tests.json") });
                    break;
                case "aux-prefab": result = ConfigureAuxPrefab(request); break;
                case "sampling-tests":
                    var samplingType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VmdProductionValidation")).First(x => x != null);
                    result = (string)samplingType.GetMethod("RunSampling").Invoke(null, new object[] { request.motion,
                        Path.Combine(Work, Path.GetFileName(Path.GetDirectoryName(request.motion)) + "-sampling.json") });
                    break;
                case "study-motion": result = SetStudyMotion(request); break;
                case "pose-reference": result = ExportPoseReference(request); break;
                case "update-audio":
                    var audioScene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
                    var audioSequence = audioScene.GetRootGameObjects()
                        .Select(x => x.GetComponent<HoVmdSequencePlayer>()).First(x => x != null);
                    audioSequence.Soundtrack = AssetDatabase.LoadAssetAtPath<AudioClip>(request.audio);
                    audioSequence.ApplySources();
                    EditorSceneManager.SaveScene(audioScene);
                    result = request.audio; break;
                case "foot-audit": result = AuditFeet(request); break;
                case "record":
                    SessionState.SetString("HoMmdVideo.Request", JsonUtility.ToJson(request));
                    EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
                    EditorApplication.isPlaying = true;
                    return;
                default: throw new ArgumentException("Unknown video action: " + request.action);
            }
            File.WriteAllText(Path.Combine(Work, "result.json"),
                JsonUtility.ToJson(new Response { id = request.id, status = "ok", result = result }, true));
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Work, "result.json"),
                JsonUtility.ToJson(new Response { id = request?.id, status = "error", result = e.ToString() }, true));
            Debug.LogException(e);
        }
    }

    static string SetStudyMotion(Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.motion);
        var expression = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.facial);
        foreach (var sequence in scene.GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>()).Where(x => x != null))
        {
            sequence.Body.Animation = animation; sequence.MotionClip = animation;
            sequence.Face.Animation = expression; sequence.ExpressionClip = expression;
        }
        EditorSceneManager.SaveScene(scene);
        return scene.path;
    }

    [Serializable] sealed class PoseBone
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
    }
    [Serializable] sealed class PoseFrame
    {
        public string model;
        public int frame;
        public PoseBone[] bones;
    }
    [Serializable] sealed class PoseReference { public PoseFrame[] frames; public string[] materials; }

    static string ExportPoseReference(Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequences = scene.GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>()).Where(x => x != null).ToArray();
        var frames = new List<PoseFrame>();
        try
        {
            foreach (var sequence in sequences)
            {
                sequence.Body.RebuildBindings();
                if (sequence.Face != null) sequence.Face.RebuildBindings();
                Transform model = sequence.Retargeter != null ? sequence.Retargeter.TargetSpace : sequence.Body.ModelRoot;
                frames.Add(CaptureReference(model, -1));
                if (sequence.Retargeter != null) frames.Add(CaptureReference(sequence.Retargeter.SourceSpace, -1));
            }
            foreach (int frame in new[] { 0, 1, 15, 30, 60, 120 })
                foreach (var sequence in sequences)
                {
                    sequence.Sample(frame / 30f);
                    Transform model = sequence.Retargeter != null ? sequence.Retargeter.TargetSpace : sequence.Body.ModelRoot;
                    frames.Add(CaptureReference(model, frame));
                    if (sequence.Retargeter != null) frames.Add(CaptureReference(sequence.Retargeter.SourceSpace, frame));
                }
            string path = Path.Combine(Work, "unity-pose-reference.json");
            File.WriteAllText(path, JsonUtility.ToJson(new PoseReference { frames = frames.ToArray(),
                materials = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .Select(x => x.name + " enabled=" + x.enabled + " vertices=" + x.sharedMesh.vertexCount + " :: " +
                        string.Join(",", x.sharedMaterials.Select(m => m != null ? m.name : "NULL"))).ToArray() }, true));
            return path;
        }
        finally
        {
            // Sampling is diagnostic: restore the saved scene, never save the animated pose.
            EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        }
    }

    static PoseFrame CaptureReference(Transform model, int frame) => new PoseFrame { model = model.name, frame = frame,
        bones = model.GetComponentsInChildren<Transform>(true).Select(x => new PoseBone {
            name = x.name, position = model.InverseTransformPoint(x.position),
            rotation = Quaternion.Inverse(model.rotation) * x.rotation }).ToArray() };

    [Serializable] sealed class FootSample
    {
        public int frame;
        public string side;
        public float sourceHipHeight, targetHipHeight, sourceKneeAngle, targetKneeAngle;
        public float sourceAnkleGoalError, targetAnkleGoalError, sourceToeGoalError, targetToeGoalError, toeRotationError;
        public float toeAxisAngle, toeLengthRatio;
        public Vector3 sourceAnkleGoal, targetAnkleGoal, sourceToeGoal, targetToeGoal, sourceToeAxis, targetToeAxis;
        public Quaternion sourceToeRotation, targetToeRotation, sourceToeLocalRotation, targetToeLocalRotation;
        public bool sourceAnkleIk, targetAnkleIk, sourceToeIk, targetToeIk;
    }

    [Serializable] sealed class FootAuditReport
    {
        public string motion;
        public FootSample[] samples;
    }

    static string AuditFeet(Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        try
        {
            var sequence = scene.GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>())
                .First(x => x != null && x.Retargeter != null);
            sequence.Body.RebuildBindings();
            int last = (int)sequence.Body.Animation.LastBoneFrame;
            int[] frames = Enumerable.Range(0, 13).Select(i => last * i / 12).Distinct().ToArray();
            var samples = new List<FootSample>();
            foreach (int frame in frames)
            {
                sequence.Sample(frame / sequence.FrameRate);
                foreach (var foot in sequence.Retargeter.Feet)
                {
                    if (foot.sourceIk == null || foot.targetIk == null) continue;
                    bool toe = foot.sourceIk.Links.Count == 1;
                    Transform sourceEffector = foot.sourceIk.Effector;
                    Transform targetEffector = foot.targetIk.Effector;
                    Transform sourceKnee = toe ? null : foot.sourceIk.Links[0].bone;
                    Transform sourceHip = toe ? null : foot.sourceIk.Links[1].bone;
                    Transform targetKnee = toe ? null : foot.targetIk.Links[0].bone;
                    Transform targetHip = toe ? null : foot.targetIk.Links[1].bone;
                    var entry = new FootSample { frame = frame,
                        side = foot.sourceIk.IkName.StartsWith("左", StringComparison.Ordinal) ? "L" : "R",
                        sourceAnkleIk = foot.sourceIk.SolveEnabled, targetAnkleIk = foot.targetIk.SolveEnabled,
                        sourceToeIk = toe && foot.sourceIk.SolveEnabled, targetToeIk = toe && foot.targetIk.SolveEnabled };
                    if (toe)
                    {
                        entry.sourceToeGoal = sequence.Retargeter.SourceSpace.InverseTransformPoint(foot.sourceIk.Target.position);
                        entry.targetToeGoal = sequence.Retargeter.TargetSpace.InverseTransformPoint(foot.targetIk.Target.position);
                        entry.sourceToeGoalError = Vector3.Distance(sourceEffector.position, foot.sourceIk.Target.position);
                        entry.targetToeGoalError = Vector3.Distance(targetEffector.position, foot.targetIk.Target.position);
                        entry.sourceToeRotation = sourceEffector.rotation;
                        entry.targetToeRotation = targetEffector.rotation;
                        entry.toeRotationError = Quaternion.Angle(sourceEffector.rotation, targetEffector.rotation);
                        if (sourceEffector.parent != null && targetEffector.parent != null)
                        {
                            Vector3 sourceAxis = sourceEffector.position - sourceEffector.parent.position;
                            Vector3 targetAxis = targetEffector.position - targetEffector.parent.position;
                            entry.sourceToeAxis = sequence.Retargeter.SourceSpace.InverseTransformDirection(sourceAxis);
                            entry.targetToeAxis = sequence.Retargeter.TargetSpace.InverseTransformDirection(targetAxis);
                            entry.toeAxisAngle = Vector3.Angle(entry.sourceToeAxis, entry.targetToeAxis);
                            entry.toeLengthRatio = sourceAxis.sqrMagnitude > 1e-8f
                                ? targetAxis.magnitude / sourceAxis.magnitude : 0;
                            entry.sourceToeLocalRotation = Quaternion.Inverse(sourceEffector.parent.rotation) * sourceEffector.rotation;
                            entry.targetToeLocalRotation = Quaternion.Inverse(targetEffector.parent.rotation) * targetEffector.rotation;
                        }
                    }
                    else
                    {
                        entry.sourceHipHeight = sequence.Retargeter.SourceSpace.InverseTransformPoint(sourceHip.position).y;
                        entry.targetHipHeight = sequence.Retargeter.TargetSpace.InverseTransformPoint(targetHip.position).y;
                        entry.sourceAnkleGoal = sequence.Retargeter.SourceSpace.InverseTransformPoint(foot.sourceIk.Target.position);
                        entry.targetAnkleGoal = sequence.Retargeter.TargetSpace.InverseTransformPoint(foot.targetIk.Target.position);
                        entry.sourceAnkleGoalError = Vector3.Distance(sourceEffector.position, foot.sourceIk.Target.position);
                        entry.targetAnkleGoalError = Vector3.Distance(targetEffector.position, foot.targetIk.Target.position);
                        entry.sourceKneeAngle = Vector3.Angle(sourceHip.position - sourceKnee.position, sourceEffector.position - sourceKnee.position);
                        entry.targetKneeAngle = Vector3.Angle(targetHip.position - targetKnee.position, targetEffector.position - targetKnee.position);
                    }
                    samples.Add(entry);
                }
            }
            string path = Path.Combine(Work, "foot-audit.json");
            File.WriteAllText(path, JsonUtility.ToJson(new FootAuditReport {
                motion = sequence.Body.Animation.name, samples = samples.ToArray() }, true));
            return path;
        }
        finally { EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single); }
    }

    static string ConfigureAuxPrefab(Request request)
    {
        string path = "Assets/00\u52A8\u753B/\u89D2\u8272/WTD/WTD-BUILD.prefab";
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path && stage.scene.isDirty)
            throw new InvalidOperationException("WTD Prefab has unsaved editor changes; leave them intact.");
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            int count = Hollow.HoUnityTools.Editor.RigConstraints.HoFbxConstraintImportUtility.EnsurePrimaryAvatarConfigured(animator);
            if (count == 0) throw new InvalidOperationException("No associated main-armature constraint IR was found.");
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return "WTD primary HoAux operations: " + count;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static string SourCompare(Request request)
    {
        string prefab = request.folder + "/Character/角色姿态.prefab";
        if (!File.Exists(prefab)) HoPmxCharacterExtraction.Extract(request.sourceRig, request.folder + "/Character");
        if (!File.Exists(request.scene)) Create(request);
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(x => x.name == "Sour · 无物理对照"))
            throw new InvalidOperationException("Sour comparison already exists.");
        var main = scene.GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>()).First(x => x != null);
        main.Retargeter.TargetSpace.position = new Vector3(.65f, 0, 0);
        var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), scene);
        source.name = "Sour · 无物理对照"; source.transform.position = new Vector3(-.65f, 0, 0);
        var sequenceObject = new GameObject("Sour · 同帧动作与表情");
        var sequence = sequenceObject.AddComponent<HoVmdSequencePlayer>();
        var body = source.AddComponent<HoVmdMotionPlayer>();
        body.Animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.motion);
        body.ModelRoot = source.transform; body.SkeletonRoot = source.transform.Find("MMD 骨架");
        ConfigurePlayer(body, false, true, false); sequence.Body = body;
        var face = Child(sequenceObject.transform, "表情").gameObject.AddComponent<HoVmdMotionPlayer>();
        face.Animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.facial);
        face.ModelRoot = source.transform; face.SkeletonRoot = body.SkeletonRoot;
        ConfigurePlayer(face, false, true, true); sequence.Face = face;
        sequence.Duration = main.Duration;
        EditorSceneManager.SaveScene(scene);
        return scene.path;
    }

    static string ValidateRotation(Request request)
    {
        var animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.motion);
        var keys = animation.boneFrames.Where(x => x.boneName == "左腕" || x.boneName == "右腕" ||
            x.boneName == "左足首" || x.boneName == "下半身").GroupBy(x => x.boneName).Select(x => x.First()).ToArray();
        float largest = 0;
        foreach (var key in keys)
        {
            var data = ScriptableObject.CreateInstance<HoVmdAnimation>();
            data.boneFrames.Add(key);
            var root = new GameObject("Ho VMD basis validation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Transform bone = Child(root.transform, "target");
                Quaternion rest = Quaternion.Euler(8, 17, 3);
                bone.localRotation = rest;
                var player = root.AddComponent<HoVmdMotionPlayer>();
                player.Animation = data; player.ModelRoot = root.transform;
                player.BoneBindings.Add(new HoVmdMotionPlayer.BoneBinding { vmdName = key.boneName, target = bone });
                player.RebuildBindings(); player.ApplyAtFrame(key.frame);
                float error = Quaternion.Angle(key.rotation * rest, bone.localRotation);
                largest = Mathf.Max(largest, error);
                if (error > .01f) throw new InvalidOperationException(key.boneName + ": duplicate basis conversion; error=" + error);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }
        string result = "{\"keys\":" + keys.Length + ",\"maxAngularError\":" + largest.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        File.WriteAllText(Path.Combine(Work, "rotation-validation.json"), result);
        return result;
    }

    static string Adjust(Request request)
    {
        var scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequence = scene.GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>()).First(x => x != null);
        Animator animator = sequence.Retargeter.TargetSpace.GetComponent<Animator>();
        sequence.TargetAnimator = animator;
        sequence.ReferenceRigAsset = AssetDatabase.LoadMainAssetAtPath(request.sourceRig);
        sequence.MotionClip = sequence.Body.Animation;
        sequence.ExpressionClip = sequence.Face.Animation;
        sequence.CameraClip = sequence.CameraPlayer.Animation;
        sequence.Soundtrack = sequence.Music.clip;
        SetRotationOffsets(sequence.Retargeter, animator);
        sequence.CameraPlayer.distanceMultiplier = request.distanceMultiplier;
        sequence.CameraPlayer.centerOffset = new Vector3(0, .08f, 0);
        sequence.CameraPlayer.portraitFraming = true;
        sequence.CameraPlayer.minimumVerticalSpan = .95f;
        sequence.CameraPlayer.maximumVerticalSpan = 2.05f;
        foreach (var listener in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<AudioListener>(true)))
            listener.enabled = false;
        Camera camera = sequence.CameraPlayer.TargetCamera;
        var audioListener = camera.GetComponent<AudioListener>();
        if (audioListener == null) audioListener = camera.gameObject.AddComponent<AudioListener>();
        audioListener.enabled = true;
        if (!scene.GetRootGameObjects().Any(x => x.name == "摄影棚 · 地面与补光"))
        {
            Transform studio = new GameObject("摄影棚 · 地面与补光").transform;
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "地面 · 接触阴影"; floor.transform.SetParent(studio, false); floor.transform.localScale = Vector3.one * 3;
            var material = new Material(Shader.Find("lilToon")) { name = "摄影棚地面" };
            material.SetColor("_Color", new Color(.32f, .37f, .44f));
            material.SetFloat("_UseShadow", 1); material.SetFloat("_ShadowReceive", 1);
            material.SetColor("_ShadowColor", new Color(.17f, .22f, .32f));
            material.SetFloat("_ShadowBlur", .3f);
            string materialPath = Path.GetDirectoryName(Path.GetDirectoryName(scene.path)).Replace('\\', '/') + "/Rendering/摄影棚地面.mat";
            AssetDatabase.CreateAsset(material, materialPath);
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var item in new[] {
                new { name = "暖色轮廓光", position = new Vector3(1.4f, 2.2f, -1.4f), color = new Color(1, .78f, .48f), intensity = 2.4f },
                new { name = "冷色柔补光", position = new Vector3(-1.8f, 1.7f, 2), color = new Color(.67f, .81f, 1), intensity = 1.0f } })
            {
                Light light = Child(studio, item.name).gameObject.AddComponent<Light>();
                light.type = LightType.Spot; light.transform.position = item.position;
                light.transform.LookAt(new Vector3(0, .9f, 0)); light.range = 7; light.spotAngle = 65;
                light.innerSpotAngle = 35; light.color = item.color; light.intensity = item.intensity;
            }
        }
        EditorSceneManager.SaveScene(scene);
        return scene.path;
    }

    static void SetRotationOffsets(HoVmdHumanoidRetargeter rig, Animator animator)
    { Hollow.HoUnityTools.Editor.MmdMotion.HoVmdSequenceBuilder.SetRotationOffsets(rig, animator); }

    static string Setup(Request request)
    {
        Scene scene = EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        Animator animator = scene.GetRootGameObjects().First(x => x.name == "WTD-BUILD").GetComponent<Animator>();
        var sequenceObject = new GameObject("影片 · 动作 / 表情 / 镜头 / 音乐");
        var sequence = sequenceObject.AddComponent<HoVmdSequencePlayer>();
        sequence.TargetAnimator = animator;
        sequence.ReferenceRigAsset = AssetDatabase.LoadMainAssetAtPath(request.sourceRig);
        sequence.MotionClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.motion);
        sequence.ExpressionClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.facial);
        sequence.CameraClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(request.camera);
        sequence.Soundtrack = AssetDatabase.LoadAssetAtPath<AudioClip>(request.audio);
        Hollow.HoUnityTools.Editor.MmdMotion.HoVmdSequenceBuilder.Configure(sequence);
        EditorSceneManager.SaveScene(scene);
        return "Configured " + sequence.Retargeter.Bones.Count + " bones; " + scene.path;
    }

    static string Sample(Request request)
    {
        if (!string.IsNullOrEmpty(request.scene) && SceneManager.GetActiveScene().path != request.scene)
            EditorSceneManager.OpenScene(request.scene, OpenSceneMode.Single);
        var sequence = SceneManager.GetActiveScene().GetRootGameObjects().Select(x => x.GetComponent<HoVmdSequencePlayer>()).First(x => x != null);
        sequence.Body.RebuildBindings(); sequence.Face.RebuildBindings();
        sequence.Sample(request.seconds);
        // Do not persist a sampled frame into the editable rest setup.
        return "Sampled " + request.seconds;
    }

    static void ConfigurePlayer(HoVmdMotionPlayer player, bool play, bool bones, bool morphs)
    {
        var serialized = new SerializedObject(player);
        serialized.FindProperty("playOnEnable").boolValue = play;
        serialized.FindProperty("applyBones").boolValue = bones;
        serialized.FindProperty("applyMorphs").boolValue = morphs;
        serialized.FindProperty("applyIkToggles").boolValue = !morphs;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        player.Playing = false;
    }







    static Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

    static string InspectSourceRig(Request request)
    {
        var doc = HoPmxReader.Read(Path.GetFullPath(request.sourceRig));
        string path = Path.Combine(Work, "source-rig.json");
        File.WriteAllText(path, JsonUtility.ToJson(doc, true));
        return path;
    }

    static string Create(Request request)
    {
        if (string.IsNullOrEmpty(request.folder) || !request.folder.StartsWith("Assets/") ||
            request.folder.Split('/').Contains("..")) throw new ArgumentException("Use an Assets/... output folder.");
        string scenePath = string.IsNullOrEmpty(request.scene) ? request.folder + "/Scenes/EX-HAPPY.unity" : request.scene;
        if (File.Exists(scenePath)) throw new IOException("The production scene already exists: " + scenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
        Directory.CreateDirectory(request.folder + "/Rendering");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (!AssetDatabase.CopyAsset(request.sourceScene, scenePath))
            throw new IOException("Cannot copy the source scene.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int index = 0;
        foreach (Volume volume in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Volume>(true)))
        {
            if (volume.sharedProfile == null) continue;
            string source = AssetDatabase.GetAssetPath(volume.sharedProfile);
            string target = request.folder + "/Rendering/" + (++index).ToString("00") + "-" + volume.sharedProfile.name + ".asset";
            if (!AssetDatabase.CopyAsset(source, target)) throw new IOException("Cannot isolate Volume profile: " + source);
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(target);
        }
        EditorSceneManager.SaveScene(scene);
        return scenePath;
    }

    static string Inspect()
    {
        Scene scene = SceneManager.GetActiveScene();
        var transforms = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        var result = new Inspection
        {
            scene = scene.path,
            objects = transforms.Where(x => x.parent == null || x.GetComponents<Component>().Length > 1)
                .Select(x => new ObjectInfo { path = PathOf(x), active = x.gameObject.activeInHierarchy,
                    components = x.GetComponents<Component>().Where(c => c != null)
                        .Select(c => c.GetType().FullName + (c is Behaviour b ? " enabled=" + b.enabled : "")).ToArray() }).ToArray(),
            animators = transforms.Select(x => x.GetComponent<Animator>()).Where(x => x != null)
                .Select(a => new AnimatorInfo { path = PathOf(a.transform), enabled = a.enabled, human = a.isHuman,
                    avatar = a.avatar != null ? a.avatar.name : null, controller = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : null,
                    bones = a.isHuman ? Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                        .Where(h => h != HumanBodyBones.LastBone).Select(h => new { h, t = a.GetBoneTransform(h) })
                        .Where(x => x.t != null).Select(x => new BoneInfo { human = x.h.ToString(), path = PathOf(x.t),
                            localPosition = x.t.localPosition, localEuler = x.t.localEulerAngles,
                            position = x.t.position, euler = x.t.eulerAngles, right = x.t.right, up = x.t.up, forward = x.t.forward,
                            scale = x.t.lossyScale }).ToArray() : Array.Empty<BoneInfo>() }).ToArray(),
            meshes = transforms.Select(x => x.GetComponent<SkinnedMeshRenderer>()).Where(x => x != null && x.sharedMesh != null)
                .Select(x => new MeshInfo { path = PathOf(x.transform), rootBone = PathOf(x.rootBone), bounds = x.bounds,
                    shapes = Enumerable.Range(0, x.sharedMesh.blendShapeCount).Select(x.sharedMesh.GetBlendShapeName).ToArray() }).ToArray(),
            ik = transforms.SelectMany(x => x.GetComponents<HoIkConstraint>()).Select(x => JsonUtility.ToJson(x)).ToArray(),
            cameras = transforms.Select(x => x.GetComponent<Camera>()).Where(x => x != null)
                .Select(x => PathOf(x.transform) + " pos=" + x.transform.position + " rot=" + x.transform.eulerAngles +
                    " FOV=" + x.fieldOfView + " enabled=" + x.enabled).ToArray()
        };
        Directory.CreateDirectory(Work);
        string path = Path.Combine(Work, "inspection.json");
        File.WriteAllText(path, JsonUtility.ToJson(result, true));
        return path;
    }

    static string PathOf(Transform transform)
    {
        if (transform == null) return null;
        string path = transform.name;
        while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
        return path;
    }
}
