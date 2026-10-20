using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.VolumeLight
{
    public enum HoVolumeLightAntialiasing
    {
        [InspectorName("关闭")] Off,
        [InspectorName("单帧去噪")] Spatial,
        [InspectorName("时域稳定")] Temporal
    }
    [Serializable]
    public sealed class HoVolumeLightAntialiasingParameter : VolumeParameter<HoVolumeLightAntialiasing>
    {
        public HoVolumeLightAntialiasingParameter(HoVolumeLightAntialiasing value) : base(value) { }
        public override void Interp(HoVolumeLightAntialiasing from, HoVolumeLightAntialiasing to, float t) => value = t > 0 ? to : from;
    }
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
        [InspectorName("抗锯齿"), Tooltip("时域稳定使用体积自身的相机历史；单帧去噪不积累历史，适合快速变化的镜头。")]
        public HoVolumeLightAntialiasingParameter antialiasing = new(HoVolumeLightAntialiasing.Temporal);
        [InspectorName("散射方向性覆盖"), Tooltip("仅在勾选覆盖时替换大气区域的散射方向性；同时影响雾与可见尘粒。")]
        public ClampedFloatParameter anisotropy = new(.3f, -.9f, .9f);
        public HoVolumeLightDebugParameter debugMode = new(HoVolumeLightDebugMode.Off);
        public BoolParameter debugInSceneView = new(true);
        public BoolParameter debugInGameView = new(true);
        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
