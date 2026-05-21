using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    public enum HoSubsurfaceScatteringRenderScale
    {
        [InspectorName("全分辨率")]
        Full = 1,
        [InspectorName("半分辨率")]
        Half = 2,
        [InspectorName("四分之一分辨率")]
        Quarter = 4
    }

    public enum HoSubsurfaceScatteringDebugMode
    {
        [InspectorName("关闭")]
        Off = 0,
        [InspectorName("参与遮罩")]
        Mask = 1,
        [InspectorName("源颜色")]
        Source = 2,
        [InspectorName("扩散结果")]
        Diffusion = 3,
        [InspectorName("透射结果")]
        Transmission = 4,
        [InspectorName("透射门控")]
        TransmissionGate = 5,
        [InspectorName("合成权重")]
        CompositeWeight = 6,
        [InspectorName("配置 ID")]
        ProfileId = 7,
        [InspectorName("厚度")]
        Thickness = 8,
        [InspectorName("配置半径")]
        ProfileRadius = 9,
        [InspectorName("透射方向")]
        TransmissionDirection = 10,
        [InspectorName("透射轮廓")]
        TransmissionRim = 11
    }

    public enum HoSubsurfaceScatteringQuality
    {
        [InspectorName("低")]
        Low = 8,
        [InspectorName("标准")]
        Medium = 16,
        [InspectorName("高")]
        High = 24
    }

    public enum HoSubsurfaceScatteringTransmissionBlendMode
    {
        [InspectorName("柔性染色")]
        SoftTint = 0,
        [InspectorName("加色")]
        Additive = 1,
        [InspectorName("Screen")]
        Screen = 2,
        [InspectorName("颜色注入")]
        ColorInject = 3,
        [InspectorName("Legacy Max")]
        LegacyMax = 4
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringProfileSettings
    {
        [InspectorName("启用")]
        public bool enabled = true;

        [InspectorName("配置 ID")]
        [Range(0, 255)]
        public int profileId = 1;

        [InspectorName("扩散颜色")]
        public Color diffusionColor = new Color(1.0f, 0.43f, 0.32f, 1.0f);

        [InspectorName("扩散半径")]
        [Range(0.0f, 24.0f)]
        public float diffusionRadius = 8.0f;

        [InspectorName("保留源色")]
        [Range(0.0f, 1.0f)]
        public float sourcePreserve = 0.25f;

        [InspectorName("透射颜色")]
        public Color transmissionColor = new Color(1.0f, 0.38f, 0.22f, 1.0f);

        [InspectorName("透射强度")]
        [Range(0.0f, 1.0f)]
        public float transmissionStrength = 0.18f;

        [InspectorName("透射半径")]
        [Range(0.0f, 24.0f)]
        public float transmissionRadius = 5.0f;

        [InspectorName("厚度倍率")]
        [Range(0.0f, 4.0f)]
        public float thicknessScale = 1.0f;
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringSettings
    {
        internal const int SourceOffset = 1;
        internal const int HorizontalBlurOffset = 2;
        internal const int VerticalBlurOffset = 3;
        internal const int CompositeOffset = 4;
        internal const int MaxProfileCount = 8;

        [InspectorName("启用")]
        [Tooltip("不移除 RendererFeature，但跳过所有屏幕空间 SSS pass。")]
        public bool enabled = true;

        [InspectorName("源提取时机")]
        [Tooltip("捕获不透明/镂空相机颜色并提取 SSS 源颜色的时机。HoAOV 需要在它之前完成。")]
        public RenderPassEvent sourcePassEvent = RenderPassEvent.AfterRenderingSkybox;

        [InspectorName("合成时机")]
        [Tooltip("把扩散后的 SSS 写回相机颜色的时机。通常放在透明与 OIT 之前。")]
        public RenderPassEvent compositePassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [InspectorName("渲染缩放")]
        [Tooltip("降低 SSS 扩散纹理分辨率可以节省带宽，但会降低边缘精度。")]
        public HoSubsurfaceScatteringRenderScale renderScale = HoSubsurfaceScatteringRenderScale.Half;

        [InspectorName("质量")]
        [Tooltip("控制 Burley profile disk gather 的采样预算。标准为 16 taps。")]
        public HoSubsurfaceScatteringQuality quality = HoSubsurfaceScatteringQuality.Medium;

        [InspectorName("合成着色器")]
        [Tooltip("为空时自动查找 Hidden/lilToon/URP/HoSubsurfaceScattering 着色器。")]
        public Shader shader;

        [InspectorName("强度")]
        [Tooltip("全局 SSS 合成强度。材质侧的 HoAOV surfaceData.r 仍然控制逐像素权重。")]
        [Range(0.0f, 2.0f)]
        public float strength = 0.45f;

        [InspectorName("半径")]
        [Tooltip("扩散半径，单位为屏幕像素。最终半径会乘以材质薄度遮罩。")]
        [Range(0.0f, 24.0f)]
        public float radius = 8.0f;

        [InspectorName("深度容差")]
        [Tooltip("双边滤波的深度容差，越小越不容易跨过轮廓和遮挡边界。")]
        [Range(0.0001f, 2.0f)]
        public float depthTolerance = 0.08f;

        [InspectorName("法线容差")]
        [Tooltip("双边滤波的法线容差。值越高越容易跨过法线变化。")]
        [Range(0.01f, 1.0f)]
        public float normalTolerance = 0.35f;

        [InspectorName("颜色")]
        [Tooltip("用于给散射结果着色的全局颜色。材质专属配置后续可通过 HoAOV 材质/对象 ID 接入。")]
        public Color color = new Color(1.0f, 0.43f, 0.32f, 1.0f);

        [InspectorName("保留源色")]
        [Tooltip("合成时保留一部分原始 SSS 源颜色，避免第一版只出现软雾化结果。")]
        [Range(0.0f, 1.0f)]
        public float sourcePreserve = 0.25f;

        [InspectorName("材质配置")]
        [Tooltip("按 HoAOV surfaceData.b 中的 HoSSS Profile ID 选择材质级 SSS 参数。默认 1 号为皮肤。")]
        public HoSubsurfaceScatteringProfileSettings[] profiles = CreateDefaultProfiles();

        [InspectorName("透射强度")]
        [Tooltip("类似 HDRP 的皮肤透射采样，在扩散结果上叠加短距离方向性色彩渗透。")]
        [Range(0.0f, 1.0f)]
        public float transmissionStrength = 0.18f;

        [InspectorName("透射半径")]
        [Tooltip("方向性透射采样半径，单位为屏幕像素。")]
        [Range(0.0f, 24.0f)]
        public float transmissionRadius = 5.0f;

        [InspectorName("透射采样数")]
        [Tooltip("皮肤透射使用的短距离屏幕空间采样数。")]
        [Range(2, 32)]
        public int transmissionSamples = 16;

        [InspectorName("透射主光方向")]
        [Tooltip("方向性透射采样的主光方向混合。0 使用法线屏幕投影，1 使用 URP 主光方向屏幕投影。")]
        [Range(0.0f, 1.0f)]
        public float transmissionMainLightDirection = 0.35f;

        [InspectorName("透射深度权重")]
        [Tooltip("提高从更浅/更亮皮肤区域向当前像素渗透的权重。")]
        [Range(0.0f, 2.0f)]
        public float transmissionDepthWeight = 0.45f;

        [InspectorName("透射轮廓增强")]
        [Tooltip("按视角轮廓增强耳缘、鼻翼、手指边缘的透射效果。")]
        [Range(0.0f, 2.0f)]
        public float transmissionEdgeBoost = 0.35f;

        [InspectorName("透射轮廓权重")]
        [Range(0.0f, 1.0f)]
        public float transmissionRimWeight = 0.75f;

        [InspectorName("透射混合")]
        public HoSubsurfaceScatteringTransmissionBlendMode transmissionBlendMode = HoSubsurfaceScatteringTransmissionBlendMode.SoftTint;

        [InspectorName("透射染色量")]
        [Range(0.0f, 1.0f)]
        public float transmissionTintInjection = 0.35f;

        [InspectorName("透射平滑")]
        [Range(0.0f, 1.0f)]
        public float transmissionSmoothing = 0.45f;

        [InspectorName("透射颜色")]
        [Tooltip("方向性透射使用的皮肤血色着色。")]
        public Color transmissionColor = new Color(1.0f, 0.38f, 0.22f, 1.0f);

        [InspectorName("调试模式")]
        [Tooltip("直接输出 HoSSS 中间结果到相机颜色，用于检查遮罩、扩散、透射和合成权重。")]
        public HoSubsurfaceScatteringDebugMode debugMode = HoSubsurfaceScatteringDebugMode.Off;

        [InspectorName("场景视图")]
        [Tooltip("在场景视图中运行 SSS，便于调试材质遮罩与半径。")]
        public bool renderInSceneView = true;

        internal void EnsureProfiles()
        {
            if (profiles == null || profiles.Length != MaxProfileCount)
            {
                var defaults = CreateDefaultProfiles();
                if (profiles != null)
                {
                    int copyCount = Mathf.Min(profiles.Length, defaults.Length);
                    for (int i = 0; i < copyCount; i++)
                    {
                        if (profiles[i] != null)
                        {
                            defaults[i] = profiles[i];
                        }
                    }
                }

                profiles = defaults;
            }

            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] == null)
                {
                    profiles[i] = new HoSubsurfaceScatteringProfileSettings
                    {
                        enabled = i == 0,
                        profileId = i + 1
                    };
                }
            }
        }

        internal static HoSubsurfaceScatteringProfileSettings[] CreateDefaultProfiles()
        {
            var result = new HoSubsurfaceScatteringProfileSettings[MaxProfileCount];
            result[0] = new HoSubsurfaceScatteringProfileSettings
            {
                enabled = true,
                profileId = 1,
                diffusionColor = new Color(1.0f, 0.43f, 0.32f, 1.0f),
                diffusionRadius = 8.0f,
                sourcePreserve = 0.25f,
                transmissionColor = new Color(1.0f, 0.38f, 0.22f, 1.0f),
                transmissionStrength = 0.18f,
                transmissionRadius = 5.0f,
                thicknessScale = 1.0f
            };

            for (int i = 1; i < MaxProfileCount; i++)
            {
                result[i] = new HoSubsurfaceScatteringProfileSettings
                {
                    enabled = false,
                    profileId = i + 1
                };
            }

            return result;
        }

        public void ClampPassEvents()
        {
            EnsureProfiles();

            const int earliestSourceAnchor = (int)RenderPassEvent.AfterRenderingOpaques;
            const int latestSourceAnchor = (int)RenderPassEvent.BeforeRenderingTransparents - CompositeOffset - 1;

            int source = (int)sourcePassEvent;
            if (source < earliestSourceAnchor || source > latestSourceAnchor || !IsNamedRenderPassEvent(sourcePassEvent))
            {
                source = (int)RenderPassEvent.AfterRenderingSkybox;
            }

            int composite = (int)compositePassEvent;
            if (composite < source + CompositeOffset || composite > (int)RenderPassEvent.BeforeRenderingTransparents || !IsNamedRenderPassEvent(compositePassEvent))
            {
                composite = (int)RenderPassEvent.BeforeRenderingTransparents;
            }

            sourcePassEvent = (RenderPassEvent)source;
            compositePassEvent = (RenderPassEvent)composite;
        }

        internal RenderPassEvent GetSourceRenderPassEvent()
        {
            return (RenderPassEvent)((int)sourcePassEvent + SourceOffset);
        }

        internal RenderPassEvent GetHorizontalBlurRenderPassEvent()
        {
            return (RenderPassEvent)((int)sourcePassEvent + HorizontalBlurOffset);
        }

        internal RenderPassEvent GetVerticalBlurRenderPassEvent()
        {
            return (RenderPassEvent)((int)sourcePassEvent + VerticalBlurOffset);
        }

        internal RenderPassEvent GetCompositeRenderPassEvent()
        {
            return (RenderPassEvent)((int)sourcePassEvent + CompositeOffset);
        }

        private static bool IsNamedRenderPassEvent(RenderPassEvent passEvent)
        {
            switch (passEvent)
            {
                case RenderPassEvent.BeforeRendering:
                case RenderPassEvent.BeforeRenderingShadows:
                case RenderPassEvent.AfterRenderingShadows:
                case RenderPassEvent.BeforeRenderingPrePasses:
                case RenderPassEvent.AfterRenderingPrePasses:
                case RenderPassEvent.BeforeRenderingGbuffer:
                case RenderPassEvent.AfterRenderingGbuffer:
                case RenderPassEvent.BeforeRenderingDeferredLights:
                case RenderPassEvent.AfterRenderingDeferredLights:
                case RenderPassEvent.BeforeRenderingOpaques:
                case RenderPassEvent.AfterRenderingOpaques:
                case RenderPassEvent.BeforeRenderingSkybox:
                case RenderPassEvent.AfterRenderingSkybox:
                case RenderPassEvent.BeforeRenderingTransparents:
                case RenderPassEvent.AfterRenderingTransparents:
                case RenderPassEvent.BeforeRenderingPostProcessing:
                case RenderPassEvent.AfterRenderingPostProcessing:
                case RenderPassEvent.AfterRendering:
                    return true;
                default:
                    return false;
            }
        }
    }
}
