#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.PlaySpeed;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.PostProcessing;
using System.Collections.Generic;
using lilToon.URP.Extensions.RTGI;
using Object = UnityEngine.Object;

/// <summary>Unity Recorder bridge. Only runs for an explicit CLI recording request.</summary>
[InitializeOnLoad]
public static class HoMmdVideoCapture
{
    static HoMmdVideoCapture()
    {
        EditorApplication.delayCall += () =>
        {
            string json = SessionState.GetString("HoMmdVideo.Request", "");
            if (EditorApplication.isPlaying && !string.IsNullOrEmpty(json) &&
                Object.FindFirstObjectByType<HoMmdVideoCaptureDriver>() == null)
                new GameObject("Ho MMD · 离线录制").AddComponent<HoMmdVideoCaptureDriver>()
                    .Begin(JsonUtility.FromJson<HoMmdVideoCaptureRequest>(json));
        };
        EditorApplication.playModeStateChanged += state =>
        {
            string json = SessionState.GetString("HoMmdVideo.Request", "");
            if (string.IsNullOrEmpty(json)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                var driver = new GameObject("Ho MMD · 离线录制").AddComponent<HoMmdVideoCaptureDriver>();
                driver.Begin(JsonUtility.FromJson<HoMmdVideoCaptureRequest>(json));
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                SessionState.EraseString("HoMmdVideo.Request");
        };
    }
}

// Recorder's MonoBehaviour bridge must be outside an Editor directory. The
// entire implementation is editor-only and is never serialized into a scene.
[Serializable]
public sealed class HoMmdVideoCaptureRequest
{
    public string id, output, antialiasing;
    public string qualityPreset, aspectPreset, postStack;
    public string[] enableParts, disableParts;
    public float seconds;
    public int width = 720, height = 1280, fps = 60;
    public bool fullBody, disableRtgi;
    public bool handStudy;
}
[Serializable]
public sealed class HoMmdVideoCaptureResponse { public string id, status, result; }

[DefaultExecutionOrder(32700)]
public sealed class HoMmdVideoCaptureDriver : MonoBehaviour
{
    HoMmdVideoCaptureRequest request;
    HoVmdSequencePlayer sequence;
    HoVmdSequencePlayer[] sequences;
    RecorderController controller;
    RecorderControllerSettings controllerSettings;
    MovieRecorderSettings movieSettings;
    int tick, count, warmup;
    bool started, finished;
    float previousCaptureDelta;
    bool previousBackground;
    double began;
    string work;
    Camera captureCamera;
    RenderTexture previousCameraTarget, recordingTarget;
    bool previousCameraEnabled;
    float previousCameraAspect;
    UniversalAdditionalCameraData captureCameraData;
    bool taaVerified;
    int taaHistoryWidth, taaHistoryHeight, taaHistoryUpdates, lastTaaVersion = -1;
    string renderFailure;
    int screenProcessFrames, imageProcessFrames, screenLayers, imageLayers;
    string screenReason = "No current-camera ScreenProcess diagnostic.", imageReason = "No current-camera ImageProcess diagnostic.";
    readonly Dictionary<int, CameraRenderCount> cameraRenders = new Dictionary<int, CameraRenderCount>();
    readonly List<GameObject> toggledParts = new List<GameObject>();
    int lastStudyStage = -1;
    [Serializable] sealed class BoneAudit { public string name, source; public Vector3 position, expectedEuler, actualEuler; public float angularError; }
    [Serializable] sealed class DeformAudit { public string name, parent; public Vector3 position, euler; }
    [Serializable] sealed class SkinAudit { public string bone, parent; public float weight; public Vector3 position, tipCentroid; public int tipVertices; }
    [Serializable] sealed class HandAudit { public int stage; public string model; public BoneAudit[] mapped; public DeformAudit[] deform, sourceBones; public string[] behaviours; public SkinAudit[] skin; }

    public void Begin(HoMmdVideoCaptureRequest value)
    {
        request = value;
        work = Path.GetFullPath("Library/HoMmdVideo");
        began = EditorApplication.timeSinceStartup;
        try
        {
            // Explicit ownership keeps warmup/physics deterministic and makes
            // the requesting script visible in the debug speed panel.
            HoPlaySpeedControl.SetOverride(this, 1f, lockPanel: true);
            sequences = SceneManager.GetActiveScene().GetRootGameObjects()
                .Select(x => x.GetComponent<HoVmdSequencePlayer>()).Where(x => x != null).ToArray();
            sequence = sequences.First(x => x.CameraPlayer != null);
            ApplyPartToggles();
            foreach (var item in sequences)
            {
                item.ExternallyDriven = true;
                item.Playing = false;
                if (item.Body != null) item.Body.RebuildBindings();
                if (item.Face != null) item.Face.RebuildBindings();
            }
            if (request.disableRtgi)
                foreach (var volume in SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(x => x.GetComponentsInChildren<Volume>(true)))
                    if (volume.isActiveAndEnabled && volume.profile != null &&
                        volume.profile.TryGet(out HoRTGIVolume rtgi))
                        rtgi.enable.Override(false);
            if (request.postStack == "image-off" || request.postStack == "screen-off")
                foreach (var volume in SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(x => x.GetComponentsInChildren<Volume>(true)).Where(x => x.isActiveAndEnabled))
                {
                    // Volume.profile creates an unsaved runtime copy; never edit sharedProfile.
                    if (request.postStack == "image-off" &&
                        volume.profile.TryGet(out ImageProcessStackVolume image)) image.Enable.Override(false);
                    if (request.postStack == "screen-off" &&
                        volume.profile.TryGet(out ScreenProcessStackVolume screen)) screen.Enable.Override(false);
                }
            count = Mathf.CeilToInt((request.seconds > 0 ? request.seconds : sequence.Duration) * request.fps);
            warmup = request.fps; // One real simulated second for armature merge, cloth and temporal history.
            previousCaptureDelta = Time.captureDeltaTime;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            Time.captureDeltaTime = 1f / request.fps;
            EditorApplication.update += Pump;
            Camera camera = sequence.CameraPlayer.TargetCamera;
            captureCamera = camera;
            previousCameraTarget = camera.targetTexture;
            previousCameraEnabled = camera.enabled;
            previousCameraAspect = camera.aspect;
            // A CLI recording must render even when the visible Game View is
            // showing another display. Own an offscreen target from warmup on;
            // Recorder still captures this camera through its SRP capture hook.
            recordingTarget = new RenderTexture(request.width, request.height, 24, RenderTextureFormat.ARGBHalf) {
                name = "Ho MMD recording target", antiAliasing = 1, useDynamicScale = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            recordingTarget.Create();
            camera.targetTexture = recordingTarget;
            camera.enabled = true;
            camera.aspect = (float)request.width / request.height;
            captureCameraData = camera.GetUniversalAdditionalCameraData();
            switch ((request.antialiasing ?? "inherit").ToLowerInvariant())
            {
                case "inherit": case "": break;
                case "taa":
                    camera.allowMSAA = false;
                    camera.allowDynamicResolution = false;
                    captureCameraData.renderPostProcessing = true;
                    captureCameraData.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                    captureCameraData.taaSettings.quality = TemporalAAQuality.VeryHigh;
                    captureCameraData.resetHistory = true;
                    break;
                case "smaa":
                    captureCameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    captureCameraData.antialiasingQuality = AntialiasingQuality.High;
                    break;
                case "fxaa": captureCameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing; break;
                case "none": captureCameraData.antialiasing = AntialiasingMode.None; break;
                default: throw new ArgumentException("Unknown recording AA: " + request.antialiasing);
            }
            RenderPipelineManager.endCameraRendering += OnCameraRendered;
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) listener.enabled = false;
            var videoListener = camera.GetComponent<AudioListener>();
            if (videoListener == null) videoListener = camera.gameObject.AddComponent<AudioListener>();
            videoListener.enabled = true;
            if (request.fullBody)
            {
                sequence.CameraPlayer = null;
                camera.transform.position = new Vector3(0, .9f, 3.4f);
                camera.transform.LookAt(new Vector3(0, .85f, 0));
                camera.fieldOfView = 33;
            }
            if (request.handStudy)
            {
                sequence.CameraPlayer = null;
                foreach (var renderer in sequence.Retargeter.TargetSpace.GetComponentsInChildren<Renderer>(true))
                    if (renderer is SkinnedMeshRenderer) renderer.enabled = renderer.name == "Body1";
                foreach (var item in sequences.Where(x => x != sequence))
                    foreach (var renderer in item.Body.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        // The pose prefab uses one renderer per PMX material.
                        // Disable clothing submeshes only on this diagnostic instance.
                        renderer.enabled = renderer.sharedMaterials.Any(material => material != null &&
                            (material.name.StartsWith("001_body", StringComparison.Ordinal) ||
                             string.Equals(material.name, "body", StringComparison.OrdinalIgnoreCase)));
                    }
            }
            movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "Ho MMD portrait video";
            movieSettings.Enabled = true;
            movieSettings.CaptureAudio = false; // Mux the original soundtrack sample-accurately after offline rendering.
            movieSettings.CaptureAlpha = false;
            movieSettings.EncoderSettings = new CoreEncoderSettings {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
            movieSettings.ImageInputSettings = new CameraInputSettings {
                Source = ImageSource.MainCamera, OutputWidth = request.width, OutputHeight = request.height,
                CaptureUI = false };
            movieSettings.OutputFile = Path.GetFullPath(request.output);
            Directory.CreateDirectory(Path.GetDirectoryName(movieSettings.OutputFile));
            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.FrameRate = request.fps;
            controllerSettings.CapFrameRate = false;
            // Recorder establishes the final Game View size when recording starts.
            // Skip encoding the warmup frames, but render them at the final size
            // so TAA/RT history is not discarded on the first encoded frame.
            controllerSettings.SetRecordModeToFrameInterval(warmup, warmup + count - 1);
            controller = new RecorderController(controllerSettings);
            // Recorder advances its frame clock through WaitForEndOfFrame.
            // A hidden Game View stalls that coroutine while Update and the
            // VMD clock keep running, even with an offscreen camera target.
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                throw new InvalidOperationException("Unity Game View is unavailable for Recorder.");
            var gameView = EditorWindow.GetWindow(gameViewType);
            gameView.Show();
            gameView.Focus();
            controller.PrepareRecording();
            if (!controller.StartRecording()) throw new InvalidOperationException("Unity Recorder did not start.");
            started = true;
        }
        catch (Exception e) { Finish(e); }
    }

    void ApplyPartToggles()
    {
        if ((request.enableParts == null || request.enableParts.Length == 0) &&
            (request.disableParts == null || request.disableParts.Length == 0)) return;
        Transform root = sequence.Retargeter != null && sequence.Retargeter.TargetSpace != null
            ? sequence.Retargeter.TargetSpace
            : sequence.Body != null ? sequence.Body.ModelRoot : null;
        Transform parts = null;
        for (; root != null && parts == null; root = root.parent) parts = root.Find("Parts");
        if (parts == null)
            throw new InvalidOperationException("The recording character has no Parts group.");
        Apply(request.disableParts, false);
        Apply(request.enableParts, true);

        void Apply(string[] names, bool active)
        {
            foreach (string name in names ?? Array.Empty<string>())
            {
                Transform part = parts.Find(name);
                if (part == null)
                    throw new InvalidOperationException("Character Parts child not found: " + name);
                part.gameObject.SetActive(active);
                if (!toggledParts.Contains(part.gameObject)) toggledParts.Add(part.gameObject);
            }
        }
    }

    void Pump()
    {
        if (!finished) EditorApplication.QueuePlayerLoopUpdate();
    }

    void OnCameraRendered(ScriptableRenderContext context, Camera camera)
    {
        if (finished || camera == null) return;
        int id = camera.GetInstanceID();
        if (!cameraRenders.TryGetValue(id, out CameraRenderCount countForCamera))
            cameraRenders[id] = countForCamera = new CameraRenderCount {
                name = camera.name, type = camera.cameraType.ToString(), componentEnabled = camera.enabled
            };
        ++countForCamera.renderedFrames;
        if (camera != captureCamera || captureCameraData == null) return;
        var screen = ScreenProcessRuntimeDiagnostics.CurrentSnapshot;
        if (screen.IsValid && screen.FrameCount == Time.frameCount && screen.CameraName == camera.name)
        {
            if (screen.Ready) ++screenProcessFrames;
            screenLayers = screen.WrittenLayerCount;
            screenReason = screen.Reason;
        }
        var image = ImageProcessRuntimeDiagnostics.CurrentSnapshot;
        if (image.IsValid && image.FrameCount == Time.frameCount && image.CameraName == camera.name)
        {
            if (image.Ready) ++imageProcessFrames;
            imageLayers = image.WrittenLayerCount;
            imageReason = image.Reason;
        }
        if (captureCameraData.antialiasing != AntialiasingMode.TemporalAntiAliasing) return;
        try
        {
            var history = captureCameraData.history?.GetHistoryForRead<TaaHistory>();
            var texture = history?.GetAccumulationTexture()?.rt;
            if (texture == null) return;
            taaHistoryWidth = texture.width;
            taaHistoryHeight = texture.height;
            int version = history.GetAccumulationVersion();
            if (version >= 0 && version != lastTaaVersion)
            {
                lastTaaVersion = version;
                ++taaHistoryUpdates;
                if (taaHistoryWidth == request.width && taaHistoryHeight == request.height)
                    taaVerified = true;
            }
        }
        catch (Exception error) { renderFailure = error.ToString(); }
    }

    void Update()
    {
        if (finished || request == null) return;
        try
        {
            if (File.Exists(Path.Combine(work, "cancel.txt")))
            {
                File.Delete(Path.Combine(work, "cancel.txt"));
                throw new OperationCanceledException("Recording canceled by CLI.");
            }
            if (EditorApplication.timeSinceStartup - began > 5400) throw new TimeoutException("Video capture exceeded 90 minutes.");
            if (renderFailure != null) throw new InvalidOperationException(renderFailure);
            foreach (var item in sequences)
                item.ExternalTime = request.handStudy ? (tick - warmup < request.fps * 4 ? 0 : 4) :
                    Mathf.Max(0, tick - warmup) / (float)request.fps;
            if (tick == warmup)
            {
                lastStudyStage = -1;
            }
            if (tick > warmup + 2 && captureCameraData.antialiasing == AntialiasingMode.TemporalAntiAliasing &&
                !taaVerified)
                throw new InvalidOperationException("Requested TAA has no advancing history at the output resolution. " +
                    "Check the camera stack, MSAA, render scale and motion-vector support.");
            if (started && tick > warmup + 2 && !controller.IsRecording()) { Finish(null); return; }
            if (tick % 30 == 0)
                File.WriteAllText(Path.Combine(work, "progress.json"), "{\"frame\":" + Mathf.Max(0, tick - warmup) +
                    ",\"total\":" + count + ",\"output\":\"" + request.output.Replace("\\", "/") + "\"}");
            ++tick;
        }
        catch (Exception e) { Finish(e); }
    }

    void LateUpdate()
    {
        if (finished || request == null || !request.handStudy || captureCamera == null) return;
        int stage = Mathf.Clamp((tick - warmup - 1) / request.fps, 0, 7);
        bool wtd = stage % 2 == 0;
        Transform model = wtd ? sequence.Retargeter.TargetSpace :
            sequences.First(x => x != sequence).Body.ModelRoot;
        Animator animator = model.GetComponent<Animator>();
        Transform hand = wtd ? animator.GetBoneTransform(HumanBodyBones.LeftHand) :
            model.GetComponentsInChildren<Transform>().First(x => x.name == "左手首");
        Transform middle = wtd ? animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) :
            model.GetComponentsInChildren<Transform>().First(x => x.name == "左中指１");
        Transform index = wtd ? animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal) :
            model.GetComponentsInChildren<Transform>().First(x => x.name == "左人指１");
        Transform pinky = wtd ? animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal) :
            model.GetComponentsInChildren<Transform>().First(x => x.name == "左小指１");
        Vector3 normal = Vector3.Cross(middle.position - hand.position, index.position - pinky.position).normalized;
        Vector3 center = Vector3.Lerp(hand.position, middle.position, .65f);
        Vector3 direction = stage % 4 < 2 ? Vector3.forward : normal;
        captureCamera.transform.position = center + direction * .32f;
        captureCamera.transform.LookAt(center, stage % 4 < 2 ? Vector3.up : (middle.position - hand.position).normalized);
        captureCamera.fieldOfView = 35;
        if (stage == lastStudyStage) return;
        lastStudyStage = stage;
        var audit = new HandAudit { stage = stage, model = model.name,
            mapped = sequence.Retargeter.Bones.Where(x => x.target.name.Contains("arm") || x.target.name.Contains("hand") ||
                x.target.name.Contains("index") || x.target.name.Contains("thumb") || x.target.name.Contains("middle") ||
                x.target.name.Contains("ring") || x.target.name.Contains("little")).Select(x =>
            {
                Quaternion expected = sequence.Retargeter.TargetSpace.rotation *
                    Quaternion.Inverse(sequence.Retargeter.SourceSpace.rotation) * x.source.rotation * x.rotationOffset;
                return new BoneAudit { name = x.target.name, source = x.source.name, position = x.target.position,
                    expectedEuler = expected.eulerAngles, actualEuler = x.target.eulerAngles,
                    angularError = Quaternion.Angle(expected, x.target.rotation) };
            }).ToArray(),
            deform = sequence.Retargeter.TargetSpace.GetComponentsInChildren<Transform>(true)
                .Where(x => x.name.Contains("hand.L") || x.name.Contains("index_proximal.L") || x.name.Contains("thumb_proximal.L"))
                .Select(x => new DeformAudit { name = x.name, parent = x.parent != null ? x.parent.name : "",
                    position = x.position, euler = x.eulerAngles }).ToArray(),
            skin = AuditSkin(sequence.Retargeter.TargetSpace),
            sourceBones = sequences.SelectMany(s => s.Body.ModelRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("左", StringComparison.Ordinal) &&
                    (t.name.Contains("指") || t.name.Contains("手首") || t.name.Contains("ひじ") || t.name == "左腕"))
                .Select(t => new DeformAudit { name = s.Body.ModelRoot.name + "::" + t.name,
                    parent = t.parent != null ? t.parent.name : "",
                    position = s.Body.ModelRoot.InverseTransformPoint(t.position),
                    euler = (Quaternion.Inverse(s.Body.ModelRoot.rotation) * t.rotation).eulerAngles })).ToArray(),
            behaviours = sequence.Retargeter.TargetSpace.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(x => x != null && x.isActiveAndEnabled && x.GetType().Namespace?.Contains("Hollow") == true)
                .Select(x => x.GetType().FullName + " :: " + JsonUtility.ToJson(x)).ToArray() };
        File.WriteAllText(Path.GetFullPath(request.output) + "-stage" + stage + ".json", JsonUtility.ToJson(audit, true));
    }

    SkinAudit[] AuditSkin(Transform root)
    {
        var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(x => x.name == "Body1");
        var weights = renderer.sharedMesh.boneWeights;
        var bones = renderer.bones;
        var sums = new float[bones.Length];
        var points = new Vector3[bones.Length];
        var counts = new int[bones.Length];
        var baked = new Mesh();
        renderer.BakeMesh(baked);
        var positions = baked.vertices;
        for (int v = 0; v < weights.Length; ++v)
        {
            var w = weights[v];
            int[] indices = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
            float[] values = { w.weight0, w.weight1, w.weight2, w.weight3 };
            for (int k = 0; k < 4; ++k)
            {
                int index = indices[k];
                if (index < 0 || index >= sums.Length) continue;
                sums[index] += values[k];
                if (values[k] > .95f) { points[index] += renderer.transform.TransformPoint(positions[v]); ++counts[index]; }
            }
        }
        Object.Destroy(baked);
        return Enumerable.Range(0, bones.Length).Where(i => bones[i] != null && sums[i] > .001f &&
            (bones[i].name.Contains("hand.L") || bones[i].name.Contains("index") && bones[i].name.EndsWith(".L") ||
                bones[i].name.Contains("middle") && bones[i].name.EndsWith(".L") ||
                bones[i].name.Contains("thumb") && bones[i].name.EndsWith(".L")))
            .Select(i => new SkinAudit { bone = bones[i].name, parent = bones[i].parent != null ? bones[i].parent.name : "",
                weight = sums[i], position = bones[i].position, tipVertices = counts[i],
                tipCentroid = counts[i] > 0 ? points[i] / counts[i] : Vector3.zero }).ToArray();
    }

    void Finish(Exception error)
    {
        if (finished) return;
        finished = true;
        try { controller?.StopRecording(); }
        finally
        {
            Time.captureDeltaTime = previousCaptureDelta;
            Application.runInBackground = previousBackground;
            EditorApplication.update -= Pump;
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            HoPlaySpeedControl.Release(this);
            if (captureCamera != null)
            {
                captureCamera.targetTexture = previousCameraTarget;
                captureCamera.enabled = previousCameraEnabled;
                captureCamera.aspect = previousCameraAspect;
            }
            if (recordingTarget != null)
            {
                recordingTarget.Release();
                Object.Destroy(recordingTarget);
                recordingTarget = null;
            }
        }
        File.WriteAllText(Path.GetFullPath(request.output) + ".render.json", JsonUtility.ToJson(new CaptureReport {
            width = request.width, height = request.height, fps = request.fps, frames = count,
            warmupFrames = warmup, antialiasing = captureCameraData != null ? captureCameraData.antialiasing.ToString() : "",
            taaVerified = taaVerified, taaHistoryWidth = taaHistoryWidth, taaHistoryHeight = taaHistoryHeight,
            taaHistoryUpdates = taaHistoryUpdates, qualityPreset = request.qualityPreset, aspectPreset = request.aspectPreset,
            postStack = request.postStack, postProcessing = captureCameraData != null && captureCameraData.renderPostProcessing,
            postExecution = HoPostProcessingDiagnostics.TryGet(captureCamera, out var postExecution) ? postExecution : default,
            volumeMask = captureCameraData != null ? captureCameraData.volumeLayerMask.value : 0,
            screenProcessFrames = screenProcessFrames, imageProcessFrames = imageProcessFrames,
            screenLayers = screenLayers, imageLayers = imageLayers, screenReason = screenReason, imageReason = imageReason,
            cameraRenders = cameraRenders.Values.ToArray(),
            partStates = toggledParts.Where(x => x != null).Select(x =>
                new PartState { name = x.name, active = x.activeSelf }).ToArray() }, true));
        File.WriteAllText(Path.Combine(work, "result.json"), JsonUtility.ToJson(new HoMmdVideoCaptureResponse {
            id = request?.id, status = error == null ? "ok" : "error",
            result = error == null ? Path.GetFullPath(request.output) + ".mp4" : error.ToString() }, true));
        if (error != null) Debug.LogException(error);
        EditorApplication.isPlaying = false;
    }
    void OnDisable() { if (!finished && request != null) Finish(new OperationCanceledException("Play mode ended during recording.")); }
    [Serializable] sealed class CaptureReport
    {
        public int width, height, fps, frames, warmupFrames, taaHistoryWidth, taaHistoryHeight, taaHistoryUpdates;
        public string antialiasing;
        public bool taaVerified;
        public string qualityPreset, aspectPreset, postStack, screenReason, imageReason;
        public bool postProcessing;
        public HoPostProcessingExecution postExecution;
        public int volumeMask, screenProcessFrames, imageProcessFrames, screenLayers, imageLayers;
        public CameraRenderCount[] cameraRenders;
        public PartState[] partStates;
    }
    [Serializable] sealed class PartState { public string name; public bool active; }
    [Serializable] sealed class CameraRenderCount
    {
        public string name, type;
        public bool componentEnabled;
        public int renderedFrames;
    }
}
#endif
