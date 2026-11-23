#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    internal sealed class HoDebugPerformanceOverlayPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public TextureHandle target;
            public Vector2Int size;
            public Rect viewport;
            public string[] lines;
        }

        private IReadOnlyList<HoDebugPerformanceLine> lines;

        public void Setup(IReadOnlyList<HoDebugPerformanceLine> values)
        {
            lines = values;
            renderPassEvent = (RenderPassEvent)((int)HoDebugRenderPass.PresentationEvent + 1);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid() || lines == null || lines.Count == 0)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            bool backBufferActive = resources.isActiveTargetBackBuffer;
            TextureDesc desc = backBufferActive
                ? default
                : graph.GetTextureDesc(resources.activeColorTexture);
            int width = backBufferActive ? cameraData.cameraTargetDescriptor.width : desc.width;
            int height = backBufferActive ? cameraData.cameraTargetDescriptor.height : desc.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            using (var builder = graph.AddRasterRenderPass<PassData>("Ho-Debug Performance Overlay", out PassData data))
            {
                data.target = resources.activeColorTexture;
                data.size = new Vector2Int(width, height);
                data.viewport = cameraData.camera.pixelRect;
                if (data.viewport.width <= 0.0f || data.viewport.height <= 0.0f)
                {
                    data.viewport = new Rect(0, 0, width, height);
                }
                data.lines = CopyLines(lines);
                builder.SetRenderAttachment(data.target, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    HoDebugTextOverlay.DrawLines(context.cmd, passData.lines, passData.size, passData.viewport);
                });
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;
            RenderTargetIdentifier target = camera.targetTexture != null
                ? new RenderTargetIdentifier(camera.targetTexture)
                : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            CommandBuffer cmd = CommandBufferPool.Get("Ho-Debug Performance Overlay");
            CoreUtils.SetRenderTarget(cmd, target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.clear);
            Rect viewport = camera.pixelRect;
            if (viewport.width <= 0.0f || viewport.height <= 0.0f)
            {
                viewport = new Rect(0, 0, Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight));
            }
            HoDebugTextOverlay.DrawLines(CommandBufferHelpers.GetRasterCommandBuffer(cmd), CopyLines(lines),
                new Vector2Int(Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight)), viewport);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static string[] CopyLines(IReadOnlyList<HoDebugPerformanceLine> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i].Text;
            return result;
        }
    }
}
