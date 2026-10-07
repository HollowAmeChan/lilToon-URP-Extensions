#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal sealed class HoShadowCastDebugPass : HoDebugRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ShadowCast Debug");

        private HoShadowCastRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private HoShadowCastFrameConfig config;
        private Material debugMaterial;

        private sealed class PassData
        {
            public Vector2Int debugSize;
            public TextureHandle atlasTexture;
            public TextureHandle secondAtlas;
            public Material debugMaterial;
            public int debugMode;
        }

        public void Setup(HoShadowCastFrameConfig config, HoShadowCastRenderTargets renderTargets, RTHandle cameraColorTarget, Material debugMaterial)
        {
            this.config = config;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void SetupRenderGraph(HoShadowCastFrameConfig config, Material debugMaterial)
        {
            this.config = config;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            ReleaseDebugResources();
            renderTargets = null;
            cameraColorTarget = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (debugMaterial == null || renderTargets == null || config == null || cameraColorTarget == null)
            {
                return;
            }

            RTHandle debugAtlas = config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas
                ? renderTargets.SecondDirectionalAtlasTexture
                : renderTargets.AtlasTexture;
            if (debugAtlas == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                if (config.debugMode == HoShadowCastDebugMode.All && renderTargets.SecondDirectionalAtlasTexture != null)
                    cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, renderTargets.SecondDirectionalAtlasTexture.nameID);
                cmd.SetGlobalInt(HoShadowCastShaderConstants.DebugModeId, (int)config.debugMode);
                if (config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas)
                {
                    cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, debugAtlas.nameID);
                }
                else
                {
                    cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, debugAtlas.nameID);
                }

                cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), debugMaterial, (int)config.debugMode, HoShadowCastShaderConstants.DebugModeId, HoShadowCastDebugViewInfo.Views, new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height), integerMode: true);
            }

            PublishCompatibility(renderingData.cameraData.camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (config == null)
            {
                return;
            }

            TextureHandle atlas = config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas
                ? shadowCastResources.secondDirectionalAtlasTexture
                : shadowCastResources.atlasTexture;
            if (!source.IsValid() || !atlas.IsValid())
            {
                return;
            }

            TextureHandle destination = CreateDebugTarget(renderGraph, frameData);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-ShadowCast Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugSize = DebugSize(renderGraph, destination);
                passData.atlasTexture = atlas;
                passData.secondAtlas = shadowCastResources.secondDirectionalAtlasTexture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = (int)config.debugMode;

                builder.UseTexture(atlas, AccessFlags.Read);
                if (config.debugMode == HoShadowCastDebugMode.All && shadowCastResources.secondDirectionalAtlasTexture.IsValid())
                    builder.UseTexture(shadowCastResources.secondDirectionalAtlasTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    if (data.debugMode == HoDebugGrid.All && data.secondAtlas.IsValid())
                        context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, data.secondAtlas);
                    context.cmd.SetGlobalInt(HoShadowCastShaderConstants.DebugModeId, data.debugMode);
                    if (data.debugMode == (int)HoShadowCastDebugMode.SecondDirectionalAtlas)
                    {
                        context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, data.atlasTexture);
                    }
                    else
                    {
                        context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, data.atlasTexture);
                    }

                    HoDebugGrid.Draw(context.cmd, data.debugMaterial, data.debugMode, HoShadowCastShaderConstants.DebugModeId, HoShadowCastDebugViewInfo.Views, data.debugSize, integerMode: true);
                });
            }

        }
    }
}
