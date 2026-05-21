using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AOV
{
    [DisallowMultipleRendererFeature("lilToon-HoAOV")]
    public sealed class HoAovRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoAovSettings settings = new HoAovSettings();

        private readonly HoAovRenderTargets renderTargets = new HoAovRenderTargets();
        private HoAovOutputPass outputPass;
        private HoAovDebugPass debugPass;
        private Material clearMaterial;
        private Material fallbackMaterial;
        private Material debugMaterial;
        private Shader clearShader;
        private Shader fallbackShader;
        private Shader debugShader;
        private bool registeredCameraReset;
        private bool warnedMissingClearShader;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingDebugShader;

        public HoAovSettings Settings => settings;

        public override void Create()
        {
            settings?.ClampCustomChannels();
            RegisterCameraReset();
            outputPass = new HoAovOutputPass();
            debugPass = new HoAovDebugPass();
        }

        private void OnValidate()
        {
            settings?.ClampCustomChannels();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterials();
            outputPass?.Setup(settings, renderTargets, clearMaterial, fallbackMaterial);
            debugPass?.Setup(settings, renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterials();
            if (outputPass != null)
            {
                outputPass.SetupRenderGraph(settings, renderTargets, clearMaterial, fallbackMaterial);
                renderer.EnqueuePass(outputPass);
            }

            if (debugPass != null && ShouldDebug(in renderingData))
            {
                debugPass.SetupRenderGraph(settings, renderTargets, debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            outputPass = null;
            debugPass?.Dispose();
            debugPass = null;
            CoreUtils.Destroy(clearMaterial);
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(debugMaterial);
            clearMaterial = null;
            fallbackMaterial = null;
            debugMaterial = null;
            clearShader = null;
            fallbackShader = null;
            debugShader = null;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetAovState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetAovState;
            registeredCameraReset = false;
        }

        private static void ResetAovState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalFloat(HoAovShaderConstants.ActiveId, 0.0f);
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private bool ShouldDebug(in RenderingData renderingData)
        {
            if (settings == null || settings.debugMode == HoAovDebugMode.Off)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return (cameraType == CameraType.SceneView && settings.debugInSceneView)
                || (cameraType == CameraType.Game && settings.debugInGameView);
        }

        private void EnsureMaterials()
        {
            EnsureClearMaterial();
            EnsureFallbackMaterial();
            EnsureDebugMaterial();
        }

        private void EnsureClearMaterial()
        {
            Shader shader = Shader.Find(HoAovShaderConstants.ClearShaderName);

            if (clearMaterial != null && clearShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(clearMaterial);
            clearMaterial = null;
            clearShader = shader;
            if (shader == null)
            {
                if (!warnedMissingClearShader)
                {
                    warnedMissingClearShader = true;
                    Debug.LogWarning($"HoAOV clear pass is unavailable because shader '{HoAovShaderConstants.ClearShaderName}' could not be found.");
                }

                return;
            }

            clearMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureFallbackMaterial()
        {
            Shader shader = settings != null && settings.fallbackShader != null
                ? settings.fallbackShader
                : Shader.Find(HoAovShaderConstants.FallbackShaderName);

            if (fallbackMaterial != null && fallbackShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(fallbackMaterial);
            fallbackMaterial = null;
            fallbackShader = shader;
            if (shader == null)
            {
                if (!warnedMissingFallbackShader)
                {
                    warnedMissingFallbackShader = true;
                    Debug.LogWarning($"HoAOV fallback output is unavailable because shader '{HoAovShaderConstants.FallbackShaderName}' could not be found.");
                }

                return;
            }

            fallbackMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureDebugMaterial()
        {
            Shader shader = settings != null && settings.debugShader != null
                ? settings.debugShader
                : Shader.Find(HoAovShaderConstants.DebugShaderName);

            if (debugMaterial != null && debugShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugShader = shader;
            if (shader == null)
            {
                if (!warnedMissingDebugShader)
                {
                    warnedMissingDebugShader = true;
                    Debug.LogWarning($"HoAOV debug view is unavailable because shader '{HoAovShaderConstants.DebugShaderName}' could not be found.");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed class HoAovOutputPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoAOV Output");
        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private static readonly List<ShaderTagId> AovShaderTagIds = new List<ShaderTagId>
        {
            HoAovShaderConstants.ShaderTagId
        };

        private static readonly List<ShaderTagId> SssShaderTagIds = new List<ShaderTagId>
        {
            HoAovShaderConstants.SssShaderTagId
        };

        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;

        private readonly RTHandle[] colorTargets = new RTHandle[7];
        private HoAovSettings settings;
        private HoAovRenderTargets renderTargets;
        private Material clearMaterial;
        private Material fallbackMaterial;
        private FilteringSettings aovFilteringSettings;
        private FilteringSettings fallbackFilteringSettings;
        private bool fallbackFilteringEnabled;
        private RenderStateBlock renderStateBlock;

        private sealed class PassData
        {
            public RendererListHandle fallbackRendererList;
            public RendererListHandle aovRendererList;
            public bool drawFallback;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle tangentNormalTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public TextureHandle objectCustom0Texture;
            public TextureHandle objectCustom1Texture;
            public TextureHandle sssTexture;
            public float systemChannelMask;
        }

        private sealed class ClearPassData
        {
            public Material clearMaterial;
        }

        private sealed class ResetPassData
        {
        }

        public HoAovOutputPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoAovSettings settings,
            HoAovRenderTargets renderTargets,
            Material clearMaterial,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.clearMaterial = clearMaterial;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.aovPassEvent : RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public void SetupRenderGraph(
            HoAovSettings settings,
            HoAovRenderTargets renderTargets,
            Material clearMaterial,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.clearMaterial = clearMaterial;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.aovPassEvent : RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings);
            colorTargets[0] = renderTargets.MaskIdTexture;
            colorTargets[1] = renderTargets.NormalDepthTexture;
            colorTargets[2] = renderTargets.TangentNormalTexture;
            colorTargets[3] = renderTargets.SurfaceDataTexture;
            colorTargets[4] = renderTargets.Custom0Texture;
            colorTargets[5] = renderTargets.ObjectCustom0Texture;
            colorTargets[6] = renderTargets.ObjectCustom1Texture;

            ConfigureTarget(colorTargets, renderTargets.DepthTexture);
            ConfigureClear(ClearFlag.All, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            ApplyFallbackMaterialProperties();
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                ClearAovTargets(cmd);
                SetGlobalTextures(cmd);
                cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
                SetDefaultSubjectProperties(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled)
                {
                    DrawingSettings fallbackDrawingSettings = CreateDrawingSettings(FallbackShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
                    fallbackDrawingSettings.overrideMaterialPassIndex = 0;
                    context.DrawRenderers(renderingData.cullResults, ref fallbackDrawingSettings, ref fallbackFilteringSettings, ref renderStateBlock);
                }

                DrawingSettings aovDrawingSettings = CreateDrawingSettings(AovShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref aovDrawingSettings, ref aovFilteringSettings, ref renderStateBlock);

                cmd.SetRenderTarget(
                    renderTargets.SssTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    renderTargets.DepthTexture,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings sssDrawingSettings = CreateDrawingSettings(SssShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref sssDrawingSettings, ref aovFilteringSettings, ref renderStateBlock);
                cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null)
            {
                AddResetPass(renderGraph);
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();

            TextureHandle maskIdTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetMaskGraphicsFormat(), HoAovShaderConstants.MaskIdTextureName));
            TextureHandle normalDepthTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.NormalDepthTextureName));
            TextureHandle tangentNormalTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.TangentNormalTextureName));
            TextureHandle surfaceDataTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.SurfaceDataTextureName));
            TextureHandle custom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.Custom0TextureName));
            TextureHandle objectCustom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.ObjectCustom0TextureName));
            TextureHandle objectCustom1Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.ObjectCustom1TextureName));
            TextureHandle sssTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.SssTextureName));
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoAovRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoAovShaderConstants.DepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            ApplyFallbackMaterialProperties();
            aovResources.maskIdTexture = maskIdTexture;
            aovResources.normalDepthTexture = normalDepthTexture;
            aovResources.tangentNormalTexture = tangentNormalTexture;
            aovResources.surfaceDataTexture = surfaceDataTexture;
            aovResources.custom0Texture = custom0Texture;
            aovResources.objectCustom0Texture = objectCustom0Texture;
            aovResources.objectCustom1Texture = objectCustom1Texture;
            aovResources.sssTexture = sssTexture;

            bool drawFallback = settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled;
            DrawingSettings fallbackDrawingSettings = RenderingUtils.CreateDrawingSettings(
                FallbackShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
            fallbackDrawingSettings.overrideMaterialPassIndex = 0;

            DrawingSettings aovDrawingSettings = RenderingUtils.CreateDrawingSettings(
                AovShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            RendererListParams fallbackRendererListParams = new RendererListParams(
                renderingData.cullResults,
                fallbackDrawingSettings,
                fallbackFilteringSettings);
            RendererListParams aovRendererListParams = new RendererListParams(
                renderingData.cullResults,
                aovDrawingSettings,
                aovFilteringSettings);
            DrawingSettings sssDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SssShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            RendererListParams sssRendererListParams = new RendererListParams(
                renderingData.cullResults,
                sssDrawingSettings,
                aovFilteringSettings);

            AddClearPass(
                renderGraph,
                maskIdTexture,
                normalDepthTexture,
                tangentNormalTexture,
                surfaceDataTexture,
                custom0Texture,
                objectCustom0Texture,
                objectCustom1Texture,
                depthTexture,
                clearMaterial);

            AddSssClearPass(renderGraph, sssTexture);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoAOV Output", out PassData passData, ProfilingSampler))
            {
                passData.drawFallback = drawFallback;
                passData.fallbackRendererList = drawFallback ? renderGraph.CreateRendererList(fallbackRendererListParams) : default;
                passData.aovRendererList = renderGraph.CreateRendererList(aovRendererListParams);
                passData.maskIdTexture = maskIdTexture;
                passData.normalDepthTexture = normalDepthTexture;
                passData.tangentNormalTexture = tangentNormalTexture;
                passData.surfaceDataTexture = surfaceDataTexture;
                passData.custom0Texture = custom0Texture;
                passData.objectCustom0Texture = objectCustom0Texture;
                passData.objectCustom1Texture = objectCustom1Texture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (drawFallback && passData.fallbackRendererList.IsValid())
                {
                    builder.UseRendererList(passData.fallbackRendererList);
                }

                if (passData.aovRendererList.IsValid())
                {
                    builder.UseRendererList(passData.aovRendererList);
                }

                builder.SetRenderAttachment(maskIdTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(normalDepthTexture, 1, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(tangentNormalTexture, 2, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(surfaceDataTexture, 3, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(custom0Texture, 4, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom0Texture, 5, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom1Texture, 6, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(maskIdTexture, HoAovShaderConstants.MaskIdTextureId);
                builder.SetGlobalTextureAfterPass(normalDepthTexture, HoAovShaderConstants.NormalDepthTextureId);
                builder.SetGlobalTextureAfterPass(tangentNormalTexture, HoAovShaderConstants.TangentNormalTextureId);
                builder.SetGlobalTextureAfterPass(surfaceDataTexture, HoAovShaderConstants.SurfaceDataTextureId);
                builder.SetGlobalTextureAfterPass(custom0Texture, HoAovShaderConstants.Custom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom0Texture, HoAovShaderConstants.ObjectCustom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom1Texture, HoAovShaderConstants.ObjectCustom1TextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.drawFallback && data.fallbackRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.fallbackRendererList);
                    }

                    if (data.aovRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.aovRendererList);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoAOV SSS", out PassData passData, ProfilingSampler))
            {
                passData.aovRendererList = renderGraph.CreateRendererList(sssRendererListParams);
                passData.sssTexture = sssTexture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (passData.aovRendererList.IsValid())
                {
                    builder.UseRendererList(passData.aovRendererList);
                }

                builder.SetRenderAttachment(sssTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(sssTexture, HoAovShaderConstants.SssTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.aovRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.aovRendererList);
                    }
                });
            }
        }

        private static void AddClearPass(
            RenderGraph renderGraph,
            TextureHandle maskIdTexture,
            TextureHandle normalDepthTexture,
            TextureHandle tangentNormalTexture,
            TextureHandle surfaceDataTexture,
            TextureHandle custom0Texture,
            TextureHandle objectCustom0Texture,
            TextureHandle objectCustom1Texture,
            TextureHandle depthTexture,
            Material clearMaterial)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ClearPassData>("lilToon-HoAOV Clear", out ClearPassData passData, ProfilingSampler))
            {
                passData.clearMaterial = clearMaterial;
                builder.SetRenderAttachment(maskIdTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(normalDepthTexture, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(tangentNormalTexture, 2, AccessFlags.WriteAll);
                builder.SetRenderAttachment(surfaceDataTexture, 3, AccessFlags.WriteAll);
                builder.SetRenderAttachment(custom0Texture, 4, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom0Texture, 5, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom1Texture, 6, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ClearPassData data, RasterGraphContext context) =>
                {
                    ClearAovTargets(context.cmd, data.clearMaterial);
                });
            }
        }

        private static void AddSssClearPass(RenderGraph renderGraph, TextureHandle sssTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("lilToon-HoAOV SSS Clear", out _, ProfilingSampler))
            {
                builder.SetRenderAttachment(sssTexture, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                });
            }
        }

        private void ClearAovTargets(CommandBuffer cmd)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (clearMaterial != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, clearMaterial, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private static void ClearAovTargets(RasterCommandBuffer cmd, Material material)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (material != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private void SetInactive(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 0.0f);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("lilToon-HoAOV Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 0.0f);
                });
            }
        }

        private static TextureDesc CreateTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoAovSettings settings,
            GraphicsFormat format,
            string name)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = format != GraphicsFormat.None ? format : cameraTextureDescriptor.graphicsFormat;
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = divisor == 1
                ? (MSAASamples)cameraTextureDescriptor.msaaSamples
                : MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = cameraTextureDescriptor.bindMS && divisor == 1;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        private static float GetSystemChannelMask(HoAovSettings settings)
        {
            return settings != null ? (float)settings.systemChannels : (float)HoAovChannelMask.Default;
        }

        private void SetGlobalTextures(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, renderTargets.MaskIdTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.TangentNormalTextureId, renderTargets.TangentNormalTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, renderTargets.ObjectCustom0Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, renderTargets.ObjectCustom1Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
        }

        private void ApplyFallbackMaterialProperties()
        {
            if (fallbackMaterial == null)
            {
                return;
            }

            fallbackMaterial.SetFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
        }

        private static void SetDefaultSubjectProperties(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoAovShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.SystemWriteMaskId, (float)HoAovChannelMask.Default);
            cmd.SetGlobalFloat(HoAovShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.UtilityId, 0.0f);
            cmd.SetGlobalVector(HoAovShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoAovShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectCustomMaskId, 0.0f);
        }

        private static void SetDefaultSubjectProperties(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoAovShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.SystemWriteMaskId, (float)HoAovChannelMask.Default);
            cmd.SetGlobalFloat(HoAovShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.UtilityId, 0.0f);
            cmd.SetGlobalVector(HoAovShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoAovShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectCustomMaskId, 0.0f);
        }

        private void ConfigureFiltering()
        {
            int minQueue = settings != null ? settings.minRenderQueue : 0;
            int maxQueue = settings != null ? settings.maxRenderQueue : (int)RenderQueue.Overlay - 1;
            if (maxQueue < minQueue)
            {
                maxQueue = minQueue;
            }

            RenderQueueRange renderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = maxQueue
            };
            int layerMask = settings != null ? settings.layerMask.value : -1;
            aovFilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            // The override fallback material cannot see the source material alpha/cutout data.
            // Keep it away from alpha-test and transparent queues; native HoAOV passes cover those.
            int fallbackMaxQueue = Mathf.Min(maxQueue, FallbackMaxRenderQueue);
            fallbackFilteringEnabled = fallbackMaxQueue >= minQueue;
            RenderQueueRange fallbackRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = fallbackFilteringEnabled ? fallbackMaxQueue : minQueue
            };
            fallbackFilteringSettings = new FilteringSettings(fallbackRenderQueueRange, fallbackFilteringEnabled ? layerMask : 0);
        }

    }

    internal sealed class HoAovDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoAOV Debug");
        private HoAovSettings settings;
        private HoAovRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private Material debugMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle tangentNormalTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public TextureHandle objectCustom0Texture;
            public TextureHandle objectCustom1Texture;
            public TextureHandle sssTexture;
            public Material debugMaterial;
            public HoAovDebugMode debugMode;
            public Vector4 debugDepthParams;
        }

        public void Setup(
            HoAovSettings settings,
            HoAovRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void SetupRenderGraph(
            HoAovSettings settings,
            HoAovRenderTargets renderTargets,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.debugMaterial = debugMaterial;
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void Dispose()
        {
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilHoAovDebugSource");
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || debugMaterial == null || cameraColorTarget == null || tempTexture == null || renderTargets == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(debugMaterial, settings);
                cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, renderTargets.MaskIdTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.TangentNormalTextureId, renderTargets.TangentNormalTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, renderTargets.ObjectCustom0Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, renderTargets.ObjectCustom1Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, debugMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()
                || !aovResources.maskIdTexture.IsValid()
                || !aovResources.normalDepthTexture.IsValid()
                || !aovResources.tangentNormalTexture.IsValid()
                || !aovResources.surfaceDataTexture.IsValid()
                || !aovResources.custom0Texture.IsValid()
                || !aovResources.objectCustom0Texture.IsValid()
                || !aovResources.objectCustom1Texture.IsValid()
                || !aovResources.sssTexture.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_lilHoAovDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoAOV Debug", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.maskIdTexture = aovResources.maskIdTexture;
                passData.normalDepthTexture = aovResources.normalDepthTexture;
                passData.tangentNormalTexture = aovResources.tangentNormalTexture;
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.custom0Texture = aovResources.custom0Texture;
                passData.objectCustom0Texture = aovResources.objectCustom0Texture;
                passData.objectCustom1Texture = aovResources.objectCustom1Texture;
                passData.sssTexture = aovResources.sssTexture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.debugDepthParams = GetDebugDepthParams(settings);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.tangentNormalTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.UseTexture(passData.custom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectCustom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectCustom1Texture, AccessFlags.Read);
                builder.UseTexture(passData.sssTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoAovShaderConstants.DebugModeId, (float)data.debugMode);
                    data.debugMaterial.SetVector(HoAovShaderConstants.DebugDepthParamsId, data.debugDepthParams);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.TangentNormalTextureId, data.tangentNormalTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, data.custom0Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, data.objectCustom0Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, data.objectCustom1Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, data.sssTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.debugMaterial, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void SetMaterialProperties(Material material, HoAovSettings settings)
        {
            material.SetFloat(HoAovShaderConstants.DebugModeId, (float)settings.debugMode);
            material.SetVector(HoAovShaderConstants.DebugDepthParamsId, GetDebugDepthParams(settings));
        }

        private static Vector4 GetDebugDepthParams(HoAovSettings settings)
        {
            float near = Mathf.Max(0.0f, settings.debugDepthNear);
            float far = Mathf.Max(near + 0.0001f, settings.debugDepthFar);
            return new Vector4(near, far, 1.0f / (far - near), 0.0f);
        }
    }

    internal sealed class HoAovRenderGraphResources : ContextItem
    {
        public TextureHandle maskIdTexture = TextureHandle.nullHandle;
        public TextureHandle normalDepthTexture = TextureHandle.nullHandle;
        public TextureHandle tangentNormalTexture = TextureHandle.nullHandle;
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
            tangentNormalTexture = TextureHandle.nullHandle;
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
        private RTHandle tangentNormalTexture;
        private RTHandle surfaceDataTexture;
        private RTHandle custom0Texture;
        private RTHandle objectCustom0Texture;
        private RTHandle objectCustom1Texture;
        private RTHandle sssTexture;
        private RTHandle depthTexture;

        public RTHandle MaskIdTexture => maskIdTexture;
        public RTHandle NormalDepthTexture => normalDepthTexture;
        public RTHandle TangentNormalTexture => tangentNormalTexture;
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
            RenderingUtils.ReAllocateIfNeeded(ref tangentNormalTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.TangentNormalTextureName);
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
            tangentNormalTexture?.Release();
            surfaceDataTexture?.Release();
            custom0Texture?.Release();
            objectCustom0Texture?.Release();
            objectCustom1Texture?.Release();
            sssTexture?.Release();
            depthTexture?.Release();
            maskIdTexture = null;
            normalDepthTexture = null;
            tangentNormalTexture = null;
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
