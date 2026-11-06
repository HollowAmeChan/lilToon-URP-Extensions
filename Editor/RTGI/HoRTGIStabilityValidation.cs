#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.NRD;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Explicit batch entry only. Never saves scenes, materials or Volume profiles.
    public static class HoRTGIStabilityValidation
    {
        [Serializable] sealed class ImageStats
        {
            public int valid, lit, nonfinite;
            public Vector3 mean, max;
            public double frameL1, frameMax;
        }
        [Serializable] sealed class Frame
        {
            public int index, unityFrame, signature, nrdDispatches, historyFrames;
            public uint sceneRevision;
            public bool temporal, reset;
            public ImageStats raw, filtered, signal, guideNormal, guideDepth;
        }
        [Serializable] sealed class Report
        {
            public bool pass, compatibility, temporal, spatial, gpuQualified;
            public string error, unity, graphics, device, scene, status, colorSpace, clock, sourceHead;
            public int width, height, warmup, measured, resets, temporalFrames;
            public int[] secondaryFlags;
            public int reservoirValid, reservoirHistory;
            public long texturePayloadBytes;
            public string[] textures, sceneRejections, materialRejections;
            public Vector3 rawMean, filteredMean;
            public double rawVariance, filteredVariance, rawFrameL1, filteredFrameL1;
            public List<Frame> frames = new();
            public HoRTGIGpuClock.Sample[] gpuSamples;
            public HoRTGIGpuClock.Summary[] gpuSummary;
        }
        [StructLayout(LayoutKind.Sequential)] struct Secondary
        {
            public Vector3 position;
            public uint normal, diffuse, specular;
            public Vector3 emission;
            public float pdf;
            public uint geometricNormal, flags;
        }
        [StructLayout(LayoutKind.Sequential)] struct Reservoir
        {
            public Vector3 position;
            public uint misc, radiance;
            public float weight;
            public uint normal;
            public float unused;
        }
        static readonly List<Object> owned = new();
        static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static string Arg(string name, string fallback = "")
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
        static string diagnostic;
        static void OnLog(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                diagnostic ??= text;
        }
        public static void RunEmptyGpu()
        {
            bool pass = false; string error = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIGpuProbe.compute");
                using var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 4);
                using var cmd = new CommandBuffer { name = "Empty host qualification (no AS)" };
                int kernel = shader.FindKernel("Burn");
                cmd.SetComputeIntParam(shader, "_Seed", 7); cmd.SetComputeIntParam(shader, "_Iterations", 0);
                cmd.SetComputeBufferParam(shader, kernel, "_Output", buffer);
                cmd.DispatchCompute(shader, kernel, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
                var request = AsyncGPUReadback.Request(buffer); request.WaitForCompletion();
                if (request.hasError) throw new Exception("Small no-AS compute readback failed.");
                var values = request.GetData<uint>();
                for (int i = 0; i < 64; i++) if (values[i] != i + 7) throw new Exception("Small compute mismatch.");
                pass = true;
            }
            catch (Exception e) { error = e.ToString(); }
            string path = Path.Combine(Application.dataPath, "../empty-gpu-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(new Qualification { pass = pass, error = error, graphics = SystemInfo.graphicsDeviceType.ToString() }, true));
            Debug.Log("[HoGIEmptyGpu] " + (pass ? "PASS " : "FAIL ") + error);
            EditorApplication.Exit(pass ? 0 : 1);
        }
        [Serializable] sealed class Qualification { public bool pass; public string error, graphics; }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only diagnostic.");
            Debug.Log("[HoGIStability] START");
            var report = new Report
            {
                unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
                device = SystemInfo.graphicsDeviceName, colorSpace = QualitySettings.activeColorSpace.ToString(),
                scene = Arg("-hoScene", "synthetic-emissive-patch"),
                sourceHead = Arg("-hoSourceHead", "unrecorded"),
                width = int.Parse(Arg("-hoWidth", "96")), height = int.Parse(Arg("-hoHeight", "96")),
                warmup = int.Parse(Arg("-hoWarmup", "96")), measured = int.Parse(Arg("-hoMeasured", "64")),
                temporal = Arg("-hoReuse", "both") != "none" && Arg("-hoReuse", "both") != "spatial",
                spatial = Arg("-hoReuse", "both") != "none" && Arg("-hoReuse", "both") != "temporal",
                compatibility = Arg("-hoCompatibility", "0") == "1",
                clock = "deterministic 60 Hz NRD sample time; one request per Editor update"
            };
            string output = Path.GetFullPath(Arg("-hoOutput", Path.Combine(Application.dataPath, "../stability.json")));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var oldDefault = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            Application.logMessageReceived += OnLog;
            ShaderUtil.allowAsyncCompilation = false;
            HoRTGIRendererFeature gi = null;
            HoRTGIGpuClock gpu = null;
            try
            {
                if (!HoRTScene.IsSupported || !SystemInfo.supportsRayTracing)
                    throw new InvalidOperationException("D3D12 DXR required.");
                if (report.width < 16 || report.height < 16 || report.measured < 2)
                    throw new ArgumentOutOfRangeException("Invalid measurement dimensions/count.");
                Camera camera;
                if (report.scene == "synthetic-emissive-patch")
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                    renderer.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>()));
                    var gb = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
                    gb.Settings.maxRenderQueue = (int)RenderQueue.GeometryLast;
                    renderer.rendererFeatures.Add(gb);
                    var sb = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
                    sb.Settings.enableSemanticLanes = sb.Settings.enableCorrelatedSemantics = false;
                    renderer.rendererFeatures.Add(sb);
                    var sky = Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());
                    sky.Settings.source = HoAtmosphereSource.Constant;
                    sky.Settings.constantColor = Color.black;
                    renderer.rendererFeatures.Add(sky);
                    gi = Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());
                    renderer.rendererFeatures.Add(gi);
                    var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                    pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                    GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
                    camera = Keep(new GameObject("Stability camera")).AddComponent<Camera>();
                    camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
                    camera.transform.position = new Vector3(0, 0, -3);
                    camera.cullingMask = 1;
                    var receiverMaterial = Quad("Receiver", Vector3.zero, new Vector2(2, 2), 0, Color.black);
                    if (Arg("-hoInactiveEmissionAnimation", "0") == "1")
                        receiverMaterial.SetVector("_EmissionBlink", new Vector4(1, 1, 0, 1));
                    Quad("Black bounce", new Vector3(0, 0, -1.1f), new Vector2(100, 100), 1, Color.black);
                    float emission = float.Parse(Arg("-hoEmission", "12"), System.Globalization.CultureInfo.InvariantCulture);
                    Quad("Offscreen emitter", new Vector3(.6f, .1f, -1), new Vector2(.4f, .6f), 1,
                        new Color(emission, emission / 4, emission / 16, 1));
                }
                else
                {
                    EditorSceneManager.OpenScene(report.scene, OpenSceneMode.Single);
                    camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
                    if (!camera) throw new InvalidOperationException("Scene has no camera.");
                    // Controlled producer-only pipeline; authored geometry, materials,
                    // camera, lights, sky and RTGI Volume exclusions remain the inputs.
                    var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                    renderer.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>()));
                    var gb = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
                    gb.Settings.maxRenderQueue = (int)RenderQueue.GeometryLast;
                    renderer.rendererFeatures.Add(gb);
                    var sb = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
                    sb.Settings.enableSemanticLanes = sb.Settings.enableCorrelatedSemantics = false;
                    renderer.rendererFeatures.Add(sb);
                    renderer.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>()));
                    gi = Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());
                    renderer.rendererFeatures.Add(gi);
                    var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                    pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                    GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
                }
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = report.compatibility;
                #else
                if (report.compatibility) throw new InvalidOperationException("Compatibility symbol required.");
                #endif
                gi.Settings.temporal = report.temporal; gi.Settings.spatial = report.spatial;
                gi.Settings.denoise = true;
                // Highest-priority, unsaved override; authored profiles stay untouched.
                var holder = Keep(new GameObject("Unsaved stability diagnostic Volume")).AddComponent<Volume>();
                holder.isGlobal = true; holder.priority = float.MaxValue;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                var volume = holder.sharedProfile.Add<HoRTGIVolume>();
                volume.enable.Override(true); volume.denoise.Override(true);
                volume.debugMode.Override(HoRTGIDebugMode.Off);
                camera.enabled = false; camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                var target = Keep(new RenderTexture(report.width, report.height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                target.Create();
                var image = Keep(new Texture2D(report.width, report.height, TextureFormat.RGBAFloat, false, true));
                var sums = new double[report.width * report.height];
                var squares = new double[sums.Length];
                var fsums = new double[sums.Length];
                var fsquares = new double[sums.Length];
                Color[] previousRaw = null, previousFiltered = null;
                int index = 0;
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    try
                    {
                        if (diagnostic != null) throw new InvalidOperationException(diagnostic);
                        if (gi.LastContext?.nrd != null)
                        {
                            gi.LastContext.nrd.ValidationTimeSeconds = index / 60.0;
                            gi.LastContext.nrd.ValidationCaptureSignal = true;
                        }
                        if (gpu != null) gpu.Frame = index;
                        VolumeManager.instance.Update(camera.transform, -1);
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        if (index % 16 == 0) Debug.Log("[HoGIStability] frame " + index);
                        var c = gi.LastContext ?? throw new InvalidOperationException(gi.Status);
                        if (c.nrd == null) throw new InvalidOperationException("Denoiser unavailable: " + c.nrdStatus);
                        if (index == 0 && Arg("-hoGpu", "0") == "1")
                        {
                            gpu = new HoRTGIGpuClock { TotalOnly = true };
                            c.ValidationGpuMarker = gpu.Mark; c.ValidationGpuRasterMarker = gpu.MarkRaster;
                        }
                        if (gpu != null) gpu.Collect();
                        if (index >= report.warmup)
                        {
                            var raw = Read(c.diffuse.rt, image);
                            var filtered = Read(c.PublishedDiffuse.rt, image);
                            var frame = new Frame
                            {
                                index = index, unityFrame = Time.frameCount,
                                signature = c.lightingSignature, sceneRevision = c.sceneRevision,
                                temporal = c.lastTemporal, reset = c.nrd.lastReset,
                                nrdDispatches = c.nrd.core.LastDispatchCount,
                                historyFrames = (int)c.nrd.core.AppliedSettings.diffFrames,
                                raw = Stats(raw, previousRaw), filtered = Stats(filtered, previousFiltered),
                                signal = Stats(Read(c.nrd.ValidationSignal.rt, image), null),
                                guideNormal = Stats(Read(c.nrd.normal.rt, image), null),
                                guideDepth = Stats(Read(c.nrd.viewZ.rt, image), null)
                            };
                            if (frame.raw.nonfinite + frame.filtered.nonfinite + frame.signal.nonfinite +
                                frame.guideNormal.nonfinite + frame.guideDepth.nonfinite != 0)
                                throw new InvalidOperationException("Nonfinite GI/guide data.");
                            report.frames.Add(frame);
                            if (frame.reset) report.resets++;
                            if (frame.temporal) report.temporalFrames++;
                            report.rawMean += frame.raw.mean / report.measured;
                            report.filteredMean += frame.filtered.mean / report.measured;
                            if (previousRaw != null)
                            {
                                report.rawFrameL1 += frame.raw.frameL1 / (report.measured - 1);
                                report.filteredFrameL1 += frame.filtered.frameL1 / (report.measured - 1);
                            }
                            for (int p = 0; p < raw.Length; p++)
                            {
                                double r = raw[p].r, f = filtered[p].r;
                                sums[p] += r; squares[p] += r * r;
                                fsums[p] += f; fsquares[p] += f * f;
                            }
                            previousRaw = raw; previousFiltered = filtered;
                        }
                        if (++index < report.warmup + report.measured) return;
                        for (int p = 0; p < sums.Length; p++)
                        {
                            report.rawVariance += Math.Max(0, squares[p] / report.measured - Math.Pow(sums[p] / report.measured, 2)) / sums.Length;
                            report.filteredVariance += Math.Max(0, fsquares[p] / report.measured - Math.Pow(fsums[p] / report.measured, 2)) / sums.Length;
                        }
                        var secondary = new Secondary[c.secondary.count]; c.secondary.GetData(secondary);
                        report.secondaryFlags = new int[4];
                        foreach (var s in secondary)
                        {
                            if (s.flags > 3 || !float.IsFinite(s.pdf)) throw new InvalidOperationException("Invalid secondary packet.");
                            report.secondaryFlags[s.flags]++;
                        }
                        var reservoirs = new Reservoir[c.reservoirs.count]; c.reservoirs.GetData(reservoirs);
                        // Reservoir 1 is the public final stage; ignore block-linear padding.
                        for (int y = 0; y < c.height; y++) for (int x = 0; x < c.width; x++)
                        {
                            var r = reservoirs[c.arrayPitch + y / 16 * c.rowPitch + x / 16 * 256 + y % 16 * 16 + x % 16];
                            if ((r.misc & 255) > 0 && r.weight > 0) report.reservoirValid++;
                            if ((r.misc & 255) > 1) report.reservoirHistory++;
                        }
                        var textures = new List<RTHandle> { c.diffuse, c.nrd.signal, c.nrd.normal, c.nrd.viewZ, c.nrd.output };
                        foreach (string field in new[] { "permanent", "transient" })
                            textures.AddRange((RTHandle[])typeof(HoNrdContext).GetField(field, Hidden).GetValue(c.nrd.core));
                        report.textures = textures.Select(t => t.rt.name + " " + t.rt.graphicsFormat).ToArray();
                        foreach (var t in textures)
                            report.texturePayloadBytes += (long)t.rt.width * t.rt.height *
                                UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(t.rt.graphicsFormat);
                        report.status = gi.Status;
                        var view = gi.SceneViewForCamera(camera);
                        report.sceneRejections = view.Scene.Rejections.ToArray();
                        report.materialRejections = ((HoRTGISecondaryMaterials)view.MaterialProfile).Rejections.ToArray();
                        if (gpu != null)
                        {
                            gpu.Collect(); gpu.samples.RemoveAll(sample => sample.frame < report.warmup);
                            report.gpuSummary = gpu.Summarize(8,requireComposite:false); report.gpuSamples = gpu.samples.ToArray();
                            report.gpuQualified = true;
                        }
                        report.pass = diagnostic == null && report.frames.Count == report.measured;
                        Finish(null);
                    }
                    catch (Exception error) { Finish(error); }
                };
                void Finish(Exception error)
                {
                    EditorApplication.update -= tick;
                    if (error != null) { report.error = error.ToString(); report.pass = false; }
                    if (gi?.LastContext != null) { gi.LastContext.ValidationGpuMarker = null; gi.LastContext.ValidationGpuRasterMarker = null; }
                    File.WriteAllText(output, JsonUtility.ToJson(report, true));
                    gpu?.Dispose(); gi?.Dispose();
                    GraphicsSettings.defaultRenderPipeline = oldDefault; QualitySettings.renderPipeline = oldQuality;
                    ShaderUtil.allowAsyncCompilation = oldAsync; Application.logMessageReceived -= OnLog;
                    foreach (var obj in owned.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
                    owned.Clear();
                    Debug.Log("[HoGIStability] " + (report.pass ? "PASS " : "FAIL ") + output + " " + report.error);
                    EditorApplication.Exit(report.pass ? 0 : 1);
                }
                EditorApplication.update += tick;
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Debug.LogException(error); EditorApplication.Exit(1);
            }
        }
        static Material Quad(string name, Vector3 position, Vector2 size, int layer, Color emission)
        {
            var obj = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
            obj.name = name; obj.layer = layer; obj.transform.position = position;
            obj.transform.localScale = new Vector3(size.x, size.y, 1);
            if (layer == 1) obj.transform.rotation = Quaternion.Euler(0, 180, 0);
            var mat = Keep(new Material(Shader.Find("lilToon")));
            mat.EnableKeyword("UNITY_UI_ALPHACLIP"); mat.SetFloat("_Cull", 0);
            mat.SetFloat("_AsUnlit", 1); mat.SetColor("_Color", Color.white);
            mat.SetTexture("_MainTex", Texture2D.whiteTexture); mat.SetFloat("_Metallic", 0); mat.SetFloat("_Reflectance", 0);
            mat.SetFloat("_UseEmission", emission.maxColorComponent > 0 ? 1 : 0);
            mat.SetFloat("_EmissionBlend", 1); mat.SetColor("_EmissionColor", emission);
            mat.SetTexture("_EmissionMap", Texture2D.whiteTexture); mat.SetTexture("_EmissionBlendMask", Texture2D.whiteTexture);
            obj.GetComponent<Renderer>().sharedMaterial = mat;
            return mat;
        }
        static Color[] Read(RenderTexture source, Texture2D image)
        {
            var previous = RenderTexture.active;
            try { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; }
        }
        static ImageStats Stats(Color[] pixels, Color[] previous)
        {
            var result = new ImageStats();
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (!float.IsFinite(c.r) || !float.IsFinite(c.g) || !float.IsFinite(c.b) || !float.IsFinite(c.a)) { result.nonfinite++; continue; }
                if (c.a >= .5f) result.valid++;
                if (c.maxColorComponent > 1e-6f && (c.r > 1e-6f || c.g > 1e-6f || c.b > 1e-6f)) result.lit++;
                var rgb = new Vector3(c.r, c.g, c.b); result.mean += rgb / pixels.Length; result.max = Vector3.Max(result.max, rgb);
                if (previous != null)
                {
                    var p = previous[i]; double delta = (Math.Abs(c.r - p.r) + Math.Abs(c.g - p.g) + Math.Abs(c.b - p.b)) / 3;
                    result.frameL1 += delta / pixels.Length; result.frameMax = Math.Max(result.frameMax, delta);
                }
            }
            return result;
        }
    }
}
