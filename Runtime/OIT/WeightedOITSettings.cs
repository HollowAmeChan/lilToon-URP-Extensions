using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.OIT
{
    [Serializable]
    public sealed class WeightedOITSettings
    {
        [Tooltip("Skip the OIT passes without removing the renderer feature from the renderer data.")]
        public bool enabled = true;

        [Tooltip("When the OIT composite pass should run.")]
        public RenderPassEvent compositePassEvent = RenderPassEvent.AfterRenderingTransparents;

        [Tooltip("Global strength multiplier for weighted transparency accumulation.")]
        [Min(0.0f)]
        public float weight = 1.0f;

        [Tooltip("Reject very low alpha fragments before they enter the OIT buffers.")]
        [Range(0.0f, 1.0f)]
        public float alphaClipThreshold = 0.003921569f;
    }
}
