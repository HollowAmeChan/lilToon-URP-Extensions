#pragma warning disable CS0618
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.GeometryBuffer;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.SSGI
{
    // Run in an isolated batch project. No scene, renderer or profile is saved.
    public static class HoSSGIValidation
    {
        private static readonly List<Object> created = new List<Object>();
        private static HoSSGIRendererFeature feature;
        private static UniversalRenderPipelineAsset pipeline;
        private static Camera camera;
        private static RenderTexture target;
        private static RenderPipelineAsset oldGraphics, oldQuality;
        private static bool baseline;
        private static bool legacyProjection;
        private static bool pattern;
        private static bool samplingSequence;
        private static bool response;
        private static double responseStart;
        private static double responseSourceStart;
        private static Color[] cyclePixels;
        private static int frame, stage;
        private static Color[] previousPixels;
        private static double earlyError, lateError;
        private static int earlySamples, lateSamples;
        private static HashSet<RenderTexture> historyTextures;
        private static readonly string[] stages = { "temporal", "full", "scaled" };

        public static void RunBaseline() { baseline = true; legacyProjection = false; Start(); }
        public static void Run() { baseline = legacyProjection = false; Start(); }
        public static void RunLegacyProjection() { baseline = legacyProjection = true; Start(); }
        public static void RunPatternBaseline() { pattern = baseline = true; Start(); }
        public static void RunPattern() { pattern = true; baseline = false; Start(); }
        public static void RunSamplingSequence() { samplingSequence = true; baseline = true; Start(); }
        public static void RunResponseBaseline() { response = pattern = baseline = true; Start(); }
        public static void RunResponse() { response = pattern = true; baseline = false; Start(); }

        private static void Start()
        {
            // Refuse before entering the batch cleanup/Exit path, so an
            // accidental invocation cannot close the user's interactive editor.
            Require(Application.isBatchMode, "Use a separate batch editor");
            try
            {
                stage = frame = 0;
                ShaderUtil.allowAsyncCompilation = false;
                oldGraphics = GraphicsSettings.defaultRenderPipeline;
                oldQuality = QualitySettings.renderPipeline;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                renderer.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
                feature = Keep(ScriptableObject.CreateInstance<HoSSGIRendererFeature>());
                if (response && baseline)
                {
                    feature.Settings.shader = Shader.Find("Hidden/HoSSGI/ResponseBaseline");
                    Require(feature.Settings.shader != null, "Response baseline requires the generated legacy shader in the isolated batch project");
                }
                renderer.rendererFeatures.Add(feature);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                pipeline.msaaSampleCount = 1;
                pipeline.supportsHDR = true;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * 0.2f;
                var light = Keep(new GameObject("SSGI validation sun")).AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(35, -25, 0);
                light.intensity = 1;
                RenderSettings.sun = light;
                camera = Keep(new GameObject("SSGI validation camera")).AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0, 1.4f, -5);
                camera.transform.LookAt(new Vector3(0, 1.4f, 1));
                camera.fieldOfView = 55;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                Box("back", new Vector3(0, 2, 2), new Vector3(6, 4, 0.1f), Color.white);
                Box("floor", new Vector3(0, -0.1f, 0), new Vector3(6, 0.2f, 6), Color.white);
                Box("red", new Vector3(-2, 1.5f, 0), new Vector3(0.1f, 3, 4), new Color(2, 0.03f, 0.03f));
                Box("blue", new Vector3(2, 1.5f, 0), new Vector3(0.1f, 3, 4), new Color(0.03f, 0.1f, 2));
                target = Keep(new RenderTexture(pattern ? 320 : 160, pattern ? 256 : 128, 24, RenderTextureFormat.ARGBHalf));
                target.Create();
                camera.aspect = 160f / 128;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var settings = VolumeManager.instance.stack.GetComponent<HoSSGIVolume>();
                settings.enable.value = true;
                settings.rayCount.value = 4;
                settings.stepCount.value = 24;
                settings.thickness.value = 0.08f;
                settings.rayLength.value = 4;
                settings.temporalBlend.value = 0.95f;
                settings.spatialRadius.value = 2;
                settings.debugMode.value = HoSSGIDebugMode.Off;
                if (pattern)
                {
                    stage = 1;
                    settings.rayCount.value = 9;
                    settings.stepCount.value = 99;
                    settings.rayLength.value = 1.4f;
                    settings.thickness.value = 0.37f;
                    settings.temporalBlend.value = 1;
                    settings.spatialRadius.value = 8;
                    settings.debugMode.value = HoSSGIDebugMode.RawGI;
                    feature.Settings.debugInGameView = true;
                }
                if (samplingSequence)
                {
                    settings.debugMode.value = HoSSGIDebugMode.RawTrace;
                    feature.Settings.debugInGameView = true;
                }
                ConfigureStage();
                EditorApplication.update += Tick;
                Debug.Log("[HoSSGIValidation] START " + (baseline ? "baseline" : "regression") + " " + SystemInfo.graphicsDeviceType);
            }
            catch (Exception e) { Finish(e); }
        }

        private static void ConfigureStage()
        {
            var settings = VolumeManager.instance.stack.GetComponent<HoSSGIVolume>();
            settings.temporalReservoirReuse.value = true;
            settings.spatialReservoirReuse.value = stage != 0;
            settings.temporalReservoirValidation.value = stage != 0;
            settings.spatialReservoirValidation.value = stage != 0;
            settings.fireflySuppression.value = stage != 0;
            pipeline.renderScale = stage == 2 ? 0.7f : 1;
            feature.SetActive(false);
            feature.SetActive(true);
            // Invalidate all prior stage history without mutating any asset.
            var histories = (IDictionary)typeof(HoSSGIRendererFeature).GetField("histories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature);
            foreach (object history in histories.Values) history.GetType().GetMethod("Invalidate").Invoke(history, null);
            previousPixels = null;
            historyTextures = null;
            earlyError = lateError = 0;
            earlySamples = lateSamples = frame = 0;
        }

        private static void Tick()
        {
            try
            {
                if (response && frame == 64)
                {
                    foreach (Object obj in created)
                    {
                        if (!(obj is Material material) || !material.HasProperty("_BaseColor")) continue;
                        material.SetColor("_BaseColor", material.GetColor("_BaseColor") * 0.1f);
                        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", material.GetColor("_EmissionColor") * 0.1f);
                    }
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                frame++;
                object history = GetHistory();
                if (history == null) throw new Exception("SSGI camera history was not produced");
                if (response && (frame == 64 || frame == 65 || frame == 66 || frame == 68 || frame == 72 || frame == 80 || frame == 96 || frame == 128))
                {
                    Color[] output = Read(target); double signalMean = 0;
                    foreach (Color pixel in output) signalMean += Luminance(pixel);
                    signalMean /= output.Length;
                    if (frame == 64) responseStart = signalMean;
                    double sourceMean = 0, temporalMean = 0, denoiseMean = 0;
                    foreach (Color pixel in Read(HistoryTexture(history, "PreviousSource"))) sourceMean += Luminance(pixel);
                    foreach (Color pixel in Read(HistoryTexture(history, "Previous"))) temporalMean += Luminance(pixel);
                    foreach (Color pixel in Read(HistoryTexture(history, "PreviousDenoised"))) denoiseMean += Luminance(pixel);
                    sourceMean /= output.Length; temporalMean /= output.Length; denoiseMean /= output.Length;
                    if (frame == 64) responseSourceStart = sourceMean;
                    double ratio = signalMean / Math.Max(responseStart, 0.000001);
                    Debug.Log($"[HoSSGIValidation] response after={frame - 64} signal={signalMean:F8} ratio={ratio:F6} sourceRatio={sourceMean / Math.Max(responseSourceStart, 0.000001):F6} temporal={temporalMean:F8} denoise={denoiseMean:F8}");
                    if (!baseline && frame == 65) Require(ratio < 0.16, "Source/GI response retained stale energy on the first changed frame");
                    if (!baseline && frame == 72) Require(ratio < 0.13, "Source/GI response is too slow after 8 frames");
                    if (!baseline && frame == 128) Require(ratio > 0.06 && ratio < 0.15, "Lighting response retained stale energy or lost the GI signal");
                }
                if (samplingSequence)
                {
                    if (frame == 3)
                    {
                        int index = Shader.GetGlobalInt("_HoSSGIFrameIndex");
                        int unityFrame = Time.frameCount;
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        int nextIndex = Shader.GetGlobalInt("_HoSSGIFrameIndex");
                        Require(nextIndex == index + 1, "Repeated camera renders reuse the same sampling index");
                        Debug.Log($"[HoSSGIValidation] same-update sampling={index}->{nextIndex} UnityFrame={unityFrame}->{Time.frameCount}");
                    }
                    if (frame == 16) cyclePixels = Read(target);
                    if (frame == 32)
                    {
                        Color[] nextCycle = Read(target); double difference = 0;
                        for (int i = 0; i < nextCycle.Length; i++) difference += Math.Abs(Luminance(nextCycle[i]) - Luminance(cyclePixels[i]));
                        difference /= nextCycle.Length;
                        Debug.Log($"[HoSSGIValidation] 16-frame raw difference={difference:F8}");
                        Require(difference > 0.001, "Raw candidates still repeat after 16 frames");
                        Finish(null); return;
                    }
                    return;
                }
                var currentTextures = new HashSet<RenderTexture>();
                foreach (PropertyInfo property in history.GetType().GetProperties())
                {
                    if (property.PropertyType == typeof(RTHandle)) currentTextures.Add(((RTHandle)property.GetValue(history)).rt);
                }
                Require(currentTextures.Count == 22, "Incomplete history texture set");
                if (!baseline && historyTextures != null) Require(historyTextures.SetEquals(currentTextures), "History textures reallocated after a role swap");
                historyTextures = currentTextures;
                // Negative control: retain all allocations but reproduce the old
                // CPU projection inverse for the next frame's history rejection.
                if (legacyProjection) history.GetType().GetMethod("CommitCameraMatrix").Invoke(history, new object[] { (camera.projectionMatrix * camera.worldToCameraMatrix).inverse });
                Color[] colors = Read(HistoryTexture(history, "PreviousDenoised"));
                if (previousPixels != null && colors.Length == previousPixels.Length)
                {
                    double error = 0;
                    for (int i = 0; i < colors.Length; i++)
                    {
                        Require(IsFinite(colors[i].r) && IsFinite(colors[i].g) && IsFinite(colors[i].b), "Non-finite GI");
                        error += Math.Abs(colors[i].r - previousPixels[i].r) + Math.Abs(colors[i].g - previousPixels[i].g) + Math.Abs(colors[i].b - previousPixels[i].b);
                    }
                    error /= colors.Length * 3;
                    if (frame >= 3 && frame <= 8) { earlyError += error; earlySamples++; }
                    if (frame >= 25) { lateError += error; lateSamples++; }
                }
                previousPixels = colors;
                if (pattern && (frame == 32 || frame == 64 || frame == 128))
                {
                    string patternDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SSGIResults");
                    Directory.CreateDirectory(patternDirectory);
                    string label = (response ? "response-" : baseline ? "pattern-before-" : "pattern-after-") + frame;
                    Color[] finalPixels = Read(target);
                    Color[] patternGeometry = Read(HistoryTexture(history, "PreviousDepth"));
                    double highPass = 0, signal = 0; int measured = 0;
                    int[] offsets = { -1, 1, -target.width, target.width };
                    for (int y = 3; y < target.height - 3; y++)
                        for (int x = 3; x < target.width - 3; x++)
                        {
                            int i = y * target.width + x;
                            if (patternGeometry[i].a < 0.001f) continue;
                            bool samePlane = true; double mean = 0;
                            foreach (int offset in offsets)
                            {
                                Color tapGeometry = patternGeometry[i + offset];
                                if (tapGeometry.a < 0.001f || Math.Abs(tapGeometry.r - patternGeometry[i].r) + Math.Abs(tapGeometry.g - patternGeometry[i].g) + Math.Abs(tapGeometry.b - patternGeometry[i].b) > 0.01) samePlane = false;
                                mean += Luminance(finalPixels[i + offset]) * 0.25;
                            }
                            if (!samePlane) continue;
                            highPass += Math.Abs(Luminance(finalPixels[i]) - mean);
                            signal += Luminance(finalPixels[i]);
                            measured++;
                        }
                    string metric = $"[HoSSGIValidation] {label} highPass={highPass / Math.Max(measured, 1):F8} signal={signal / Math.Max(measured, 1):F8} measured={measured}";
                    Debug.Log(metric);
                    File.AppendAllText(Path.Combine(patternDirectory, "pattern.txt"), metric + Environment.NewLine);
                    Save(target, Path.Combine(patternDirectory, label + "-final.png"));
                    Save(HistoryTexture(history, "PreviousDenoised"), Path.Combine(patternDirectory, label + "-temporal.png"));
                    if (!baseline && !response && frame == 128)
                    {
                        Require(highPass / Math.Max(measured, 1) < 0.0015, "Residual high-frequency pattern exceeds the fixture limit: " + metric);
                        Require(signal / Math.Max(measured, 1) > 0.01, "Pattern reduction lost the GI signal: " + metric);
                    }
                }
                if (frame < (pattern ? 128 : 32)) return;
                Color[] geometry = Read(HistoryTexture(history, "PreviousDepth"));
                Color[] counts = Read(HistoryTexture(history, "PreviousSampleCount"));
                Color[] invalidity = Read(HistoryTexture(history, "PreviousInvalidity"));
                int valid = 0, accepted = 0;
                double count = 0, energy = 0;
                for (int i = 0; i < geometry.Length; i++)
                {
                    if (geometry[i].a < 0.001f) continue;
                    valid++;
                    count += counts[i].r;
                    if (invalidity[i].g > 0.5f) accepted++;
                    energy += colors[i].r + colors[i].g + colors[i].b;
                }
                Require(valid > geometry.Length / 2, "Fixture geometry coverage missing");
                double meanCount = count / valid, acceptance = (double)accepted / valid;
                string result = $"[HoSSGIValidation] {(legacyProjection ? "legacy-projection" : stages[stage])} count={meanCount:F4} accepted={acceptance:F4} energy={energy / valid / 3:F6} earlyDelta={earlyError / Math.Max(earlySamples, 1):F6} lateDelta={lateError / Math.Max(lateSamples, 1):F6}";
                Debug.Log(result);
                string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SSGIResults");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, legacyProjection ? "legacy-projection.txt" : baseline ? "baseline.txt" : "regression.txt"), result + Environment.NewLine);
                Save(HistoryTexture(history, "PreviousDenoised"), Path.Combine(directory, (legacyProjection ? "legacy-projection-" : baseline ? "baseline-" : "fixed-") + stages[stage] + ".png"));
                if (!baseline)
                {
                    Require(meanCount > 14, "Stationary history does not accumulate: " + result);
                    Require(acceptance > 0.95, "Stationary geometry rejects history: " + result);
                    Require(energy > 0.0001, "GI signal missing: " + result);
                    Require(lateError / Math.Max(lateSamples, 1) < earlyError / Math.Max(earlySamples, 1) * 0.8, "Stationary GI noise did not decrease: " + result);
                }
                if (legacyProjection)
                {
                    Require(meanCount < 3 && acceptance < 0.1, "Negative control did not reproduce history rejection: " + result);
                    Finish(null); return;
                }
                if (pattern || ++stage >= stages.Length) { Finish(null); return; }
                ConfigureStage();
            }
            catch (Exception e) { Finish(e); }
        }

        private static object GetHistory()
        {
            var histories = (IDictionary)typeof(HoSSGIRendererFeature).GetField("histories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature);
            return histories[camera.GetInstanceID()];
        }

        private static RenderTexture HistoryTexture(object history, string property)
        {
            return ((RTHandle)history.GetType().GetProperty(property).GetValue(history)).rt;
        }

        private static Color[] Read(RenderTexture texture)
        {
            var prior = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            Color[] pixels = image.GetPixels();
            Object.DestroyImmediate(image);
            RenderTexture.active = prior;
            return pixels;
        }

        private static void Save(RenderTexture texture, string path)
        {
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            image.SetPixels(Read(texture)); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        private static void Box(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = name; go.transform.position = position; go.transform.localScale = scale;
            var material = Keep(new Material(Shader.Find(response ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")));
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.5f);
            }
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static T Keep<T>(T obj) where T : Object { created.Add(obj); return obj; }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static double Luminance(Color value) { return 0.2126 * value.r + 0.7152 * value.g + 0.0722 * value.b; }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            GraphicsSettings.defaultRenderPipeline = oldGraphics;
            QualitySettings.renderPipeline = oldQuality;
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
            if (error != null) Debug.LogException(error);
            else Debug.Log("[HoSSGIValidation] PASS");
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
