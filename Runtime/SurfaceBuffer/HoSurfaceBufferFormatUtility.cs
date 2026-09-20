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
        /// owner 用 `R16_UNorm` 承载 16-bit IdentityId：0..65535 在 UNorm16 上是**逐值精确**的
        /// （65536 级），而且仍是普通可采样纹理 —— 规划里写的 `R16_UINT` 需要 `Texture2D&lt;uint&gt;` 与整数采样，
        /// 消费端（AC 的 owner 对齐、各效果的 validity）都要跟着换成整数通道，不值当。
        /// </summary>
        public static GraphicsFormat GetOwnerGraphicsFormat()
        {
            return IsUsable(GraphicsFormat.R16_UNorm) ? GraphicsFormat.R16_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
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
    }
}
