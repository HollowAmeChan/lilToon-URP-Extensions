#pragma warning disable CS0618, CS0672
using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Atmosphere
{
    internal sealed class HoAtmosphereDebugPass : HoDebugRenderPass
    {
        private static readonly int ModeId = Shader.PropertyToID("_HoAtmosphereDebugMode");
        private Material material;
        private HoAtmosphereDebugMode mode;
        public HoAtmosphereDebugPass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; }
        public void Setup(HoAtmosphereDebugMode mode, Material material) { this.mode = mode; this.material = material; }
        private sealed class PassData
        {
            public Material material;
            public int mode;
            public Vector2Int size;
            public HoAtmosphereRenderGraphResources input;
            public HoAtmosphereMediumResources medium;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var input = frameData.GetOrCreate<HoAtmosphereRenderGraphResources>();
            var output = CreateDebugTarget(graph, frameData);
            var medium = frameData.GetOrCreate<HoAtmosphereMediumResources>();
            using (var builder = graph.AddRasterRenderPass<PassData>("HoAtmosphere Debug", out var data))
            {
                data.material = material; data.mode = (int)mode; data.input = input; data.medium = medium; data.size = DebugSize(graph, output);
                if (input.radianceTexture.IsValid()) builder.UseTexture(input.radianceTexture, AccessFlags.Read);
                if (medium.densityTexture.IsValid()) builder.UseTexture(medium.densityTexture, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    if (d.input.radianceTexture.IsValid()) HoAtmosphereBindings.Bind(context.cmd, d.input);
                    HoAtmosphereMediumBindings.Bind(context.cmd, d.medium, d.medium.snapshot.sampleTime);
                    HoDebugGrid.Draw(context.cmd, d.material, d.mode, ModeId, HoAtmosphereDebugViewInfo.Views, d.size);
                });
            }
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("HoAtmosphere Debug");
            CoreUtils.SetRenderTarget(cmd, DebugTarget);
            HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), material, (int)mode, ModeId, HoAtmosphereDebugViewInfo.Views,
                new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height));
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            PublishCompatibility(renderingData.cameraData.camera);
        }
    }
}
