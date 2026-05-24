using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ShoostPostProcessRuntimeLayer
    {
        public readonly ShoostPostProcessLayer settings;
        public readonly Material material;
        public readonly ShoostPostProcessEffectDescriptor descriptor;

        public ShoostPostProcessRuntimeLayer(
            ShoostPostProcessLayer settings,
            Material material,
            ShoostPostProcessEffectDescriptor descriptor)
        {
            this.settings = settings;
            this.material = material;
            this.descriptor = descriptor;
        }
    }
}
