using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTShadow
{
    public enum HoRTShadowDebugMode
    {
        [InspectorName("关闭")] Off,
        [InspectorName("重建可见度")] Visibility,
        [InspectorName("原始可见度")] RawVisibility,
        [InspectorName("重建与原始差值")] Difference,
        [InspectorName("最近遮挡距离")] BlockerDistance,
        [InspectorName("接收对象身份")] Receiver,
        [InspectorName("像素状态")] Status,
        [InspectorName("lilToon 接收状态")] Consumption = 7,
        [InspectorName("lilToon 主光强制无遮挡")] ForceLit = 8,
        [InspectorName("lilToon 主光强制全遮挡")] ForceShadow = 9,
        [InspectorName("全部视图")] All = 255
    }

    [Serializable]
    public sealed class HoRTShadowReconstructionParameter : VolumeParameter<HoRTShadowReconstruction>
    {
        public HoRTShadowReconstructionParameter(HoRTShadowReconstruction value) : base(value) { }
        public override void Interp(HoRTShadowReconstruction from, HoRTShadowReconstruction to, float t) => value = t > 0 ? to : from;
    }
    [Serializable]
    public sealed class HoRTShadowDebugModeParameter : VolumeParameter<HoRTShadowDebugMode>
    {
        public HoRTShadowDebugModeParameter(HoRTShadowDebugMode value) : base(value) { }
        public override void Interp(HoRTShadowDebugMode from, HoRTShadowDebugMode to, float t) => value = t > 0 ? to : from;
    }

    [Serializable, VolumeComponentMenu(HoVolumeNames.RTShadow)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.RTShadow)]
#endif
    public sealed class HoRTShadowVolume : VolumeComponent, IPostProcessComponent
    {
        public HoRTShadowVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.RTShadow;
#endif
        }

        [InspectorName("启用"), Tooltip("逐相机开关；Feature 生产总开关仍须打开。未覆盖时使用 Feature 默认值。\n\n未覆盖字段沿用 Feature 默认值。Feature 总开关仍须开启；投射物、请求灯和接收声明在 Feature 或场景组件中配置。")]
        public BoolParameter enable = new(true);
        [InspectorName("阴影重建")]
        [Tooltip("Raw 使用原始样本数，SIGMA 无历史使用同帧估计数；增加估计数会增加完整降噪成本，稳定预览按请求执行。点光/聚光当前输出 delta 硬影。")]
        public HoRTShadowReconstructionParameter reconstruction = new(HoRTShadowReconstruction.Raw);
        [InspectorName("默认角直径（度）"), Tooltip("未声明方向灯的默认角直径；Ho-RTShadow Light 上的逐灯角度优先。点 / 聚光当前是 delta 硬影。\n\n逐灯声明的角度优先于这里的默认值。")]
        public ClampedFloatParameter angularDiameter = new(0, 0, 8);
        [InspectorName("原始参考样本数"), Tooltip("只在原始参考模式生效。SIGMA 每次估计只接收一个真实光源样本。")]
        public ClampedIntParameter samplesPerPixel = new(1, 1, 4096);
        [InspectorName("无历史同帧估计数"), Tooltip("仅 SIGMA 无历史使用。每次完整追踪、降噪再平均，不等待后续帧；成本随次数增加。")]
        public ClampedIntParameter singleFrameEstimates = new(1, 1, 256);
        [InspectorName("采样种子")]
        public IntParameter samplingSeed = new(0);
        [InspectorName("调试模式")]
        [Tooltip("状态图：黑=未请求、绿=有效、红=无效表面、蓝=灯域外、橙=距离不足、紫=查询错误；Blocker 无命中为蓝。Raw 是最后一个真实单样本。lilToon 接收状态：绿=取样成功、蓝=无 OB 身份、红=RT 产品未启用、青=身份不匹配、橙=深度不匹配、紫=像素未请求/无效、洋红=接收分支未编译。强制明暗只隔离主光 Cast，调试结束后切回关闭。")]
        public HoRTShadowDebugModeParameter debugMode = new(HoRTShadowDebugMode.Off);
        [InspectorName("调试灯槽"), Tooltip("当前请求的生产槽，从 0 开始；对应灯名与稳定 LightId 见 Feature 运行状态。不是持久灯 ID，灯集合变化时槽会变化。无产品时显示紫色。\n\n灯槽从 0 开始，映射见 Feature 运行状态；没有当前产品时显示紫色。")]
        public MinIntParameter debugLight = new(0, 0);
        [InspectorName("距离显示上限（米）"), Tooltip("只影响距离视图：0 米黑，达到上限白；没有命中 blocker 为蓝色。")]
        public MinFloatParameter debugDistance = new(1, .0001f);
        [InspectorName("Scene View 调试")]
        public BoolParameter debugInSceneView = new(true);
        [InspectorName("Game View 调试")]
        public BoolParameter debugInGameView = new(false);

        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;

        // Never mutate serialized Feature settings or another camera's resolved values.
        internal static HoRTShadowSettings Resolve(HoRTShadowSettings defaults, HoRTShadowVolume volume, out int overrides)
        {
            var resolved = defaults.Copy();
            overrides = 0;
            if (volume == null || !volume.active) return resolved;
            Apply(volume.enable, ref resolved.enabled, ref overrides);
            Apply(volume.reconstruction, ref resolved.reconstruction, ref overrides);
            Apply(volume.angularDiameter, ref resolved.angularDiameter, ref overrides);
            Apply(volume.samplesPerPixel, ref resolved.samplesPerPixel, ref overrides);
            Apply(volume.singleFrameEstimates, ref resolved.singleFrameEstimates, ref overrides);
            Apply(volume.samplingSeed, ref resolved.samplingSeed, ref overrides);
            Apply(volume.debugMode, ref resolved.debugMode, ref overrides);
            Apply(volume.debugLight, ref resolved.debugLight, ref overrides);
            Apply(volume.debugDistance, ref resolved.debugDistance, ref overrides);
            Apply(volume.debugInSceneView, ref resolved.debugInSceneView, ref overrides);
            Apply(volume.debugInGameView, ref resolved.debugInGameView, ref overrides);
            return resolved;
        }
        private static void Apply<T>(VolumeParameter<T> parameter, ref T value, ref int count)
        { if (parameter.overrideState) { value = parameter.value; count++; } }
    }
}
