// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.TransparentPass
{
    internal sealed class HoTransparentStatePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ActivateSampler = new ProfilingSampler("Ho-Transparent Activate");
        private static readonly ProfilingSampler ResetSampler = new ProfilingSampler("Ho-Transparent Reset");
        private readonly float activeValue;

        private sealed class PassData
        {
            public float activeValue;
        }

        public HoTransparentStatePass(float activeValue)
        {
            this.activeValue = activeValue;
        }

        public void Setup(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
            profilingSampler = activeValue > 0.5f ? ActivateSampler : ResetSampler;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalFloat(HoTransparentShaderConstants.ActiveId, activeValue);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                activeValue > 0.5f ? "Ho-Transparent Activate" : "Ho-Transparent Reset",
                out PassData passData,
                profilingSampler))
            {
                passData.activeValue = activeValue;
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoTransparentShaderConstants.ActiveId, data.activeValue);
                });
            }
        }

        public static void ResetGlobalState()
        {
            Shader.SetGlobalFloat(HoTransparentShaderConstants.ActiveId, 0.0f);
        }
    }
}
