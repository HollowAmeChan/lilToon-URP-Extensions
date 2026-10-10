using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    public enum HoVolumeLightDebugMode
    {
        [InspectorName("关闭")] Off,
        [InspectorName("体积贡献")] Contribution,
        [InspectorName("无阴影贡献")] Unshadowed
    }
    [Serializable]
    public sealed class HoVolumeLightDebugParameter : VolumeParameter<HoVolumeLightDebugMode>
    {
        public HoVolumeLightDebugParameter(HoVolumeLightDebugMode value) : base(value) { }
        public override void Interp(HoVolumeLightDebugMode from, HoVolumeLightDebugMode to, float t) => value = t > 0 ? to : from;
    }
    [Serializable, VolumeComponentMenu("Post-processing/Ho-VolumeLight/太阳 HD")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoVolumeLightVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable = new(true);
        [InspectorName("体积亮度倍率"), Tooltip("只改变体积效果，不修改太阳的表面照明。")]
        public MinFloatParameter intensity = new(1, 0);
        [InspectorName("最大照亮距离"), Tooltip("太阳体积效果的相机工作距离（米）；独立于 SDF 激活区域。")]
        public MinFloatParameter maxDistance = new(100, .01f);
        [InspectorName("采样步数")] public ClampedIntParameter stepCount = new(48, 8, 256);
        [InspectorName("散射方向性"), Tooltip("前向散射程度；越大，朝向光源观察时越明亮。")]
        public ClampedFloatParameter anisotropy = new(.3f, 0, .9f);
        public HoVolumeLightDebugParameter debugMode = new(HoVolumeLightDebugMode.Off);
        public BoolParameter debugInSceneView = new(true);
        public BoolParameter debugInGameView = new(true);
        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
