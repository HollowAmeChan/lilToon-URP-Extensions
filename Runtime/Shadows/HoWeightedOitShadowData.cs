using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoWeightedOitShadowData : ContextItem
    {
        private static readonly ShaderTagId ReceiverTag = new("HoOITShadowReceiver");
        internal HoTransparentScreenShadowPass Resolver { private get; set; }

        internal HoLayeredShadowDrawInputs Record(RenderGraph graph, ContextContainer frameData,
            FilteringSettings filtering, float weight, float alphaClip, Vector4 queueRange)
        {
            if (Resolver == null)
                return default;
            TextureHandle opaque = frameData.Get<UniversalResourceData>().activeDepthTexture;
            HoTransparentShadowSurfacePass.RecordSurfaces(graph, frameData, opaque, ReceiverTag, filtering,
                out var first, out var second, true, weight, alphaClip, queueRange);
            return Resolver.RecordLayers(graph, frameData, first, second, weightedOit: true);
        }

        public override void Reset() => Resolver = null;
    }

    internal sealed class HoWeightedOitShadowSetupPass : ScriptableRenderPass
    {
        private readonly HoTransparentScreenShadowPass resolver;

        internal HoWeightedOitShadowSetupPass(HoTransparentScreenShadowPass resolver)
        {
            this.resolver = resolver;
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            frameData.GetOrCreate<HoWeightedOitShadowData>().Resolver = resolver;
        }
    }
}
