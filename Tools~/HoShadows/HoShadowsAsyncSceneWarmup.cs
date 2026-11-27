using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>Captures one configured Ho Shadows comparison arm from a Windows batch Editor.</summary>
public static class HoShadowsAsyncSceneWarmup
{
    const int Width = 640, Height = 360;
    const float StablePixelDelta = .01f;
    const float MaximumChangedFraction = .005f;

    [Serializable]
    public sealed class Result
    {
        public string arm, scene, camera, graphics, status, error;
        public int rendererIndex, submissions, signalPixels, changedPixels;
        public float deadlineSeconds, elapsedSeconds, maximumDelta, meanDelta;
        public bool reframedSkin, temporallyStable;
        public Vector3 cameraPosition;
        public float fieldOfView;
    }

    public static void Run()
    {
        RequireProject();
        string scenePath = System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_SCENE");
        if (string.IsNullOrEmpty(scenePath) || !scenePath.StartsWith("Assets/", StringComparison.Ordinal)
            || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set HO_SHADOWS_WARMUP_SCENE to the selected project-relative scene.");
        string sceneFile = Path.GetFullPath(Path.Combine(Application.dataPath, scenePath.Substring("Assets/".Length)));
        if (!sceneFile.StartsWith(Application.dataPath.Replace('/', '\\') + "\\", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(sceneFile) || File.GetAttributes(sceneFile).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Use a physical scene file in the isolated project's Assets directory.");
        RequirePhysicalDirectory(Path.GetDirectoryName(sceneFile));

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        string cameraName = System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_CAMERA");
        Camera camera = string.IsNullOrEmpty(cameraName) ? Camera.main : scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single(item => item.name == cameraName);
        if (camera == null) throw new InvalidOperationException("No selected camera in " + scenePath);
        var cameraData = camera.GetUniversalAdditionalCameraData();
        int rendererIndex = ReadInt("HO_SHADOWS_WARMUP_RENDERER_INDEX", -1);
        if (rendererIndex >= 0) cameraData.SetRenderer(rendererIndex);

        bool reframe = System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_FRAME_SKIN") == "1";
        if (reframe) FrameSkinSubject(scene, camera);
        bool allowTemporal = System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_ALLOW_TEMPORAL") == "1";
        Capture(camera, System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_OUTPUT"),
            System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_ARM"), rendererIndex, reframe,
            ReadInt("HO_SHADOWS_WARMUP_SECONDS", 360),
            result => EditorApplication.Exit(result.status == "settled" || result.status == "rendered_temporal" ? 0 : 1),
            allowTemporal);
    }

    // Use this entry point after the caller has selected its renderer and prepared material previews.
    public static void Capture(Camera camera, string output, string arm, int rendererIndex, bool reframedSkin,
        int deadlineSeconds, Action<Result> finished, bool allowTemporal = false)
    {
        RequireProject();
        if (camera == null || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base)
            throw new InvalidOperationException("Select a URP Base camera.");
        if (string.IsNullOrWhiteSpace(arm) || arm == "." || arm == ".." || arm.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Use a valid, unique arm name.");
        if (deadlineSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(deadlineSeconds), "Use a positive deadline in seconds.");
        output = RequirePhysicalDirectory(output);
        foreach (string suffix in new[] { ".json", ".exr", "-repeat.exr" })
            if (File.Exists(Path.Combine(output, arm + suffix)))
                throw new InvalidOperationException("The scene capture output already contains arm " + arm);
        new CaptureJob(camera, output, arm, rendererIndex, reframedSkin, deadlineSeconds, finished, allowTemporal).Start();
    }

    sealed class CaptureJob
    {
        readonly Camera camera;
        readonly string output;
        readonly Result result;
        readonly Action<Result> finished;
        readonly bool allowTemporal;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly RenderTexture target;
        readonly RenderPipeline.StandardRequest request;
        readonly bool previousAsync;
        readonly float previousAspect;
        readonly HoDebugTileRendererFeature[] activeHuds;
        Color[] previousPixels;
        byte[] previousExr;
        double nextPoll, lastHeartbeat;

        internal CaptureJob(Camera camera, string output, string arm, int rendererIndex, bool reframedSkin,
            int deadlineSeconds, Action<Result> finished, bool allowTemporal)
        {
            this.camera = camera;
            this.output = output;
            this.finished = finished;
            this.allowTemporal = allowTemporal;
            previousAsync = ShaderUtil.allowAsyncCompilation;
            previousAspect = camera.aspect;
            // Suppress the feature-count HUD on the actual camera renderer, including a separate pilot renderer.
            var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            var features = renderer == null ? null : typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(renderer) as System.Collections.Generic.IEnumerable<ScriptableRendererFeature>;
            if (features == null) throw new InvalidOperationException("Could not inspect the camera renderer's features.");
            activeHuds = features.OfType<HoDebugTileRendererFeature>().Where(hud => hud.isActive).ToArray();
            result = new Result {
                arm = arm, scene = camera.gameObject.scene.path, camera = camera.name, rendererIndex = rendererIndex,
                reframedSkin = reframedSkin, deadlineSeconds = deadlineSeconds, graphics = SystemInfo.graphicsDeviceType.ToString(),
                cameraPosition = camera.transform.position, fieldOfView = camera.fieldOfView
            };
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            request = new RenderPipeline.StandardRequest { destination = target };
        }

        internal void Start()
        {
            if (!RenderPipeline.SupportsRenderRequest(camera, request) || !target.Create())
            {
                Finish("error", "Camera render request or float target is unavailable.");
                return;
            }
            foreach (var hud in activeHuds) hud.SetActive(false);
            camera.aspect = Width / (float)Height;
            ShaderUtil.allowAsyncCompilation = true;
            Trace("opened " + result.scene + " via " + result.camera);
            nextPoll = clock.Elapsed.TotalSeconds + 1;
            EditorApplication.update += Tick;
        }

        void Tick()
        {
            try
            {
                double elapsed = clock.Elapsed.TotalSeconds;
                if (elapsed >= result.deadlineSeconds) { Finish("deadline"); return; }
                if (elapsed < nextPoll) return;
                nextPoll = elapsed + 1;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (ShaderUtil.anythingCompiling)
                {
                    previousPixels = null;
                    previousExr = null;
                    if (elapsed - lastHeartbeat >= 10)
                    {
                        lastHeartbeat = elapsed;
                        Trace("shader queue busy");
                    }
                    return;
                }

                int submission = ++result.submissions;
                Trace($"submit {submission} begin");
                var watch = Stopwatch.StartNew();
                RenderPipeline.SubmitRenderRequest(camera, request);
                bool pending = ShaderUtil.anythingCompiling;
                Trace($"submit {submission} returned in {watch.Elapsed.TotalMilliseconds:F1} ms, compiling={pending}");
                if (pending)
                {
                    previousPixels = null;
                    previousExr = null;
                    return;
                }

                var image = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
                var oldTarget = RenderTexture.active;
                bool havePair;
                try
                {
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply(false);
                    var pixels = image.GetPixels();
                    result.signalPixels = SceneSignalPixels(pixels);
                    if (result.signalPixels < 128)
                    {
                        previousPixels = null;
                        previousExr = null;
                        Trace("waiting for scene color");
                        return;
                    }
                    byte[] exr = image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                    havePair = previousPixels != null && MeasureDelta(previousPixels, pixels)
                        && (result.temporallyStable || allowTemporal);
                    if (havePair)
                    {
                        File.WriteAllBytes(Path.Combine(output, result.arm + ".exr"), previousExr);
                        File.WriteAllBytes(Path.Combine(output, result.arm + "-repeat.exr"), exr);
                    }
                    previousPixels = pixels;
                    previousExr = exr;
                }
                finally { RenderTexture.active = oldTarget; UnityEngine.Object.DestroyImmediate(image); }
                if (havePair) Finish(result.temporallyStable ? "settled" : "rendered_temporal");
            }
            catch (Exception exception) { Finish("error", exception.ToString()); }
        }

        bool MeasureDelta(Color[] first, Color[] second)
        {
            int changed = 0;
            float maximum = 0;
            double total = 0;
            for (int i = 0; i < first.Length; i++)
            {
                float delta = MaxRgb(first[i] - second[i]);
                if (float.IsNaN(delta) || float.IsInfinity(delta)) return false;
                total += delta;
                maximum = Mathf.Max(maximum, delta);
                if (delta > StablePixelDelta) changed++;
            }
            result.changedPixels = changed;
            result.maximumDelta = maximum;
            result.meanDelta = (float)(total / first.Length);
            result.temporallyStable = changed <= first.Length * MaximumChangedFraction;
            return true;
        }

        void Finish(string status, string error = null)
        {
            if (result.status != null) return;
            EditorApplication.update -= Tick;
            ShaderUtil.allowAsyncCompilation = previousAsync;
            if (camera != null) camera.aspect = previousAspect;
            foreach (var hud in activeHuds) if (hud != null) hud.SetActive(true);
            UnityEngine.Object.DestroyImmediate(target);
            result.status = status;
            result.error = error;
            result.elapsedSeconds = (float)clock.Elapsed.TotalSeconds;
            Trace(status + (error == null ? string.Empty : "  " + error));
            File.WriteAllText(Path.Combine(output, result.arm + ".json"), JsonUtility.ToJson(result, true));
            finished?.Invoke(result);
        }

        void Trace(string message)
        {
            string line = $"{clock.Elapsed.TotalSeconds:F2}s {message}";
            File.AppendAllText(Path.Combine(output, result.arm + ".progress.txt"), line + System.Environment.NewLine);
            Debug.Log("HO_SHADOWS_ASYNC " + result.arm + " " + line);
        }
    }

    static int SceneSignalPixels(Color[] pixels)
    {
        int count = 0;
        for (int y = Height / 4; y < Height * 3 / 4; y++)
        {
            Color scanline = pixels[y * Width];
            for (int x = Width / 4; x < Width * 3 / 4; x++)
                if (MaxRgb(pixels[y * Width + x] - scanline) > .01f) count++;
        }
        return count;
    }

    static float MaxRgb(Color value) => Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g), Mathf.Abs(value.b));

    static void FrameSkinSubject(Scene scene, Camera camera)
    {
        var subjects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SkinnedMeshRenderer>())
            .Where(mesh => mesh.enabled && mesh.sharedMaterials.Any(material => material != null && material.shader != null
                && material.shader.name == "Ho/Skin/URP/Stylized")).ToArray();
        if (camera.orthographic || subjects.Length == 0)
            throw new InvalidOperationException("Reframing requires a perspective camera and an enabled Skin subject.");
        Bounds bounds = subjects[0].bounds;
        foreach (var subject in subjects.Skip(1)) bounds.Encapsulate(subject.bounds);
        if (bounds.extents.sqrMagnitude <= .001f) throw new InvalidOperationException("The Skin subject has no usable bounds.");
        camera.fieldOfView = Mathf.Max(camera.fieldOfView, 25f);
        float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) + camera.nearClipPlane;
        camera.transform.position = bounds.center - camera.transform.forward * distance;
        camera.useOcclusionCulling = false;
    }

    static int ReadInt(string name, int fallback)
    {
        string value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : int.Parse(value, CultureInfo.InvariantCulture);
    }

    static void RequireProject()
    {
        if (!Application.isBatchMode || Application.platform != RuntimePlatform.WindowsEditor)
            throw new InvalidOperationException("Use a Windows batch Editor with graphics enabled.");
        string expected = RequirePhysicalDirectory(System.Environment.GetEnvironmentVariable("HO_SHADOWS_WARMUP_PROJECT"));
        string actual = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\');
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current project does not match HO_SHADOWS_WARMUP_PROJECT.");
    }

    static string RequirePhysicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Supply an existing isolated directory under D:/Temp.");
        path = Path.GetFullPath(path).TrimEnd('\\');
        if (!path.StartsWith(@"D:\Temp\", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path))
            throw new InvalidOperationException("Use a physical directory under D:/Temp.");
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Use a physical directory, without reparse points in its path.");
        return path;
    }
}
