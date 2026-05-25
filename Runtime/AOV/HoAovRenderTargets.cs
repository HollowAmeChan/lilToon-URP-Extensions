#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AOV
{
    internal sealed class HoAovRenderGraphResources : ContextItem
    {
        public TextureHandle maskIdTexture = TextureHandle.nullHandle;
        public TextureHandle normalDepthTexture = TextureHandle.nullHandle;
        public TextureHandle surfaceDataTexture = TextureHandle.nullHandle;
        public TextureHandle custom0Texture = TextureHandle.nullHandle;
        public TextureHandle objectCustom0Texture = TextureHandle.nullHandle;
        public TextureHandle objectCustom1Texture = TextureHandle.nullHandle;
        public TextureHandle sssTexture = TextureHandle.nullHandle;

        public bool HasRequiredTextures => maskIdTexture.IsValid()
            && normalDepthTexture.IsValid()
            && surfaceDataTexture.IsValid()
            && sssTexture.IsValid();

        public override void Reset()
        {
            maskIdTexture = TextureHandle.nullHandle;
            normalDepthTexture = TextureHandle.nullHandle;
            surfaceDataTexture = TextureHandle.nullHandle;
            custom0Texture = TextureHandle.nullHandle;
            objectCustom0Texture = TextureHandle.nullHandle;
            objectCustom1Texture = TextureHandle.nullHandle;
            sssTexture = TextureHandle.nullHandle;
        }
    }

    internal sealed class HoAovRenderTargets
    {
        private RTHandle maskIdTexture;
        private RTHandle normalDepthTexture;
        private RTHandle surfaceDataTexture;
        private RTHandle custom0Texture;
        private RTHandle objectCustom0Texture;
        private RTHandle objectCustom1Texture;
        private RTHandle sssTexture;
        private RTHandle depthTexture;

        public RTHandle MaskIdTexture => maskIdTexture;
        public RTHandle NormalDepthTexture => normalDepthTexture;
        public RTHandle SurfaceDataTexture => surfaceDataTexture;
        public RTHandle Custom0Texture => custom0Texture;
        public RTHandle ObjectCustom0Texture => objectCustom0Texture;
        public RTHandle ObjectCustom1Texture => objectCustom1Texture;
        public RTHandle SssTexture => sssTexture;
        public RTHandle DepthTexture => depthTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoAovSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = divisor == 1 ? Mathf.Max(1, descriptor.msaaSamples) : 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);

            RenderTextureDescriptor maskDescriptor = descriptor;
            GraphicsFormat maskFormat = GetMaskGraphicsFormat();
            if (maskFormat != GraphicsFormat.None)
            {
                maskDescriptor.graphicsFormat = maskFormat;
            }

            RenderTextureDescriptor highPrecisionDescriptor = descriptor;
            GraphicsFormat highPrecisionFormat = GetHighPrecisionGraphicsFormat();
            if (highPrecisionFormat != GraphicsFormat.None)
            {
                highPrecisionDescriptor.graphicsFormat = highPrecisionFormat;
            }

            RenderTextureDescriptor depthDescriptor = CreateDepthDescriptor(cameraTextureDescriptor, settings);

            RenderingUtils.ReAllocateIfNeeded(ref maskIdTexture, maskDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.MaskIdTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref normalDepthTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.NormalDepthTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref surfaceDataTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.SurfaceDataTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref custom0Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.Custom0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref objectCustom0Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.ObjectCustom0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref objectCustom1Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.ObjectCustom1TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref sssTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.SssTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.DepthTextureName);
        }

        public void Release()
        {
            maskIdTexture?.Release();
            normalDepthTexture?.Release();
            surfaceDataTexture?.Release();
            custom0Texture?.Release();
            objectCustom0Texture?.Release();
            objectCustom1Texture?.Release();
            sssTexture?.Release();
            depthTexture?.Release();
            maskIdTexture = null;
            normalDepthTexture = null;
            surfaceDataTexture = null;
            custom0Texture = null;
            objectCustom0Texture = null;
            objectCustom1Texture = null;
            sssTexture = null;
            depthTexture = null;
        }

        internal static GraphicsFormat GetMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8G8B8A8_UNorm;
            return IsColorFormatUsable(preferredFormat) ? preferredFormat : GetFallbackColorFormat();
        }

        internal static GraphicsFormat GetHighPrecisionGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return IsColorFormatUsable(preferredFormat) ? preferredFormat : GetFallbackColorFormat();
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
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, FormatUsage.Render);
        }

        internal static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoAovSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            int width = Mathf.Max(1, cameraTextureDescriptor.width / divisor);
            int height = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            GraphicsFormat depthFormat = GetDepthStencilFormat(cameraTextureDescriptor);
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.None, depthFormat);
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.volumeDepth = cameraTextureDescriptor.volumeDepth;
            descriptor.msaaSamples = divisor == 1 ? Mathf.Max(1, cameraTextureDescriptor.msaaSamples) : 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        internal static GraphicsFormat GetDepthStencilFormat(RenderTextureDescriptor cameraTextureDescriptor)
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

        private static bool IsDepthStencilFormatUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, FormatUsage.Render);
        }
    }
}
