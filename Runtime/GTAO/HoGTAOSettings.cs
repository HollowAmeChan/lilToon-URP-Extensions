using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GTAO
{
    public enum HoGTAOQuality
    {
        [InspectorName("Low"), Tooltip("低档（参数保守）：Quarter 分辨率 + 8 步 + 仅空间滤波。")]
        Low = 0,
        [InspectorName("Medium"), Tooltip("中档：Half 分辨率 + 12 步 + 简单时间累积（4 帧）+ 双层 Box。")]
        Medium = 1,
        [InspectorName("High"), Tooltip("高档（默认）：Half 分辨率 + 16 步 + 时间累积（8 帧）+ Disk 双边滤波。最高质量档。")]
        High = 2,
    }

    public enum HoGTAOResolution
    {
        [InspectorName("Full"), Tooltip("全分辨率计算。质量最高，最贵。")]
        Full = 0,
        [InspectorName("Half"), Tooltip("半分辨率计算，输出后按深度/法线引导上采样。")]
        Half = 1,
        [InspectorName("Quarter"), Tooltip("四分之一分辨率计算。最便宜，细节有损。")]
        Quarter = 2,
    }

    public enum HoGTAOSpatialFilter
    {
        [InspectorName("Disk"), Tooltip("动态半径双边滤波：半径随局部对比度自适应，边缘保护更好。")]
        Disk = 0,
        [InspectorName("Box"), Tooltip("固定步长多次双层滤波（Poisson 偏移），便宜稳定。")]
        Box = 1,
    }

    public enum HoGTAODebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("GTAO"), Tooltip("输出 HTrace 对照的 GTAO visibility（1=无遮挡）。")]
        AO = 1,
        [InspectorName("Depth"), Tooltip("直出 GeometryBuffer 线性深度。")]
        Depth = 2,
        [InspectorName("Normal"), Tooltip("直出 GeometryBuffer 世界法线。")]
        Normal = 3,
        // Keep the serialized value 5 for compatibility with scenes/assets that
        // already selected the previously-added Temporal mode.
        [InspectorName("Temporal Disocclusion"), Tooltip("输出历史重投影后的 disocclusion/rejection 掩码：稳定历史为白色，拒绝区域为红色。")]
        Temporal = 5,
    }

    [Serializable]
    public sealed class HoGTAOSettings
    {
        public bool enabled = true;

        // 渲染时机：BeforeRenderingOpaques（250）。与 Ho-GeometryBuffer 同事件，
        // 先后由 Renderer 特性列表顺序保证（GeometryBuffer 在前、Ho-GTAO 紧随其后）。
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;

        public HoGTAOQuality quality = HoGTAOQuality.High;
        public HoGTAOResolution resolution = HoGTAOResolution.Half;

        public int sliceCount = 2;
        public int stepCount = 32;

        // 追踪参数
        public float worldSpaceRadius = 3.0f;
        public float screenSpaceRadius = 30.0f;
        public float thickness = 0.6f;
        public bool useAttenuation = true;
        public bool useLinearThickness = true;

        // 去噪参数（High 档）
        public int temporalFrameCount = 8;
        public float temporalRejection = 0.7f;
        public HoGTAOSpatialFilter spatialFilter = HoGTAOSpatialFilter.Disk;
        public float filterRadius = 0.4f;
        public float filterAdaptivity = 0.1f;
        public int boxPassCount = 2;

        // 调试
        public HoGTAODebugMode debugMode = HoGTAODebugMode.Off;
        public bool debugInSceneView = true;
        public bool debugInGameView;

        // Shader 引用（留空自动 Shader.Find）
        public Shader shader;

        public int ResolutionDivisor => resolution == HoGTAOResolution.Full ? 1 : resolution == HoGTAOResolution.Half ? 2 : 4;
    }
}
