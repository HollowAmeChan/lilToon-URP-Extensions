using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ScreenProcessStackSettings
    {
        [Tooltip("Skip the whole ScreenProcess stack without removing the renderer feature.")]
        public bool enabled = true;

        [Tooltip("Fallback shader used by ScreenProcess layers without an override.")]
        public Shader defaultLayerShader;

        [Tooltip("Compatibility fallback subject mask for Drop Shadow. MetadataBuffer is preferred when available.")]
        public LayerMask subjectLayerMask = -1;

        [Tooltip("Lowest render queue included in the ScreenProcess subject mask.")]
        public int subjectMinRenderQueue = 0;

        [Tooltip("Highest render queue included in the ScreenProcess subject mask.")]
        public int subjectMaxRenderQueue = (int)UnityEngine.Rendering.RenderQueue.GeometryLast;

        [Tooltip("Override shader used to render ScreenProcess subjects into the internal mask texture.")]
        public Shader subjectMaskShader;
    }
}
