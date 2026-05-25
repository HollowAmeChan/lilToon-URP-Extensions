using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ImageProcessRuntimeLayerBuilder
    {
        public static void Build(
            ImageProcessStackVolume volume,
            ImageProcessStackSettings settings,
            ImageProcessMaterialCache materialCache,
            List<ImageProcessRuntimeLayer> runtimeLayers)
        {
            runtimeLayers.Clear();
            List<ImageProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (ImageProcessLayer layer in layers)
            {
                ImageProcessEffectDescriptor descriptor = layer != null
                    ? ImageProcessEffectDescriptor.Get(layer.effect)
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

                runtimeLayers.Add(new ImageProcessRuntimeLayer(layer, material, descriptor));
            }

        }
    }
}
