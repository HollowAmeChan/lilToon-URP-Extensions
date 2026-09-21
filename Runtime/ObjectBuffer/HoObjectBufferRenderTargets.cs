#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 兼容路径（非 RenderGraph）的 RTHandle 集合。
    /// **CB 只有身份与覆盖率**（OB 架构 §5.1 / 决策 20）：3 张 RGBA8 装 4 层 `(ID, 覆盖率)` + 按需的选择层；
    /// 线性表面色与材质数值（roughness / metallic / thickness / reflectance / PLR …）已搬到
    /// `Ho-SurfaceBuffer`，这里不再有 `_Surface` / `Material0`。
    /// ID pass 自己的 depth-stencil **只服务于自身绘制**，永不发布（决策 16）。
    /// </summary>
    internal sealed class HoObjectBufferRenderTargets
    {
        private RTHandle id0Texture;
        private RTHandle id1Texture;
        private RTHandle coverageTexture;
        private RTHandle selectionTexture;
        private RTHandle depthTexture;
        private RTHandle idMsaaTexture;
        private RTHandle selectionMsaaTexture;
        private RTHandle depthMsaaTexture;
        private int msaaSamples = 1;
        private bool idFormatIsInteger = true;

        public RTHandle Id0Texture => id0Texture;

        public RTHandle Id1Texture => id1Texture;

        public RTHandle CoverageTexture => coverageTexture;

        public RTHandle SelectionTexture => selectionTexture;

        public RTHandle DepthTexture => depthTexture;

        public RTHandle IdMsaaTexture => idMsaaTexture;

        public RTHandle SelectionMsaaTexture => selectionMsaaTexture;

        public RTHandle DepthMsaaTexture => depthMsaaTexture;

        public int MsaaSamples => msaaSamples;

        public bool IdFormatIsInteger => idFormatIsInteger;

        public bool UseMsaaResolve => msaaSamples > 1 && idMsaaTexture != null && depthMsaaTexture != null;

        public bool HasSelection => selectionTexture != null;

        public void ReAllocateIfNeeded(
            RenderTextureDescriptor cameraTextureDescriptor,
            int samples,
            bool selectionEnabled)
        {
            idFormatIsInteger = HoObjectBufferFormatUtility.TryGetIdGraphicsFormat(out _, out bool isInteger) && isInteger;

            RenderingUtils.ReAllocateIfNeeded(ref id0Texture, HoObjectBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.Id0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref id1Texture, HoObjectBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.Id1TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref coverageTexture, HoObjectBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.CoverageTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, HoObjectBufferFormatUtility.CreateDepthDescriptor(cameraTextureDescriptor, 1, false), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.Id0TextureName + "Depth");

            if (selectionEnabled)
            {
                RenderingUtils.ReAllocateIfNeeded(ref selectionTexture, HoObjectBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.SelectionTextureName);
            }
            else
            {
                selectionTexture?.Release();
                selectionTexture = null;
            }

            msaaSamples = Mathf.Max(1, samples);
            if (msaaSamples <= 1)
            {
                ReleaseMsaaResources();
                return;
            }

            RenderingUtils.ReAllocateIfNeeded(ref idMsaaTexture, HoObjectBufferFormatUtility.CreateMsaaIdDescriptor(cameraTextureDescriptor, msaaSamples), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.Id0TextureName + "MSAA");
            RenderingUtils.ReAllocateIfNeeded(ref depthMsaaTexture, HoObjectBufferFormatUtility.CreateDepthDescriptor(cameraTextureDescriptor, msaaSamples, true), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.Id0TextureName + "DepthMSAA");

            if (selectionEnabled)
            {
                RenderingUtils.ReAllocateIfNeeded(ref selectionMsaaTexture, HoObjectBufferFormatUtility.CreateMsaaLayerDescriptor(cameraTextureDescriptor, msaaSamples), FilterMode.Point, TextureWrapMode.Clamp, name: HoObjectBufferShaderConstants.SelectionTextureName + "MSAA");
            }
            else
            {
                selectionMsaaTexture?.Release();
                selectionMsaaTexture = null;
            }
        }

        private void ReleaseMsaaResources()
        {
            idMsaaTexture?.Release();
            selectionMsaaTexture?.Release();
            depthMsaaTexture?.Release();
            idMsaaTexture = null;
            selectionMsaaTexture = null;
            depthMsaaTexture = null;
        }

        public void Release()
        {
            id0Texture?.Release();
            id1Texture?.Release();
            coverageTexture?.Release();
            selectionTexture?.Release();
            depthTexture?.Release();
            ReleaseMsaaResources();
            id0Texture = null;
            id1Texture = null;
            coverageTexture = null;
            selectionTexture = null;
            depthTexture = null;
        }
    }
}
