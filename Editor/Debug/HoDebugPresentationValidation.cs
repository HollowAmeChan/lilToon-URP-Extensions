#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.ShadowCast;
using lilToon.URP.Extensions.SubsurfaceScattering;
using lilToon.URP.Extensions.PlanarReflection;
using lilToon.URP.Extensions.SSGI;
using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    // Isolated batch project only. Does not save scenes, renderer data, or pipeline assets.
    public static class HoDebugPresentationValidation
    {
        private static readonly List<Object> Objects = new();
        private static RenderPipelineAsset previousDefault, previousQuality;
        private static UniversalRendererData renderer;
        private static Camera camera, otherCamera;
        private static RenderTexture target, normal;
        private static DebugValidationFeature debug;
        private static LateOverwriteFeature overwrite;
        private static bool compatibility, oldCompatibility, oldAsync;
        private static int checks;

        public static void RunCompatibility() { compatibility = true; Run(); }
        public static void Run()
        {
            try
            {
                previousDefault = GraphicsSettings.defaultRenderPipeline;
                previousQuality = QualitySettings.renderPipeline;
                oldAsync = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                Require(renderer.postProcessData != null, "built-in post-process shaders available for final FXAA validation");
                debug = Keep(ScriptableObject.CreateInstance<DebugValidationFeature>());
                overwrite = Keep(ScriptableObject.CreateInstance<LateOverwriteFeature>());
                target = Texture(); normal = Texture(0);
                overwrite.normal = RTHandles.Alloc(normal);
                renderer.rendererFeatures.Add(debug); renderer.rendererFeatures.Add(overwrite);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                oldCompatibility = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                Require(!compatibility, "Compatibility validation needs URP_COMPATIBILITY_MODE in the isolated project");
                #endif
                camera = Camera("Debug Camera"); otherCamera = Camera("No Debug");
                EditorApplication.delayCall += Validate;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Validate()
        {
            try
            {
                Render();
                Near(Read(normal, 64, 64), Color.green, "diagnostic did not modify normal camera color");
                Near(Read(target, 32, 32), new Color(32.5f / 128, 32.5f / 128, 0.25f, 1), "late presentation and UV orientation");
                renderer.rendererFeatures.Reverse(); renderer.SetDirty(); Render();
                Near(Read(target, 32, 32), new Color(32.5f / 128, 32.5f / 128, 0.25f, 1), "feature reorder");
                debug.SetActive(false); Render(); Near(Read(target, 64, 64), Color.magenta, "disabled debug restores final image");
                debug.SetActive(true); Render(); Near(Read(target, 64, 64), new Color(64.5f / 128, 64.5f / 128, 0.25f, 1), "debug re-enable");
                Render(otherCamera); Near(Read(target, 64, 64), Color.magenta, "camera isolation");
                Render(); Near(Read(target, 64, 64), new Color(64.5f / 128, 64.5f / 128, 0.25f, 1), "camera return");
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                Render(); Near(Read(target, 32, 32), new Color(32.5f / 128, 32.5f / 128, 0.25f, 1), "presentation after final FXAA");
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
                debug.available = false; Render(); Near(Read(target, 64, 64), Color.magenta, "missing diagnostic input does not retain previous output");
                debug.available = true;
                camera.rect = new Rect(.25f, .25f, .5f, .5f);
                var previousTarget = RenderTexture.active; RenderTexture.active = target; GL.Clear(true, true, Color.red); RenderTexture.active = previousTarget;
                Render(); Near(Read(target, 64, 64), new Color(.5f, .5f, .25f, 1), "partial viewport diagnostic");
                Near(Read(target, 8, 8), Color.red, "partial viewport preserves outside pixels");
                camera.rect = new Rect(0, 0, 1, 1);
                var rival = Keep(ScriptableObject.CreateInstance<RivalDebugFeature>());
                renderer.rendererFeatures.Insert(0, rival); renderer.SetDirty(); Render();
                Near(Read(target, 64, 64), new Color(64.5f / 128, 64.5f / 128, 0.25f, 1), "deterministic simultaneous debug selection");
                renderer.rendererFeatures.Reverse(); renderer.SetDirty(); Render();
                Near(Read(target, 64, 64), new Color(64.5f / 128, 64.5f / 128, 0.25f, 1), "simultaneous debug reorder");
                rival.SetActive(false); debug.SetActive(false);
                BindInputs();
                var grid = Keep(ScriptableObject.CreateInstance<GridValidationFeature>());
                renderer.rendererFeatures.Add(grid); renderer.SetDirty();
                CheckGrid(grid, HoObjectBufferDebugViewInfo.Views, "_HoObjectBufferDebugMode");
                CheckGrid(grid, HoGeometryBufferDebugViewInfo.Views, "_HoGeometryBufferDebugMode");
                CheckGrid(grid, HoSurfaceBufferDebugViewInfo.Views, "_HoSurfaceBufferDebugMode");
                CheckGrid(grid, HoShadowCastDebugViewInfo.Views, "_HoShadowCastDebugMode", integer: true);
                CheckGrid(grid, HoSubsurfaceScatteringDebugViewInfo.Views, "_lilHoSSSDebugParams", new Vector4(0, 0, 0, 0));
                CheckGrid(grid, HoPlanarReflectionDebugViewInfo.Views, "_HoPlanarReflectionDebugParams", new Vector4(0, 100, 32, 0));
                CheckGrid(grid, HoSSGIDebugViewInfo.Views, "_HoSSGIDebugMode", integer: true);
                CheckGrid(grid, HoGTAODebugViewInfo.Views, "_HoGTAODebugViewMode");
                CheckGrid(grid, HoDebugViewRegistry.SystemViews, "_HoDebugTileMode", integer: true);
                CheckGrid(grid, lilToon.URP.Extensions.CharacterShadow.HoCharacterShadowDebugViewInfo.Views, "_HoCSDebugMode", integer: true);
                using (var frames = new lilToon.URP.Extensions.CharacterSpecialization.HoCharacterReferenceFrameBuffer())
                {
                    Shader.SetGlobalBuffer(lilToon.URP.Extensions.CharacterSpecialization.HoCharacterReferenceFrameBuffer.BufferId, frames.Capture());
                    Shader.SetGlobalBuffer(lilToon.URP.Extensions.CharacterSpecialization.HoCharacterReferenceFrameBuffer.IdentityMapId, lilToon.URP.Extensions.CharacterSpecialization.HoCharacterReferenceFrameBuffer.IdentityMapBuffer);
                    Shader.SetGlobalInt(lilToon.URP.Extensions.CharacterSpecialization.HoCharacterReferenceFrameBuffer.FrameCountId, 0);
                    CheckGrid(grid, lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationDebugViewInfo.Views, "_HoCharacterOptions", Vector4.zero, modeComponent: 3);
                }
                grid.SetActive(false);
                CheckActualBuffers();
                CheckScreenProcessDebug();
                ValidateApvVariants();
                Require(!Enum.IsDefined(typeof(lilToon.URP.Extensions.AttributeComposite.HoAttributeCompositeDebugMode), HoDebugGrid.All), "AC retains parameterized modes");
                Debug.Log($"[HoDebugPresentationValidation] PASS checks={checks}; API={SystemInfo.graphicsDeviceType}; compatibility={compatibility}");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CheckGrid(GridValidationFeature feature, HoDebugViewInfo[] views, string modeId, Vector4? vector = null, bool integer = false, int modeComponent = 0)
        {
            feature.Configure(views, modeId, vector, integer, modeComponent);
            feature.mode = HoDebugGrid.All; Render();
            var expected = new Color[views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                Vector4 rect = HoDebugGrid.Rect(i, views.Length);
                int x = Mathf.FloorToInt((rect.x + rect.z * 0.5f) * 128);
                int y = Mathf.FloorToInt((1 - rect.y - rect.w * 0.5f) * 128);
                expected[i] = Read(target, x, y);
            }
            for (int i = 0; i < views.Length; i++)
            {
                feature.mode = views[i].ModeValue; Render();
                Near(Read(target, 64, 64), expected[i], views[i].ViewId + " grid matches fullscreen");
            }
            foreach (var message in ShaderUtil.GetShaderMessages(feature.material.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
        }

        private static void BindInputs()
        {
            var texture = Keep(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            texture.SetPixels(new[] { new Color(.2f,.4f,.6f,1), new Color(.2f,.4f,.6f,1), new Color(.2f,.4f,.6f,1), new Color(.2f,.4f,.6f,1) }); texture.Apply();
            string[] names = { "_BlitTexture", "_HoGeometryBufferNormalDepthTexture", "_HoGeometryBufferOutlineNormalDepthTexture", "_HoGeometryBufferCoverageTexture", "_HoGeometryBufferOutlineCoverageTexture", "_HoGeometryBufferSkyTexture", "_HoObjectBufferId0Texture", "_HoObjectBufferId1Texture", "_HoObjectBufferCoverageTexture", "_HoObjectBufferSelectionTexture", "_HoSurfaceBufferColorTexture", "_HoSurfaceBufferEmissionTexture", "_HoSurfaceBufferNormalTexture", "_HoSurfaceBufferMaterialTexture", "_HoSurfaceBufferReflectionTexture", "_HoSurfaceBufferClassificationTexture", "_HoSurfaceBufferOwnerTexture", "_HoSurfaceSemanticOwnerTexture", "_HoSurfaceSemanticLane0Texture", "_HoSurfaceSemanticLane1Texture", "_HoSurfaceSemanticLane2Texture", "_HoSurfaceSemanticLane3Texture", "_HoShadowCastAtlas", "_HoShadowCastSecondDirectionalAtlas", "_lilHoSSSSourceTexture", "_lilHoSSSTransmissionTexture", "_LILPBRPlanarReflectionTexture" };
            foreach (string name in names) Shader.SetGlobalTexture(name, texture);
            foreach (string name in new[] { "_HoSurfaceBufferActive", "_HoObjectBufferActive", "_HoGeometryBufferValid", "_HoGeometryBufferCoverageTextureValid", "_HoGeometryBufferSkyTextureValid", "_HoShadowCastActive", "_HoSurfaceSemanticActive" }) Shader.SetGlobalFloat(name, 1);
            Shader.SetGlobalVector("_HoPlanarReflectionDebugInputStatus", Vector4.one);
            Shader.SetGlobalVector("_HoGeometryBufferDebugDepthParams", new Vector4(0, 100, .01f, 0));
            Shader.SetGlobalFloat("_HoGTAODebugIntensity", 1); Shader.SetGlobalFloat("_HoGTAODebugInvert", 0);
            HoObjectBufferRegistry.EnsureBuilt();
        }

        private static void CheckActualBuffers()
        {
            var objectBuffer = Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>());
            objectBuffer.Settings.debugInGameView = true;
            renderer.rendererFeatures.Add(objectBuffer); renderer.SetDirty();
            CheckActual(HoObjectBufferDebugViewInfo.Views, mode => objectBuffer.Settings.debugMode = (HoObjectBufferDebugMode)mode);
            objectBuffer.SetActive(false);
            var geometryBuffer = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
            geometryBuffer.Settings.debugInGameView = true;
            renderer.rendererFeatures.Add(geometryBuffer); renderer.SetDirty();
            CheckActual(HoGeometryBufferDebugViewInfo.Views, mode => geometryBuffer.Settings.debugMode = (HoGeometryBufferDebugMode)mode);
            geometryBuffer.SetActive(false);
        }

        private static void CheckScreenProcessDebug()
        {
            var volume = Keep(new GameObject("Debug layer validation volume")).AddComponent<Volume>();
            volume.isGlobal = true; volume.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            var stack = volume.sharedProfile.Add<lilToon.URP.Extensions.PostProcessing.ScreenProcessStackVolume>(true);
            stack.Enable.value = true;
            var first = Keep(new Material(Shader.Find("Hidden/HoDebugPresentationValidation")));
            var second = Keep(new Material(first)); second.SetFloat("_ValidationRival", 1);
            stack.layers.value = new List<lilToon.URP.Extensions.PostProcessing.ScreenProcessLayer>
            {
                new() { name="First debug", effect=lilToon.URP.Extensions.PostProcessing.ScreenProcessEffect.CustomMaterial, materialOverride=first, debugMask=true },
                new() { name="Second debug", effect=lilToon.URP.Extensions.PostProcessing.ScreenProcessEffect.CustomMaterial, materialOverride=second, debugMask=true },
                new() { name="Normal layer", effect=lilToon.URP.Extensions.PostProcessing.ScreenProcessEffect.CustomMaterial, materialOverride=first }
            };
            var feature = Keep(ScriptableObject.CreateInstance<lilToon.URP.Extensions.PostProcessing.ScreenProcessRendererFeature>());
            renderer.rendererFeatures.Add(feature); renderer.SetDirty();
            VolumeManager.instance.Update(camera.transform, -1);
            Render(); Near(Read(target,64,64), Color.cyan, "ScreenProcess debug is presented after image effects");
            Near(Read(normal,64,64), new Color(64.5f/128,64.5f/128,.25f,1), "ScreenProcess normal layers do not consume debug output");
            stack.layers.value[0].debugMask=false; stack.layers.value[1].debugMask=false;
            VolumeManager.instance.Update(camera.transform, -1);
            Render(); Near(Read(target,64,64), Color.magenta, "ScreenProcess debug disable restores final image");
        }

        private static void CheckActual(HoDebugViewInfo[] views, Action<int> setMode)
        {
            setMode(HoDebugGrid.All); Render();
            var expected = new Color[views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                Vector4 rect = HoDebugGrid.Rect(i, views.Length);
                expected[i] = Read(target, Mathf.FloorToInt((rect.x + rect.z * .5f) * 128), Mathf.FloorToInt((1 - rect.y - rect.w * .5f) * 128));
            }
            for (int i = 0; i < views.Length; i++)
            {
                setMode(views[i].ModeValue); Render();
                Near(Read(target, 64, 64), expected[i], "actual producer " + views[i].ViewId);
            }
        }

        private static void ValidateApvVariants()
        {
            var shader = Shader.Find(HoDebugTileShaderConstants.ShaderName);
            var variants = Keep(new ShaderVariantCollection());
            foreach (string keyword in new[] { "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2" })
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keyword));
            variants.WarmUp();
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            Require(variants.isWarmedUp, "APV L1/L2 shader variants compiled");
        }

        private static Camera Camera(string name)
        {
            var value = Keep(new GameObject(name)).AddComponent<Camera>(); value.enabled = false;
            value.clearFlags = CameraClearFlags.SolidColor; value.backgroundColor = Color.green;
            value.allowHDR = true; value.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return value;
        }
        private static RenderTexture Texture(int depth = 24) { var value = Keep(new RenderTexture(128, 128, depth, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)); value.Create(); return value; }
        private static void Render(Camera value = null) => RenderPipeline.SubmitRenderRequest(value != null ? value : camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        private static Color Read(RenderTexture texture, int x, int y)
        {
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var result = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            result.ReadPixels(new Rect(x, y, 1, 1), 0, 0); result.Apply(); var color = result.GetPixel(0, 0);
            Object.DestroyImmediate(result); RenderTexture.active = previous; return color;
        }
        private static void Near(Color actual, Color expected, string label)
        {
            float distance = Mathf.Max(Mathf.Abs(actual.r-expected.r), Mathf.Abs(actual.g-expected.g), Mathf.Abs(actual.b-expected.b));
            Require(distance < .025f, label + $": actual={actual} expected={expected}");
        }
        private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks++; }
        private static T Keep<T>(T value) where T : Object { Objects.Add(value); return value; }
        private static void Finish(Exception error)
        {
            ShaderUtil.allowAsyncCompilation = oldAsync;
            #if URP_COMPATIBILITY_MODE
            var graphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (graphSettings != null) graphSettings.enableRenderCompatibilityMode = oldCompatibility;
            #endif
            GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality;
            overwrite?.normal?.Release();
            for (int i=Objects.Count-1;i>=0;i--) if (Objects[i] != null) Object.DestroyImmediate(Objects[i]);
            Objects.Clear();
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class DebugValidationFeature : ScriptableRendererFeature
    {
        public bool available = true;
        private APatternDebugPass pass;
        public override void Create() { pass?.ReleaseDebugResources(); pass = new APatternDebugPass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { pass.available=available; if (data.cameraData.camera.name != "No Debug") pass.Enqueue(renderer); }
        protected override void Dispose(bool disposing) { pass?.ReleaseDebugResources(); }
    }
    [SupportedOnRenderer()]
    internal sealed class RivalDebugFeature : ScriptableRendererFeature
    {
        private ZPatternDebugPass pass;
        public override void Create() { pass?.ReleaseDebugResources(); pass = new ZPatternDebugPass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { pass.Enqueue(renderer); }
        protected override void Dispose(bool disposing) { pass?.ReleaseDebugResources(); }
    }
    internal class APatternDebugPass : HoDebugRenderPass
    {
        public bool available = true;
        private readonly Material material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/HoDebugPresentationValidation"));
        private sealed class Data { public Material material; }
        public APatternDebugPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            material.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            material.SetFloat("_ValidationRival", GetType() == typeof(ZPatternDebugPass) ? 1 : 0);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (!available) return;
            var target = CreateDebugTarget(graph, frameData);
            using (var builder = graph.AddRasterRenderPass<Data>("Validation Debug Capture", out var d))
            { d.material=material; builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll); builder.SetRenderFunc(static (Data data, RasterGraphContext ctx) => { ctx.cmd.DrawProcedural(Matrix4x4.identity,data.material,0,MeshTopology.Triangles,3); }); }
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        { if (!available) return; var cmd=CommandBufferPool.Get(); cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3); PublishCompatibility(data.cameraData.camera); context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd); }
    }
    internal sealed class ZPatternDebugPass : APatternDebugPass { }

    [SupportedOnRenderer()]
    internal sealed class LateOverwriteFeature : ScriptableRendererFeature
    {
        public RTHandle normal;
        private ProbePass probe; private OverwritePass overwrite;
        public override void Create() { probe=new ProbePass(); overwrite=new OverwritePass(); }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { probe.normal=normal; renderer.EnqueuePass(probe); renderer.EnqueuePass(overwrite); }
        private sealed class ProbePass : ScriptableRenderPass
        {
            public RTHandle normal;
            private sealed class Data { public TextureHandle source; }
            public ProbePass() { renderPassEvent=(RenderPassEvent)601; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                using(var builder=graph.AddRasterRenderPass<Data>("Validation Normal Probe",out var d))
                { d.source=frameData.Get<UniversalResourceData>().activeColorTexture; builder.UseTexture(d.source,AccessFlags.Read); builder.SetRenderAttachment(graph.ImportTexture(normal),0); builder.AllowPassCulling(false); builder.SetRenderFunc(static(Data data,RasterGraphContext ctx)=>Blitter.BlitTexture(ctx.cmd,data.source,new Vector4(1,1,0,0),0,false)); }
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            { var cmd=CommandBufferPool.Get(); Blitter.BlitCameraTexture(cmd,data.cameraData.renderer.cameraColorTargetHandle,normal); context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd); }
        }
        private sealed class OverwritePass : ScriptableRenderPass
        {
            private sealed class Data { }
            public OverwritePass() { renderPassEvent=(RenderPassEvent)602; }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            { using(var builder=graph.AddRasterRenderPass<Data>("Validation Later Image Effect",out var d)) { builder.SetRenderAttachment(frameData.Get<UniversalResourceData>().activeColorTexture,0,AccessFlags.Write); builder.AllowPassCulling(false); builder.SetRenderFunc(static(Data _,RasterGraphContext ctx)=>ctx.cmd.ClearRenderTarget(RTClearFlags.Color,Color.magenta,1,0)); } }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            { var cmd=CommandBufferPool.Get(); CoreUtils.SetRenderTarget(cmd,data.cameraData.renderer.cameraColorTargetHandle); cmd.ClearRenderTarget(false,true,Color.magenta); context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd); }
        }
    }

    [SupportedOnRenderer()]
    internal sealed class GridValidationFeature : ScriptableRendererFeature
    {
        public int mode;
        public Material material;
        private HoDebugViewInfo[] views;
        private int modeId;
        private Vector4? vector;
        private bool integer;
        private int modeComponent;
        private GridPass pass;
        public override void Create() { pass?.ReleaseDebugResources(); pass=new GridPass(); }
        public void Configure(HoDebugViewInfo[] views,string modeId,Vector4? vector,bool integer,int modeComponent)
        { CoreUtils.Destroy(material); material=CoreUtils.CreateEngineMaterial(Shader.Find(views[0].ShaderName)); this.views=views; this.modeId=Shader.PropertyToID(modeId); this.vector=vector; this.integer=integer; this.modeComponent=modeComponent; }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        { pass.material=material; pass.views=views; pass.modeId=modeId; pass.vector=vector; pass.integer=integer; pass.modeComponent=modeComponent; pass.mode=mode; pass.Enqueue(renderer); }
        protected override void Dispose(bool disposing) { pass?.ReleaseDebugResources(); CoreUtils.Destroy(material); }
        private sealed class GridPass : HoDebugRenderPass
        {
            public Material material; public HoDebugViewInfo[] views; public int mode,modeId,modeComponent; public Vector4? vector; public bool integer;
            private sealed class Data { public GridPass pass; public Vector2Int size; }
            public GridPass() { renderPassEvent=(RenderPassEvent)600; }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            { var target=CreateDebugTarget(graph,frameData); using(var builder=graph.AddRasterRenderPass<Data>("Validation Module Grid",out var d)) { d.pass=this; d.size=DebugSize(graph,target); builder.SetRenderAttachment(target,0,AccessFlags.Write); builder.SetRenderFunc(static(Data data,RasterGraphContext ctx)=> { var p=data.pass; HoDebugGrid.Draw(ctx.cmd,p.material,p.mode,p.modeId,p.views,data.size,p.vector,integerMode:p.integer,modeComponent:p.modeComponent); }); } }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            { var cmd=CommandBufferPool.Get(); HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd),material,mode,modeId,views,new Vector2Int(DebugTarget.rt.width,DebugTarget.rt.height),vector,integerMode:integer,modeComponent:modeComponent); PublishCompatibility(data.cameraData.camera); context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd); }
        }
    }
}
