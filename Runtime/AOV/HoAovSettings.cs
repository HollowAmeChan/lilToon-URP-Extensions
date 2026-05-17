using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AOV
{
    [Flags]
    public enum HoAovChannelMask
    {
        [InspectorName("无")]
        None = 0,
        [InspectorName("遮罩")]
        Mask = 1 << 0,
        [InspectorName("ID")]
        Id = 1 << 1,
        [InspectorName("标记")]
        Flags = 1 << 2,
        [InspectorName("线性深度")]
        LinearDepth = 1 << 3,
        [InspectorName("世界法线")]
        WorldNormal = 1 << 4,
        [InspectorName("视图法线")]
        ViewNormal = 1 << 5,
        [InspectorName("切线法线")]
        TangentNormal = 1 << 6,
        [InspectorName("速度")]
        Velocity = 1 << 7,
        [InspectorName("厚度")]
        Thickness = 1 << 8,
        [InspectorName("曲率")]
        Curvature = 1 << 9,
        [InspectorName("材质")]
        Material = 1 << 10,
        [InspectorName("系统预留")]
        Utility = 1 << 11,
        [InspectorName("默认")]
        Default = Mask | Id | Flags | LinearDepth | WorldNormal | TangentNormal | Thickness | Curvature | Material | Utility
    }

    public enum HoAovDebugMode
    {
        [InspectorName("关闭")]
        Off = 0,
        [InspectorName("遮罩")]
        Mask,
        [InspectorName("ID")]
        Id,
        [InspectorName("标记")]
        Flags,
        [InspectorName("线性深度")]
        LinearDepth,
        [InspectorName("世界法线")]
        WorldNormal,
        [InspectorName("视图法线")]
        ViewNormal,
        [InspectorName("切线法线")]
        TangentNormal,
        [InspectorName("速度")]
        Velocity,
        [InspectorName("厚度")]
        Thickness,
        [InspectorName("曲率")]
        Curvature,
        [InspectorName("材质")]
        Material,
        [InspectorName("系统预留")]
        Utility,
        [InspectorName("材质自定义通道 0")]
        Custom0,
        [InspectorName("材质自定义通道 1")]
        Custom1,
        [InspectorName("材质自定义通道 2")]
        Custom2,
        [InspectorName("材质自定义通道 3")]
        Custom3,
        [InspectorName("主体")]
        ObjectCustom0,
        [InspectorName("脸")]
        ObjectCustom1,
        [InspectorName("前发")]
        ObjectCustom2,
        [InspectorName("眼睛")]
        ObjectCustom3,
        [InspectorName("眼透区域")]
        ObjectCustom4,
        [InspectorName("配件")]
        ObjectCustom5,
        [InspectorName("预留 6")]
        ObjectCustom6,
        [InspectorName("预留 7")]
        ObjectCustom7,
        [InspectorName("RSUV 总览")]
        RsuvPacked,
        [InspectorName("RSUV 角色组 ID")]
        RsuvCharacterId,
        [InspectorName("RSUV 部件 ID")]
        RsuvPartId,
        [InspectorName("RSUV 标记")]
        RsuvFlags,
        [InspectorName("RSUV 仅写 ID")]
        RsuvIdOnly
    }

    public enum HoAovRenderScale
    {
        [InspectorName("全分辨率")]
        Full = 1,
        [InspectorName("半分辨率")]
        Half = 2,
        [InspectorName("四分之一分辨率")]
        Quarter = 4
    }

    public static class HoAovCustomChannels
    {
        public const int DefaultCount = 4;
        public const int MaxSupportedCount = 4;
        public const int ChannelsPerTexture = 4;

        public static int GetTextureCount(int channelCount)
        {
            int clampedCount = Mathf.Clamp(channelCount, 0, MaxSupportedCount);
            return Mathf.CeilToInt(clampedCount / (float)ChannelsPerTexture);
        }
    }

    [Serializable]
    public sealed class HoAovSettings
    {
        [InspectorName("已启用")]
        [Tooltip("不移除 RendererFeature，但跳过 HoAOV 所有 pass。")]
        public bool enabled = true;

        [InspectorName("图层遮罩")]
        [Tooltip("允许写入 HoAOV fallback 数据的图层。")]
        public LayerMask layerMask = -1;

        [InspectorName("最小渲染队列")]
        [Tooltip("HoAOV fallback 渲染包含的最低 render queue。")]
        public int minRenderQueue = 0;

        [InspectorName("最大渲染队列")]
        [Tooltip("HoAOV fallback 渲染包含的最高 render queue。")]
        public int maxRenderQueue = (int)RenderQueue.Overlay - 1;

        [InspectorName("AOV 写入时机")]
        [Tooltip("HoAOV 写入数据纹理的时机。应早于 HoPost 和 ShoostStack。")]
        public RenderPassEvent aovPassEvent = RenderPassEvent.AfterRenderingTransparents;

        [InspectorName("调试显示时机")]
        [Tooltip("调试预览写回 camera color 的时机。")]
        public RenderPassEvent debugPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [InspectorName("渲染缩放")]
        [Tooltip("降低分辨率可以省带宽，但会损失遮罩和法线边缘精度。")]
        public HoAovRenderScale renderScale = HoAovRenderScale.Full;

        [InspectorName("系统通道")]
        [Tooltip("当前请求输出的系统 AOV 通道。")]
        public HoAovChannelMask systemChannels = HoAovChannelMask.Default;

        [InspectorName("自定义通道数量")]
        [Tooltip("暴露给用户和 Shoost 高级模式使用的 custom 通道数量。")]
        [Range(0, HoAovCustomChannels.MaxSupportedCount)]
        public int customChannelCount = HoAovCustomChannels.DefaultCount;

        [InspectorName("使用回退材质")]
        [Tooltip("在 lilToon/lilPBR 还没有正式 HoAOV pass 前，使用 override material 输出基础 AOV。")]
        public bool useFallbackMaterial = true;

        [InspectorName("回退 Shader")]
        [Tooltip("为空时自动查找 Hidden/lilToon-HoAOV/URP/Fallback。")]
        public Shader fallbackShader;

        [InspectorName("调试 Shader")]
        [Tooltip("为空时自动查找 Hidden/lilToon-HoAOV/URP/DebugView。")]
        public Shader debugShader;

        [InspectorName("调试模式")]
        [Tooltip("在 Scene View 或 Game View 中预览指定 AOV 通道。第一次测试建议选“遮罩”。")]
        public HoAovDebugMode debugMode = HoAovDebugMode.Off;

        [InspectorName("场景视图显示调试")]
        [Tooltip("在 Scene View 显示当前 AOV 调试通道。")]
        public bool debugInSceneView = true;

        [InspectorName("游戏视图显示调试")]
        [Tooltip("在 Game View 显示当前 AOV 调试通道。默认关闭，避免误进正式画面。")]
        public bool debugInGameView;

        [InspectorName("调试深度近端")]
        [Tooltip("线性深度调试的近端重映射距离。")]
        [Min(0.0f)]
        public float debugDepthNear = 0.0f;

        [InspectorName("调试深度远端")]
        [Tooltip("线性深度调试的远端重映射距离。")]
        [Min(0.0001f)]
        public float debugDepthFar = 25.0f;

        [InspectorName("自定义通道名称")]
        [Tooltip("自定义通道的显示名称。后续可以超过前 12 个。")]
        public string[] customChannelNames = new string[HoAovCustomChannels.DefaultCount];

        [InspectorName("自定义通道颜色")]
        [Tooltip("自定义通道的调试颜色。后续可以超过前 12 个。")]
        public Color[] customChannelColors = new Color[HoAovCustomChannels.DefaultCount];

        public int ClampedCustomChannelCount => Mathf.Clamp(customChannelCount, 0, HoAovCustomChannels.MaxSupportedCount);

        public void ClampCustomChannels()
        {
            customChannelCount = ClampedCustomChannelCount;
            ResizeArray(ref customChannelNames);
            ResizeArray(ref customChannelColors);
        }

        private static void ResizeArray<T>(ref T[] values)
        {
            if (values == null)
            {
                values = new T[HoAovCustomChannels.DefaultCount];
                return;
            }

            if (values.Length != HoAovCustomChannels.DefaultCount)
            {
                Array.Resize(ref values, HoAovCustomChannels.DefaultCount);
            }
        }
    }

    public static class HoAovObjectChannels
    {
        public const int DefaultCount = 8;
        public const int MaxSupportedCount = 8;
        public const int ChannelsPerTexture = 4;
    }
}
