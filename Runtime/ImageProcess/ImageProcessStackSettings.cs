using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ImageProcessStackSettings
    {
        [Tooltip("Skip the whole stack without removing the renderer feature.")]
        public bool enabled = true;

        [Tooltip("Fallback blit shader used by Custom Material layers without an override.")]
        public Shader defaultLayerShader;
    }
}
