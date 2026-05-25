using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ImageProcessLayerListParameter : VolumeParameter<List<ImageProcessLayer>>
    {
        public ImageProcessLayerListParameter(List<ImageProcessLayer> value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(List<ImageProcessLayer> from, List<ImageProcessLayer> to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/lilToon-ImageProcess/Post Process Stack"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/lilToon-ImageProcess/Post Process Stack", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(ImageProcessRendererFeature))]
#endif
    [Serializable]
    public sealed class ImageProcessStackVolume : VolumeComponent, IPostProcessComponent
    {
        public ImageProcessStackVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "lilToon-ImageProcess Post Process Stack";
#endif
        }

        [InspectorName("启用"), Tooltip("启用 ImageProcess 风格的后处理栈。")]
        public BoolParameter Enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [InspectorName("场景视图"), Tooltip("是否让 Scene View 也应用这个后处理栈。")]
        public BoolParameter ShowInSceneView = new BoolParameter(true, false);

        [Tooltip("按顺序排列的后处理图层。")]
        public ImageProcessLayerListParameter layers = new ImageProcessLayerListParameter(
            new List<ImageProcessLayer>(),
            true);

        public bool IsActive()
        {
            if (!active || layers.value == null)
            {
                return false;
            }

            foreach (ImageProcessLayer layer in layers.value)
            {
                if (layer != null && layer.IsActive && !ImageProcessEffectDescriptor.Get(layer.effect).IsRemoved)
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
