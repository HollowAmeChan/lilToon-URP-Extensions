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

        [InspectorName("启用"), Tooltip("启用 Shoost 风格的后处理栈。")]
        public BoolParameter Enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("场景视图"), Tooltip("是否让 Scene View 也应用这个后处理栈。")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [Tooltip("按顺序排列的后处理图层。")]
        public ShoostPostProcessLayerListParameter layers = new ShoostPostProcessLayerListParameter(
            new List<ShoostPostProcessLayer>(),
            true);

        public bool IsActive()
        {
            if (!active || layers.value == null)
            {
                return false;
            }

            foreach (ShoostPostProcessLayer layer in layers.value)
            {
                if (layer != null && layer.IsActive && !IsRemovedEffectSlot(layer.effect))
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

        private static bool IsRemovedEffectSlot(ShoostPostProcessEffect effect)
        {
            return effect == ShoostPostProcessEffect.RemovedEffectSlot13 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot30 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot31 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot32;
        }
    }
}
