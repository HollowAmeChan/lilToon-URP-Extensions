using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.TransparentPass
{
    [Serializable]
    public sealed class HoTransparentSettings
    {
        [InspectorName("Enabled")]
        public bool enabled = true;

        [InspectorName("Layer Mask")]
        public LayerMask layerMask = -1;

        [InspectorName("Min Render Queue")]
        public int minRenderQueue = (int)RenderQueue.AlphaTest + 1;

        [InspectorName("Max Render Queue")]
        public int maxRenderQueue = (int)RenderQueue.Overlay - 1;

        [InspectorName("Activate Pass Event")]
        public RenderPassEvent activatePassEvent = RenderPassEvent.AfterRenderingSkybox;

        [InspectorName("Draw Pass Event")]
        public RenderPassEvent drawPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [InspectorName("Reset Pass Event")]
        public RenderPassEvent resetPassEvent = RenderPassEvent.AfterRenderingTransparents;

        [InspectorName("Publish Active Flag")]
        public bool publishActiveFlag = true;

        [InspectorName("Passes")]
        public HoTransparentPassDescriptor[] passes =
        {
            HoTransparentPassDescriptor.Backface(),
            HoTransparentPassDescriptor.Frontface()
        };

        public RenderQueueRange RenderQueueRange
        {
            get
            {
                int lower = Mathf.Min(minRenderQueue, maxRenderQueue);
                int upper = Mathf.Max(minRenderQueue, maxRenderQueue);
                return new RenderQueueRange
                {
                    lowerBound = lower,
                    upperBound = upper
                };
            }
        }

        public bool HasActivePasses
        {
            get
            {
                if (passes == null)
                {
                    return false;
                }

                for (int i = 0; i < passes.Length; i++)
                {
                    if (passes[i] != null && passes[i].IsValid)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void EnsurePasses()
        {
            if (passes != null && passes.Length > 0)
            {
                return;
            }

            passes = new[]
            {
                HoTransparentPassDescriptor.Backface(),
                HoTransparentPassDescriptor.Frontface()
            };
        }
    }
}
