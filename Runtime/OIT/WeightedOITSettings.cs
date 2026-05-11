using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.OIT
{
    public enum WeightedOITRenderScale
    {
        Full = 1,
        Half = 2,
        Quarter = 4
    }

    [Serializable]
    public sealed class WeightedOITSettings
    {
        [Tooltip("Skip the OIT passes without removing the renderer feature from the renderer data.")]
        public bool enabled = true;

        [Tooltip("Layers that can write to the OIT accumulation buffers.")]
        public LayerMask layerMask = -1;

        [Tooltip("Lowest render queue included in the OIT accumulation pass. lilToon transparent shaders often use AlphaTest+10.")]
        public int minRenderQueue = (int)RenderQueue.AlphaTest + 1;

        [Tooltip("Highest render queue included in the OIT accumulation pass.")]
        public int maxRenderQueue = (int)RenderQueue.Overlay - 1;

        [Tooltip("When transparent OIT objects are drawn into accumulation buffers.")]
        public RenderPassEvent accumulationPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Tooltip("When the OIT composite pass writes back to the camera color target.")]
        public RenderPassEvent compositePassEvent = RenderPassEvent.AfterRenderingTransparents;

        [Tooltip("Lower resolutions reduce bandwidth at the cost of transparent edge quality.")]
        public WeightedOITRenderScale renderScale = WeightedOITRenderScale.Full;

        [Tooltip("Composite shader. If empty, the feature tries to find Hidden/lilToon/URP/WeightedOITComposite.")]
        public Shader compositeShader;

        [Tooltip("Global strength multiplier for weighted transparency accumulation.")]
        [Min(0.0f)]
        public float weight = 1.0f;

        [Tooltip("Reject very low alpha fragments before they enter the OIT buffers.")]
        [Range(0.0f, 1.0f)]
        public float alphaClipThreshold = 0.003921569f;
    }
}
