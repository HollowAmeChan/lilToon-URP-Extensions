using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private static void EnsureHdrDescriptor(ref RenderTextureDescriptor descriptor)
        {
            GraphicsFormat hdrFormat = GetShoostHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = hdrFormat;
            }
        }

        private static void EnsureHdrTextureDesc(ref TextureDesc descriptor)
        {
            GraphicsFormat hdrFormat = GetShoostHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.format = hdrFormat;
            }
        }

        private static GraphicsFormat GetShoostHdrGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

    }
}
