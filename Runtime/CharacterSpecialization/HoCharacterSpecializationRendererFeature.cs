using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.MetadataBuffer;
using lilToon.URP.Extensions.GeometryBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    [DisallowMultipleRendererFeature("Ho-CharacterSpecialization")]
    public sealed class HoCharacterSpecializationRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoCharacterSpecializationSettings settings = new HoCharacterSpecializationSettings();

        private readonly HoCharacterSpecializationRenderTargets renderTargets = new HoCharacterSpecializationRenderTargets();
        private readonly HoCharacterSpecializationSettings runtimeSettings = new HoCharacterSpecializationSettings();
        private HoCharacterSpecializationPass pass;
        private Material compositeMaterial;
        private Material captureClearMaterial;
        private Shader compositeShader;
        private Shader captureClearShader;
        private bool warnedMissingCompositeShader;
        private bool warnedMissingCaptureClearShader;

        [InspectorName("使用 Volume 参数")]
        [Tooltip("开启后，RendererFeature 只负责安装 pass，实际参数从 Volume 里的 Ho-CharacterSpecialization/角色特化 读取。")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public HoCharacterSpecializationSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            pass = new HoCharacterSpecializationPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            HoCharacterSpecializationSettings activeSettings = ResolveSettings(in renderingData);
            if (!ShouldRender(in renderingData, activeSettings))
            {
                return;
            }

            EnsureMaterial(activeSettings);
            pass?.Setup(activeSettings, renderTargets, renderer.cameraColorTargetHandle, compositeMaterial, captureClearMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoCharacterSpecializationSettings activeSettings = ResolveSettings(in renderingData);
            if (!ShouldRender(in renderingData, activeSettings))
            {
                return;
            }

            EnsureMaterial(activeSettings);
            if (compositeMaterial == null)
            {
                return;
            }

            pass?.SetupRenderGraph(activeSettings, compositeMaterial, captureClearMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            renderTargets.Release();
            CoreUtils.Destroy(compositeMaterial);
            CoreUtils.Destroy(captureClearMaterial);
            compositeMaterial = null;
            captureClearMaterial = null;
            compositeShader = null;
            captureClearShader = null;
        }

        private bool ShouldRender(in RenderingData renderingData, HoCharacterSpecializationSettings activeSettings)
        {
            if (activeSettings == null || !activeSettings.enabled)
            {
                return false;
            }

            if (!activeSettings.eyeRevealEnabled && !activeSettings.hairDropShadowEnabled && activeSettings.debugMode == HoCharacterSpecializationDebugMode.Off)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private HoCharacterSpecializationSettings ResolveSettings(in RenderingData renderingData)
        {
            IsUseVolumes = UseVolumes;
            if (!UseVolumes)
            {
                return settings;
            }

            HoCharacterSpecializationVolume volume = GetVolumeComponent();
            if (volume == null || !volume.IsActiveForCamera(renderingData.cameraData.cameraType))
            {
                return null;
            }

            runtimeSettings.CopyFrom(settings);
            volume.ApplyTo(runtimeSettings);
            return runtimeSettings;
        }

        private static HoCharacterSpecializationVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoCharacterSpecializationVolume>() : null;
        }

        private void EnsureMaterial(HoCharacterSpecializationSettings activeSettings)
        {
            Shader shader = activeSettings != null && activeSettings.compositeShader != null
                ? activeSettings.compositeShader
                : Shader.Find(HoCharacterSpecializationShaderConstants.CompositeShaderName);

            if (compositeMaterial == null || compositeShader != shader)
            {
                CoreUtils.Destroy(compositeMaterial);
                compositeMaterial = null;
                compositeShader = shader;
                if (shader == null)
                {
                    if (!warnedMissingCompositeShader)
                    {
                        warnedMissingCompositeShader = true;
                        Debug.LogWarning($"HoCharacterSpecialization is unavailable because shader '{HoCharacterSpecializationShaderConstants.CompositeShaderName}' could not be found.");
                    }
                }
                else
                {
                    compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
                }
            }

            Shader clearShader = Shader.Find(HoCharacterSpecializationShaderConstants.CaptureClearShaderName);
            if (captureClearMaterial != null && captureClearShader == clearShader)
            {
                return;
            }

            CoreUtils.Destroy(captureClearMaterial);
            captureClearMaterial = null;
            captureClearShader = clearShader;
            if (clearShader == null)
            {
                if (!warnedMissingCaptureClearShader)
                {
                    warnedMissingCaptureClearShader = true;
                    Debug.LogWarning($"HoCharacterSpecialization capture clear falls back to CommandBuffer clear because shader '{HoCharacterSpecializationShaderConstants.CaptureClearShaderName}' could not be found.");
                }

                return;
            }

            captureClearMaterial = CoreUtils.CreateEngineMaterial(clearShader);
        }
    }

    internal sealed class HoCharacterSpecializationPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-CharacterSpecialization");
        private static readonly List<ShaderTagId> CaptureShaderTagIds = new List<ShaderTagId>
        {
            HoCharacterSpecializationShaderConstants.CaptureShaderTagId
        };

        private readonly RTHandle[] captureColorTargets = new RTHandle[2];
        private readonly RenderTargetIdentifier[] captureColorIdentifiers = new RenderTargetIdentifier[2];
        private HoCharacterSpecializationSettings settings;
        private HoCharacterSpecializationRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private Material compositeMaterial;
        private Material captureClearMaterial;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;

        private sealed class CapturePassData
        {
            public RendererListHandle rendererList;
            public TextureHandle eyeColorTexture;
            public TextureHandle eyeDataTexture;
            public TextureHandle captureDepthTexture;
            public Material clearMaterial;
            public float captureMode;
            public bool clearTargets;
        }

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle aovMaskIdTexture;
            public TextureHandle aovNormalDepthTexture;
            public TextureHandle aovObjectCustom0Texture;
            public TextureHandle aovObjectCustom1Texture;
            public TextureHandle eyeColorTexture;
            public TextureHandle eyeDataTexture;
            public Material material;
            public Vector4 eyeRevealParams;
            public Vector4 hairShadowParams;
            public Vector4 hairShadowParams1;
            public Vector4 hairShadowParams2;
            public Color hairShadowColor;
            public Vector4 options;
        }

        public HoCharacterSpecializationPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoCharacterSpecializationSettings settings,
            HoCharacterSpecializationRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material compositeMaterial,
            Material captureClearMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            ConfigurePass();
        }

        public void SetupRenderGraph(HoCharacterSpecializationSettings settings, Material compositeMaterial, Material captureClearMaterial)
        {
            this.settings = settings;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            ConfigurePass();
        }

        public void Dispose()
        {
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
            captureColorTargets[0] = renderTargets.EyeColorTexture;
            captureColorTargets[1] = renderTargets.EyeDataTexture;

            RenderTextureDescriptor tempDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            tempDescriptor.depthBufferBits = 0;
            tempDescriptor.depthStencilFormat = GraphicsFormat.None;
            tempDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref tempDescriptor);
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, tempDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.TempTextureName);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null || cameraColorTarget == null || tempTexture == null || compositeMaterial == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                captureColorIdentifiers[0] = renderTargets.EyeColorTexture.nameID;
                captureColorIdentifiers[1] = renderTargets.EyeDataTexture.nameID;
                cmd.SetRenderTarget(captureColorIdentifiers, renderTargets.CaptureDepthTexture.nameID);
                ClearCaptureTargets(cmd, captureClearMaterial);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings drawingSettings = CreateCharacterDrawingSettings(CaptureShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, 1.0f);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);

                cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, 2.0f);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);

                cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, 0.0f);
                ApplyMaterialProperties(compositeMaterial, settings);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeColorTextureId, renderTargets.EyeColorTexture.nameID);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeDataTextureId, renderTargets.EyeDataTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, compositeMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || compositeMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            if (resourceData.isActiveTargetBackBuffer
                || !resourceData.activeColorTexture.IsValid()
                || !metadataResources.maskIdTexture.IsValid()
                || !geometryResources.normalDepthTexture.IsValid()
                || !metadataResources.objectCustom0Texture.IsValid()
                || !metadataResources.objectCustom1Texture.IsValid())
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle eyeColorTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, GetHdrGraphicsFormat(), HoCharacterSpecializationShaderConstants.EyeColorTextureName));
            TextureHandle eyeDataTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, GetDataGraphicsFormat(), HoCharacterSpecializationShaderConstants.EyeDataTextureName));
            TextureHandle captureDepthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoCharacterSpecializationRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoCharacterSpecializationShaderConstants.CaptureDepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                CaptureShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            using (var builder = renderGraph.AddRasterRenderPass<CapturePassData>("Ho-CharacterSpecialization CaptureFace", out CapturePassData passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.eyeColorTexture = eyeColorTexture;
                passData.eyeDataTexture = eyeDataTexture;
                passData.captureDepthTexture = captureDepthTexture;
                passData.clearMaterial = captureClearMaterial;
                passData.captureMode = 1.0f;
                passData.clearTargets = true;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(eyeColorTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(eyeDataTexture, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(captureDepthTexture, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CapturePassData data, RasterGraphContext context) =>
                {
                    if (data.clearTargets)
                    {
                        ClearCaptureTargets(context.cmd, data.clearMaterial);
                    }

                    context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, data.captureMode);
                    context.cmd.DrawRendererList(data.rendererList);
                    context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, 0.0f);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CapturePassData>("Ho-CharacterSpecialization CaptureEye", out CapturePassData passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.eyeColorTexture = eyeColorTexture;
                passData.eyeDataTexture = eyeDataTexture;
                passData.captureDepthTexture = captureDepthTexture;
                passData.clearMaterial = null;
                passData.captureMode = 2.0f;
                passData.clearTargets = false;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(eyeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(eyeDataTexture, 1, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(captureDepthTexture, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CapturePassData data, RasterGraphContext context) =>
                {
                    if (data.clearTargets)
                    {
                        ClearCaptureTargets(context.cmd, data.clearMaterial);
                    }

                    context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, data.captureMode);
                    context.cmd.DrawRendererList(data.rendererList);
                    context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.CaptureModeId, 0.0f);
                });
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_lilHoCharacterCompositeColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref destinationDesc);
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Ho-CharacterSpecialization Composite", out CompositePassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.aovMaskIdTexture = metadataResources.maskIdTexture;
                passData.aovNormalDepthTexture = geometryResources.normalDepthTexture;
                passData.aovObjectCustom0Texture = metadataResources.objectCustom0Texture;
                passData.aovObjectCustom1Texture = metadataResources.objectCustom1Texture;
                passData.eyeColorTexture = eyeColorTexture;
                passData.eyeDataTexture = eyeDataTexture;
                passData.material = compositeMaterial;
                FillMaterialVectors(settings, out passData.eyeRevealParams, out passData.hairShadowParams, out passData.hairShadowParams1, out passData.hairShadowParams2, out passData.hairShadowColor, out passData.options);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.aovMaskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.aovNormalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.aovObjectCustom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.aovObjectCustom1Texture, AccessFlags.Read);
                builder.UseTexture(eyeColorTexture, AccessFlags.Read);
                builder.UseTexture(eyeDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    ApplyMaterialProperties(data.material, data.eyeRevealParams, data.hairShadowParams, data.hairShadowParams1, data.hairShadowParams2, data.hairShadowColor, data.options);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.aovMaskIdTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.NormalDepthTextureId, data.aovNormalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.aovObjectCustom0Texture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.aovObjectCustom1Texture);
                    context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeColorTextureId, data.eyeColorTexture);
                    context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeDataTextureId, data.eyeDataTexture);
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private void ConfigurePass()
        {
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Color);
            ConfigureFiltering();
        }

        private static void ClearCaptureTargets(CommandBuffer cmd, Material clearMaterial)
        {
            cmd.ClearRenderTarget(true, false, Color.clear);
            if (clearMaterial != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, clearMaterial, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        private static void ClearCaptureTargets(RasterCommandBuffer cmd, Material clearMaterial)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (clearMaterial != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, clearMaterial, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private void ConfigureFiltering()
        {
            int minQueue = settings != null ? settings.minRenderQueue : 0;
            int maxQueue = settings != null ? settings.maxRenderQueue : (int)RenderQueue.Overlay - 1;
            if (maxQueue < minQueue)
            {
                maxQueue = minQueue;
            }

            filteringSettings = new FilteringSettings(
                new RenderQueueRange { lowerBound = minQueue, upperBound = maxQueue },
                settings != null ? settings.layerMask.value : -1);
        }

        private static void ApplyMaterialProperties(Material material, HoCharacterSpecializationSettings settings)
        {
            FillMaterialVectors(settings, out Vector4 eyeRevealParams, out Vector4 hairShadowParams, out Vector4 hairShadowParams1, out Vector4 hairShadowParams2, out Color hairShadowColor, out Vector4 options);
            ApplyMaterialProperties(material, eyeRevealParams, hairShadowParams, hairShadowParams1, hairShadowParams2, hairShadowColor, options);
        }

        private static void ApplyMaterialProperties(
            Material material,
            Vector4 eyeRevealParams,
            Vector4 hairShadowParams,
            Vector4 hairShadowParams1,
            Vector4 hairShadowParams2,
            Color hairShadowColor,
            Vector4 options)
        {
            material.SetVector(HoCharacterSpecializationShaderConstants.EyeRevealParamsId, eyeRevealParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParamsId, hairShadowParams);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParams1Id, hairShadowParams1);
            material.SetVector(HoCharacterSpecializationShaderConstants.HairShadowParams2Id, hairShadowParams2);
            material.SetColor(HoCharacterSpecializationShaderConstants.HairShadowColorId, hairShadowColor);
            material.SetVector(HoCharacterSpecializationShaderConstants.OptionsId, options);
        }

        private static void FillMaterialVectors(
            HoCharacterSpecializationSettings settings,
            out Vector4 eyeRevealParams,
            out Vector4 hairShadowParams,
            out Vector4 hairShadowParams1,
            out Vector4 hairShadowParams2,
            out Color hairShadowColor,
            out Vector4 options)
        {
            if (settings == null)
            {
                eyeRevealParams = Vector4.zero;
                hairShadowParams = Vector4.zero;
                hairShadowParams1 = Vector4.zero;
                hairShadowParams2 = Vector4.zero;
                hairShadowColor = Color.white;
                options = Vector4.zero;
                return;
            }

            eyeRevealParams = new Vector4(
                Mathf.Clamp01(settings.eyeRevealStrength),
                Mathf.Max(0.0f, settings.eyeRevealFeatherPixels),
                Mathf.Max(0.0f, settings.eyeRevealDilationPixels),
                Mathf.Max(0.0f, settings.eyeRevealDepthBias));
            hairShadowParams = new Vector4(
                Mathf.Clamp01(settings.hairShadowOpacity),
                Mathf.Max(0.0f, settings.hairShadowDistancePixels),
                settings.hairShadowAngleDegrees,
                Mathf.Max(0.0f, settings.hairShadowSoftnessPixels));
            hairShadowParams1 = new Vector4(
                Mathf.Max(0.0f, settings.hairShadowSpreadPixels),
                Mathf.Clamp01(settings.hairShadowKeepOffHair),
                (float)settings.hairShadowBlendMode,
                settings.useEyeRevealArea ? 1.0f : 0.0f);
            hairShadowParams2 = new Vector4(
                Mathf.Clamp01(settings.hairShadowDistancePerspectiveStrength),
                Mathf.Max(0.0f, settings.hairShadowDistanceReferenceDepth),
                Mathf.Clamp01(settings.hairShadowDistanceMinScale),
                0.0f);
            hairShadowColor = settings.hairShadowColor;
            options = new Vector4(
                settings.eyeRevealEnabled ? 1.0f : 0.0f,
                settings.hairDropShadowEnabled ? 1.0f : 0.0f,
                settings.sameCharacterOnly ? 1.0f : 0.0f,
                (float)settings.debugMode);
        }

        private static TextureDesc CreateTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoCharacterSpecializationSettings settings,
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
            descriptor.msaaSamples = divisor == 1 ? (MSAASamples)cameraTextureDescriptor.msaaSamples : MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = cameraTextureDescriptor.bindMS && divisor == 1;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        private static DrawingSettings CreateCharacterDrawingSettings(List<ShaderTagId> shaderTagIds, ref RenderingData renderingData, SortingCriteria sortingCriteria)
        {
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagIds[0], new SortingSettings(renderingData.cameraData.camera) { criteria = sortingCriteria })
            {
                perObjectData = renderingData.perObjectData,
                enableDynamicBatching = renderingData.supportsDynamicBatching,
                enableInstancing = true
            };

            for (int i = 1; i < shaderTagIds.Count; i++)
            {
                drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
            }

            return drawingSettings;
        }

        private static void EnsureHdrDescriptor(ref RenderTextureDescriptor descriptor)
        {
            GraphicsFormat hdrFormat = GetHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = hdrFormat;
            }
        }

        private static void EnsureHdrTextureDesc(ref TextureDesc descriptor)
        {
            GraphicsFormat hdrFormat = GetHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.format = hdrFormat;
            }
        }

        private static GraphicsFormat GetHdrGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return IsColorFormatUsable(preferredFormat) ? preferredFormat : GetFallbackColorFormat();
        }

        private static GraphicsFormat GetDataGraphicsFormat()
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
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
        }
    }

    internal sealed class HoCharacterSpecializationRenderTargets
    {
        private RTHandle eyeColorTexture;
        private RTHandle eyeDataTexture;
        private RTHandle captureDepthTexture;

        public RTHandle EyeColorTexture => eyeColorTexture;
        public RTHandle EyeDataTexture => eyeDataTexture;
        public RTHandle CaptureDepthTexture => captureDepthTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoCharacterSpecializationSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = divisor == 1 ? Mathf.Max(1, descriptor.msaaSamples) : 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);
            GraphicsFormat colorFormat = GetHdrGraphicsFormat();
            if (colorFormat != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = colorFormat;
            }

            RenderingUtils.ReAllocateIfNeeded(ref eyeColorTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.EyeColorTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref eyeDataTexture, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.EyeDataTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref captureDepthTexture, CreateDepthDescriptor(cameraTextureDescriptor, settings), FilterMode.Point, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.CaptureDepthTextureName);
        }

        public void Release()
        {
            eyeColorTexture?.Release();
            eyeDataTexture?.Release();
            captureDepthTexture?.Release();
            eyeColorTexture = null;
            eyeDataTexture = null;
            captureDepthTexture = null;
        }

        internal static RenderTextureDescriptor CreateDepthDescriptor(RenderTextureDescriptor cameraTextureDescriptor, HoCharacterSpecializationSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor),
                GraphicsFormat.None,
                GetDepthStencilFormat(cameraTextureDescriptor));
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

        private static GraphicsFormat GetDepthStencilFormat(RenderTextureDescriptor cameraTextureDescriptor)
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

            return GraphicsFormat.D32_SFloat;
        }

        private static bool IsDepthStencilFormatUsable(GraphicsFormat format)
        {
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
        }

        private static GraphicsFormat GetHdrGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            if (SystemInfo.IsFormatSupported(preferredFormat, GraphicsFormatUsage.Render))
            {
                return preferredFormat;
            }

            GraphicsFormat format = SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
            if (format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
            {
                return format;
            }

            format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)
                ? format
                : GraphicsFormat.B8G8R8A8_UNorm;
        }
    }
}
