using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class HoPostProcessStackSettings
    {
        [Tooltip("Skip the whole HoPost stack without removing the renderer feature.")]
        public bool enabled = true;

        [Tooltip("Fallback shader used by HoPost layers without an override.")]
        public Shader defaultLayerShader;
    }
}
