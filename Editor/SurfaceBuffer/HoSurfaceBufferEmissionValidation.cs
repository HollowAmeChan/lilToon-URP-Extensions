#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    // Run in an isolated Unity batch project; never saves a scene or renderer asset.
    public static class HoSurfaceBufferEmissionValidation
    {
        private static readonly List<Object> objects = new List<Object>();
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static Camera camera;
        private static Material material;
        private static Renderer surface;
        private static RenderTexture target;
        private static RTHandle capture;
        private static HoSurfaceBufferRendererFeature feature;
        private static HoSurfaceBufferVolume volumeSettings;
        private static UniversalRenderPipelineAsset pipeline;
        private static int frame;
        private static bool compatibility;
        private static bool previousAsyncCompilation;

        public static void RunCompatibility() { compatibility = true; Run(); }

        public static void Run()
        {
            Debug.Log("[HoSBEmissionValidation] Starting isolated render validation");
            try
            {
                previousDefault = GraphicsSettings.defaultRenderPipeline;
                previousQuality = QualitySettings.renderPipeline;
                previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                feature = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
                feature.Settings.enableSemanticLanes = false;
                feature.Settings.enableCorrelatedSemantics = false;
                feature.Settings.debugMode = HoSurfaceBufferDebugMode.Emission;
                renderer.rendererFeatures.Add(feature);
                var captureFeature = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferEmissionCaptureFeature>());
                var captureTexture = Keep(new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                captureTexture.Create();
                capture = RTHandles.Alloc(captureTexture);
                captureFeature.destination = capture;
                captureFeature.sourceFeature = feature;
                renderer.rendererFeatures.Add(captureFeature);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                pipeline.supportsHDR = true;
                pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Compatibility validation requires URP_COMPATIBILITY_MODE in the isolated project");
                #endif
                var volume = Keep(new GameObject("Emission validation volume")).AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                volumeSettings = volume.sharedProfile.Add<HoSurfaceBufferVolume>(true);
                volumeSettings.debugMode.value = HoSurfaceBufferDebugMode.Emission;
                camera = Keep(new GameObject("Emission validation camera")).AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -3);
                camera.orthographic = true;
                camera.orthographicSize = 1;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                target.Create();
                var quad = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
                var shader = Shader.Find("Hidden/lilToonCutout");
                Require(shader != null, "lilToon cutout shader missing");
                material = Keep(new Material(shader));
                material.EnableKeyword("UNITY_UI_ALPHACLIP");
                surface = quad.GetComponent<Renderer>();
                surface.sharedMaterial = material;
                material.SetColor("_Color", Color.white);
                material.SetFloat("_Cull", 0);
                material.SetFloat("_UseEmission", 1);
                material.SetFloat("_UseEmission2nd", 1);
                material.SetColor("_EmissionColor", new Color(4, 2, 1, 1));
                material.SetColor("_Emission2ndColor", new Color(1, 4, 2, 1));
                material.SetFloat("_EmissionBlend", 0.5f);
                material.SetFloat("_Emission2ndBlend", 0.25f);
                material.SetFloat("_EmissionMainStrength", 0);
                material.SetFloat("_Emission2ndMainStrength", 0);
                material.SetFloat("_EmissionFluorescence", 0);
                material.SetFloat("_Emission2ndFluorescence", 0);
                material.SetVector("_EmissionBlink", Vector4.zero);
                material.SetVector("_Emission2ndBlink", Vector4.zero);
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                var firstResult = Render();
                Near(DrawDirect(), new Color(2.25f, 2, 1, 1), "direct seven-MRT shader output");
                Near(firstResult, new Color(2.25f, 2, 1, 1), "two-layer HDR sum");
                Near(Read(target), new Color(2.25f, 2, 1, 1), "emission debug output");
                Near(DrawModuleDebug(), new Color(2.25f, 2, 1, 1), "emission module debug output");
                volumeSettings.debugMode.value = HoSurfaceBufferDebugMode.All;
                Render();
                Near(Read(target, 24, 24), new Color(2.25f, 2, 1, 1), "emission in module All grid");
                volumeSettings.debugMode.value = HoSurfaceBufferDebugMode.Emission;
                Render();
                var emission = capture.rt;
                Require(emission != null, "emission global missing");
                var raw = Read(emission);
                Require(raw.a > 0.99f, "unregistered surface needs writer alpha");

                material.SetFloat("_UseEmission", 0);
                Near(Render(), new Color(0.25f, 1, 0.5f, 1), "second layer alone");
                material.SetFloat("_UseEmission2nd", 0);
                Near(Render(), Color.black, "emission disabled");
                Require(Read(capture.rt).a > 0.99f,
                    "zero emission is a valid written surface");

                material.SetFloat("_UseEmission", 1);
                var mask = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
                mask.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1));
                mask.Apply();
                material.SetTexture("_EmissionBlendMask", mask);
                Near(Render(), new Color(1, 0.5f, 0.25f, 1), "emission blend mask");
                material.SetFloat("_EmissionBlendMode", 3);
                Near(Render(), new Color(1, 0.5f, 0.25f, 1), "source independent of final surface blend mode");

                material.SetColor("_Color", new Color(1, 1, 1, 0));
                Render();
                Require(Read(capture.rt).a < 0.001f,
                    "cutout must not leave an emission writer");
                feature.SetActive(false);
                Render();
                Require(Shader.GetGlobalFloat("_HoSurfaceBufferActive") == 0, "disabled feature retained active flag");
                Near(Read(Shader.GetGlobalTexture("_HoSurfaceBufferEmissionTexture")), Color.black, "disabled feature black binding");
                material.SetColor("_Color", Color.white);
                feature.SetActive(true);
                Near(Render(), new Color(1, 0.5f, 0.25f, 1), "feature re-enable");
                feature.Dispose();
                Require(Shader.GetGlobalFloat("_HoSurfaceBufferActive") == 0, "disposed feature retained active flag");
                Near(Read(Shader.GetGlobalTexture("_HoSurfaceBufferEmissionTexture")), Color.black, "disposed feature black binding");
                Debug.Log("[HoSBEmissionValidation] PASS: HDR layers, mask, blend separation, unregistered/zero writer, cutout, reset; API=" + SystemInfo.graphicsDeviceType + "; compatibility=" + compatibility);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static Color Render()
        {
            VolumeManager.instance.Update(camera.transform, -1);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            return Read(capture.rt);
        }

        private static Color Read(Texture source, int x = 32, int y = 32)
        {
            Require(source != null, "readback texture missing");
            var sourceTarget = source as RenderTexture;
            var copy = sourceTarget != null ? sourceTarget : RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var prior = RenderTexture.active;
            var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try
            {
                if (sourceTarget == null) Graphics.Blit(source, copy);
                RenderTexture.active = copy;
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                return image.GetPixel(x, y);
            }
            finally
            {
                RenderTexture.active = prior;
                Object.DestroyImmediate(image);
                if (sourceTarget == null) RenderTexture.ReleaseTemporary(copy);
            }
        }

        private static Color DrawDirect()
        {
            var targets = new RenderTexture[7];
            var ids = new RenderTargetIdentifier[7];
            var cmd = new CommandBuffer();
            try
            {
                for (int i = 0; i < targets.Length; ++i)
                {
                    targets[i] = new RenderTexture(64, 64, i == 0 ? 24 : 0, i == 0 || i == 6 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    targets[i].Create();
                    ids[i] = targets[i];
                }
                cmd.SetRenderTarget(ids, targets[0]);
                cmd.ClearRenderTarget(RTClearFlags.All, new Color[7], 1.0f, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                cmd.DrawRenderer(surface, material, 0, material.FindPass("HO_SURFACE_BUFFER"));
                Graphics.ExecuteCommandBuffer(cmd);
                return Read(targets[6]);
            }
            finally
            {
                cmd.Release();
                foreach (var texture in targets) if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        private static Color DrawModuleDebug()
        {
            var shader = Shader.Find("Hidden/lilToon/URP/SurfaceBuffer/DebugView");
            Require(shader != null, "SurfaceBuffer debug shader missing");
            var tile = Keep(new Material(shader));
            tile.SetFloat("_HoSurfaceBufferDebugMode", 10);
            tile.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            tile.SetFloat("_HoSurfaceBufferActive", 1);
            tile.SetTexture("_HoSurfaceBufferEmissionTexture", capture.rt);
            var cmd = new CommandBuffer();
            try
            {
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.DrawProcedural(Matrix4x4.identity, tile, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                return Read(target);
            }
            finally { cmd.Release(); }
        }

        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(Color actual, Color expected, string label)
        {
            Require(Mathf.Abs(actual.r - expected.r) < 0.03f && Mathf.Abs(actual.g - expected.g) < 0.03f && Mathf.Abs(actual.b - expected.b) < 0.03f,
                label + ": expected " + expected + ", got " + actual);
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            GraphicsSettings.defaultRenderPipeline = previousDefault;
            QualitySettings.renderPipeline = previousQuality;
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            capture?.Release();
            capture = null;
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    // Copy while the RenderGraph resource is alive. CPU Shader.GetGlobalTexture
    // is not a readback contract for command-buffer-published transient handles.
    internal sealed class HoSurfaceBufferEmissionCaptureFeature : ScriptableRendererFeature
    {
        public RTHandle destination;
        public HoSurfaceBufferRendererFeature sourceFeature;
        private CapturePass pass;
        public override void Create() { pass = new CapturePass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            pass.destination = destination;
            pass.sourceFeature = sourceFeature;
            renderer.EnqueuePass(pass);
        }
        private sealed class CapturePass : ScriptableRenderPass
        {
            public RTHandle destination;
            public HoSurfaceBufferRendererFeature sourceFeature;
            public CapturePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class ClearData { }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer data)
            {
                var type = typeof(HoSurfaceBufferRendererFeature).Assembly.GetType("lilToon.URP.Extensions.SurfaceBuffer.HoSurfaceBufferRenderGraphResources", true);
                var method = typeof(ContextContainer).GetMethod("GetOrCreate").MakeGenericMethod(type);
                var args = new object[method.GetParameters().Length];
                for (int i = 0; i < args.Length; ++i) args[i] = Type.Missing;
                var resources = method.Invoke(data, args);
                var source = (TextureHandle)type.GetField("emissionTexture").GetValue(resources);
                var target = graph.ImportTexture(destination);
                if (source.IsValid())
                    graph.AddBlitPass(source, target, Vector2.one, Vector2.zero, passName: "Emission validation capture");
                else
                {
                    using (var builder = graph.AddRasterRenderPass<ClearData>("Emission validation clear", out var passData))
                    {
                        builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static (ClearData unused, RasterGraphContext context) => context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0));
                    }
                }
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var producer = typeof(HoSurfaceBufferRendererFeature).GetField("pass", flags).GetValue(sourceFeature);
                var source = producer != null ? (RTHandle)producer.GetType().GetField("emissionTexture", flags).GetValue(producer) : null;
                if (sourceFeature.isActive && source != null)
                    cmd.Blit(source.nameID, destination.nameID);
                else
                {
                    cmd.SetRenderTarget(destination.nameID);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
