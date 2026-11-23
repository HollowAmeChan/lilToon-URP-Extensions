using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RT
{
    [Serializable, VolumeComponentMenu(HoVolumeNames.RT)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.RT)]
#endif
    public sealed class HoRTVolume : VolumeComponent, IPostProcessComponent
    {
        public HoRTVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.RT;
#endif
        }

        [InspectorName("启用")]
        [Tooltip("逐相机覆盖公共 RT 宿主。关闭后停止场景准备，RTDI / RTGI / RTShadow 无法取得场景；未覆盖时沿用 Feature 的启用状态。\n\nVolume 可以停止已启用的公共 RT Feature，不能开启已停用的 Feature。宿主状态见 Ho RT Feature，各消费者的质量和图像调试见对应 Volume。")]
        public BoolParameter enable = new(true);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
