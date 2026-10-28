using System;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Explicit, scene-isolated capture session. Nothing runs until Begin is called.
/// Each native SRP render request is separated by an editor/player-loop update.
/// </summary>
public static class HoMmdStageCapture
{
    private static Session session;
    public static bool Busy => session != null;
    public static string Status => session != null ? session.status : "空闲";

    public static void Begin(string scenePath, string outputPath, int width, int height,
        Action<string> completed, Action<Exception> failed, Action<Camera> configure = null, int warmupFrames = 4)
    {
        if (Busy) throw new InvalidOperationException("已有 MMD 场景截帧正在执行。");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("请在编辑器导入/编译/播放结束后截帧。");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("截帧需要图形设备，不能使用 -nographics。");
        if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("场景文件不存在：" + scenePath);
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("需要输出 PNG 路径。");
        if (width < 16 || height < 16 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width), "截帧尺寸不受当前设备支持。");
        foreach (Scene loaded in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            if (loaded.isDirty)
                throw new InvalidOperationException("检测到未保存场景，截帧不会自动保存或丢弃它：" + loaded.name);

        var next = new Session(scenePath, outputPath, width, height, completed, failed, configure,
            Mathf.Clamp(warmupFrames, 1, 32));
        session = next;
        try { next.Start(); }
        catch (Exception error) { next.Finish(error); }
    }

    public static void Cancel()
    {
        if (session != null) session.Finish(new OperationCanceledException("场景截帧已取消。"));
    }

    private sealed class Session
    {
        private enum Phase { Settle, Warmup, Readback, Complete }
        private readonly string scenePath, outputPath;
        private readonly int width, height, warmupFrames;
        private readonly Action<string> completed;
        private readonly Action<Exception> failed;
        private readonly Action<Camera> configure;
        private readonly SceneSetup[] previousSetup;
        private readonly RenderPipelineAsset previousPipeline;
        private readonly double began;
        private Scene captureScene;
        private Camera camera;
        private HoMmdStageEnvironment environment;
        private RenderTexture target;
        private Texture2D readback;
        private AsyncGPUReadbackRequest gpuReadback;
        private bool asyncReadback, changedScene, disposed;
        private int stageReflectionSurfaces;
        private Phase phase;
        private int ticks, frames, lastFrame = -1;
        private string renderError;
        internal string status;

        internal Session(string scenePath, string outputPath, int width, int height,
            Action<string> completed, Action<Exception> failed, Action<Camera> configure, int warmupFrames)
        {
            this.scenePath = scenePath;
            this.outputPath = Path.GetFullPath(outputPath);
            this.width = width; this.height = height;
            this.completed = completed; this.failed = failed; this.configure = configure;
            this.warmupFrames = warmupFrames;
            previousSetup = EditorSceneManager.GetSceneManagerSetup();
            previousPipeline = QualitySettings.renderPipeline;
            began = EditorApplication.timeSinceStartup;
            status = "准备场景";
        }

        internal void Start()
        {
            // Scene culling masks do not isolate custom global registries such as
            // HoAtmosphereRegionRegistry. Single-scene capture isolates both lights and regions.
            captureScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            changedScene = true;
            if (!captureScene.IsValid() || !captureScene.isLoaded || SceneManager.GetActiveScene() != captureScene)
                throw new InvalidOperationException("无法激活截帧场景：" + scenePath);
            var roots = captureScene.GetRootGameObjects();
            stageReflectionSurfaces=roots.SelectMany(x=>x.GetComponentsInChildren<lilToon.URP.Extensions.PlanarReflection.HoPlanarReflectionSurface>()).Count(x=>x.enabled);
            environment = roots.SelectMany(x => x.GetComponentsInChildren<HoMmdStageEnvironment>(true)).FirstOrDefault(x => x.isActiveAndEnabled);
            if (environment == null || environment.pipeline == null)
                throw new InvalidOperationException("场景缺少有效的 HoMmdStageEnvironment/pipeline。");
            camera = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true))
                .Where(x => x.gameObject.activeInHierarchy)
                .OrderByDescending(x => x.CompareTag("MainCamera")).ThenBy(x => x.depth).FirstOrDefault();
            if (camera == null) throw new InvalidOperationException("场景缺少相机。");
            foreach (var item in roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true))) item.enabled = false;
            configure?.Invoke(camera);
            var extra = camera.GetUniversalAdditionalCameraData();
            if (extra.renderType != CameraRenderType.Base)
                throw new InvalidOperationException("截帧相机必须是 URP Base 相机。");
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(captureScene);
            extra.resetHistory = true;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default)
            {
                name = "HoMmdStageCapture", antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create()) throw new InvalidOperationException("无法创建截帧 RenderTexture。");
            camera.targetTexture = target;
            environment.ApplyNow();
            phase = Phase.Settle;
            status = "等待场景与渲染管线切换";
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Reloading;
            EditorApplication.quitting += Quitting;
            Application.logMessageReceived += Log;
            Pump();
        }

        private void Tick()
        {
            if (disposed) return;
            File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"capture-status.json"),
                "{\"time\":\""+DateTime.UtcNow.ToString("O")+"\",\"phase\":\""+phase+"\",\"frames\":"+frames+",\"status\":\""+status+"\"}");
            try
            {
                if (EditorApplication.timeSinceStartup - began > 90)
                    throw new TimeoutException("场景截帧超过 90 秒：" + status);
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Pump(); return; }
                if (camera == null || !captureScene.IsValid() || SceneManager.GetActiveScene() != captureScene)
                    throw new InvalidOperationException("截帧期间相机或活动场景发生变化。");
                if (renderError != null) throw new InvalidOperationException("渲染报告错误：" + renderError);
                switch (phase)
                {
                    case Phase.Settle:
                        if (++ticks < 3) break;
                        if (GraphicsSettings.currentRenderPipeline != environment.pipeline ||
                            QualitySettings.renderPipeline != environment.pipeline)
                            throw new InvalidOperationException("活动渲染管线不匹配场景配置。");
                        var request = new RenderPipeline.StandardRequest { destination = target };
                        if (!RenderPipeline.SupportsRenderRequest(camera, request))
                            throw new NotSupportedException("当前 SRP 不支持 StandardRequest。");
                        phase = Phase.Warmup;
                        status = "预热 0/" + warmupFrames;
                        break;
                    case Phase.Warmup:
                        // Do not fake temporal warmup by rendering multiple times in one frame.
                        if (lastFrame == Time.frameCount) break;
                        lastFrame = Time.frameCount;
                        var warmup = new RenderPipeline.StandardRequest { destination = target };
                        RenderPipeline.SubmitRenderRequest(camera, warmup);
                        var stack=VolumeManager.instance.stack;
                        var bloom=stack.GetComponent<Bloom>();
                        var adjustments=stack.GetComponent<ColorAdjustments>();
                        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"look-status.json"),
                            "{\"bloom\":"+bloom.intensity.value.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                            ",\"exposure\":"+adjustments.postExposure.value.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                            ",\"postProcessing\":"+camera.GetUniversalAdditionalCameraData().renderPostProcessing.ToString().ToLowerInvariant()+"}");
                        var plr=lilToon.URP.Extensions.PlanarReflection.HoPlanarReflectionRuntimeDiagnostics.CurrentSnapshot;
                        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,"reflection-status.json"),
                            "{\"ready\":"+plr.Ready.ToString().ToLowerInvariant()+",\"reason\":\""+plr.Reason+"\",\"surfaceCount\":"+plr.SurfaceCount+"}");
                        File.WriteAllText(Path.Combine(HoMmdStageWork.Work,Path.GetFileNameWithoutExtension(scenePath)+"-render-state.json"),
                            "{\"reflectionReady\":"+(stageReflectionSurfaces>0&&plr.Ready).ToString().ToLowerInvariant()+",\"reason\":\""+(stageReflectionSurfaces>0?plr.Reason:"本场景未配置地面反射")+
                            "\",\"surfaces\":"+stageReflectionSurfaces+",\"targetSrgb\":"+target.sRGB.ToString().ToLowerInvariant()+
                            ",\"exposure\":"+adjustments.postExposure.value.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                            ",\"bloom\":"+bloom.intensity.value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
                        if (renderError != null) throw new InvalidOperationException("渲染报告错误：" + renderError);
                        status = "预热 " + (++frames) + "/" + warmupFrames;
                        if (frames >= warmupFrames)
                        {
                            phase = Phase.Readback;
                            asyncReadback = SystemInfo.supportsAsyncGPUReadback;
                            if (asyncReadback) gpuReadback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                            status = "等待 GPU 像素读回";
                        }
                        break;
                    case Phase.Readback:
                        if (asyncReadback && !gpuReadback.done) break;
                        readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                        if (asyncReadback)
                        {
                            if (gpuReadback.hasError) throw new InvalidOperationException("GPU 异步像素读回失败。");
                            readback.LoadRawTextureData(gpuReadback.GetData<byte>());
                            readback.Apply(false, false);
                        }
                        else
                        {
                            var previousActive = RenderTexture.active;
                            try
                            {
                                RenderTexture.active = target;
                                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                                readback.Apply(false, false);
                            }
                            finally { RenderTexture.active = previousActive; }
                        }
                        phase = Phase.Complete;
                        var directory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                        // The HDR render target is linear and URP does not encode sRGB for
                        // target textures. PNG is display-referred; encode once, after tone
                        // mapping, retaining float precision in the shadows until this step.
                        var pixels=readback.GetPixels();
                        bool encode=QualitySettings.activeColorSpace==ColorSpace.Linear&&!target.sRGB;
                        var bytes=new Color32[pixels.Length];
                        for(int i=0;i<pixels.Length;i++)bytes[i]=encode?pixels[i].gamma:pixels[i];
                        var png=new Texture2D(width,height,TextureFormat.RGBA32,false);
                        try{png.SetPixels32(bytes);png.Apply(false,false);File.WriteAllBytes(outputPath,png.EncodeToPNG());}
                        finally{Object.DestroyImmediate(png);}
                        Finish(null);
                        return;
                }
                Pump();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        private void Log(string message, string stack, LogType type)
        {
            if (phase == Phase.Warmup && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                renderError = message;
        }
        private void Reloading() { Finish(new OperationCanceledException("脚本重载取消了场景截帧。")); }
        private void Quitting() { Finish(new OperationCanceledException("编辑器退出取消了场景截帧。")); }

        internal void Finish(Exception error)
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Reloading;
            EditorApplication.quitting -= Quitting;
            Application.logMessageReceived -= Log;
            try
            {
                if (camera != null) camera.targetTexture = null;
                if (asyncReadback && !gpuReadback.done)
                {
                    // Keep the target alive until the outstanding native request completes.
                    var pending = target;
                    var pendingReadback = gpuReadback;
                    target = null;
                    EditorApplication.CallbackFunction release = null;
                    release = () =>
                    {
                        if (!pendingReadback.done) return;
                        EditorApplication.update -= release;
                        if (pending != null) { pending.Release(); Object.DestroyImmediate(pending); }
                    };
                    EditorApplication.update += release;
                }
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (changedScene)
                {
                    if (captureScene.IsValid() && captureScene.isLoaded) EditorSceneManager.CloseScene(captureScene, true);
                    if (previousSetup.Length != 0) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
                QualitySettings.renderPipeline = previousPipeline;
            }
            catch (Exception cleanup)
            {
                error = error == null ? cleanup : new AggregateException(error, cleanup);
            }
            finally
            {
                target = null; readback = null;
                if (ReferenceEquals(session, this)) session = null;
            }
            if (error == null) completed?.Invoke(outputPath);
            else if (failed != null) failed(error);
            else Debug.LogException(error);
        }
    }
}
