#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.RT
{
    // Isolated batch validation. The two large output RTs are test readbacks, never runtime GBuffer copies.
    public static class HoRTPrimarySurfaceValidation
    {
        private static readonly List<Object> objects = new();
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static bool compatibility, previousAsync;
        private static UniversalRenderPipelineAsset pipeline;
        private static HoSurfaceBufferRendererFeature sb;
        private static HoGeometryBufferRendererFeature gb;
        private static HoRTPrimaryProbeFeature probe;
        private static RTHandle raster, compute;
        private static RTHandle legacyMaterial;
        private static HoSurfaceBufferVolume volume;
        private static Camera camera;
        private static Material material;
        private static GameObject surface, foreground;
        private static RenderTexture target;
        private static int frame;
        private static readonly Color BaseColor = new Color(2.5f, .75f, .25f, 1);

        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void Run()
        {
            try
            {
                Debug.Log("[HoRTPrimarySurfaceValidation] START");
                previousDefault = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()); gb.Settings.maxRenderQueue = (int)RenderQueue.GeometryLast;
                sb = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>()); sb.Settings.enableSemanticLanes = sb.Settings.enableCorrelatedSemantics = false;
                renderer.rendererFeatures.Add(gb); renderer.rendererFeatures.Add(sb);
                probe = Keep(ScriptableObject.CreateInstance<HoRTPrimaryProbeFeature>());
                probe.material = Mat("Hidden/lilToon/URP/RayTracing/PrimarySurfaceValidation");
                probe.shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RT/HoRTPrimarySurfaceValidation.compute");
                Require(probe.shader != null, "Missing compute probe");
                raster = Output(false); compute = Output(true); probe.raster = raster; probe.compute = compute;
                var legacy = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false, true)); legacy.SetPixel(0, 0, new Color(.4f, .4f, 0, 0)); legacy.Apply();
                legacyMaterial = RTHandles.Alloc(legacy); probe.legacyMaterial = legacyMaterial;
                renderer.rendererFeatures.Add(probe);
                pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Isolated project must define URP_COMPATIBILITY_MODE");
                #endif
                camera = Keep(new GameObject("Primary surface camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -3); camera.nearClipPlane = .1f; camera.farClipPlane = 50;
                camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1; camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                var holder = Keep(new GameObject("Primary surface debug volume")).AddComponent<Volume>(); holder.isGlobal = true;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume = holder.sharedProfile.Add<HoSurfaceBufferVolume>(true);
                target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); target.Create();
                material = Mat("Hidden/lilToonCutout"); material.EnableKeyword("UNITY_UI_ALPHACLIP");
                material.SetColor("_Color", BaseColor); material.SetTexture("_MainTex", Texture2D.whiteTexture); material.SetFloat("_Cull", 0);
                material.SetFloat("_Smoothness", .6f); material.SetFloat("_Metallic", .4f); material.SetFloat("_Reflectance", .12f); material.SetFloat("_GSAAStrength", 0);
                material.SetFloat("_UseEmission", 1); material.SetColor("_EmissionColor", new Color(6, 3, 1.5f, 1));
                material.SetFloat("_EmissionBlend", .5f); material.SetFloat("_EmissionMainStrength", 0); material.SetFloat("_EmissionFluorescence", 0);
                material.SetVector("_EmissionBlink", Vector4.zero);
                var normal = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false, true)); normal.SetPixel(0, 0, new Color(1, .5f, 1, .8f)); normal.Apply();
                material.SetTexture("_BumpMap", normal); material.SetFloat("_UseBumpMap", 1); material.SetFloat("_BumpScale", 1);
                surface = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); surface.transform.localScale = new Vector3(3, 3, 1); surface.GetComponent<Renderer>().sharedMaterial = material;
                foreground = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); foreground.transform.position = new Vector3(0, 0, -1);
                var unlit = Mat("Universal Render Pipeline/Unlit"); unlit.SetFloat("_Cull", 0); foreground.GetComponent<Renderer>().sharedMaterial = unlit; foreground.SetActive(false);
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                if (++frame < 3) { Render(); return; }
                Validate("orthographic", Render());
                volume.debugMode.value = HoSurfaceBufferDebugMode.GeometryNormal; Render(); NearRGB(ReadMain(), new Vector3(.5f, .5f, 0), "geometry normal debug");
                volume.debugMode.value = HoSurfaceBufferDebugMode.Normal; Render();
                Require(ReadMain().r > .7f, "shading normal debug should retain normal map");
                volume.debugMode.value = HoSurfaceBufferDebugMode.All; Render(); NearRGB(ReadMain(8, 8), new Vector3(.5f, .5f, 0), "geometry normal All tile");
                volume.debugMode.value = HoSurfaceBufferDebugMode.Off;
                camera.orthographic = false; Validate("perspective", Render());
                camera.projectionMatrix = ShiftedProjection(); Validate("lens shift / jitter-shaped projection", Render()); camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(new Vector3(.1f, .2f, -4), Quaternion.identity); Validate("moved camera", Render());
                camera.transform.rotation = Quaternion.Euler(3, 4, 0); Validate("rotated camera", Render(), false); camera.transform.rotation = Quaternion.identity;
                camera.orthographic = true; Validate("moved orthographic", Render());
                camera.transform.position = new Vector3(0, 0, -3);
                probe.includeOwner = true;
                Require(HoGeometryRendererBinding.SetObjectIdentity(surface.GetComponent<Renderer>(), 0xbeefu), "Renderer identity binding unavailable");
                var values = Render(); foreach (var image in values) { Near(Pixel(image, 5).a, 48879, "optional identity", .1f); Near(Pixel(image, 6).g, 1, "owner present"); }
                probe.includeOwner = false; foreach (var image in Render()) { Near(Pixel(image, 5).a, 0, "identity omitted"); Near(Pixel(image, 6).g, 0, "owner not requested"); }
                HoGeometryRendererBinding.SetObjectIdentity(surface.GetComponent<Renderer>(), 0);
                material.SetColor("_Color", Color.black); material.SetFloat("_UseEmission", 0); material.SetFloat("_Smoothness", 1); material.SetFloat("_Metallic", 0); material.SetFloat("_Reflectance", 0);
                values = Render(); foreach (var image in values) { Valid(image, "black/zero writer"); NearRGB(Pixel(image, 4), Vector3.zero, "zero main colour"); NearRGB(Pixel(image, 5), Vector3.zero, "zero emission"); }
                material.SetColor("_Color", BaseColor);
                foreground.SetActive(true); Invalid(Render(), 4, "occluded SB rejected"); foreground.SetActive(false);
                material.SetColor("_Color", new Color(1, 1, 1, 0)); Invalid(Render(), 1, "cutout rejected"); material.SetColor("_Color", BaseColor);
                gb.Settings.layerMask = 0; Invalid(Render(), 2, "geometry missing"); gb.Settings.layerMask = -1;
                gb.Settings.renderScale = HoGeometryBufferRenderScale.Half; Invalid(Render(), 3, "sample domain mismatch"); gb.Settings.renderScale = HoGeometryBufferRenderScale.Full;
                probe.query = 1; Invalid(Render(), 6, "UV bounds"); probe.query = 2; Invalid(Render(), 6, "pixel bounds"); probe.query = 0;
                probe.missingAttributes = true; Invalid(Render(), 8, "missing material channel"); probe.missingAttributes = false;
                probe.legacyLayout = true; Invalid(Render(), 8, "old writer without mesh-normal marker"); probe.legacyLayout = false;
                camera.projectionMatrix = camera.CalculateObliqueMatrix(new Vector4(.1f, 0, 1, -.5f)); Invalid(Render(), 7, "oblique projection unsupported"); camera.ResetProjectionMatrix();
                gb.SetActive(false); Invalid(Render(), 0, "GB disabled"); gb.SetActive(true);
                sb.SetActive(false); Invalid(Render(), 0, "SB disabled"); sb.SetActive(true);
                material.SetColor("_Color", BaseColor); material.SetFloat("_UseEmission", 1); material.SetFloat("_Smoothness", .6f); material.SetFloat("_Metallic", .4f); material.SetFloat("_Reflectance", .12f);
                Validate("re-enabled", Render());
                foreach (var message in ShaderUtil.GetComputeShaderMessages(probe.shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,
                        "Compute shader diagnostic: " + message.message);
                Debug.Log("[HoRTPrimarySurfaceValidation] PASS: raster + explicit compute bindings, mono ortho/perspective/shifted/moved cameras, pixel-centre positions, separate normals, raw HDR/material/emission, optional owner, failure zeroing, no compute warnings/errors; API=" + SystemInfo.graphicsDeviceType + "; compatibility=" + compatibility);
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static Matrix4x4 ShiftedProjection() { var p = camera.projectionMatrix; p.m02 += .09f; p.m12 -= .07f; return p; }
        private static Color[][] Render()
        {
            VolumeManager.instance.Update(camera.transform, -1);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            return new[] { Read(raster.rt), Read(compute.rt) };
        }
        private static void Validate(string label, Color[][] images, bool offAxis = true)
        {
            foreach (var image in images)
            {
                Valid(image, label);
                foreach (int x in offAxis ? new[] { 16, 32, 48 } : new[] { 32 })
                {
                    int y = x;
                    var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 64, (y + .5f) / 64, 0));
                    var expected = ray.origin + ray.direction * (-ray.origin.z / ray.direction.z);
                    NearRGB(Pixel(image, 0, x, y), expected, label + " world position", .012f);
                    NearRGB(Pixel(image, 7, x, y), Vector3.zero, label + " UV snaps to pixel centre");
                    var expectedView = camera.orthographic ? -camera.transform.forward : (camera.transform.position - expected).normalized;
                    NearRGB(Pixel(image, 3, x, y), expectedView, label + " view direction");
                }
                var centreRay = camera.ViewportPointToRay(new Vector3(32.5f / 64, 32.5f / 64, 0));
                var centrePosition = centreRay.origin + centreRay.direction * (-centreRay.origin.z / centreRay.direction.z);
                Near(Pixel(image, 1).a, -camera.worldToCameraMatrix.MultiplyPoint3x4(centrePosition).z, label + " eye depth", .025f);
                NearRGB(Pixel(image, 1), new Vector3(0, 0, -1), label + " geometric normal");
                var shading = (Vector3)(Vector4)Pixel(image, 2);
                Require(Mathf.Abs(shading.x) > .4f && shading.z < -.6f && Mathf.Abs(shading.magnitude - 1) < .03f, label + " shading normal was not separately decoded: " + shading);
                Near(Pixel(image, 2).a, .4f, label + " metallic"); Near(Pixel(image, 3).a, .4f, label + " perceptual roughness (not squared)");
                NearRGB(Pixel(image, 4), new Vector3(BaseColor.r, BaseColor.g, BaseColor.b), label + " HDR main colour"); Near(Pixel(image, 4).a, .12f, label + " raw reflectance");
                NearRGB(Pixel(image, 5), new Vector3(3, 1.5f, .75f), label + " dedicated emission");
            }
        }
        private static void Valid(Color[] image, string label) { Near(Pixel(image, 0).a, 5, label + " status"); Near(Pixel(image, 6).r, 1, label + " valid"); }
        private static void Invalid(Color[][] images, int status, string label)
        {
            foreach (var image in images)
            {
                Near(Pixel(image, 0).a, status, label + " status"); Near(Pixel(image, 6).r, 0, label + " invalid");
                for (int band = 0; band < 6; ++band) NearRGB(Pixel(image, band), Vector3.zero, label + " cleared payload " + band);
            }
        }
        private static Color Pixel(Color[] image, int band, int x = 32, int y = 32) => image[(band * 64 + y) * 64 + x];
        private static Color ReadMain(int x = 32, int y = 32)
        {
            var previous = RenderTexture.active; var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply(); return image.GetPixel(x, y); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static Color[] Read(RenderTexture source)
        {
            var previous = RenderTexture.active; var image = new Texture2D(64, 512, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = source; image.ReadPixels(new Rect(0, 0, 64, 512), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static RTHandle Output(bool randomWrite) { var rt = Keep(new RenderTexture(64, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = randomWrite }); rt.Create(); return RTHandles.Alloc(rt); }
        private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private static Material Mat(string name) { var shader = Shader.Find(name); Require(shader != null, "Missing shader " + name); return Keep(new Material(shader)); }
        private static void NearRGB(Color actual, Vector3 expected, string label, float tolerance = .025f) { Near(actual.r, expected.x, label + " X", tolerance); Near(actual.g, expected.y, label + " Y", tolerance); Near(actual.b, expected.z, label + " Z", tolerance); }
        private static void Near(float actual, float expected, string label, float tolerance = .025f) => Require(Mathf.Abs(actual - expected) < tolerance, label + ": expected " + expected + ", got " + actual);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality; ShaderUtil.allowAsyncCompilation = previousAsync;
            raster?.Release(); compute?.Release(); legacyMaterial?.Release(); raster = compute = legacyMaterial = null;
            for (int i = objects.Count - 1; i >= 0; --i) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoRTPrimaryProbeFeature : ScriptableRendererFeature
    {
        public Material material;
        public ComputeShader shader;
        public RTHandle raster, compute, legacyMaterial;
        public int query;
        public bool includeOwner, missingAttributes, legacyLayout;
        private ProbePass pass;
        public override void Create() { pass = new ProbePass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { pass.owner = this; renderer.EnqueuePass(pass); }
        private sealed class ProbePass : ScriptableRenderPass
        {
            public HoRTPrimaryProbeFeature owner;
            public ProbePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            private sealed class Data { public HoRTPrimarySurfaceInputs input; public HoRTPrimaryProbeFeature owner; public TextureHandle output, legacy; public MaterialPropertyBlock properties; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var sb = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>(); var reflection = sb.reflectionTexture;
                if (owner.missingAttributes) sb.reflectionTexture = TextureHandle.nullHandle;
                using (var builder = graph.AddRasterRenderPass<Data>("Primary surface raster validation", out var d))
                {
                    d.input = HoRTPrimarySurfaceInputs.Read(graph, frame, builder, owner.includeOwner); d.owner = owner;
                    if (owner.legacyLayout) { d.legacy = graph.ImportTexture(owner.legacyMaterial); builder.UseTexture(d.legacy); }
                    d.properties = new MaterialPropertyBlock(); d.properties.SetInteger("_TestQuery", owner.query);
                    d.properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    builder.SetRenderAttachment(graph.ImportTexture(owner.raster), 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data data, RasterGraphContext ctx) =>
                    {
                        data.input.Bind(ctx.cmd); ctx.cmd.SetViewport(new Rect(0, 0, 64, 512));
                        if (data.legacy.IsValid()) ctx.cmd.SetGlobalTexture("_HoRTPrimaryMaterial", data.legacy);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.owner.material, 0, MeshTopology.Triangles, 3, 1, data.properties);
                    });
                }
                using (var builder = graph.AddComputePass<Data>("Primary surface compute validation", out var d))
                {
                    d.input = HoRTPrimarySurfaceInputs.Read(graph, frame, builder, owner.includeOwner); d.owner = owner; d.output = graph.ImportTexture(owner.compute);
                    if (owner.legacyLayout) { d.legacy = graph.ImportTexture(owner.legacyMaterial); builder.UseTexture(d.legacy); }
                    builder.UseTexture(d.output, AccessFlags.WriteAll); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data data, ComputeGraphContext ctx) =>
                    {
                        data.input.Bind(ctx.cmd, data.owner.shader, 0);
                        if (data.legacy.IsValid()) ctx.cmd.SetComputeTextureParam(data.owner.shader, 0, "_HoRTPrimaryMaterial", data.legacy);
                        ctx.cmd.SetComputeIntParam(data.owner.shader, Shader.PropertyToID("_TestQuery"), data.owner.query);
                        ctx.cmd.SetComputeTextureParam(data.owner.shader, 0, "_TestOutput", data.output); ctx.cmd.DispatchCompute(data.owner.shader, 0, 8, 64, 1);
                    });
                }
                sb.reflectionTexture = reflection;
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var camera = data.cameraData; var cmd = CommandBufferPool.Get();
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd, camera, owner.includeOwner);
                if (owner.legacyLayout) cmd.SetGlobalTexture("_HoRTPrimaryMaterial", owner.legacyMaterial.nameID);
                var properties = new MaterialPropertyBlock(); properties.SetInteger("_TestQuery", owner.query); properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                cmd.SetRenderTarget(owner.raster.nameID); cmd.SetViewport(new Rect(0, 0, 64, 512));
                cmd.DrawProcedural(Matrix4x4.identity, owner.material, 0, MeshTopology.Triangles, 3, 1, properties);
                HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd, owner.shader, 0, camera, owner.includeOwner);
                if (owner.legacyLayout) cmd.SetComputeTextureParam(owner.shader, 0, "_HoRTPrimaryMaterial", owner.legacyMaterial.nameID);
                // Simulate a missing channel in the same explicit state used by the runtime helper.
                if (owner.missingAttributes)
                {
                    cmd.SetGlobalVector("_HoRTPrimaryState", new Vector4(0, 1, 0, 0));
                    // The raster draw above has already been recorded; update and draw again.
                    cmd.DrawProcedural(Matrix4x4.identity, owner.material, 0, MeshTopology.Triangles, 3, 1, properties);
                    cmd.SetComputeVectorParam(owner.shader, Shader.PropertyToID("_HoRTPrimaryState"), new Vector4(0, 1, 0, 0));
                }
                cmd.SetComputeIntParam(owner.shader, Shader.PropertyToID("_TestQuery"), owner.query);
                cmd.SetComputeTextureParam(owner.shader, 0, "_TestOutput", owner.compute.nameID); cmd.DispatchCompute(owner.shader, 0, 8, 64, 1);
                CoreUtils.SetRenderTarget(cmd, data.cameraData.renderer.cameraColorTargetHandle, data.cameraData.renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
