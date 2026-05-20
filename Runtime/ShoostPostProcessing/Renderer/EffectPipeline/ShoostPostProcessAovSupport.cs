using System.Collections.Generic;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessAovSupport
    {
        public static bool SupportsComposite(ShoostPostProcessEffect effect)
        {
            return ShoostPostProcessEffectDescriptor.Get(effect).SupportsAovComposite;
        }

        public static bool ContainsAovMaskedLayer(List<ShoostPostProcessRuntimeLayer> layers)
        {
            if (layers == null)
            {
                return false;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ShoostPostProcessLayer layer = layers[i]?.settings;
                if (layer != null && SupportsComposite(layer.effect) && (layer.useAovMask || layer.debugAovMask))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
