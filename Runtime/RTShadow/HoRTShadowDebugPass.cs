#pragma warning disable CS0618, CS0672
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTShadow
{
    public static class HoRTShadowDebugViewInfo
    {
        public static readonly HoDebugViewInfo[] Views = HoDebugGrid.EnumViews<HoRTShadowDebugMode>(
            "RTShadow", "Hidden/lilToon/URP/RTShadow/Debug",
            "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTShadow/Shaders/HoRTShadowDebug.shader",
            "VISIBILITY", "RAW", "DIFFERENCE", "BLOCKER", "RECEIVER", "STATUS");
    }

    internal sealed class HoRTShadowDebugPass : HoDebugRenderPass
    {
        private static readonly int ModeId = Shader.PropertyToID("_HoRTShadowDebugMode");
        private Material material;
        private int mode, slot;
        private float distance;
        private HoRTShadowRendererFeature.Output output;
        internal void Setup(Material material, HoRTShadowSettings settings, HoRTShadowRendererFeature.Output output)
        {
            this.material = material; this.output = output;
            mode = (int)settings.debugMode; slot = settings.debugLight; distance = Mathf.Max(.0001f, settings.debugDistance);
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }
        private sealed class Data
        { internal Material material; internal int mode; internal Vector2Int size; internal TextureHandle raw, visibility; internal Vector4 parameters; }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var products = frame.GetOrCreate<HoRTShadowRenderGraphResources>();
            TextureHandle raw = graph.defaultResources.blackTexture, visibility = graph.defaultResources.blackTexture;
            bool available = false;
            foreach (var product in products.Products)
                if (product.Status == HoRTShadowProductStatus.Available && product.Slot == slot)
                { raw = product.Raw; visibility = product.Visibility; available = raw.IsValid() && visibility.IsValid(); break; }
            var target = CreateDebugTarget(graph, frame);
            using (var builder = graph.AddRasterRenderPass<Data>("Ho-RTShadow debug", out var data))
            {
                data.material = material; data.mode = mode; data.size = DebugSize(graph, target);
                data.raw = raw; data.visibility = visibility; data.parameters = new Vector4(available ? 1 : 0, distance, 0, 0);
                builder.UseTexture(raw); builder.UseTexture(visibility); builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture("_HoRTShadowDebugRaw", d.raw);
                    context.cmd.SetGlobalTexture("_HoRTShadowDebugVisibility", d.visibility);
                    context.cmd.SetGlobalVector("_HoRTShadowDebugParams", d.parameters);
                    HoDebugGrid.Draw(context.cmd, d.material, d.mode, ModeId, HoRTShadowDebugViewInfo.Views, d.size);
                });
            }
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            bool available = output != null && output.lastView.IsValid && output.producedSerial == output.renderSerial;
            var cmd = CommandBufferPool.Get("Ho-RTShadow debug");
            try
            {
                CoreUtils.SetRenderTarget(cmd, DebugTarget);
                cmd.SetGlobalTexture("_HoRTShadowDebugRaw", available ? output.raw.nameID : new RenderTargetIdentifier(Texture2D.blackTexture));
                cmd.SetGlobalTexture("_HoRTShadowDebugVisibility", available ? output.Visibility.nameID : new RenderTargetIdentifier(Texture2D.blackTexture));
                cmd.SetGlobalVector("_HoRTShadowDebugParams", new Vector4(available ? 1 : 0, distance, 0, 0));
                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), material, mode, ModeId, HoRTShadowDebugViewInfo.Views,
                    new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height));
                context.ExecuteCommandBuffer(cmd);
                PublishCompatibility(data.cameraData.camera);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }
}
