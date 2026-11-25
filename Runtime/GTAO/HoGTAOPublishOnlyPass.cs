#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GTAO
{
    // The producer owns the output; attachments borrow it without publishing.
    internal static class HoAOCompatibilityPublication
    {
        internal static Camera Camera;
        internal static RTHandle Texture;
        internal static void Reset() { Camera = null; Texture = null; }
    }

    internal sealed class HoGTAOPublishOnlyPass : ScriptableRenderPass
    {
        private sealed class PublishData { }
        private readonly Dictionary<int, RTHandle> targets = new Dictionary<int, RTHandle>();

        internal HoGTAOPublishOnlyPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        internal static RenderTextureDescriptor Descriptor(RenderTextureDescriptor camera)
        {
            camera.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm : GraphicsFormat.R16G16B16A16_SFloat;
            camera.depthStencilFormat = GraphicsFormat.None;
            camera.depthBufferBits = 0;
            camera.msaaSamples = 1;
            camera.bindMS = false;
            camera.useMipMap = false;
            camera.autoGenerateMips = false;
            camera.enableRandomWrite = false;
            camera.memoryless = RenderTextureMemoryless.None;
            camera.sRGB = false;
            return camera;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            var descriptor = Descriptor(camera.cameraTargetDescriptor);
            var desc = new TextureDesc(descriptor.width, descriptor.height)
            {
                name = "_HoAOTexture",
                colorFormat = descriptor.graphicsFormat,
                dimension = descriptor.dimension,
                slices = descriptor.volumeDepth,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.white
            };
            TextureHandle target = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PublishData>("Ho-GTAO Publish White AO", out _))
            {
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(target, HoGTAOShaderConstants.AOTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PublishData _, RasterGraphContext context) =>
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.AOAvailableId, 1f));
            }
            var output = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            output.aoTexture = target;
            output.canAppend = true;
            output.cameraId = camera.camera.GetInstanceID();
            output.size = new Vector2Int(descriptor.width, descriptor.height);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            int id = camera.GetInstanceID();
            targets.TryGetValue(id, out RTHandle target);
            var desc = Descriptor(renderingData.cameraData.cameraTargetDescriptor);
            RenderingUtils.ReAllocateIfNeeded(ref target, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_HoAOTexture");
            targets[id] = target;
            var cmd = CommandBufferPool.Get("Ho-GTAO Publish White AO");
            CoreUtils.SetRenderTarget(cmd, target, ClearFlag.Color, Color.white);
            cmd.SetGlobalTexture(HoGTAOShaderConstants.AOTextureId, target);
            cmd.SetGlobalFloat(HoGTAOShaderConstants.AOAvailableId, 1f);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            HoAOCompatibilityPublication.Camera = camera;
            HoAOCompatibilityPublication.Texture = target;
        }

        internal void Dispose()
        {
            foreach (RTHandle target in targets.Values) target.Release();
            targets.Clear();
            HoAOCompatibilityPublication.Reset();
        }
    }
}
