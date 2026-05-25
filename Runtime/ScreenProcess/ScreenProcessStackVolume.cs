using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ScreenProcessLayerListParameter : VolumeParameter<List<ScreenProcessLayer>>
    {
        public ScreenProcessLayerListParameter(List<ScreenProcessLayer> value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(List<ScreenProcessLayer> from, List<ScreenProcessLayer> to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/lilToon-ScreenProcess/Process Stack"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/lilToon-ScreenProcess/Process Stack", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(ScreenProcessRendererFeature))]
#endif
    [Serializable]
    public sealed class ScreenProcessStackVolume : VolumeComponent, IPostProcessComponent
    {
        public ScreenProcessStackVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "lilToon-ScreenProcess";
#endif
        }

        [InspectorName("Enable"), Tooltip("Enable the ScreenProcess subject/effect stack.")]
        public BoolParameter Enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("Scene View"), Tooltip("Apply this ScreenProcess stack in Scene View.")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [Tooltip("Ordered ScreenProcess layers. Unlike ImageProcess, these layers can consume semantic buffers.")]
        public ScreenProcessLayerListParameter layers = new ScreenProcessLayerListParameter(
            new List<ScreenProcessLayer>(),
            true);

        public bool IsActive()
        {
            if (!active || layers.value == null)
            {
                return false;
            }

            foreach (ScreenProcessLayer layer in layers.value)
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
