using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ImageProcessRuntimeLayer
    {
        public readonly ImageProcessLayer settings;
        public readonly Material material;
        public readonly ImageProcessEffectDescriptor descriptor;
        /// <summary>Baked GradientMap ramp (null for every other effect).</summary>
        public readonly Texture2D rampTexture;

        public ImageProcessRuntimeLayer(
            ImageProcessLayer settings,
            Material material,
            ImageProcessEffectDescriptor descriptor,
            Texture2D rampTexture = null)
        {
            this.settings = settings;
            this.material = material;
            this.descriptor = descriptor;
            this.rampTexture = rampTexture;
        }
    }
}
