using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class HoPostProcessLayerListParameter : VolumeParameter<List<HoPostProcessLayer>>
    {
        public HoPostProcessLayerListParameter(List<HoPostProcessLayer> value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(List<HoPostProcessLayer> from, List<HoPostProcessLayer> to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/lilToon-HoPost/Process Stack"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/lilToon-HoPost/Process Stack", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(HoPostProcessRendererFeature))]
#endif
    [Serializable]
    public sealed class HoPostProcessStackVolume : VolumeComponent, IPostProcessComponent
    {
        public HoPostProcessStackVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "lilToon-HoPost Process Stack";
#endif
        }

        [InspectorName("Enable"), Tooltip("Enable the HoPost subject/effect stack.")]
        public BoolParameter Enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("Scene View"), Tooltip("Apply this HoPost stack in Scene View.")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [Tooltip("Ordered HoPost layers. Unlike Shoost Final Stack, this order is user-controlled.")]
        public HoPostProcessLayerListParameter layers = new HoPostProcessLayerListParameter(
            new List<HoPostProcessLayer>(),
            true);

        public bool IsActive()
        {
            if (!active || layers.value == null)
            {
                return false;
            }

            foreach (HoPostProcessLayer layer in layers.value)
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
