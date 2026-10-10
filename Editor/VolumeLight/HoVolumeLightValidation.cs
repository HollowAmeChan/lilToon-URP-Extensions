#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.Atmosphere;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.VolumeLight
{
    /// <summary>Isolated batch project only. GPU readback, no edits to user scenes/renderer assets.</summary>
    public static class HoVolumeLightValidation
    {
        private static readonly List<Object> objects = new();
        private static Camera camera;
        private static RenderTexture target;
        private static UniversalRenderPipelineAsset pipeline;
        private static HoAtmosphereRendererFeature atmosphere;
        private static HoVolumeLightRendererFeature lightFeature;
        private static HoAtmosphereRegion region;
        private static HoVolumeLightVolume lightVolume;
        private static HoAtmosphereVolume atmosphereVolume;
        private static HoVolumeLightProbeFeature probe;
        private static ScriptableRendererFeature screenShadows;
        private static Light sun;
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static Material previousSkybox;
        private static Light previousSun;
        private static bool previousAsync;
        private static int frame;
        private static float initialWorldTime;
        private static readonly Dictionary<string, object> results = new();
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string graphicsAPI, error, medium, solar;
            public float windowReduction;
        }
        public static void Run()
        {
            try
            {
                Debug.Log("[HoVolumeLightValidation] START");
                initialWorldTime = HoAtmosphereMediumClock.SampleTime;
                previousDefault = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
                previousSkybox = RenderSettings.skybox; previousSun = RenderSettings.sun;
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                atmosphere = Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());
                atmosphere.Settings.source = HoAtmosphereSource.Constant; atmosphere.Settings.constantColor = Color.black;
                renderer.rendererFeatures.Add(atmosphere);
                lightFeature = Keep(ScriptableObject.CreateInstance<HoVolumeLightRendererFeature>());
                lightFeature.FeatureSettings.intensity = 10; lightFeature.FeatureSettings.maxDistance = 30; lightFeature.FeatureSettings.stepCount = 128; lightFeature.FeatureSettings.anisotropy = 0;
                renderer.rendererFeatures.Add(lightFeature);
                var screenType = typeof(UniversalRenderer).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceShadows");
                Require(screenType != null, "ScreenSpaceShadows type missing");
                screenShadows = Keep((ScriptableRendererFeature)ScriptableObject.CreateInstance(screenType)); screenShadows.SetActive(false);
                // PostPass runs after VolumeLight at the same event: exercise the SCREEN keyword variant.
                renderer.rendererFeatures.Add(screenShadows);
                probe = Keep(ScriptableObject.CreateInstance<HoVolumeLightProbeFeature>());
                probe.material = Mat("Hidden/lilToon/URP/VolumeLight/Validation");
                var probeTexture = Keep(new RenderTexture(16, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); probeTexture.Create();
                probe.destination = RTHandles.Alloc(probeTexture); renderer.rendererFeatures.Add(probe);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                var pipelineObject = new SerializedObject(pipeline); pipelineObject.FindProperty("m_MainLightShadowsSupported").boolValue = true; pipelineObject.ApplyModifiedPropertiesWithoutUndo();
                pipeline.supportsHDR = true; pipeline.supportsCameraDepthTexture = true; pipeline.msaaSampleCount = 1;
                pipeline.mainLightShadowmapResolution = 2048; pipeline.shadowDistance = 50; pipeline.shadowCascadeCount = 4;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = false;
                #endif
                RenderSettings.skybox = null; RenderSettings.ambientLight = Color.black;
                camera = Keep(new GameObject("VolumeLight validation camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.allowHDR = true;
                camera.transform.position = new Vector3(0, 1, -8); camera.transform.rotation = Quaternion.identity; camera.fieldOfView = 60; camera.aspect = 1; camera.farClipPlane = 100;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                target = Keep(new RenderTexture(160, 160, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
                var holder = Keep(new GameObject("VolumeLight controls")).AddComponent<Volume>(); holder.isGlobal = true;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                lightVolume = holder.sharedProfile.Add<HoVolumeLightVolume>(false);
                atmosphereVolume = holder.sharedProfile.Add<HoAtmosphereVolume>(false);
                region = Keep(new GameObject("Atmosphere region")).AddComponent<HoAtmosphereRegion>();
                region.transform.position = new Vector3(0, 1, 5); region.size = new Vector3(12, 6, 8); region.blendDistance = .5f; region.density = .1f;
                sun = Keep(new GameObject("Existing sun")).AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2; sun.color = Color.white; sun.shadows = LightShadows.None;
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(.6f, 0, .8f)); RenderSettings.sun = sun;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                Require(HoAtmosphereMediumClock.SampleTime > initialWorldTime, "editor medium clock did not advance");
                EditorApplication.update -= Tick;
                ValidateAuthoring(); ValidateMedium(); ValidateSolar();
                Debug.Log("[HoVolumeLightValidation] PASS: Medium GPU sampling/revision + solar shadow/cutout/offscreen/region mask/depth/inside/SSS + lifecycle; API=" + SystemInfo.graphicsDeviceType);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void ValidateAuthoring()
        {
            var editor = Keep(UnityEditor.Editor.CreateEditor(region));
            Require(editor.GetType().Name == "HoAtmosphereRegionEditor", "region inspector was not selected");
            var featureEditor = Keep(UnityEditor.Editor.CreateEditor(lightFeature));
            Require(featureEditor.GetType().Name == "HoVolumeLightFeatureEditor", "feature inspector was not selected");
            Require(Resources.Load<Shader>("HoVolumeLightSolarHD") != null, "default player shader resource missing");
            Vector3 original = region.size;
            Undo.RecordObject(region, "Validation region edit"); region.size = new Vector3(7, 8, 9);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Require(region.size == original, "region size Undo failed");
            Undo.PerformRedo(); Require(region.size == new Vector3(7, 8, 9), "region size Redo failed"); region.size = original;
            results["authoring"] = "PASS";
        }
        private static void ValidateMedium()
        {
            lightFeature.FeatureSettings.enabled = false;
            region.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); region.transform.localScale = Vector3.one;
            region.size = new Vector3(4, 4, 4); region.blendDistance = 1; region.density = .2f; region.tint = new Color(.4f, .6f, .8f);
            probe.Points(new Vector3(0, 0, 0), new Vector3(1.5f, 0, 0), new Vector3(3, 0, 0));
            Render(); var pixels = Read(probe.destination.rt);
            Near(pixels[0].r, 1, "inside activation"); Near(pixels[1].r, .5f, "soft edge activation"); Near(pixels[2].r, 0, "outside activation");
            Near(pixels[0].g, .2f, "density"); Near(pixels[0].b, .4f, "tint"); Near(pixels[0].a, 1, "medium available");
            var environmentRevision = probe.environmentRevision; var mediumRevision = probe.mediumRevision;
            region.density = .3f; Render(); Require(probe.environmentRevision == environmentRevision && probe.mediumRevision != mediumRevision, "medium changed environment revision or retained stale revision");
            atmosphere.Settings.source = HoAtmosphereSource.SkyboxMaterial;
            atmosphere.Settings.skyboxMaterial = Mat("Hidden/lilToon/URP/Atmosphere/ValidationSky");
            Render(); var captureCount = atmosphere.CaptureCount; Require(captureCount > 0, "sky source was not captured");
            region.wind = new Vector3(.2f, .1f, 0); region.density = .4f; Render();
            Require(atmosphere.CaptureCount == captureCount, "medium mutation recaptured environment cube"); region.density = .3f;
            atmosphere.Settings.source = HoAtmosphereSource.RadianceCubemap; atmosphere.Settings.radianceCubemap = null;
            Render(); Require(!probe.environmentValid && probe.mediumValid, "missing sky invalidated valid medium");
            atmosphereVolume.enable.Override(false); Render(); Require(probe.mediumValid && !probe.environmentValid, "environment volume disabled medium"); atmosphereVolume.enable.overrideState = false;
            atmosphere.Settings.source = HoAtmosphereSource.Constant;
            region.transform.rotation = Quaternion.Euler(0, 0, 90); region.transform.localScale = new Vector3(2, 1, .5f);
            probe.Points(region.transform.TransformPoint(new Vector3(1.75f, 0, 0)), region.transform.TransformPoint(new Vector3(0, 1.5f, 0)));
            Render(); pixels = Read(probe.destination.rt); Near(pixels[0].r, .5f, "scaled world-metre fade"); Near(pixels[1].r, .5f, "rotated fade");
            var noise = Keep(new Texture3D(4, 1, 1, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            noise.SetPixels(new[] { new Color(.1f, 0, 0), new Color(.3f, 0, 0), new Color(.6f, 0, 0), new Color(.9f, 0, 0) }); noise.Apply();
            region.densityTexture = noise; region.noiseStrength = 1; region.noiseWorldSize = 4; region.wind = Vector3.right;
            region.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); region.transform.localScale = Vector3.one; region.size = Vector3.one * 20;
            probe.Points(new Vector3(.5f, 0, 0)); probe.material.SetFloat("_ProbeTime", 0); Render(); Near(Read(probe.destination.rt)[0].g, .03f, "world noise t0");
            probe.material.SetFloat("_ProbeTime", 1); Render(); Near(Read(probe.destination.rt)[0].g, .27f, "advected noise t1");
            region.transform.position = Vector3.one; Render(); Near(Read(probe.destination.rt)[0].g, .27f, "region movement dragged world noise");
            var duplicate = Keep(new GameObject("Extra region")).AddComponent<HoAtmosphereRegion>(); Render(); Require(!probe.mediumValid, "ambiguous regions silently selected"); duplicate.enabled = false;
            region.enabled = false; Render(); Require(!probe.mediumValid, "removed region retained medium");
            Near(Read(probe.destination.rt)[0].a, 0, "empty medium global reset"); region.enabled = true;
            var firstCamera = camera; camera = Keep(new GameObject("second observer")).AddComponent<Camera>(); camera.enabled = false; camera.transform.position = new Vector3(0, 1, -5);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
            Render(); Near(Read(probe.destination.rt)[0].g, .27f, "cross-camera world noise"); camera = firstCamera;
            region.densityTexture = null; region.tint = Color.white; probe.material.SetFloat("_ProbeTime", 0);
            results["medium"] = "PASS";
            Debug.Log("[HoVolumeLightValidation] Medium contract PASS");
        }
        private static void ValidateSolar()
        {
            probe.material.SetFloat("_ProbeMode", 0);
            region.transform.SetPositionAndRotation(new Vector3(0, 1, 5), Quaternion.identity); region.transform.localScale = Vector3.one;
            region.size = new Vector3(12, 6, 8); region.density = .1f; region.blendDistance = .5f; region.volumeLightWeight = 1;
            lightFeature.FeatureSettings.enabled = true;
            var baseImage = Render(); Require(Mean(baseImage) > .01f, "solar produced no volume: " + lightFeature.Status);
            Save(baseImage, "solar-open");
            sun.intensity = 1; Near(Mean(Render()), Mean(baseImage) * .5f, "live sun intensity", .005f); sun.intensity = 2;
            sun.color = Color.blue; var blueImage = Render(); Require(blueImage[80 + 80 * 160].b > .01f && blueImage[80 + 80 * 160].r < .001f, "sun color binding failed"); sun.color = Color.white;
            var animatedNoise = Keep(new Texture3D(4, 1, 1, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            animatedNoise.SetPixels(new[] { new Color(.1f, 0, 0), new Color(.3f, 0, 0), new Color(.6f, 0, 0), new Color(.9f, 0, 0) }); animatedNoise.Apply();
            region.densityTexture = animatedNoise; region.wind = Vector3.right; region.noiseWorldSize = 4;
            HoAtmosphereMediumClock.TimeOverride = 0; var at0 = Render(); uint noiseRevision = probe.mediumRevision;
            HoAtmosphereMediumClock.TimeOverride = 1; var at1 = Render();
            Require(ImageError(at0, at1) > .005f, "dynamic world noise did not change actual volume");
            Require(probe.mediumRevision == noiseRevision, "time advancement changed static medium revision");
            region.densityTexture = null; HoAtmosphereMediumClock.TimeOverride = null;
            region.volumeLightWeight = 0; Require(Mean(Render()) < .0001f, "activation off left volume"); region.volumeLightWeight = 1;
            region.transform.position = new Vector3(80, 1, 5); Require(Mean(Render()) < .0001f, "outside SDF still displayed"); region.transform.position = new Vector3(0, 1, 5);
            var wall = Box("Outside-region window frame", new Vector3(0, 1, 0), new Vector3(.8f, 6, .2f));
            Require(!region.WorldBounds.Contains(wall.transform.position), "test frame accidentally inside region");
            sun.shadows = LightShadows.None; var unshadowed = Render(); sun.shadows = LightShadows.Hard; var shadowed = Render();
            Require(MaxReduction(unshadowed, shadowed) > .1f, "external frame cast no volumetric shadow");
            results["windowReduction"] = MaxReduction(unshadowed, shadowed); Save(shadowed, "solar-frame");
            probe.material.SetFloat("_ProbeMode", 1);
            probe.Points(new Vector3(3.75f, 1, 5), new Vector3(-2, 1, 5)); Render(); var shadowSamples = Read(probe.destination.rt);
            Require(shadowSamples[0].r < .25f && shadowSamples[1].r > .75f, "light-space point shadow mismatch: " + shadowSamples[0] + " / " + shadowSamples[1]);
            region.volumeLightWeight = 0; Render(); var gatedShadow = Read(probe.destination.rt);
            Near(gatedShadow[0].r, shadowSamples[0].r, "SDF activation changed shadow state"); Near(gatedShadow[1].g, 0, "activation gate remained on"); region.volumeLightWeight = 1;
            pipeline.shadowDistance = 8; var distant = Render(); Require(Read(probe.destination.rt)[0].r > .75f, "beyond-shadow-distance reference did not become unshadowed");
            sun.shadows = LightShadows.None; var distantReference = Render(); sun.shadows = LightShadows.Hard;
            Require(ImageError(distant, distantReference) / Mathf.Max(.001f, Mean(distantReference)) < .02f, "actual volume retained shadows beyond shadow distance"); pipeline.shadowDistance = 50;
            screenShadows.SetActive(true); var screenImage = Render();
            Require(Read(probe.destination.rt)[0].a > 1.5f, "SCREEN shadow keyword variant was not exercised");
            Require(ImageError(shadowed, screenImage) / Mathf.Max(.001f, Mean(shadowed)) < .02f, "screen-space shadow changed volume atlas semantics"); screenShadows.SetActive(false);
            wall.SetActive(false);
            var cutout = Quad("Alpha cutout window", new Vector3(0, 1, 0), new Vector3(6, 6, 1));
            var cutoutMaterial = cutout.GetComponent<Renderer>().sharedMaterial;
            var alpha = Keep(new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            alpha.SetPixels(new[] { new Color(0, 0, 0, 0), new Color(0, 0, 0, 1) }); alpha.Apply();
            cutoutMaterial.SetTexture("_BaseMap", alpha); cutoutMaterial.SetFloat("_AlphaClip", 1); cutoutMaterial.SetFloat("_Cutoff", .5f); cutoutMaterial.EnableKeyword("_ALPHATEST_ON"); cutoutMaterial.renderQueue = 2450;
            Vector3 offset = sun.transform.forward * (5 / sun.transform.forward.z);
            probe.Points(new Vector3(-1.5f, 1, 0) + offset, new Vector3(1.5f, 1, 0) + offset);
            var cutoutImage = Render(); shadowSamples = Read(probe.destination.rt);
            Require(shadowSamples[0].r > .75f && shadowSamples[1].r < .25f, "alpha holes lost in atlas: " + shadowSamples[0] + " / " + shadowSamples[1]);
            Save(cutoutImage, "solar-cutout"); cutout.SetActive(false);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.95f, 0, .31f));
            var offscreen = Box("Offscreen caster", new Vector3(-10, 1, -2), new Vector3(1.5f, 6, .3f));
            Require(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), offscreen.GetComponent<Renderer>().bounds), "caster is not offscreen");
            sun.shadows = LightShadows.None; unshadowed = Render(); sun.shadows = LightShadows.Hard; shadowed = Render();
            Require(MaxReduction(unshadowed, shadowed) > .04f, "offscreen caster did not affect region"); Save(shadowed, "solar-offscreen"); offscreen.SetActive(false);
            sun.shadows = LightShadows.None;
            camera.transform.position = new Vector3(0, 1, 4); Require(Mean(Render()) > .01f, "camera inside region lost volume");
            camera.orthographic = true; camera.orthographicSize = 4; Require(Mean(Render()) > .01f, "orthographic ray reconstruction failed"); camera.orthographic = false;
            camera.transform.position = new Vector3(0, 1, -8);
            var foreground = Quad("Foreground depth blocker", new Vector3(0, 1, -1), new Vector3(3, 3, 1));
            var blocked = Render(); lightFeature.FeatureSettings.enabled = false; var blockedBaseline = Render(); lightFeature.FeatureSettings.enabled = true;
            Near(blocked[80 + 80 * 160].r, blockedBaseline[80 + 80 * 160].r, "foreground depth rejected volume", .0005f); foreground.SetActive(false);
            var offCenter = Quad("Off-centre depth blocker", new Vector3(-1.3f, 1.9f, -1), new Vector3(.6f, .6f, 1));
            Vector3 viewport = camera.WorldToViewportPoint(offCenter.transform.position);
            int sample = Mathf.Clamp((int)(viewport.x * 160), 0, 159) + Mathf.Clamp((int)(viewport.y * 160), 0, 159) * 160;
            var offCenterWith = Render(); lightFeature.FeatureSettings.enabled = false; var offCenterBaseline = Render(); lightFeature.FeatureSettings.enabled = true;
            offCenter.SetActive(false); var offCenterClear = Render();
            Require(offCenterClear[sample].r > offCenterBaseline[sample].r + .01f, "off-centre depth test had no volume behind blocker");
            Near(offCenterWith[sample].r, offCenterBaseline[sample].r, "off-centre depth/UV reconstruction", .0005f);
            region.blendDistance = 2; probe.material.SetFloat("_ProbeMode", 0); probe.Points(new Vector3(5.5f, 1, 5), new Vector3(0, 1, 5));
            Render(); var edge = Read(probe.destination.rt); Require(edge[0].r > 0 && edge[0].r < .3f && edge[1].r > .99f, "region soft edge invalid");
            region.transform.rotation = Quaternion.Euler(0, 30, 0); Require(Mean(Render()) > .005f, "rotated region failed"); region.transform.rotation = Quaternion.identity;
            float fullScale = Mean(Render()); pipeline.renderScale = .5f;
            Near(Mean(Render()), fullScale, "half resolution depth/ray reconstruction", .015f); pipeline.renderScale = 1;
            pipeline.msaaSampleCount = 4; Near(Mean(Render()), fullScale, "MSAA4 color/depth", .015f); pipeline.msaaSampleCount = 1;
            lightVolume.debugMode.value = HoVolumeLightDebugMode.Contribution; Require(Mean(Render()) > .005f, "debug contribution missing"); lightVolume.debugMode.value = HoVolumeLightDebugMode.Off;
            lightFeature.FeatureSettings.enabled = false; Require(Mean(Render()) < .0001f && !lightFeature.HasMaterial, "disabled effect retained output/material");
            Require(probe.mediumValid && probe.environmentValid, "disabled effect invalidated foundation inputs");
            lightFeature.FeatureSettings.enabled = true; Require(Mean(Render()) > .005f, "re-enabled effect did not recreate material");
            lightFeature.SetActive(false); Require(Mean(Render()) < .0001f && !lightFeature.HasMaterial, "disabled renderer feature retained output/material"); lightFeature.Dispose(); Require(!lightFeature.HasMaterial, "dispose retained material");
            region.enabled = false; Render(); Require(!probe.mediumValid, "region disable retained GPU data");
            foreach (string shaderName in new[] { "Hidden/lilToon/URP/VolumeLight/SolarHD", "Hidden/lilToon/URP/VolumeLight/Validation", "Hidden/lilToon/URP/Atmosphere/Debug" })
                foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find(shaderName))) Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, shaderName + ": " + message.message);
            results["solar"] = "PASS";
        }
        private static GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var go = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); go.name = name; go.transform.position = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = BlackMaterial(); return go;
        }
        private static GameObject Quad(string name, Vector3 position, Vector3 size)
        {
            var go = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); go.name = name; go.transform.position = position; go.transform.localScale = size;
            var mat = BlackMaterial(); mat.SetFloat("_Cull", 0); go.GetComponent<Renderer>().sharedMaterial = mat; return go;
        }
        private static Material BlackMaterial() { var mat = Mat("Universal Render Pipeline/Lit"); mat.SetColor("_BaseColor", Color.black); mat.SetFloat("_Smoothness", 0); return mat; }
        private static Color[] Render()
        {
            VolumeManager.instance.Update(camera.transform, -1);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            return Read(target);
        }
        private static Color[] Read(RenderTexture source)
        {
            var previous = RenderTexture.active; var image = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static float Mean(Color[] pixels) { double sum = 0; foreach (var p in pixels) sum += (p.r + p.g + p.b) / 3; return (float)(sum / pixels.Length); }
        private static float MaxReduction(Color[] a, Color[] b) { float max = 0; for (int i = 0; i < a.Length; i++) if (a[i].r > .01f) max = Mathf.Max(max, (a[i].r - b[i].r) / a[i].r); return max; }
        private static float ImageError(Color[] a, Color[] b) { double sum = 0; for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r); return (float)(sum / a.Length); }
        private static void Save(Color[] pixels, string name)
        {
            var image = new Texture2D(160, 160, TextureFormat.RGBAFloat, false, true); image.SetPixels(pixels); image.Apply();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "VolumeLightValidation"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".exr"), image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var preview = new Color[pixels.Length]; for (int i = 0; i < pixels.Length; i++) preview[i] = new Color(pixels[i].r / (1 + pixels[i].r), pixels[i].g / (1 + pixels[i].g), pixels[i].b / (1 + pixels[i].b), 1);
            image.SetPixels(preview); image.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        private static void Near(float actual, float expected, string label, float epsilon = .015f) => Require(Mathf.Abs(actual - expected) < epsilon, label + ": expected " + expected + ", got " + actual);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static Material Mat(string name) { var shader = Shader.Find(name); Require(shader != null, "Missing shader " + name); return Keep(new Material(shader)); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            HoAtmosphereMediumClock.TimeOverride = null;
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality;
            RenderSettings.skybox = previousSkybox; RenderSettings.sun = previousSun; ShaderUtil.allowAsyncCompilation = previousAsync;
            probe?.destination?.Release();
            lightFeature?.Dispose(); atmosphere?.Dispose(); screenShadows?.Dispose();
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
            results["passed"] = error == null; results["graphicsAPI"] = SystemInfo.graphicsDeviceType.ToString(); results["error"] = error?.Message ?? "";
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "VolumeLightValidation"); Directory.CreateDirectory(dir);
            var report = new Report { passed = error == null, graphicsAPI = SystemInfo.graphicsDeviceType.ToString(), error = error?.Message ?? "",
                medium = results.ContainsKey("medium") ? (string)results["medium"] : "NOT RUN", solar = results.ContainsKey("solar") ? (string)results["solar"] : "NOT PASSED",
                windowReduction = results.ContainsKey("windowReduction") ? Convert.ToSingle(results["windowReduction"]) : 0 };
            File.WriteAllText(Path.Combine(dir, "result-" + SystemInfo.graphicsDeviceType + ".json"), JsonUtility.ToJson(report, true));
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoVolumeLightProbeFeature : ScriptableRendererFeature
    {
        internal Material material;
        internal RTHandle destination;
        internal uint environmentRevision, mediumRevision;
        internal bool environmentValid, mediumValid;
        private ProbePass pass;
        internal void Points(params Vector3[] points)
        {
            var values = new Vector4[16]; for (int i = 0; i < points.Length; i++) values[i] = points[i]; material.SetVectorArray("_ProbePoints", values);
        }
        public override void Create() => pass = new ProbePass(this);
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) => renderer.EnqueuePass(pass);
        private sealed class ProbePass : ScriptableRenderPass
        {
            private readonly HoVolumeLightProbeFeature owner;
            internal ProbePass(HoVolumeLightProbeFeature owner) { this.owner = owner; renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data { internal HoVolumeLightProbeFeature owner; internal HoAtmosphereMediumResources medium; internal TextureHandle shadow; internal float cascades; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var medium = frame.GetOrCreate<HoAtmosphereMediumResources>(); var environment = frame.GetOrCreate<HoAtmosphereRenderGraphResources>();
                owner.mediumValid = medium.IsValid; owner.mediumRevision = medium.snapshot.revision; owner.environmentValid = environment.IsValid; owner.environmentRevision = environment.revision;
                var resources = frame.Get<UniversalResourceData>(); var shadows = frame.Get<UniversalShadowData>();
                using (var builder = graph.AddRasterRenderPass<Data>("VolumeLight validation probes", out var data))
                {
                    data.owner = owner; data.medium = medium; data.shadow = resources.mainShadowsTexture; data.cascades = shadows.mainLightShadowCascadesCount > 1 ? 1 : 0;
                    if (medium.densityTexture.IsValid()) builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                    if (data.shadow.IsValid()) builder.UseTexture(data.shadow, AccessFlags.Read);
                    builder.SetRenderAttachment(graph.ImportTexture(owner.destination), 0, AccessFlags.WriteAll); builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.medium.snapshot.sampleTime);
                        if (d.shadow.IsValid()) context.cmd.SetGlobalTexture(Shader.PropertyToID("_MainLightShadowmapTexture"), d.shadow);
                        d.owner.material.SetFloat("_ProbeCascades", d.cascades);
                        context.cmd.SetViewport(new Rect(0, 0, 16, 1)); context.cmd.DrawProcedural(Matrix4x4.identity, d.owner.material, 0, MeshTopology.Triangles, 3);
                    });
                }
            }
        }
    }
}
