using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.OIT
{
    [DisallowMultipleRendererFeature("lilToon Weighted OIT")]
    public sealed class WeightedOITRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private WeightedOITSettings settings = new WeightedOITSettings();

        private WeightedOITCompositePass compositePass;

        public WeightedOITSettings Settings => settings;

        public override void Create()
        {
            compositePass = new WeightedOITCompositePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return;
            }

            compositePass.Configure(settings);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            compositePass = null;
        }
    }

    internal sealed class WeightedOITCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Composite");

        public void Configure(WeightedOITSettings settings)
        {
            renderPassEvent = settings.compositePassEvent;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                // Rendering work starts here once accumulation and revealage targets are added.
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
