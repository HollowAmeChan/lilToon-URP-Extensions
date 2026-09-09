using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GTAO
{
    [Serializable]
    public sealed class HoGTAOQualityParameter : VolumeParameter<HoGTAOQuality>
    {
        public HoGTAOQualityParameter(HoGTAOQuality value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoGTAOQuality from, HoGTAOQuality to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoGTAOResolutionParameter : VolumeParameter<HoGTAOResolution>
    {
        public HoGTAOResolutionParameter(HoGTAOResolution value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoGTAOResolution from, HoGTAOResolution to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoGTAOSpatialFilterParameter : VolumeParameter<HoGTAOSpatialFilter>
    {
        public HoGTAOSpatialFilterParameter(HoGTAOSpatialFilter value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoGTAOSpatialFilter from, HoGTAOSpatialFilter to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoGTAODebugModeParameter : VolumeParameter<HoGTAODebugMode>
    {
        public HoGTAODebugModeParameter(HoGTAODebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoGTAODebugMode from, HoGTAODebugMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [VolumeComponentMenu("Post-processing/Ho-GTAO/屏幕空间 AO")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoGTAOVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用"), Tooltip("启用 Ho-GTAO，并将结果发布给材质和调试输出。")]
        public BoolParameter enable = new BoolParameter(true);

        [InspectorName("场景视图"), Tooltip("是否在 Scene View 中显示 GTAO 调试结果。")]
        public BoolParameter debugInSceneView = new BoolParameter(true);

        [InspectorName("游戏视图"), Tooltip("是否在 Game View 中显示 GTAO 调试结果。")]
        public BoolParameter debugInGameView = new BoolParameter(false);

        [Header("质量")]

        [InspectorName("质量档"), Tooltip("Low/Medium/High 三档预设。High 使用 Visibility Bitmasks + Full + 4x32 + 12 帧 + Box x3。")]
        public HoGTAOQualityParameter quality = new HoGTAOQualityParameter(HoGTAOQuality.High);

        [InspectorName("计算分辨率"), Tooltip("AO 计算分辨率。Half 输出后按深度/法线引导上采样。")]
        public HoGTAOResolutionParameter resolution = new HoGTAOResolutionParameter(HoGTAOResolution.Full);

        [Header("追踪")]

        [InspectorName("世界空间半径"), Tooltip("AO 检测的世界空间半径，单位为米。数值越大遮罩跨度越远，越早出现漏光。")]
        public MinFloatParameter worldSpaceRadius = new MinFloatParameter(5.0f, 0.01f);

        [InspectorName("最小屏幕半径"), Tooltip("屏幕像素半径下限（随 FOV/分辨率自动换算为屏幕偏移）。")]
        public MinIntParameter screenSpaceRadius = new MinIntParameter(25, 1);

        [InspectorName("厚度"), Tooltip("深度偏置。线性模式下按相机距离线性放大（厚度/10 × 线性深度），防止自遮挡。")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.2f, 0.05f, 1.0f);

        [InspectorName("切片数"), Tooltip("正交切片数量（每片按不同角度扫描）。2 是常规值，4 更全面。")]
        public ClampedIntParameter sliceCount = new ClampedIntParameter(4, 1, 4);

        [InspectorName("每切片步数"), Tooltip("每切片 march 步数。步进按平方分布（近密远疏）。")]
        public ClampedIntParameter stepCount = new ClampedIntParameter(32, 8, 32);

        [InspectorName("距离衰减"), Tooltip("开启后，地平线样本按距离衰减混合，远处遮挡影响更柔和。")]
        public BoolParameter useAttenuation = new BoolParameter(true);

        [InspectorName("线性厚度"), Tooltip("按线性眼深度放大厚度，保持近处接触和远处遮挡的尺度一致。")]
        public BoolParameter useLinearThickness = new BoolParameter(true);

        [Header("去噪")]

        [InspectorName("时间累积帧数"), Tooltip("历史累积上限帧数（0 = 关闭时间累积）。帧数越高噪声越低，运动时越迟钝。")]
        public ClampedIntParameter temporalFrameCount = new ClampedIntParameter(12, 0, 12);

        [InspectorName("时间拒绝强度"), Tooltip("历史重投影不一致时拒绝历史的强度。越高越抗鬼影。")]
        public ClampedFloatParameter temporalRejection = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);

        [InspectorName("空间滤波类型"), Tooltip("Disk：动态半径双边（边缘更好）。Box：固定步长多趟（便宜稳定）。")]
        public HoGTAOSpatialFilterParameter spatialFilter = new HoGTAOSpatialFilterParameter(HoGTAOSpatialFilter.Box);

        [InspectorName("滤波半径"), Tooltip("Disk 滤波的世界空间半径。")]
        public ClampedFloatParameter filterRadius = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        [InspectorName("边缘自适应"), Tooltip("Disk 滤波半径随局部对比度自适应的强度。越高越保边缘。")]
        public ClampedFloatParameter filterAdaptivity = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);

        [InspectorName("Box 趟数"), Tooltip("Box 滤波趟数（1-3）。每趟步长扩大，覆盖更大范围。")]
        public ClampedIntParameter boxPassCount = new ClampedIntParameter(3, 1, 3);

        [Header("调试")]

        [InspectorName("调试模式"), Tooltip("输出 GTAO、GeometryBuffer 深度/法线、Motion 或 Temporal Disocclusion。")]
        public HoGTAODebugModeParameter debugMode = new HoGTAODebugModeParameter(HoGTAODebugMode.Off);

        [InspectorName("AO Debug Pow"), Tooltip("只影响 AO 调试画面的显示曲线，不改变公共 AO 输出。")]
        public ClampedFloatParameter debugIntensity = new ClampedFloatParameter(3.672f, 0.1f, 8.0f);

        public bool IsActive()
        {
            return enable.value;
        }

        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
