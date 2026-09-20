using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>SB 的格式选择。与 OB 的同名工具同形：**平台不支持时明确回退，不静默降级**。</summary>
    internal static class HoSurfaceBufferFormatUtility
    {
        public static GraphicsFormat GetColorGraphicsFormat()
        {
            return IsUsable(GraphicsFormat.R16G16B16A16_SFloat)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;
        }

        public static GraphicsFormat GetUnormGraphicsFormat()
        {
            return IsUsable(GraphicsFormat.R8G8B8A8_UNorm) ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.B8G8R8A8_UNorm;
        }

        /// <summary>
        /// owner（16-bit IdentityId）用 **RGBA8 的两个字节**承载（R = 高字节、G = 低字节），
        /// 与 OB 身份池 `Id0.r/.g` 同一套做法。
        /// <para>
        /// **不用 `R16_UINT` / `R16_UNorm` 单独扛**：本趟是 6 个 MRT，而 R16 作为 MRT 在本仓库从未验证过 ——
        /// 附件组合非法时 D3D 会整趟丢 draw，表现就是"什么都没写进切图"（R1 已经踩过一次同类坑）。
        /// RGBA8 是这里已经被 MB/OB 跑通的组合。
        /// </para>
        /// </summary>
        public static GraphicsFormat GetOwnerGraphicsFormat()
        {
            return GetUnormGraphicsFormat();
        }

        public static bool IsUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None
                && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)
                && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample);
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
        /// 语义 lane 的**自建 MSAA 采样数**：只问平台，**与相机 MSAA 解耦**（与 OB 的决策同一条）。
        /// 相机把 AA 关掉时采样数照样是 4 —— 语义 lane 的质量不该由相机 AA 决定。
        /// 平台给不到就返回实际值（1 表示这趟没法跑），由 feature 报出来，不静默降级。
        /// </summary>
        public static int GetSupportedSemanticSampleCount(RenderTextureDescriptor cameraTextureDescriptor, int requestedSamples)
        {
            if (SystemInfo.supportsMultisampledTextures == 0 || requestedSamples <= 1)
            {
                return 1;
            }

            int width = Mathf.Max(1, cameraTextureDescriptor.width);
            int height = Mathf.Max(1, cameraTextureDescriptor.height);
            int samples = Mathf.Max(2, requestedSamples);

            var laneDescriptor = new RenderTextureDescriptor(width, height, GetUnormGraphicsFormat(), GraphicsFormat.None)
            {
                msaaSamples = samples,
                bindMS = false
            };
            int supported = SystemInfo.GetRenderTextureSupportedMSAASampleCount(laneDescriptor);

            var ownerDescriptor = new RenderTextureDescriptor(width, height, GetOwnerGraphicsFormat(), GraphicsFormat.None)
            {
                msaaSamples = samples,
                bindMS = false
            };
            supported = Mathf.Min(supported, SystemInfo.GetRenderTextureSupportedMSAASampleCount(ownerDescriptor));

            var depthDescriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.None, GetDepthStencilFormat(cameraTextureDescriptor))
            {
                msaaSamples = samples,
                bindMS = false
            };
            supported = Mathf.Min(supported, SystemInfo.GetRenderTextureSupportedMSAASampleCount(depthDescriptor));

            return Mathf.Clamp(supported, 1, samples);
        }
    }
}
