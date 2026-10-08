#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    // Run only in an isolated batch project; no scene/renderer assets are saved.
    public static class HoSurfaceAssociationValidation
    {
        private static readonly List<Object> objects = new();
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static bool compatibility, previousAsync;
        private static UniversalRenderPipelineAsset pipeline;
        private static HoSurfaceBufferRendererFeature sb;
        private static HoGeometryBufferRendererFeature gb;
        private static HoSurfaceBufferVolume volume;
        private static HoSurfaceAssociationProbeFeature probe;
        private static Camera camera;
        private static GameObject background, foreground;
        private static Material surface, frontSurface;
        private static RenderTexture target;
        private static RTHandle capture;
        private static int frame;

        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void Run()
        {
            try
            {
                Debug.Log("[HoSurfaceAssociationValidation] START");
                previousDefault = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
                gb.Settings.maxRenderQueue = (int)RenderQueue.GeometryLast;
                renderer.rendererFeatures.Add(gb);
                sb = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
                sb.Settings.enableSemanticLanes = sb.Settings.enableCorrelatedSemantics = false;
                renderer.rendererFeatures.Add(sb);
                probe = Keep(ScriptableObject.CreateInstance<HoSurfaceAssociationProbeFeature>());
                probe.material = Mat("Hidden/lilToon/URP/SurfaceBuffer/AssociationValidation");
                probe.material.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                var output = Keep(new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); output.Create();
                capture = RTHandles.Alloc(output); probe.destination = capture;
                renderer.rendererFeatures.Add(probe);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Isolated project must define URP_COMPATIBILITY_MODE");
                #endif
                var holder = Keep(new GameObject("Surface association volume")).AddComponent<Volume>(); holder.isGlobal = true;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                volume = holder.sharedProfile.Add<HoSurfaceBufferVolume>(true); volume.debugMode.value = HoSurfaceBufferDebugMode.GeometryAssociation;
                camera = Keep(new GameObject("Surface association camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -3); camera.allowHDR = true;
                camera.orthographic = true; camera.orthographicSize = 1; camera.nearClipPlane = .1f; camera.farClipPlane = 50;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
                surface = Mat("Hidden/lilToonCutout"); surface.SetColor("_Color", Color.black); surface.SetFloat("_Cull", 0);
                surface.EnableKeyword("UNITY_UI_ALPHACLIP");
                surface.SetTexture("_MainTex", Texture2D.whiteTexture);
                surface.SetFloat("_Smoothness", 1); surface.SetFloat("_Reflectance", 0); surface.SetFloat("_Metallic", 0);
                background = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); background.GetComponent<Renderer>().sharedMaterial = surface;
                var unlit = Mat("Universal Render Pipeline/Unlit"); unlit.SetFloat("_Cull", 0);
                foreground = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); foreground.transform.position = new Vector3(0, 0, -1);
                foreground.GetComponent<Renderer>().sharedMaterial = unlit; foreground.SetActive(false);
                frontSurface = Mat("Hidden/lilToonCutout"); frontSurface.SetColor("_Color", Color.white); frontSurface.SetFloat("_Cull", 0);
                frontSurface.EnableKeyword("UNITY_UI_ALPHACLIP");
                frontSurface.SetTexture("_MainTex", Texture2D.whiteTexture);
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                var value = Render();
                Debug.Log("[HoSurfaceAssociationValidation] Initial=" + value + "; debug=" + Read(target));
                Status(value, 5, "unregistered black/zero material"); Near(value.g, 3, "orthographic SB depth"); Near(value.b, 1, "writer");
                Near(Read(target).g, 1, "association debug green");
                Status(Read(capture.rt, 2, 2), 1, "background no writer");
                volume.debugMode.value = HoSurfaceBufferDebugMode.Depth; Render(); Near(Read(target).r, .75f, "depth debug");
                volume.debugMode.value = HoSurfaceBufferDebugMode.GeometryAssociation;
                foreground.SetActive(true);
                value = Render(); Status(value, 4, "GB-only foreground rejects rear SB"); Near(value.g, 3, "rear SB remains its own surface");
                Near(Read(target).g, .5f, "mismatch debug orange");
                foreground.GetComponent<Renderer>().sharedMaterial = frontSurface;
                value = Render(); Status(value, 5, "two SB writers select front"); Near(value.g, 2, "front depth");
                frontSurface.SetColor("_Color", new Color(1, 1, 1, 0));
                value = Render(); Status(value, 5, "cutout exposes rear on both producers"); Near(value.g, 3, "cutout rear depth");
                foreground.SetActive(false); surface.SetColor("_Color", new Color(0, 0, 0, 0));
                value = Render(); Status(value, 1, "cutout does not retain writer"); Near(value.b, 0, "cleared writer");
                surface.SetColor("_Color", Color.black);
                gb.Settings.layerMask = 0; Status(Render(), 2, "SB writer without GB geometry"); gb.Settings.layerMask = -1;
                gb.Settings.renderScale = HoGeometryBufferRenderScale.Half; Status(Render(), 3, "half-resolution sampling domain");
                Near(Read(target).r, 1, "domain debug yellow"); Near(Read(target).g, 1, "domain debug yellow green");
                gb.Settings.renderScale = HoGeometryBufferRenderScale.Quarter; Status(Render(), 3, "quarter-resolution sampling domain");
                gb.Settings.renderScale = HoGeometryBufferRenderScale.Full; Status(Render(), 5, "full resolution restored");
                pipeline.renderScale = .5f; Status(Render(), 5, "shared render scale"); pipeline.renderScale = 1;
                camera.orthographic = false; value = Render(); Status(value, 5, "perspective match"); Near(value.g, 3, "perspective SB depth");
                // A near-by foreground cannot be accepted by a broad depth band.
                foreground.GetComponent<Renderer>().sharedMaterial = Mat("Universal Render Pipeline/Unlit");
                foreground.transform.position = new Vector3(0, 0, -.002f); foreground.SetActive(true);
                Status(Render(), 4, "2mm foreground depth separation"); foreground.SetActive(false);
                camera.transform.rotation = Quaternion.Euler(0, 7, 0); Status(Render(), 5, "sloped view agrees"); camera.transform.rotation = Quaternion.identity;
                probe.material.SetFloat("_TestOutOfBounds", 1); Status(Render(), 6, "out of bounds rejected"); probe.material.SetFloat("_TestOutOfBounds", 0);
                gb.SetActive(false); Status(Render(), 0, "GB disabled"); gb.SetActive(true); Status(Render(), 5, "GB re-enabled");
                sb.SetActive(false); Status(Render(), 0, "SB disabled");
                Require(Shader.GetGlobalTexture(HoSurfaceBufferShaderConstants.DepthTextureId) == Texture2D.blackTexture, "disabled SB depth binding");
                sb.SetActive(true); Status(Render(), 5, "SB re-enabled");
                volume.debugMode.value = HoSurfaceBufferDebugMode.All; Render();
                // New views are indices 10/11 in the 4x4 All grid.
                Near(Read(target, 40, 24).r, .75f, "depth All tile"); Near(Read(target, 56, 24).g, 1, "association All tile");
                volume.debugMode.value = HoSurfaceBufferDebugMode.Off;
                var priorTarget = target;
                target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }); target.Create();
                pipeline.msaaSampleCount = 4; camera.allowMSAA = true; Status(Render(), 3, "resolved MSAA is not centre sample");
                Require(probe.samples > 1, "MSAA test did not enable GB multi-sampling");
                target = priorTarget; pipeline.msaaSampleCount = 1; Status(Render(), 5, "single sample restored");
                sb.Dispose(); Require(Shader.GetGlobalTexture(HoSurfaceBufferShaderConstants.DepthTextureId) == Texture2D.blackTexture, "disposed SB depth binding");
                Debug.Log("[HoSurfaceAssociationValidation] PASS: borrowed depth, ortho/perspective, zero/unregistered writer, foreground mismatch, cutout, missing geometry, resolution/MSAA domains, lifecycle, debug; API=" + SystemInfo.graphicsDeviceType + "; compatibility=" + compatibility);
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
        private static Color Read(RenderTexture source, int x = 32, int y = 32)
        {
            var previous = RenderTexture.active; var image = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); return image.GetPixel(x, y); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Status(Color value, int expected, string label) => Near(value.r, expected, label);
        private static void Near(float value, float expected, string label) => Require(Mathf.Abs(value - expected) < .015f, label + ": expected " + expected + ", got " + value);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static Material Mat(string name) { var shader = Shader.Find(name); Require(shader != null, "Missing shader " + name); return Keep(new Material(shader)); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality; ShaderUtil.allowAsyncCompilation = previousAsync;
            capture?.Release(); capture = null;
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoSurfaceAssociationProbeFeature : ScriptableRendererFeature
    {
        public Material material;
        public RTHandle destination;
        public int samples;
        private MaterialPropertyBlock properties;
        private ProbePass pass;
        public override void Create()
        {
            pass = new ProbePass(); properties = new MaterialPropertyBlock();
            properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { pass.owner = this; renderer.EnqueuePass(pass); }
        private sealed class ProbePass : ScriptableRenderPass
        {
            public HoSurfaceAssociationProbeFeature owner;
            public ProbePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data { public HoSurfaceAssociationInputs input; public Material material; public MaterialPropertyBlock properties; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                owner.samples = frame.GetOrCreate<HoGeometryBufferRenderGraphResources>().rasterSamples;
                using (var builder = graph.AddRasterRenderPass<Data>("Surface association validation", out var data))
                {
                    data.input = HoSurfaceAssociationInputs.Read(graph, frame, builder); data.material = owner.material; data.properties = owner.properties;
                    builder.SetRenderAttachment(graph.ImportTexture(owner.destination), 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        d.input.Bind(context.cmd); context.cmd.SetViewport(new Rect(0, 0, 64, 64));
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.properties);
                    });
                }
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                owner.samples = HoGeometryBufferPass.CompatibilityTargets?.MsaaSamples ?? 0;
                var cmd = CommandBufferPool.Get(); HoSurfaceAssociationInputs.BindCompatibility(cmd);
                cmd.SetRenderTarget(owner.destination.nameID); cmd.SetViewport(new Rect(0, 0, 64, 64));
                cmd.DrawProcedural(Matrix4x4.identity, owner.material, 0, MeshTopology.Triangles, 3, 1, owner.properties);
                // Do not leave the unmanaged probe target bound for later compatibility passes.
                CoreUtils.SetRenderTarget(cmd, data.cameraData.renderer.cameraColorTargetHandle, data.cameraData.renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
