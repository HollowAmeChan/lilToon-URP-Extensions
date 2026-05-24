using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessRuntimeLayerBuilder
    {
        public static void Build(
            ShoostPostProcessStackVolume volume,
            ShoostPostProcessStackSettings settings,
            ShoostPostProcessMaterialCache materialCache,
            List<ShoostPostProcessRuntimeLayer> runtimeLayers)
        {
            runtimeLayers.Clear();
            List<ShoostPostProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (ShoostPostProcessLayer layer in layers)
            {
                ShoostPostProcessEffectDescriptor descriptor = layer != null
                    ? ShoostPostProcessEffectDescriptor.Get(layer.effect)
                    : default;
                if (layer == null ||
                    !layer.IsActive ||
                    descriptor.IsRemoved)
                {
                    continue;
                }

                Material material = materialCache.ResolveMaterial(layer, settings);
                if (material == null)
                {
                    continue;
                }

                runtimeLayers.Add(new ShoostPostProcessRuntimeLayer(layer, material, descriptor));
            }

        }
    }
}
