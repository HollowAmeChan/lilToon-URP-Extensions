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
            public bool flipY;
            public HoDebugPerformanceLine[] lines;
        }

        private HoDebugTilePerformanceMode mode;

        public void Setup(HoDebugTilePerformanceMode value)
        {
            mode = value;
            renderPassEvent = (RenderPassEvent)((int)HoDebugRenderPass.PresentationEvent + 1);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid())
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            // All features have finished AddRenderPasses by now, even if the HUD
            // is first in RendererData. Build from this camera's complete schedule.
            HoDebugPerformanceRegistry.Update();
            IReadOnlyList<HoDebugPerformanceLine> lines = HoDebugPerformanceRegistry.BuildLines(mode, cameraData.camera);
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
                data.flipY = SystemInfo.graphicsUVStartsAtTop
                    && cameraData.camera.cameraType != CameraType.SceneView;
                if (data.viewport.width <= 0.0f || data.viewport.height <= 0.0f)
                {
                    data.viewport = new Rect(0, 0, width, height);
                }
                data.lines = CopyLines(lines);
                builder.SetRenderAttachment(data.target, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    HoDebugTextOverlay.DrawLines(context.cmd, passData.lines, passData.size, passData.viewport, passData.flipY);
                });
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            HoDebugPerformanceRegistry.Update();
            IReadOnlyList<HoDebugPerformanceLine> lines = HoDebugPerformanceRegistry.BuildLines(mode, camera);
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
            bool flipY = SystemInfo.graphicsUVStartsAtTop
                && camera.cameraType != CameraType.SceneView;
            HoDebugTextOverlay.DrawLines(CommandBufferHelpers.GetRasterCommandBuffer(cmd), CopyLines(lines),
                new Vector2Int(Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight)), viewport, flipY);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static HoDebugPerformanceLine[] CopyLines(IReadOnlyList<HoDebugPerformanceLine> values)
        {
            HoDebugPerformanceLine[] result = new HoDebugPerformanceLine[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }
    }
}
