#pragma warning disable CS0618, CS0672

using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    /// <summary>Feature-owned diagnostic capture, followed by a presentation after final AA/blit.</summary>
    internal abstract class HoDebugRenderPass : ScriptableRenderPass
    {
        internal const RenderPassEvent PresentationEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRendering + 4);
        private readonly HoDebugPresentationPass presentation;
        protected RTHandle DebugTarget { get; private set; }
        private readonly string debugKey;
        private static readonly ConditionalWeakTable<Camera, CompatibilitySelection> CompatibilitySelections = new();

        protected HoDebugRenderPass()
        {
            debugKey = GetType().FullName;
            presentation = new HoDebugPresentationPass(this);
        }

        static HoDebugRenderPass()
        {
            RenderPipelineManager.beginCameraRendering += (_, camera) => CompatibilitySelections.Remove(camera);
            RenderPipelineManager.endCameraRendering += (_, camera) => CompatibilitySelections.Remove(camera);
        }

        public void Enqueue(ScriptableRenderer renderer)
        {
            HoDebugGrid.EnsureOverlay();
            renderer.EnqueuePass(this);
            renderer.EnqueuePass(presentation);
        }

        protected TextureHandle CreateDebugTarget(RenderGraph graph, ContextContainer frameData)
        {
            TextureDesc desc = graph.GetTextureDesc(frameData.Get<UniversalResourceData>().activeColorTexture);
            desc.name = debugKey + " Output";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.clearBuffer = true;
            desc.clearColor = new Color(0.02f, 0.02f, 0.02f, 1);
            TextureHandle output = graph.CreateTexture(desc);
            var selection = frameData.GetOrCreate<HoDebugPresentationResources>();
            if (selection.owner == null || string.CompareOrdinal(debugKey, selection.owner.debugKey) < 0)
            {
                selection.owner = this;
                selection.output = output;
            }
            return output;
        }

        protected static Vector2Int DebugSize(RenderGraph graph, TextureHandle target)
        {
            var desc = graph.GetTextureDesc(target);
            return new Vector2Int(desc.width, desc.height);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            RTHandle output = DebugTarget;
            RenderingUtils.ReAllocateIfNeeded(ref output, desc, FilterMode.Point, TextureWrapMode.Clamp, name: debugKey + " Output");
            DebugTarget = output;
            ConfigureTarget(DebugTarget);
            ConfigureClear(ClearFlag.Color, new Color(0.02f, 0.02f, 0.02f, 1));
        }

        protected void PublishCompatibility(Camera camera)
        {
            var selection = CompatibilitySelections.GetOrCreateValue(camera);
            if (selection.owner == null || string.CompareOrdinal(debugKey, selection.owner.debugKey) < 0)
                selection.owner = this;
        }

        public void ReleaseDebugResources()
        {
            DebugTarget?.Release();
            DebugTarget = null;
        }

        private sealed class CompatibilitySelection { public HoDebugRenderPass owner; }

        private sealed class HoDebugPresentationResources : ContextItem
        {
            public HoDebugRenderPass owner;
            public TextureHandle output;
            public override void Reset() { owner = null; output = TextureHandle.nullHandle; }
        }

        private sealed class HoDebugPresentationPass : ScriptableRenderPass
        {
            private readonly HoDebugRenderPass owner;
            private sealed class PassData
            {
                public TextureHandle source, target;
                public UniversalCameraData camera;
            }

            public HoDebugPresentationPass(HoDebugRenderPass owner)
            {
                this.owner = owner;
                renderPassEvent = PresentationEvent;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var selection = frameData.GetOrCreate<HoDebugPresentationResources>();
                if (selection.owner != owner || !selection.output.IsValid()) return;
                var resources = frameData.Get<UniversalResourceData>();
                using (var builder = graph.AddRasterRenderPass<PassData>("Ho-Debug Present", out var data))
                {
                    data.source = selection.output;
                    data.target = resources.activeColorTexture;
                    data.camera = frameData.Get<UniversalCameraData>();
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.SetRenderAttachment(data.target, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    {
                        bool flip = d.camera.IsHandleYFlipped(d.source) != d.camera.IsHandleYFlipped(d.target);
                        var scale = flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);
                        ctx.cmd.SetViewport(d.camera.camera.pixelRect);
                        Blitter.BlitTexture(ctx.cmd, d.source, scale, 0, false);
                    });
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var camera = renderingData.cameraData.camera;
                if (!CompatibilitySelections.TryGetValue(camera, out var selection) || selection.owner != owner || owner.DebugTarget == null) return;
                CommandBuffer cmd = CommandBufferPool.Get("Ho-Debug Present");
                RenderTargetIdentifier target = camera.targetTexture != null
                    ? new RenderTargetIdentifier(camera.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                CoreUtils.SetRenderTarget(cmd, target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.clear);
                cmd.SetViewport(camera.pixelRect);
                bool flip = SystemInfo.graphicsUVStartsAtTop && camera.targetTexture == null && camera.cameraType != CameraType.SceneView;
                Blitter.BlitTexture(cmd, owner.DebugTarget, flip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0), 0, false);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
