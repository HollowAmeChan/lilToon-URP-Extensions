using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    public enum HoSubsurfaceScatteringRenderScale
    {
        Full = 1,
        Half = 2,
        Quarter = 4
    }

    [Serializable]
    public sealed class HoSubsurfaceScatteringSettings
    {
        internal const int SourceOffset = 1;
        internal const int HorizontalBlurOffset = 2;
        internal const int VerticalBlurOffset = 3;
        internal const int CompositeOffset = 4;

        [InspectorName("启用")]
        [Tooltip("不移除 RendererFeature，但跳过所有屏幕空间 SSS pass。")]
        public bool enabled = true;

        [InspectorName("Source 时机")]
        [Tooltip("捕获 opaque/cutout 相机颜色并提取 SSS 源的时机。HoAOV 需要在它之前完成。")]
        public RenderPassEvent sourcePassEvent = RenderPassEvent.AfterRenderingSkybox;

        [InspectorName("Composite 时机")]
        [Tooltip("把扩散后的 SSS 写回相机颜色的时机。通常放在透明与 OIT 之前。")]
        public RenderPassEvent compositePassEvent = RenderPassEvent.BeforeRenderingTransparents;

        [InspectorName("渲染缩放")]
        [Tooltip("降低 SSS 扩散纹理分辨率可以节省带宽，但会降低边缘精度。")]
        public HoSubsurfaceScatteringRenderScale renderScale = HoSubsurfaceScatteringRenderScale.Half;

        [InspectorName("Composite Shader")]
        [Tooltip("为空时自动查找 Hidden/lilToon/URP/HoSubsurfaceScattering。")]
        public Shader shader;

        [InspectorName("强度")]
        [Tooltip("全局 SSS 合成强度。材质侧的 HoAOV surfaceData.r 仍然控制逐像素权重。")]
        [Range(0.0f, 2.0f)]
        public float strength = 0.45f;

        [InspectorName("半径")]
        [Tooltip("扩散半径，单位为屏幕像素。最终半径会乘以材质 thinness mask。")]
        [Range(0.0f, 32.0f)]
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
        [Tooltip("用于 tint 散射结果的全局颜色。材质专属 profile 后续可通过 HoAOV material/object id 接入。")]
        public Color color = new Color(1.0f, 0.43f, 0.32f, 1.0f);

        [InspectorName("保留源色")]
        [Tooltip("合成时保留一部分原始 SSS source，避免第一版只出现软雾化结果。")]
        [Range(0.0f, 1.0f)]
        public float sourcePreserve = 0.25f;

        [InspectorName("Scene View")]
        [Tooltip("在 Scene View 中运行 SSS，便于调试材质 mask 与半径。")]
        public bool renderInSceneView = true;

        public void ClampPassEvents()
        {
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
