#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferRenderTargets
    {
        private RTHandle normalDepthTexture;
        private RTHandle depthTexture;
        private RTHandle skyTexture;

        public RTHandle NormalDepthTexture => normalDepthTexture;
        public RTHandle DepthTexture => depthTexture;
        public RTHandle SkyTexture => skyTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            RenderTextureDescriptor descriptor = CreateColorDescriptor(cameraTextureDescriptor, settings);
            GraphicsFormat format = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (format != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = format;
            }

            RenderingUtils.ReAllocateIfNeeded(ref normalDepthTexture, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.NormalDepthTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, CreateDepthDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.DepthTextureName);
        }

        public void ReAllocateSkyIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            RenderingUtils.ReAllocateIfNeeded(ref skyTexture, CreateSkyDescriptor(cameraTextureDescriptor, settings), FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.SkyTextureName);
        }

        public void Release()
        {
            normalDepthTexture?.Release();
            depthTexture?.Release();
            skyTexture?.Release();
            normalDepthTexture = null;
            depthTexture = null;
            skyTexture = null;
        }

        internal static RenderTextureDescriptor CreateColorDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = divisor == 1 ? Mathf.Max(1, descriptor.msaaSamples) : 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);
            return descriptor;
        }

        internal static RenderTextureDescriptor CreateSkyDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.skyRenderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);
            GraphicsFormat format = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (format != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = format;
            }

            return descriptor;
        }

        internal static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            int width = Mathf.Max(1, cameraTextureDescriptor.width / divisor);
            int height = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            GraphicsFormat depthFormat = HoGeometryBufferFormatUtility.GetDepthStencilFormat(cameraTextureDescriptor);
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
