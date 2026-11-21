using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RT
{
    [Serializable, VolumeComponentMenu("Post-processing/Ho-RT/光追公共底座")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoRTVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")]
        [Tooltip("逐相机覆盖公共 RT 宿主。关闭后停止场景准备，RTDI / RTGI / RTShadow 无法取得场景；未覆盖时沿用 Feature 的启用状态。")]
        public BoolParameter enable = new(true);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
