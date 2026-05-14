using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ShoostPostProcessLayerListParameter : VolumeParameter<List<ShoostPostProcessLayer>>
    {
        public ShoostPostProcessLayerListParameter(List<ShoostPostProcessLayer> value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(List<ShoostPostProcessLayer> from, List<ShoostPostProcessLayer> to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/lilToon-Shoost/Post Process Stack"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/lilToon-Shoost/Post Process Stack", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(ShoostPostProcessRendererFeature))]
#endif
    [Serializable]
    public sealed class ShoostPostProcessStackVolume : VolumeComponent, IPostProcessComponent
    {
        public ShoostPostProcessStackVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "lilToon-Shoost Post Process Stack";
#endif
        }

        [Tooltip("Enable the Shoost-style post-process stack.")]
        public BoolParameter Enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("Scene View"), Tooltip("Render the stack for Scene view cameras.")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [Tooltip("Ordered post-process layers. The list is evaluated from top to bottom inside each injection group.")]
        public ShoostPostProcessLayerListParameter layers = new ShoostPostProcessLayerListParameter(
            new List<ShoostPostProcessLayer>(),
            true);

        public bool IsActive()
        {
            if (!active || !Enable.value || layers.value == null)
            {
                return false;
            }

            foreach (ShoostPostProcessLayer layer in layers.value)
            {
                if (layer != null && layer.IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
