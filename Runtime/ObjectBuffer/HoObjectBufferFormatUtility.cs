using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 格式选择与 **MSAA 采样数协商**。
    /// 关键点（规划决策 7）：采样数**只看平台能力，不看相机的 MSAA 设置**——
    /// <c>SystemInfo.GetRenderTextureSupportedMSAASampleCount</c> 官方语义就是"不支持就返回一个平台支持的更低值"，
    /// 所以我们直接要 4、拿回 2 或 1 即可。
    /// </summary>
    internal static class HoObjectBufferFormatUtility
    {
        /// <summary>层图（ID / 覆盖率 / Cryptomatte 选择）统一用 RGBA8。</summary>
        public static GraphicsFormat GetLayerGraphicsFormat()
        {
            return IsUsable(GraphicsFormat.R8G8B8A8_UNorm) ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.B8G8R8A8_UNorm;
        }

        /// <summary>线性 HDR 表面色要 16F。**CB 自己不用**（表面色归 Ho-SurfaceBuffer），留给它复用。</summary>
        public static GraphicsFormat GetSurfaceGraphicsFormat()
        {
            return IsUsable(GraphicsFormat.R16G16B16A16_SFloat) ? GraphicsFormat.R16G16B16A16_SFloat : GetLayerGraphicsFormat();
        }

        /// <summary>
        /// 逐样本 ID 的首选格式：`R16_UInt`（整数 RT，天然不可滤波、不可混合）。
        /// 平台不支持时退化为 `R16_UNorm`，消费端用 <c>round(v * 65535)</c> 还原（与 GB 用 UNORM8 存身份同法）。
        /// </summary>
        public static bool TryGetIdGraphicsFormat(out GraphicsFormat format, out bool isInteger)
        {
            if (IsUsable(GraphicsFormat.R16_UInt))
            {
                format = GraphicsFormat.R16_UInt;
                isInteger = true;
                return true;
            }

            if (IsUsable(GraphicsFormat.R16_UNorm))
            {
                format = GraphicsFormat.R16_UNorm;
                isInteger = false;
                return true;
            }

            format = GraphicsFormat.None;
            isInteger = false;
            return false;
        }

        public static GraphicsFormat GetDepthStencilFormat(RenderTextureDescriptor cameraTextureDescriptor)
        {
            GraphicsFormat format = cameraTextureDescriptor.depthStencilFormat;
            if (IsUsable(format))
            {
                return format;
            }

            format = CoreUtils.GetDefaultDepthStencilFormat();
            if (IsUsable(format))
            {
                return format;
            }

            format = GraphicsFormatUtility.GetDepthStencilFormat(24);
            if (IsUsable(format))
            {
                return format;
            }

            return GraphicsFormat.D32_SFloat;
        }

        /// <summary>
        /// 协商本 feature 自建 MSAA 的采样数：从 <paramref name="requestedSamples"/> 往下取平台支持的最大值。
        /// 不支持多重采样纹理时返回 1（此时覆盖率退化成 0/1——这是平台能力问题，不是"跟随相机"）。
        /// </summary>
        public static int GetSupportedSampleCount(RenderTextureDescriptor cameraTextureDescriptor, int requestedSamples, bool selectionEnabled)
        {
            if (SystemInfo.supportsMultisampledTextures == 0 || requestedSamples <= 1)
            {
                return 1;
            }

            if (!TryGetIdGraphicsFormat(out GraphicsFormat idFormat, out _))
            {
                return 1;
            }

            int samples = Mathf.Max(2, requestedSamples);
            var idDescriptor = new RenderTextureDescriptor(
                Mathf.Max(1, cameraTextureDescriptor.width),
                Mathf.Max(1, cameraTextureDescriptor.height),
                idFormat,
                GraphicsFormat.None)
            {
                msaaSamples = samples,
                bindMS = false
            };

            int supported = SystemInfo.GetRenderTextureSupportedMSAASampleCount(idDescriptor);
            if (selectionEnabled)
            {
                var selectionDescriptor = new RenderTextureDescriptor(
                    Mathf.Max(1, cameraTextureDescriptor.width),
                    Mathf.Max(1, cameraTextureDescriptor.height),
                    GetLayerGraphicsFormat(),
                    GraphicsFormat.None)
                {
                    msaaSamples = samples,
                    bindMS = false
                };

                supported = Mathf.Min(supported, SystemInfo.GetRenderTextureSupportedMSAASampleCount(selectionDescriptor));
            }

            var depthDescriptor = CreateDepthDescriptor(cameraTextureDescriptor, samples, false);
            supported = Mathf.Min(supported, SystemInfo.GetRenderTextureSupportedMSAASampleCount(depthDescriptor));

            return Mathf.Clamp(supported, 1, samples);
        }

        public static RenderTextureDescriptor CreateLayerDescriptor(RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            // 辅助数据一律按普通纹理采样，因此单采样（与 GB / MetadataBuffer 同一约定）。
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.graphicsFormat = GetLayerGraphicsFormat();
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            return descriptor;
        }

        public static RenderTextureDescriptor CreateSurfaceDescriptor(RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor descriptor = CreateLayerDescriptor(cameraTextureDescriptor);
            descriptor.graphicsFormat = GetSurfaceGraphicsFormat();
            return descriptor;
        }

        public static RenderTextureDescriptor CreateMsaaLayerDescriptor(RenderTextureDescriptor cameraTextureDescriptor, int samples)
        {
            RenderTextureDescriptor descriptor = CreateLayerDescriptor(cameraTextureDescriptor);
            descriptor.msaaSamples = Mathf.Max(2, samples);
            descriptor.bindMS = true;
            return descriptor;
        }

        public static RenderTextureDescriptor CreateMsaaIdDescriptor(RenderTextureDescriptor cameraTextureDescriptor, int samples)
        {
            RenderTextureDescriptor descriptor = CreateLayerDescriptor(cameraTextureDescriptor);
            if (TryGetIdGraphicsFormat(out GraphicsFormat format, out _))
            {
                descriptor.graphicsFormat = format;
            }

            descriptor.msaaSamples = Mathf.Max(2, samples);
            descriptor.bindMS = true;
            return descriptor;
        }

        public static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, int samples, bool bindMS)
        {
            var descriptor = new RenderTextureDescriptor(
                Mathf.Max(1, cameraTextureDescriptor.width),
                Mathf.Max(1, cameraTextureDescriptor.height),
                GraphicsFormat.None,
                GetDepthStencilFormat(cameraTextureDescriptor))
            {
                dimension = cameraTextureDescriptor.dimension,
                volumeDepth = cameraTextureDescriptor.volumeDepth,
                msaaSamples = Mathf.Max(1, samples),
                useMipMap = false,
                autoGenerateMips = false,
                useDynamicScale = cameraTextureDescriptor.useDynamicScale,
                vrUsage = cameraTextureDescriptor.vrUsage
            };
            descriptor.bindMS = bindMS && descriptor.msaaSamples > 1;
            return descriptor;
        }

        private static bool IsUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
        }
    }
}
