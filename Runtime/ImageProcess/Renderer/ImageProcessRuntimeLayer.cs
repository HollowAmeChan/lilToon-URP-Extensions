using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ImageProcessRuntimeLayer
    {
        public readonly ImageProcessLayer settings;
        public readonly Material material;
        public readonly ImageProcessEffectDescriptor descriptor;

        public ImageProcessRuntimeLayer(
            ImageProcessLayer settings,
            Material material,
            ImageProcessEffectDescriptor descriptor)
        {
            this.settings = settings;
            this.material = material;
            this.descriptor = descriptor;
        }
    }
}
