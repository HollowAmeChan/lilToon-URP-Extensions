using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void ApplySkyGodRaysLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordSkyGodRaysLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            return RecordSinglePassLayer(renderGraph, source, runtimeLayer, layerIndex);
        }

        private static void ApplySkyGodRaysPropertyDefaults(ref LayerPropertyBlock properties)
        {
            if (Mathf.Max(properties.Color.r, Mathf.Max(properties.Color.g, properties.Color.b)) <= 0.0001f && properties.Color.a <= 0.0001f)
            {
                properties.Color = Color.white;
            }
            else if (properties.Color.a <= 0.0001f)
            {
                properties.Color.a = 1.0f;
            }

            if (properties.Params0.sqrMagnitude <= 0.000001f)
            {
                properties.Params0 = new Vector4(1.22f, 0.99f, 181.0f, 1.08f);
            }

            if (properties.Params1.sqrMagnitude <= 0.000001f)
            {
                properties.Params1 = new Vector4(130.0f, 85.0f, 234.0f, -53.0f);
            }

            if (properties.Params2.sqrMagnitude <= 0.000001f)
            {
                properties.Params2 = new Vector4(1.04f, 146.0f, 3.0f, 3.0f);
            }

            if (properties.Params3.sqrMagnitude <= 0.000001f)
            {
                properties.Params3 = new Vector4(32.0f, 0.36f, 0.0f, 0.21f);
            }
        }
    }
}
