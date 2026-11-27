using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class HoCaptureManagedProbeFeature : ScriptableRendererFeature
{
    public HoCharacterSpecializationRendererFeature Capture { get; set; }
    public RTHandle Color { get; set; }
    public RTHandle Optical { get; set; }
    public bool ExpectMain { get; set; }
    public bool ExpectAdditional { get; set; }
    Material clear, main, additional;
    Replay pass;

    public override void Create()
    {
        Release();
        var clearShader = Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/CaptureClear");
        var mainShader = Resources.Load<Shader>("HoCharacterCaptureShadowResolve");
        var additionalShader = Resources.Load<Shader>("HoAdditionalScreenShadowResolve");
        if (clearShader == null || mainShader == null || additionalShader == null)
            throw new InvalidOperationException("Capture production shaders must be available.");

        clear = CoreUtils.CreateEngineMaterial(clearShader);
        main = CoreUtils.CreateEngineMaterial(mainShader);
        additional = CoreUtils.CreateEngineMaterial(additionalShader);
        var keyword = new LocalKeyword(additionalShader, "_HO_CAPTURE_SHADOW_SURFACE");
        if (!keyword.isValid)
            throw new InvalidOperationException("The additional resolve must decode the dedicated Capture input.");
        additional.SetKeyword(keyword, true);
        pass = new Replay(this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData rendering)
    {
        if (Capture != null && Color != null && rendering.cameraData.cameraType == CameraType.Game)
            renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => Release();

    private void Release()
    {
        CoreUtils.Destroy(clear);
        CoreUtils.Destroy(main);
        CoreUtils.Destroy(additional);
    }

    private sealed class Replay : ScriptableRenderPass
    {
        private static readonly int ModeId = Shader.PropertyToID("_HoCharacterCaptureMode");
        private static readonly ShaderTagId CaptureTag = new("HoCharacterCapture");
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private readonly HoCaptureManagedProbeFeature owner;
        private readonly object realCapture;
        private readonly FieldInfo settings, filtering;
        private readonly MethodInfo recordInputs, recordResolve, useInputs, begin, end;

        public Replay(HoCaptureManagedProbeFeature owner)
        {
            this.owner = owner;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            var assembly = typeof(HoCharacterSpecializationRendererFeature).Assembly;
            var capture = assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationPass", true);
            var resolve = assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterCaptureShadowPass", true);
            realCapture = Activator.CreateInstance(capture, true);
            settings = capture.GetField("settings", Instance);
            filtering = capture.GetField("filteringSettings", Instance);
            recordInputs = capture.GetMethod("RecordCaptureShadowInputs", Instance);
            recordResolve = resolve.GetMethod("Record", Static);
            useInputs = resolve.GetMethod("UseDrawInputs", Static);
            begin = resolve.GetMethod("BeginDraw", Static);
            end = resolve.GetMethod("EndDraw", Static);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var camera = frame.Get<UniversalCameraData>();
            var rendering = frame.Get<UniversalRenderingData>();
            var lights = frame.Get<UniversalLightData>();
            var filter = new FilteringSettings(new RenderQueueRange { lowerBound = 0, upperBound = 3999 }, 1);
            var draw = RenderingUtils.CreateDrawingSettings(CaptureTag, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var list = new RendererListParams(rendering.cullResults, draw, filter);
            HoCharacterCaptureInputFrameData inputs = null;
            if (owner.Capture.CaptureShadowInputsEnabled)
            {
                settings.SetValue(realCapture, owner.Capture.Settings);
                filtering.SetValue(realCapture, filter);
                recordInputs.Invoke(realCapture, new object[] { graph, frame, camera, rendering, lights, true });
                inputs = frame.Get<HoCharacterCaptureInputFrameData>();
                if (inputs.CameraInstanceId != camera.camera.GetInstanceID() || inputs.Width != owner.Color.rt.width
                    || inputs.Height != owner.Color.rt.height || !inputs.FaceSurface.IsValid() || !inputs.EyeSurface.IsValid())
                    throw new InvalidOperationException("Capture source camera, raster size and both phases must match.");
                recordResolve.Invoke(null, new object[] { graph, frame, owner.main, owner.additional, inputs });
            }
            if ((inputs?.FaceVisibility.IsValid() ?? false) != owner.ExpectMain
                || (inputs?.EyeVisibility.IsValid() ?? false) != owner.ExpectMain
                || (inputs?.AdditionalVisibility.IsValid() ?? false) != owner.ExpectAdditional)
                throw new InvalidOperationException("Managed Capture products do not match the expected atlas availability.");

            var depth = graph.CreateTexture(new TextureDesc(owner.Color.rt.width, owner.Color.rt.height)
            {
                name = "Managed Capture readback depth/stencil",
                format = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt
            });
            var color = graph.ImportTexture(owner.Color);
            var optical = graph.ImportTexture(owner.Optical);
            AddDraw(graph, list, inputs, color, optical, depth, eye: false);
            AddDraw(graph, list, inputs, color, optical, depth, eye: true);
        }

        private sealed class PassData
        {
            public RendererListHandle list;
            public Material clear;
            public bool eye;
            public object inputs;
            public MethodInfo begin, end;
        }

        private void AddDraw(RenderGraph graph, RendererListParams list, HoCharacterCaptureInputFrameData inputs,
            TextureHandle color, TextureHandle optical, TextureHandle depth, bool eye)
        {
            using var builder = graph.AddRasterRenderPass<PassData>(eye ? "Probe real CaptureEye" : "Probe real CaptureFace", out var data);
            data.list = graph.CreateRendererList(list);
            data.clear = owner.clear;
            data.eye = eye;
            data.inputs = useInputs.Invoke(null, new object[] { builder, inputs, eye });
            data.begin = begin;
            data.end = end;
            builder.UseRendererList(data.list);
            var access = eye ? AccessFlags.ReadWrite : AccessFlags.WriteAll;
            builder.SetRenderAttachment(color, 0, access);
            builder.SetRenderAttachment(optical, 1, access);
            builder.SetRenderAttachmentDepth(depth, access);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                if (!pass.eye)
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.DepthStencil, UnityEngine.Color.clear, 1, 0);
                    CoreUtils.DrawFullScreen(context.cmd, pass.clear);
                }
                context.cmd.SetGlobalFloat(ModeId, pass.eye ? 2 : 1);
                pass.begin.Invoke(null, new object[] { context, pass.inputs, pass.eye });
                context.cmd.DrawRendererList(pass.list);
                pass.end.Invoke(null, new object[] { context.cmd, pass.inputs, pass.eye });
                context.cmd.SetGlobalFloat(ModeId, 0);
            });
        }
    }
}
