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
        private RTHandle outlineNormalDepthTexture;
        private RTHandle coverageTexture;
        private RTHandle outlineCoverageTexture;
        private RTHandle normalDepthMsaaTexture;
        private RTHandle depthMsaaTexture;
        private RTHandle outlineNormalDepthMsaaTexture;
        private RTHandle skyTexture;
        private int msaaSamples = 1;

        public RTHandle NormalDepthTexture => normalDepthTexture;
        public RTHandle DepthTexture => depthTexture;
        public RTHandle OutlineNormalDepthTexture => outlineNormalDepthTexture;
        public RTHandle CoverageTexture => coverageTexture;
        public RTHandle OutlineCoverageTexture => outlineCoverageTexture;
        public RTHandle NormalDepthMsaaTexture => normalDepthMsaaTexture;
        public RTHandle DepthMsaaTexture => depthMsaaTexture;
        public RTHandle OutlineNormalDepthMsaaTexture => outlineNormalDepthMsaaTexture;
        public RTHandle SkyTexture => skyTexture;
        public bool UseMsaaResolve => msaaSamples > 1 &&
            normalDepthMsaaTexture != null &&
            depthMsaaTexture != null &&
            outlineNormalDepthMsaaTexture != null &&
            coverageTexture != null &&
            outlineCoverageTexture != null;
        public int MsaaSamples => msaaSamples;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings, int requestedMsaaSamples)
        {
            RenderTextureDescriptor descriptor = CreateColorDescriptor(cameraTextureDescriptor, settings);
            GraphicsFormat format = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (format != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = format;
            }

            RenderingUtils.ReAllocateIfNeeded(ref normalDepthTexture, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.NormalDepthTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, CreateDepthDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.DepthTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref outlineNormalDepthTexture, CreateOutlineNormalDepthDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.OutlineNormalDepthTextureName);
            msaaSamples = Mathf.Max(1, requestedMsaaSamples);
            if (msaaSamples <= 1)
            {
                ReleaseMsaaResolveResources();
                return;
            }

            RenderTextureDescriptor msaaColorDescriptor = CreateMsaaColorDescriptor(cameraTextureDescriptor, settings, msaaSamples);
            if (format != GraphicsFormat.None)
            {
                msaaColorDescriptor.graphicsFormat = format;
            }

            RenderingUtils.ReAllocateIfNeeded(ref normalDepthMsaaTexture, msaaColorDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.NormalDepthTextureName + "MSAA");
            RenderingUtils.ReAllocateIfNeeded(ref depthMsaaTexture, CreateDepthDescriptor(cameraTextureDescriptor, settings, msaaSamples, true), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.DepthTextureName + "MSAA");
            RenderingUtils.ReAllocateIfNeeded(ref outlineNormalDepthMsaaTexture, msaaColorDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.OutlineNormalDepthTextureName + "MSAA");
            RenderingUtils.ReAllocateIfNeeded(ref coverageTexture, CreateCoverageDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.CoverageTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref outlineCoverageTexture, CreateCoverageDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.OutlineCoverageTextureName);
        }

        public void ReAllocateSkyIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            RenderingUtils.ReAllocateIfNeeded(ref skyTexture, CreateSkyDescriptor(cameraTextureDescriptor, settings), FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoGeometryBufferShaderConstants.SkyTextureName);
        }

        public void Release()
        {
            normalDepthTexture?.Release();
            depthTexture?.Release();
            outlineNormalDepthTexture?.Release();
            coverageTexture?.Release();
            outlineCoverageTexture?.Release();
            normalDepthMsaaTexture?.Release();
            depthMsaaTexture?.Release();
            outlineNormalDepthMsaaTexture?.Release();
            skyTexture?.Release();
            normalDepthTexture = null;
            depthTexture = null;
            outlineNormalDepthTexture = null;
            coverageTexture = null;
            outlineCoverageTexture = null;
            normalDepthMsaaTexture = null;
            depthMsaaTexture = null;
            outlineNormalDepthMsaaTexture = null;
            skyTexture = null;
            msaaSamples = 1;
        }

        public void ReleaseMsaaResolveResources()
        {
            coverageTexture?.Release();
            outlineCoverageTexture?.Release();
            normalDepthMsaaTexture?.Release();
            depthMsaaTexture?.Release();
            outlineNormalDepthMsaaTexture?.Release();
            coverageTexture = null;
            outlineCoverageTexture = null;
            normalDepthMsaaTexture = null;
            depthMsaaTexture = null;
            outlineNormalDepthMsaaTexture = null;
            msaaSamples = 1;
        }

        internal static RenderTextureDescriptor CreateColorDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            // Auxiliary geometry data is sampled as a regular texture.
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
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
            return CreateDepthDescriptor(cameraTextureDescriptor, settings, 1, false);
        }

        internal static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings, int msaaSamples, bool bindMS)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            int width = Mathf.Max(1, cameraTextureDescriptor.width / divisor);
            int height = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            GraphicsFormat depthFormat = HoGeometryBufferFormatUtility.GetDepthStencilFormat(cameraTextureDescriptor);
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.None, depthFormat);
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.volumeDepth = cameraTextureDescriptor.volumeDepth;
            descriptor.msaaSamples = Mathf.Max(1, msaaSamples);
            descriptor.bindMS = bindMS && descriptor.msaaSamples > 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        internal static RenderTextureDescriptor CreateCoverageDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            RenderTextureDescriptor descriptor = CreateColorDescriptor(cameraTextureDescriptor, settings);
            descriptor.graphicsFormat = HoGeometryBufferFormatUtility.GetCoverageGraphicsFormat();
            return descriptor;
        }

        internal static RenderTextureDescriptor CreateMsaaColorDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings, int msaaSamples)
        {
            RenderTextureDescriptor descriptor = CreateColorDescriptor(cameraTextureDescriptor, settings);
            descriptor.msaaSamples = Mathf.Max(2, msaaSamples);
            descriptor.bindMS = true;
            return descriptor;
        }

        internal static RenderTextureDescriptor CreateOutlineNormalDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            RenderTextureDescriptor descriptor = CreateColorDescriptor(cameraTextureDescriptor, settings);
            GraphicsFormat format = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (format != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = format;
            }

            return descriptor;
        }

        internal static int GetSupportedMsaaSampleCount(RenderTextureDescriptor cameraTextureDescriptor, HoGeometryBufferSettings settings)
        {
            if (settings == null ||
                settings.renderScale != HoGeometryBufferRenderScale.Full ||
                cameraTextureDescriptor.msaaSamples <= 1 ||
                SystemInfo.supportsMultisampledTextures == 0)
            {
                return 1;
            }

            RenderTextureDescriptor colorDescriptor = CreateMsaaColorDescriptor(cameraTextureDescriptor, settings, cameraTextureDescriptor.msaaSamples);
            GraphicsFormat colorFormat = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            if (colorFormat != GraphicsFormat.None)
            {
                colorDescriptor.graphicsFormat = colorFormat;
            }

            RenderTextureDescriptor depthDescriptor = CreateDepthDescriptor(cameraTextureDescriptor, settings, cameraTextureDescriptor.msaaSamples, true);
            int colorSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(colorDescriptor);
            int depthSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(depthDescriptor);
            return Mathf.Min(cameraTextureDescriptor.msaaSamples, colorSamples, depthSamples);
        }
    }
}
