using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 兼容路径（非 RenderGraph）的 RTHandle 集合。
    /// 布局见规划 §5.1：3 张 RGBA8 装 4 层 `(ID, 覆盖率)` + `_Surface` + 按需的 `Material0` / 选择层；
    /// ID pass 自己的 depth-stencil **只服务于自身绘制**，永不发布（决策 16）。
    /// </summary>
    internal sealed class HoCharacterBufferRenderTargets
    {
        private RTHandle id0Texture;
        private RTHandle id1Texture;
        private RTHandle coverageTexture;
        private RTHandle surfaceTexture;
        private RTHandle material0Texture;
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

        public RTHandle SurfaceTexture => surfaceTexture;

        public RTHandle Material0Texture => material0Texture;

        public RTHandle SelectionTexture => selectionTexture;

        public RTHandle DepthTexture => depthTexture;

        public RTHandle IdMsaaTexture => idMsaaTexture;

        public RTHandle SelectionMsaaTexture => selectionMsaaTexture;

        public RTHandle DepthMsaaTexture => depthMsaaTexture;

        public int MsaaSamples => msaaSamples;

        public bool IdFormatIsInteger => idFormatIsInteger;

        public bool UseMsaaResolve => msaaSamples > 1 && idMsaaTexture != null && depthMsaaTexture != null;

        public bool HasSelection => selectionTexture != null;

        public bool HasMaterial0 => material0Texture != null;

        public void ReAllocateIfNeeded(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoCharacterBufferSettings settings,
            int samples,
            bool material0Enabled,
            bool selectionEnabled)
        {
            idFormatIsInteger = HoCharacterBufferFormatUtility.TryGetIdGraphicsFormat(out _, out bool isInteger) && isInteger;

            RenderingUtils.ReAllocateIfNeeded(ref id0Texture, HoCharacterBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Id0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref id1Texture, HoCharacterBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Id1TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref coverageTexture, HoCharacterBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.CoverageTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref surfaceTexture, HoCharacterBufferFormatUtility.CreateSurfaceDescriptor(cameraTextureDescriptor), FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.SurfaceTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref depthTexture, HoCharacterBufferFormatUtility.CreateDepthDescriptor(cameraTextureDescriptor, 1, false), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Id0TextureName + "Depth");

            if (material0Enabled)
            {
                RenderingUtils.ReAllocateIfNeeded(ref material0Texture, HoCharacterBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Material0TextureName);
            }
            else
            {
                material0Texture?.Release();
                material0Texture = null;
            }

            if (selectionEnabled)
            {
                RenderingUtils.ReAllocateIfNeeded(ref selectionTexture, HoCharacterBufferFormatUtility.CreateLayerDescriptor(cameraTextureDescriptor), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.SelectionTextureName);
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

            RenderingUtils.ReAllocateIfNeeded(ref idMsaaTexture, HoCharacterBufferFormatUtility.CreateMsaaIdDescriptor(cameraTextureDescriptor, msaaSamples), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Id0TextureName + "MSAA");
            RenderingUtils.ReAllocateIfNeeded(ref depthMsaaTexture, HoCharacterBufferFormatUtility.CreateDepthDescriptor(cameraTextureDescriptor, msaaSamples, true), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.Id0TextureName + "DepthMSAA");

            if (selectionEnabled)
            {
                RenderingUtils.ReAllocateIfNeeded(ref selectionMsaaTexture, HoCharacterBufferFormatUtility.CreateMsaaLayerDescriptor(cameraTextureDescriptor, msaaSamples), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterBufferShaderConstants.SelectionTextureName + "MSAA");
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
            surfaceTexture?.Release();
            material0Texture?.Release();
            selectionTexture?.Release();
            depthTexture?.Release();
            ReleaseMsaaResources();
            id0Texture = null;
            id1Texture = null;
            coverageTexture = null;
            surfaceTexture = null;
            material0Texture = null;
            selectionTexture = null;
            depthTexture = null;
        }
    }
}
