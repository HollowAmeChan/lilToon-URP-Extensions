using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal static class HoGeometryBufferFormatUtility
    {
        public static GraphicsFormat GetHighPrecisionGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return IsColorFormatUsable(preferredFormat) ? preferredFormat : GetFallbackColorFormat();
        }

        public static GraphicsFormat GetCoverageGraphicsFormat()
        {
            // R = fraction of covered MSAA samples, whichever surface wrote them.
            //     This stays the public coverage contract.
            // G = share of the pixel owned by the surface the resolve selected
            //     (the nearest sample). The two differ at a silhouette shared
            //     with a farther surface: R is 1 while G is < 1. An occlusion
            //     consumer needs G to know that such a pixel's colour is partly
            //     the background's, and therefore that the resolved surface's
            //     occlusion must not be presented for the whole pixel.
            if (IsColorFormatUsable(GraphicsFormat.R8G8_UNorm))
            {
                return GraphicsFormat.R8G8_UNorm;
            }

            if (IsColorFormatUsable(GraphicsFormat.R16G16_SFloat))
            {
                return GraphicsFormat.R16G16_SFloat;
            }

            return GetFallbackColorFormat();
        }

        public static GraphicsFormat GetDepthStencilFormat(RenderTextureDescriptor cameraTextureDescriptor)
        {
            GraphicsFormat format = cameraTextureDescriptor.depthStencilFormat;
            if (IsDepthStencilFormatUsable(format))
            {
                return format;
            }

            format = CoreUtils.GetDefaultDepthStencilFormat();
            if (IsDepthStencilFormatUsable(format))
            {
                return format;
            }

            format = GraphicsFormatUtility.GetDepthStencilFormat(24);
            if (IsDepthStencilFormatUsable(format))
            {
                return format;
            }

            format = GraphicsFormatUtility.GetDepthStencilFormat(32);
            if (IsDepthStencilFormatUsable(format))
            {
                return format;
            }

            return GraphicsFormat.D32_SFloat;
        }

        private static GraphicsFormat GetFallbackColorFormat()
        {
            GraphicsFormat format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            if (IsColorFormatUsable(format))
            {
                return format;
            }

            if (IsColorFormatUsable(GraphicsFormat.R8G8B8A8_UNorm))
            {
                return GraphicsFormat.R8G8B8A8_UNorm;
            }

            return GraphicsFormat.B8G8R8A8_UNorm;
        }

        private static bool IsColorFormatUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
        }

        private static bool IsDepthStencilFormatUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
        }
    }
}
