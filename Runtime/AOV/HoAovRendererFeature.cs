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
        private Material fallbackMaterial;
        private Material debugMaterial;
        private Shader fallbackShader;
        private Shader debugShader;
        private bool registeredCameraReset;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingDebugShader;

        public HoAovSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            outputPass = new HoAovOutputPass();
            debugPass = new HoAovDebugPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterials();
            outputPass?.Setup(settings, renderTargets, renderer.cameraDepthTargetHandle, fallbackMaterial);
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
                outputPass.SetupRenderGraph(settings, renderTargets, fallbackMaterial);
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
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(debugMaterial);
            fallbackMaterial = null;
            debugMaterial = null;
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
            EnsureFallbackMaterial();
            EnsureDebugMaterial();
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
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private static readonly List<ShaderTagId> AovShaderTagIds = new List<ShaderTagId>
        {
            HoAovShaderConstants.ShaderTagId
        };

        private readonly RTHandle[] colorTargets = new RTHandle[4];
        private HoAovSettings settings;
        private HoAovRenderTargets renderTargets;
        private RTHandle cameraDepthTarget;
        private Material fallbackMaterial;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;

        private sealed class PassData
        {
            public RendererListHandle rendererList;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public float systemChannelMask;
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
            RTHandle cameraDepthTarget,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraDepthTarget = cameraDepthTarget;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.aovPassEvent : RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public void SetupRenderGraph(
            HoAovSettings settings,
            HoAovRenderTargets renderTargets,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.aovPassEvent : RenderPassEvent.AfterRenderingTransparents;
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
            colorTargets[2] = renderTargets.SurfaceDataTexture;
            colorTargets[3] = renderTargets.Custom0Texture;

            ConfigureTarget(colorTargets);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            if (settings.useFallbackMaterial && fallbackMaterial == null)
            {
                SetInactive(context);
                return;
            }

            ApplyFallbackMaterialProperties();
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetGlobalTextures(cmd);
                cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                List<ShaderTagId> shaderTagIds = settings.useFallbackMaterial ? FallbackShaderTagIds : AovShaderTagIds;
                DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
                if (settings.useFallbackMaterial)
                {
                    drawingSettings.overrideMaterial = fallbackMaterial;
                    drawingSettings.overrideMaterialPassIndex = 0;
                }

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
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

            if (settings.useFallbackMaterial && fallbackMaterial == null)
            {
                AddResetPass(renderGraph);
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();

            TextureHandle maskIdTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetMaskGraphicsFormat(), HoAovShaderConstants.MaskIdTextureName));
            TextureHandle normalDepthTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.NormalDepthTextureName));
            TextureHandle surfaceDataTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.SurfaceDataTextureName));
            TextureHandle custom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoAovRenderTargets.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.Custom0TextureName));

            ApplyFallbackMaterialProperties();
            aovResources.maskIdTexture = maskIdTexture;
            aovResources.normalDepthTexture = normalDepthTexture;
            aovResources.surfaceDataTexture = surfaceDataTexture;
            aovResources.custom0Texture = custom0Texture;

            List<ShaderTagId> shaderTagIds = settings.useFallbackMaterial ? FallbackShaderTagIds : AovShaderTagIds;
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                shaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);

            if (settings.useFallbackMaterial)
            {
                drawingSettings.overrideMaterial = fallbackMaterial;
                drawingSettings.overrideMaterialPassIndex = 0;
            }

            RendererListParams rendererListParams = new RendererListParams(
                renderingData.cullResults,
                drawingSettings,
                filteringSettings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoAOV Output", out PassData passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.maskIdTexture = maskIdTexture;
                passData.normalDepthTexture = normalDepthTexture;
                passData.surfaceDataTexture = surfaceDataTexture;
                passData.custom0Texture = custom0Texture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (!passData.rendererList.IsValid())
                {
                    return;
                }

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(maskIdTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(normalDepthTexture, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(surfaceDataTexture, 2, AccessFlags.WriteAll);
                builder.SetRenderAttachment(custom0Texture, 3, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(maskIdTexture, HoAovShaderConstants.MaskIdTextureId);
                builder.SetGlobalTextureAfterPass(normalDepthTexture, HoAovShaderConstants.NormalDepthTextureId);
                builder.SetGlobalTextureAfterPass(surfaceDataTexture, HoAovShaderConstants.SurfaceDataTextureId);
                builder.SetGlobalTextureAfterPass(custom0Texture, HoAovShaderConstants.Custom0TextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
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
            cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.Custom1TextureId, renderTargets.Custom1Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.Custom2TextureId, renderTargets.Custom2Texture.nameID);
        }

        private void ApplyFallbackMaterialProperties()
        {
            if (fallbackMaterial == null)
            {
                return;
            }

            fallbackMaterial.SetFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
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
            filteringSettings = new FilteringSettings(renderQueueRange, settings != null ? settings.layerMask.value : -1);
        }

        private static bool CanUseDepthTarget(RTHandle colorTarget, RTHandle depthTarget)
        {
            RenderTexture color = colorTarget != null ? colorTarget.rt : null;
            RenderTexture depth = depthTarget != null ? depthTarget.rt : null;
            if (color == null || depth == null)
            {
                return false;
            }

            return color.width == depth.width
                && color.height == depth.height
                && color.volumeDepth == depth.volumeDepth
                && color.antiAliasing == depth.antiAliasing;
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
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
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
                cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.Custom1TextureId, renderTargets.Custom1Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.Custom2TextureId, renderTargets.Custom2Texture.nameID);
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
                || !aovResources.surfaceDataTexture.IsValid()
                || !aovResources.custom0Texture.IsValid())
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
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.custom0Texture = aovResources.custom0Texture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.debugDepthParams = GetDebugDepthParams(settings);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.UseTexture(passData.custom0Texture, AccessFlags.Read);
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
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, data.custom0Texture);
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
        public TextureHandle surfaceDataTexture = TextureHandle.nullHandle;
        public TextureHandle custom0Texture = TextureHandle.nullHandle;

        public override void Reset()
        {
            maskIdTexture = TextureHandle.nullHandle;
            normalDepthTexture = TextureHandle.nullHandle;
            surfaceDataTexture = TextureHandle.nullHandle;
            custom0Texture = TextureHandle.nullHandle;
        }
    }

    internal sealed class HoAovRenderTargets
    {
        private RTHandle maskIdTexture;
        private RTHandle normalDepthTexture;
        private RTHandle surfaceDataTexture;
        private RTHandle custom0Texture;
        private RTHandle custom1Texture;
        private RTHandle custom2Texture;

        public RTHandle MaskIdTexture => maskIdTexture;
        public RTHandle NormalDepthTexture => normalDepthTexture;
        public RTHandle SurfaceDataTexture => surfaceDataTexture;
        public RTHandle Custom0Texture => custom0Texture;
        public RTHandle Custom1Texture => custom1Texture;
        public RTHandle Custom2Texture => custom2Texture;

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

            RenderingUtils.ReAllocateIfNeeded(ref maskIdTexture, maskDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.MaskIdTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref normalDepthTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.NormalDepthTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref surfaceDataTexture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.SurfaceDataTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref custom0Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.Custom0TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref custom1Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.Custom1TextureName);
            RenderingUtils.ReAllocateIfNeeded(ref custom2Texture, highPrecisionDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoAovShaderConstants.Custom2TextureName);
        }

        public void Release()
        {
            maskIdTexture?.Release();
            normalDepthTexture?.Release();
            surfaceDataTexture?.Release();
            custom0Texture?.Release();
            custom1Texture?.Release();
            custom2Texture?.Release();
            maskIdTexture = null;
            normalDepthTexture = null;
            surfaceDataTexture = null;
            custom0Texture = null;
            custom1Texture = null;
            custom2Texture = null;
        }

        internal static GraphicsFormat GetMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8G8B8A8_UNorm;
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

        internal static GraphicsFormat GetHighPrecisionGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }
    }
}
