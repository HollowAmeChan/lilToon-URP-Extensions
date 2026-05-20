using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ShoostPostProcessRuntimeLayer
    {
        public readonly ShoostPostProcessLayer settings;
        public readonly Material material;

        public ShoostPostProcessRuntimeLayer(ShoostPostProcessLayer settings, Material material)
        {
            this.settings = settings;
            this.material = material;
        }
    }
}