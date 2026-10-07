#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Atmosphere
{
    // Execute only in an isolated batch project. No scene or renderer assets are saved.
    public static class HoAtmosphereValidation
    {
        private static readonly List<Object> objects = new();
        internal static readonly Vector4[] Directions = {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            new Vector3(1, .3f, .8f), new Vector3(-1, .6f, -.7f), new Vector3(.3f, 1, -.8f),
            new Vector3(-.4f, -1, .7f), new Vector3(.8f, -.6f, 1), new Vector3(-.7f, -.4f, -1),
            new Vector3(1, 1, 1), new Vector3(-1, -1, -1), Vector3.zero, Vector3.forward };
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static Material previousSkybox;
        private static Light previousSun;
        private static bool previousAsync, compatibility;
        private static Camera camera;
        private static RenderTexture target;
        private static HoAtmosphereRendererFeature feature;
        private static HoAtmosphereProbeFeature probe;
        private static HoAtmosphereVolume volume;
        private static RTHandle output;
        private static Material sky;
        private static UniversalRenderPipelineAsset pipeline;
        private static int frame;
        private static uint previousRevision;

        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void Run()
        {
            try
            {
                Debug.Log("[HoAtmosphereValidation] START");
                previousDefault = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
                previousSkybox = RenderSettings.skybox; previousSun = RenderSettings.sun;
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                feature = Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());
                feature.Settings.resolution = 64;
                renderer.rendererFeatures.Add(feature);
                probe = Keep(ScriptableObject.CreateInstance<HoAtmosphereProbeFeature>());
                probe.material = Mat("Hidden/lilToon/URP/Atmosphere/Validation");
                probe.material.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                var texture = Keep(new RenderTexture(16, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); texture.Create();
                output = RTHandles.Alloc(texture); probe.destination = output;
                renderer.rendererFeatures.Add(probe);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Isolated project needs URP_COMPATIBILITY_MODE");
                #endif
                var holder = Keep(new GameObject("Atmosphere validation volume")).AddComponent<Volume>(); holder.isGlobal = true;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                volume = holder.sharedProfile.Add<HoAtmosphereVolume>(true);
                volume.intensity.overrideState = volume.tint.overrideState = volume.rotationY.overrideState = false;
                camera = Keep(new GameObject("Atmosphere validation camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -3); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
                var quad = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
                var red = Mat("Universal Render Pipeline/Unlit"); red.SetColor("_BaseColor", Color.red); red.SetFloat("_Cull", 0);
                quad.GetComponent<Renderer>().sharedMaterial = red;
                sky = Mat("Hidden/lilToon/URP/Atmosphere/ValidationSky"); sky.SetFloat("_TestGain", 8);
                RenderSettings.skybox = sky;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                var samples = Render();
                for (int i = 0; i < 14; i++)
                {
                    Vector3 d = ((Vector3)Directions[i]).normalized;
                    Near(samples[i], new Color((d.x * .5f + .5f) * 8, (d.y * .5f + .5f) * 8, (d.z * .5f + .5f) * 8), "native sky direction " + i, .10f);
                }
                Near(Read(target)[32 + 32 * 64], Color.red, "main camera opaque after capture");
                Require(feature.CaptureCount == 1, "unchanged source should be captured once");
                var firstCamera = camera;
                camera = Keep(new GameObject("Second atmosphere validation camera")).AddComponent<Camera>();
                camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.transform.position = new Vector3(2, 1, -4);
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                Near(Render()[0], samples[0], "cross-camera cache");
                Require(feature.CaptureCount == 1, "second camera must reuse unchanged source");
                camera = firstCamera;
                // Simulate native storage loss without discarding the source/cache object.
                var cache = (HoAtmosphereCache)typeof(HoAtmosphereRendererFeature).GetField("cache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(feature);
                cache.capturedCube.rt.Release();
                Near(Render()[0], samples[0], "lost native cube restoration");
                Require(feature.CaptureCount == 2, "lost storage must recapture");
                var initialCount = feature.CaptureCount;
                previousRevision = probe.revision;
                feature.Settings.intensity = 2; feature.Settings.tint = new Color(.5f, 1, .25f); feature.Settings.rotationY = 90;
                samples = Render(); Near(samples[0], new Color(4, 8, 4), "yaw + tint + gain");
                Require(feature.CaptureCount == initialCount, "sampling parameters must not recapture source");
                Require(compatibility || probe.revision != previousRevision, "sampling parameter change must revise input");
                feature.Settings.intensity = 1; feature.Settings.tint = Color.white; feature.Settings.rotationY = 0;
                sky.SetFloat("_TestGain", 4); Near(Render()[0], new Color(4, 2, 2), "material change");
                Require(feature.CaptureCount == initialCount + 1, "material CRC change must recapture");
                feature.Settings.update = HoAtmosphereUpdate.Manual; Render(); var count = feature.CaptureCount;
                sky.SetFloat("_TestGain", 2); Near(Render()[0], new Color(4, 2, 2), "manual cache reuse");
                Require(feature.CaptureCount == count, "manual source mutation should wait");
                feature.RefreshEnvironment(); Near(Render()[0], new Color(2, 1, 1), "manual refresh");
                Require(feature.CaptureCount == count + 1, "refresh should capture once");
                feature.Settings.update = HoAtmosphereUpdate.OnChange;
                var cube = Cube(); feature.Settings.source = HoAtmosphereSource.RadianceCubemap; feature.Settings.radianceCubemap = cube;
                samples = Render();
                for (int i = 0; i < 6; ++i) Near(samples[i], FaceColor(i), "raw HDR cube " + i);
                previousRevision = probe.revision;
                var replacement = new Color[256]; for (int i = 0; i < replacement.Length; ++i) replacement[i] = new Color(9, 8, 7, 1);
                cube.SetPixels(replacement, CubemapFace.PositiveX); cube.Apply();
                Near(Render()[0], new Color(9, 8, 7), "raw cube mutation");
                Require(compatibility || probe.revision != previousRevision, "cube mutation must revise input");
                count = feature.CaptureCount;
                Require(samples[0].r > 1 && samples[0].a > .99f, "HDR/validity lost");
                feature.Settings.radianceCubemap = null; Invalid(Render(), "missing cube");
                feature.Settings.source = HoAtmosphereSource.Constant; feature.Settings.constantColor = Color.black;
                samples = Render(); Near(samples[0], Color.black, "valid black constant"); Require(samples[0].a > .99f, "black environment is valid");
                feature.Settings.constantColor = new Color(3, 2, 1); Near(Render()[4], new Color(3, 2, 1), "HDR constant");
                Near(Render()[14], Color.black, "zero direction");
                volume.intensity.Override(2); Near(Render()[0], new Color(6, 4, 2), "volume override"); volume.intensity.overrideState = false;
                feature.SetActive(false); Invalid(Render(), "disabled feature"); feature.SetActive(true); Near(Render()[0], new Color(3, 2, 1), "re-enabled feature");
                volume.enable.Override(false); Invalid(Render(), "volume disabled"); volume.enable.Override(true);
                camera.cameraType = CameraType.Reflection; Invalid(Render(), "excluded reflection camera"); camera.cameraType = CameraType.Game;
                feature.Settings.source = HoAtmosphereSource.SkyboxMaterial; feature.Settings.skyboxMaterial = null; Invalid(Render(), "missing material");
                feature.Settings.skyboxMaterial = sky; Near(Render()[0], new Color(2, 1, 1), "explicit skybox");
                pipeline.renderScale = .5f; feature.RefreshEnvironment(); Render();
                Near(Read(target)[32 + 32 * 64], Color.red, "main camera after capture at half render scale");
                pipeline.renderScale = 1;
                feature.Settings.update = HoAtmosphereUpdate.EveryFrame; Render(); count = feature.CaptureCount;
                Render(); Require(feature.CaptureCount == count, "EveryFrame must reuse within same frame");
                feature.Settings.update = HoAtmosphereUpdate.OnChange;
                ValidateBuiltin(cube);
                volume.debugMode.value = HoAtmosphereDebugMode.Availability;
                Near(RenderAndReadTarget(), Color.green, "debug availability presentation");
                volume.debugMode.value = HoAtmosphereDebugMode.All; Render();
                volume.debugMode.value = HoAtmosphereDebugMode.Off;
                feature.Settings.source = HoAtmosphereSource.SceneSkybox; RenderSettings.skybox = null; Invalid(Render(), "missing scene sky");
                feature.Dispose(); Require(Shader.GetGlobalVector(HoAtmosphereBindings.ParametersId).w == 0, "dispose reset");
                Debug.Log("[HoAtmosphereValidation] PASS: native six-face HDR + off-axis orientation, opaque canary, cache/refresh, cube/constant, built-in sky sources, volume, reset, debug; API=" + SystemInfo.graphicsDeviceType + "; compatibility=" + compatibility);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void ValidateBuiltin(Cubemap cube)
        {
            var mat = Mat("Skybox/Cubemap"); mat.SetTexture("_Tex", cube); mat.SetFloat("_Exposure", 1);
            feature.Settings.skyboxMaterial = mat; var before = Render();
            Require(before[0].r > 1, "built-in cubemap HDR capture");
            mat.SetFloat("_Exposure", 2); var after = Render(); Near(after[0], before[0] * 2, "built-in cubemap exposure", .1f);
            var panorama = Keep(new Texture2D(16, 8, TextureFormat.RGBAFloat, false, true));
            var pixels = new Color[128]; for (int i = 0; i < pixels.Length; ++i) pixels[i] = new Color(2, 3, 4, 1);
            panorama.SetPixels(pixels); panorama.Apply();
            mat = Mat("Skybox/Panoramic"); mat.SetTexture("_MainTex", panorama); feature.Settings.skyboxMaterial = mat;
            before = Render(); Require(before[0].b > 1, "panoramic HDR capture");
            mat.SetFloat("_Exposure", 2); after = Render(); Near(after[4], before[4] * 2, "panorama exposure", .1f);
            mat = Mat("Skybox/Procedural"); feature.Settings.skyboxMaterial = mat;
            var sun = Keep(new GameObject("validation sun")).AddComponent<Light>(); sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45, 20, 0); RenderSettings.sun = sun;
            before = Render(); Require(before[2].maxColorComponent > .01f, "procedural sky capture");
            var count = feature.CaptureCount; sun.transform.Rotate(10, 0, 0); Render(); Require(feature.CaptureCount == count + 1, "sun changes recapture");
            Debug.Log("[HoAtmosphereValidation] Built-in Cubemap/Panoramic/Procedural PASS");
        }
        private static Cubemap Cube()
        {
            var cube = Keep(new Cubemap(16, TextureFormat.RGBAFloat, false));
            for (int face = 0; face < 6; ++face)
            {
                var pixels = new Color[256]; for (int i = 0; i < pixels.Length; ++i) pixels[i] = FaceColor(face);
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply(); return cube;
        }
        private static Color FaceColor(int face) => new Color(2 + face, 3 + face * .5f, 4 + face * .25f, 1);
        private static Color[] Render()
        {
            VolumeManager.instance.Update(camera.transform, -1);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            return Read(output.rt);
        }
        private static Color RenderAndReadTarget() { Render(); return Read(target)[32 + 32 * 64]; }
        private static Color[] Read(RenderTexture source)
        {
            var previous = RenderTexture.active; var image = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Invalid(Color[] samples, string label) { Near(samples[0], Color.black, label); Require(samples[0].a < .001f, label + " retained validity"); }
        private static void Near(Color actual, Color expected, string label, float epsilon = .03f) => Require(
            Mathf.Abs(actual.r - expected.r) < epsilon && Mathf.Abs(actual.g - expected.g) < epsilon && Mathf.Abs(actual.b - expected.b) < epsilon,
            label + ": expected " + expected + ", got " + actual);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static Material Mat(string name) { var shader = Shader.Find(name); Require(shader != null, "Missing shader " + name); return Keep(new Material(shader)); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality;
            RenderSettings.skybox = previousSkybox; RenderSettings.sun = previousSun; ShaderUtil.allowAsyncCompilation = previousAsync;
            output?.Release(); output = null;
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    internal sealed class HoAtmosphereProbeFeature : ScriptableRendererFeature
    {
        public RTHandle destination;
        public Material material;
        public uint revision;
        private ProbePass pass;
        public override void Create() { pass = new ProbePass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { pass.owner = this; renderer.EnqueuePass(pass); }
        private sealed class ProbePass : ScriptableRenderPass
        {
            public HoAtmosphereProbeFeature owner;
            public ProbePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data { public HoAtmosphereProbeFeature owner; public HoAtmosphereRenderGraphResources input; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var input = frame.GetOrCreate<HoAtmosphereRenderGraphResources>(); owner.revision = input.revision;
                using (var builder = graph.AddRasterRenderPass<Data>("Atmosphere validation probe", out var data))
                {
                    data.owner = owner; data.input = input;
                    if (input.radianceTexture.IsValid()) builder.UseTexture(input.radianceTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(graph.ImportTexture(owner.destination), 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        if (d.input.radianceTexture.IsValid()) HoAtmosphereBindings.Bind(context.cmd, d.input);
                        context.cmd.SetViewport(new Rect(0, 0, 16, 1));
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.owner.material, 0, MeshTopology.Triangles, 3);
                    });
                }
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get(); cmd.SetRenderTarget(owner.destination.nameID); cmd.SetViewport(new Rect(0, 0, 16, 1));
                cmd.DrawProcedural(Matrix4x4.identity, owner.material, 0, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
