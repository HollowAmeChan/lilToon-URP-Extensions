using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GTAO
{
    public enum HoGTAOQuality
    {
        [InspectorName("Low"), Tooltip("填写低档 Volume 参数：Quarter 配置 + 2 切片/8 步 + 仅空间滤波。")]
        Low = 0,
        [InspectorName("Medium"), Tooltip("填写中档 Volume 参数：Half 配置 + 2 切片/12 步 + 4 帧累积 + Box x2。")]
        Medium = 1,
        [InspectorName("High"), Tooltip("填写高档 Volume 参数：Full + 4 切片/32 步 + 12 帧累积 + Box x3。")]
        High = 2,
    }

    public enum HoGTAOResolution
    {
        [InspectorName("Full"), Tooltip("全分辨率计算。质量最高，最贵。")]
        Full = 0,
        [InspectorName("Half"), Tooltip("半分辨率计算，并在输出前做双线性放大。")]
        Half = 1,
        [InspectorName("Quarter"), Tooltip("四分之一分辨率计算，并在输出前做双线性放大。")]
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
        [InspectorName("Motion"), Tooltip("输出对象运动遮罩、深度增量和位移幅度。")]
        Motion = 4,
        // Keep the serialized value 5 for compatibility with scenes/assets that
        // already selected the previously-added Temporal mode.
        [InspectorName("Temporal Disocclusion"), Tooltip("输出历史重投影后的 disocclusion/rejection 掩码：稳定历史为白色，拒绝区域为红色。")]
        Temporal = 5,
        [UnityEngine.InspectorName("All")]
        All = 255
    }

    [Serializable]
    public sealed class HoGTAOSettings
    {
        public bool enabled = true;

        [InspectorName("仅发布 AO 纹理"), Tooltip("发布全白、可写的 HoAO，跳过 GTAO 追踪、运动输入和去噪；独立接收面胶囊 AO 不需要开启此模式。")]
        public bool publishAOTextureOnly;

        // 当前 Renderer Feature 资产配置为 BeforeRenderingOpaques（250）。
        // 与 Ho-GeometryBuffer 同事件，先后由 Renderer 特性列表顺序保证
        // （GeometryBuffer 在前、Ho-GTAO 紧随其后）；序列化默认值仅用于兼容旧资产。
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;

        public HoGTAOResolution resolution = HoGTAOResolution.Full;

        public int sliceCount = 4;
        public int stepCount = 32;

        // 追踪参数
        public float worldSpaceRadius = 3.0f;
        public float screenSpaceRadius = 30.0f;
        public float thickness = 0.6f;
        public bool useAttenuation = true;
        public bool useLinearThickness = true;

        // 去噪参数（High 档）
        public int temporalFrameCount = 12;
        public float temporalRejection = 0.7f;
        public HoGTAOSpatialFilter spatialFilter = HoGTAOSpatialFilter.Box;
        public float filterRadius = 0.4f;
        public float filterAdaptivity = 0.1f;
        public int boxPassCount = 3;

        // 调试
        public HoGTAODebugMode debugMode = HoGTAODebugMode.Off;
        public bool debugInSceneView = true;
        public bool debugInGameView;
        [InspectorName("AO Debug Pow"), Tooltip("AO 调试显示的 pow 指数。仅影响调试画面，不改变公共 AO 输出；HTrace 对照值为 3.672。")]
        [Range(0.1f, 8.0f)]
        public float debugIntensity = 3.672f;

        // Shader 引用（留空自动 Shader.Find）
        public Shader shader;

        public int ResolutionDivisor => resolution == HoGTAOResolution.Full ? 1 : resolution == HoGTAOResolution.Half ? 2 : 4;

        internal void CopyFrom(HoGTAOSettings source)
        {
            enabled = source.enabled; publishAOTextureOnly = source.publishAOTextureOnly;
            passEvent = source.passEvent; resolution = source.resolution;
            sliceCount = source.sliceCount; stepCount = source.stepCount;
            worldSpaceRadius = source.worldSpaceRadius; screenSpaceRadius = source.screenSpaceRadius;
            thickness = source.thickness; useAttenuation = source.useAttenuation;
            useLinearThickness = source.useLinearThickness; temporalFrameCount = source.temporalFrameCount;
            temporalRejection = source.temporalRejection; spatialFilter = source.spatialFilter;
            filterRadius = source.filterRadius; filterAdaptivity = source.filterAdaptivity;
            boxPassCount = source.boxPassCount; debugMode = source.debugMode;
            debugInSceneView = source.debugInSceneView; debugInGameView = source.debugInGameView;
            debugIntensity = source.debugIntensity; shader = source.shader;
        }
    }
}
