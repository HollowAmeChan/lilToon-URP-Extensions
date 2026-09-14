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

                Texture2D rampTexture = layer.effect == ImageProcessEffect.GradientMap
                    ? ImageProcessGradientRampCache.GetRamp(layer)
                    : null;
                runtimeLayers.Add(new ImageProcessRuntimeLayer(layer, material, descriptor, rampTexture));
            }

            // Ramp textures belong to the layers that asked for them; drop the ones no camera has
            // rendered for a while so a deleted or re-typed layer cannot leak its texture.
            ImageProcessGradientRampCache.PruneUnusedLayers(Time.frameCount);
        }
    }
}
