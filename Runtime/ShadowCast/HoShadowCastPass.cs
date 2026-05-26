#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal sealed class HoShadowCastPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ShadowCast");
        private static readonly ShaderTagId ShadowCasterShaderTagId = new ShaderTagId("ShadowCaster");
        private readonly HoShadowCastFrame frame = new HoShadowCastFrame();
        private readonly HoShadowCastSecondDirectionalFrame secondDirectionalFrame = new HoShadowCastSecondDirectionalFrame();
        private HoShadowCastSettings settings;
        private HoShadowCastFrameConfig config;
        private HoShadowCastRenderTargets renderTargets;

        private sealed class PassData
        {
            public TextureHandle atlasTexture;
            public HoShadowCastFrame frame;
            public RendererListHandle[] rendererLists;
        }

        private sealed class SecondDirectionalPassData
        {
            public TextureHandle atlasTexture;
            public HoShadowCastSecondDirectionalFrame frame;
            public RendererListHandle[] rendererLists;
        }

        public HoShadowCastPass()
        {
            profilingSampler = ProfilingSampler;
        }

        public void Setup(HoShadowCastSettings settings, HoShadowCastFrameConfig config, HoShadowCastRenderTargets renderTargets)
        {
            this.settings = settings;
            this.config = config;
            this.renderTargets = renderTargets;
            ConfigurePass();
        }

        public void SetupRenderGraph(HoShadowCastSettings settings, HoShadowCastFrameConfig config)
        {
            this.settings = settings;
            this.config = config;
            ConfigurePass();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null || config == null)
            {
                return;
            }

            renderTargets.ReAllocateIfNeeded(HoShadowCastAtlasDescriptors.CreateAtlasDescriptor(config));
            renderTargets.ReAllocateSecondDirectionalIfNeeded(HoShadowCastAtlasDescriptors.CreateSecondDirectionalAtlasDescriptor(config));
            ConfigureTarget(renderTargets.AtlasTexture);
            ConfigureClear(ClearFlag.Depth, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null || config == null || renderTargets.AtlasTexture == null)
            {
                return;
            }

            LightData lightData = renderingData.lightData;
            ShadowData shadowData = renderingData.shadowData;
            HoShadowCastFrameDiagnostics diagnostics = HoShadowCastRuntimeDiagnostics.Begin(
                "Compatibility",
                config,
                lightData.visibleLights.IsCreated ? lightData.visibleLights.Length : 0,
                renderingData.cameraData.camera);
            bool hasFrame = HoShadowCastFrameCollector.BuildFrameData(
                    config,
                    ref renderingData.cullResults,
                    lightData,
                    ref shadowData,
                    renderingData.cameraData.camera,
                    lightData.mainLightIndex,
                    renderingData.cameraData.worldSpaceCameraPos,
                    renderingData.cameraData.GetViewMatrix(),
                    renderingData.cameraData.GetProjectionMatrix(),
                    frame,
                    diagnostics);
            bool hasSecondDirectionalFrame = HoShadowCastFrameCollector.BuildSecondDirectionalFrameData(
                    config,
                    ref renderingData.cullResults,
                    lightData.visibleLights,
                    renderingData.cameraData.camera,
                    lightData.mainLightIndex,
                    renderingData.cameraData.worldSpaceCameraPos,
                    renderingData.cameraData.GetViewMatrix(),
                    renderingData.cameraData.GetProjectionMatrix(),
                    secondDirectionalFrame,
                    diagnostics);
            diagnostics.Publish(hasFrame, frame, hasSecondDirectionalFrame, secondDirectionalFrame);
            HoShadowCastFrameCollector.MaybeLogDebugFrame(config, frame, secondDirectionalFrame, "Compatibility", hasFrame, hasSecondDirectionalFrame);
            if (!hasFrame && !hasSecondDirectionalFrame)
            {
                HoShadowCastPublisher.SetGlobalEmpty();
                HoShadowCastPublisher.SetSecondDirectionalGlobalEmpty();
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                if (hasFrame)
                {
                    cmd.SetRenderTarget(renderTargets.AtlasTexture.nameID);
                    cmd.ClearRenderTarget(true, false, Color.clear);
                    HoShadowCastPublisher.ApplyGlobalData(cmd, frame, renderTargets.AtlasTexture.nameID);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    for (int i = 0; i < frame.sliceCount; i++)
                    {
                        ShadowSliceInfo slice = frame.slices[i];
                        DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(renderingData.cameraData.camera);
                        FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(config, slice.renderingLayerMask);

                        SetShadowCasterGlobals(cmd, frame.cameraPosition, slice);
                        RenderShadowSlice(cmd, ref context, ref renderingData.cullResults, ref slice.shadowSliceData, ref drawingSettings, ref filteringSettings, slice.projectionMatrix, slice.viewMatrix);
                    }
                }
                else
                {
                    HoShadowCastPublisher.SetGlobalEmpty(cmd);
                }

                if (hasSecondDirectionalFrame && renderTargets.SecondDirectionalAtlasTexture != null)
                {
                    cmd.SetRenderTarget(renderTargets.SecondDirectionalAtlasTexture.nameID);
                    cmd.ClearRenderTarget(true, false, Color.clear);
                    HoShadowCastPublisher.ApplySecondDirectionalGlobalData(cmd, secondDirectionalFrame, renderTargets.SecondDirectionalAtlasTexture.nameID);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    for (int i = 0; i < secondDirectionalFrame.sliceCount; i++)
                    {
                        ShadowSliceInfo slice = secondDirectionalFrame.slices[i];
                        DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(renderingData.cameraData.camera);
                        FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(config, slice.renderingLayerMask);

                        SetShadowCasterGlobals(cmd, secondDirectionalFrame.cameraPosition, slice);
                        RenderShadowSlice(cmd, ref context, ref renderingData.cullResults, ref slice.shadowSliceData, ref drawingSettings, ref filteringSettings, slice.projectionMatrix, slice.viewMatrix);
                    }
                }
                else
                {
                    HoShadowCastPublisher.SetSecondDirectionalGlobalEmpty(cmd);
                }

                cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, false);
                RestoreCameraGlobals(cmd, hasFrame ? frame.cameraPosition : secondDirectionalFrame.cameraPosition, hasFrame ? frame.cameraViewMatrix : secondDirectionalFrame.cameraViewMatrix, hasFrame ? frame.cameraProjectionMatrix : secondDirectionalFrame.cameraProjectionMatrix);
                if (hasFrame)
                {
                    HoShadowCastPublisher.ApplyGlobalData(cmd, frame, renderTargets.AtlasTexture.nameID);
                }

                if (hasSecondDirectionalFrame && renderTargets.SecondDirectionalAtlasTexture != null)
                {
                    HoShadowCastPublisher.ApplySecondDirectionalGlobalData(cmd, secondDirectionalFrame, renderTargets.SecondDirectionalAtlasTexture.nameID);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null || config == null)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
            HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();
            HoShadowCastFrameDiagnostics diagnostics = HoShadowCastRuntimeDiagnostics.Begin(
                "RenderGraph",
                config,
                lightData.visibleLights.IsCreated ? lightData.visibleLights.Length : 0,
                cameraData.camera);

            HoShadowCastFrame renderGraphFrame = new HoShadowCastFrame();
            bool hasFrame = HoShadowCastFrameCollector.BuildFrameData(
                    config,
                    ref renderingData.cullResults,
                    lightData,
                    shadowData,
                    cameraData.camera,
                    lightData.mainLightIndex,
                    cameraData.worldSpaceCameraPos,
                    cameraData.GetViewMatrix(),
                    cameraData.GetProjectionMatrix(),
                    renderGraphFrame,
                    diagnostics);
            HoShadowCastSecondDirectionalFrame renderGraphSecondDirectionalFrame = new HoShadowCastSecondDirectionalFrame();
            bool hasSecondDirectionalFrame = HoShadowCastFrameCollector.BuildSecondDirectionalFrameData(
                    config,
                    ref renderingData.cullResults,
                    lightData.visibleLights,
                    cameraData.camera,
                    lightData.mainLightIndex,
                    cameraData.worldSpaceCameraPos,
                    cameraData.GetViewMatrix(),
                    cameraData.GetProjectionMatrix(),
                    renderGraphSecondDirectionalFrame,
                    diagnostics);
            diagnostics.Publish(hasFrame, renderGraphFrame, hasSecondDirectionalFrame, renderGraphSecondDirectionalFrame);
            HoShadowCastFrameCollector.MaybeLogDebugFrame(config, renderGraphFrame, renderGraphSecondDirectionalFrame, "RenderGraph", hasFrame, hasSecondDirectionalFrame);
            if (!hasFrame && !hasSecondDirectionalFrame)
            {
                return;
            }

            if (hasFrame)
            {
                TextureHandle atlasTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    HoShadowCastAtlasDescriptors.CreateAtlasDescriptor(config),
                    HoShadowCastShaderConstants.AtlasTextureName,
                    true,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-ShadowCast ShadowMap", out PassData passData, ProfilingSampler))
                {
                    passData.atlasTexture = atlasTexture;
                    passData.frame = renderGraphFrame;
                    passData.rendererLists = new RendererListHandle[renderGraphFrame.sliceCount];

                    for (int i = 0; i < renderGraphFrame.sliceCount; i++)
                    {
                        ShadowSliceInfo slice = renderGraphFrame.slices[i];
                        DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(cameraData.camera);
                        FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(config, slice.renderingLayerMask);
                        RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                        passData.rendererLists[i] = renderGraph.CreateRendererList(rendererListParams);
                        builder.UseRendererList(passData.rendererLists[i]);
                    }

                    builder.SetRenderAttachmentDepth(atlasTexture, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetGlobalTextureAfterPass(atlasTexture, HoShadowCastShaderConstants.AtlasTextureId);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        HoShadowCastFrame frame = data.frame;
                        cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1.0f, 0);
                        HoShadowCastPublisher.ApplyGlobalData(cmd, frame);

                        for (int i = 0; i < frame.sliceCount; i++)
                        {
                            ShadowSliceInfo slice = frame.slices[i];
                            SetShadowCasterGlobals(cmd, frame.cameraPosition, slice);
                            RenderShadowSlice(cmd, ref slice.shadowSliceData, data.rendererLists[i], slice.projectionMatrix, slice.viewMatrix);
                        }

                        cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, false);
                        RestoreCameraGlobals(cmd, frame);
                        HoShadowCastPublisher.ApplyGlobalData(cmd, frame);
                    });

                    shadowCastResources.atlasTexture = atlasTexture;
                }
            }

            if (hasSecondDirectionalFrame)
            {
                TextureHandle secondDirectionalAtlasTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    HoShadowCastAtlasDescriptors.CreateSecondDirectionalAtlasDescriptor(config),
                    HoShadowCastShaderConstants.SecondDirectionalAtlasTextureName,
                    true,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);

                using (var builder = renderGraph.AddRasterRenderPass<SecondDirectionalPassData>("Ho-ShadowCast SecondDirectional", out SecondDirectionalPassData passData, ProfilingSampler))
                {
                    passData.atlasTexture = secondDirectionalAtlasTexture;
                    passData.frame = renderGraphSecondDirectionalFrame;
                    passData.rendererLists = new RendererListHandle[renderGraphSecondDirectionalFrame.sliceCount];

                    for (int i = 0; i < renderGraphSecondDirectionalFrame.sliceCount; i++)
                    {
                        ShadowSliceInfo slice = renderGraphSecondDirectionalFrame.slices[i];
                        DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(cameraData.camera);
                        FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(config, slice.renderingLayerMask);
                        RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                        passData.rendererLists[i] = renderGraph.CreateRendererList(rendererListParams);
                        builder.UseRendererList(passData.rendererLists[i]);
                    }

                    builder.SetRenderAttachmentDepth(secondDirectionalAtlasTexture, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetGlobalTextureAfterPass(secondDirectionalAtlasTexture, HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId);
                    builder.SetRenderFunc(static (SecondDirectionalPassData data, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        HoShadowCastSecondDirectionalFrame frame = data.frame;
                        cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1.0f, 0);
                        HoShadowCastPublisher.ApplySecondDirectionalGlobalData(cmd, frame);

                        for (int i = 0; i < frame.sliceCount; i++)
                        {
                            ShadowSliceInfo slice = frame.slices[i];
                            SetShadowCasterGlobals(cmd, frame.cameraPosition, slice);
                            RenderShadowSlice(cmd, ref slice.shadowSliceData, data.rendererLists[i], slice.projectionMatrix, slice.viewMatrix);
                        }

                        cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, false);
                        RestoreCameraGlobals(cmd, frame.cameraPosition, frame.cameraViewMatrix, frame.cameraProjectionMatrix);
                        HoShadowCastPublisher.ApplySecondDirectionalGlobalData(cmd, frame);
                    });

                    shadowCastResources.secondDirectionalAtlasTexture = secondDirectionalAtlasTexture;
                }
            }
        }

        private void ReleaseCompatibilityResources()
        {
            renderTargets?.Release();
            renderTargets = null;
        }

        private void ConfigurePass()
        {
            RenderPassEvent passEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingPrePasses;
            renderPassEvent = passEvent < RenderPassEvent.BeforeRenderingPrePasses ? RenderPassEvent.BeforeRenderingPrePasses : passEvent;
        }

        private static void SetShadowCasterGlobals(CommandBuffer cmd, Vector3 cameraPosition, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, slice.viewMatrix);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.ShadowBiasId, slice.shadowBias);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightDirectionId, new Vector4(slice.lightDirection.x, slice.lightDirection.y, slice.lightDirection.z, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightPositionId, new Vector4(slice.lightPosition.x, slice.lightPosition.y, slice.lightPosition.z, 1.0f));
            cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, slice.lightType != LightType.Directional);
        }

        private static void SetShadowCasterGlobals(RasterCommandBuffer cmd, Vector3 cameraPosition, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, slice.viewMatrix);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.ShadowBiasId, slice.shadowBias);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightDirectionId, new Vector4(slice.lightDirection.x, slice.lightDirection.y, slice.lightDirection.z, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightPositionId, new Vector4(slice.lightPosition.x, slice.lightPosition.y, slice.lightPosition.z, 1.0f));
            cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, slice.lightType != LightType.Directional);
        }

        private static void SetWorldToCameraMatrices(CommandBuffer cmd, Matrix4x4 viewMatrix)
        {
            Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * viewMatrix;
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.WorldToCameraMatrixId, worldToCameraMatrix);
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.CameraToWorldMatrixId, worldToCameraMatrix.inverse);
        }

        private static void SetWorldToCameraMatrices(RasterCommandBuffer cmd, Matrix4x4 viewMatrix)
        {
            Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * viewMatrix;
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.WorldToCameraMatrixId, worldToCameraMatrix);
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.CameraToWorldMatrixId, worldToCameraMatrix.inverse);
        }

        private static DrawingSettings CreateShadowCasterDrawingSettings(Camera camera)
        {
            SortingSettings sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.None
            };

            return new DrawingSettings(ShadowCasterShaderTagId, sortingSettings)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true
            };
        }

        private static FilteringSettings CreateShadowCasterFilteringSettings(HoShadowCastFrameConfig config, uint renderingLayerMask)
        {
            int layerMask = config != null ? config.casterLayerMask.value : -1;
            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);
            filteringSettings.renderingLayerMask = renderingLayerMask != 0u ? renderingLayerMask : uint.MaxValue;
            return filteringSettings;
        }

        private static void RestoreCameraGlobals(CommandBuffer cmd, HoShadowCastFrame frame)
        {
            RestoreCameraGlobals(cmd, frame.cameraPosition, frame.cameraViewMatrix, frame.cameraProjectionMatrix);
        }

        private static void RestoreCameraGlobals(CommandBuffer cmd, Vector3 cameraPosition, Matrix4x4 cameraViewMatrix, Matrix4x4 cameraProjectionMatrix)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, cameraViewMatrix);
            cmd.SetViewProjectionMatrices(cameraViewMatrix, cameraProjectionMatrix);
        }

        private static void RestoreCameraGlobals(RasterCommandBuffer cmd, HoShadowCastFrame frame)
        {
            RestoreCameraGlobals(cmd, frame.cameraPosition, frame.cameraViewMatrix, frame.cameraProjectionMatrix);
        }

        private static void RestoreCameraGlobals(RasterCommandBuffer cmd, Vector3 cameraPosition, Matrix4x4 cameraViewMatrix, Matrix4x4 cameraProjectionMatrix)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, cameraViewMatrix);
            cmd.SetViewProjectionMatrices(cameraViewMatrix, cameraProjectionMatrix);
        }

        private static void RenderShadowSlice(
            CommandBuffer cmd,
            ref ScriptableRenderContext context,
            ref CullingResults cullingResults,
            ref ShadowSliceData shadowSliceData,
            ref DrawingSettings drawingSettings,
            ref FilteringSettings filteringSettings,
            Matrix4x4 projectionMatrix,
            Matrix4x4 viewMatrix)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f);
            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);

            cmd.DisableScissorRect();
            cmd.SetGlobalDepthBias(0.0f, 0.0f);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private static void RenderShadowSlice(RasterCommandBuffer cmd, ref ShadowSliceData shadowSliceData, RendererListHandle rendererList, Matrix4x4 projectionMatrix, Matrix4x4 viewMatrix)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f);
            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            cmd.DrawRendererList(rendererList);
            cmd.DisableScissorRect();
            cmd.SetGlobalDepthBias(0.0f, 0.0f);
        }
    }
}
