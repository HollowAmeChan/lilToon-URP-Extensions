#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal sealed class HoMetadataBufferRenderTargets
    {
        private RTHandle maskIdTexture;
        private RTHandle surfaceDataTexture;
        private RTHandle custom0Texture;
        private RTHandle objectCustom0Texture;
        private RTHandle objectCustom1Texture;
        private RTHandle surfaceColorTexture;
        private RTHandle depthTexture;

        public RTHandle MaskIdTexture => maskIdTexture;
        public RTHandle SurfaceDataTexture => surfaceDataTexture;
        public RTHandle Custom0Texture => custom0Texture;
        public RTHandle ObjectCustom0Texture => objectCustom0Texture;
        public RTHandle ObjectCustom1Texture => objectCustom1Texture;
        public RTHandle SurfaceColorTexture => surfaceColorTexture;
        public RTHandle DepthTexture => depthTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoMetadataBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = divisor == 1 ? Mathf.Max(1, descriptor.msaaSamples) : 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);

            RenderTextureDescriptor maskDescriptor = descriptor;
            GraphicsFormat maskFormat = HoMetadataBufferFormatUtility.GetMaskGraphicsFormat();
            if (maskFormat != GraphicsFormat.None)
            {
                maskDescriptor.graphicsFormat = maskFormat;
            }

            RenderTextureDescriptor highPrecisionDescriptor = descriptor;
            GraphicsFormat highPrecisionFormat = HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (highPrecisionFormat != GraphicsFormat.None)
            {
                highPrecisionDescriptor.graphicsFormat = highPrecisionFormat;
            }

            RenderTextureDescriptor depthDescriptor = CreateDepthDescriptor(cameraTextureDescriptor, settings);

            RenderingUtils.ReAllocateIfNeeded(ref maskIdTexture, maskDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.MaskIdTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref surfaceDataTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.SurfaceDataTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref custom0Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.Custom0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref objectCustom0Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.ObjectCustom0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref objectCustom1Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.ObjectCustom1TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref surfaceColorTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.SurfaceColorTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoMetadataBufferShaderConstants.DepthTextureName);
        }

        public void Release()
        {
            maskIdTexture?.Release();
            surfaceDataTexture?.Release();
            custom0Texture?.Release();
            objectCustom0Texture?.Release();
            objectCustom1Texture?.Release();
            surfaceColorTexture?.Release();
            depthTexture?.Release();
            maskIdTexture = null;
            surfaceDataTexture = null;
            custom0Texture = null;
            objectCustom0Texture = null;
            objectCustom1Texture = null;
            surfaceColorTexture = null;
            depthTexture = null;
        }

        internal static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoMetadataBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            int width = Mathf.Max(1, cameraTextureDescriptor.width / divisor);
            int height = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            GraphicsFormat depthFormat = HoMetadataBufferFormatUtility.GetDepthStencilFormat(cameraTextureDescriptor);
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

    }
}
