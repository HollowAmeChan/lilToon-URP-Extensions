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
    }
}
