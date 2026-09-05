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
        private Material faceHairDiffuseMaterial;
        private Material subjectOutlineMaterial;
        private HoCharacterEyeAngleTable eyeAngleTable;
        private Shader compositeShader;
        private Shader captureClearShader;
        private Shader faceHairDiffuseShader;
        private Shader subjectOutlineShader;
        private bool warnedMissingCompositeShader;
        private bool warnedMissingCaptureClearShader;
        private bool warnedMissingFaceHairDiffuseShader;
        private bool warnedMissingSubjectOutlineShader;

        public HoCharacterSpecializationSettings Settings => settings;

        public override void Create()
        {
            pass = new HoCharacterSpecializationPass();
            eyeAngleTable = new HoCharacterEyeAngleTable();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            HoCharacterSpecializationSettings activeSettings = ResolveSettings(in renderingData);
            if (!ShouldRender(in renderingData, activeSettings))
            {
                pass?.ReleaseCompatibilityResources();
                renderTargets.Release();
                return;
            }

            EnsureMaterial(activeSettings);
            pass?.Setup(
                activeSettings,
                renderTargets,
                renderer.cameraColorTargetHandle,
                compositeMaterial,
                captureClearMaterial,
                faceHairDiffuseMaterial,
                subjectOutlineMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoCharacterSpecializationSettings activeSettings = ResolveSettings(in renderingData);
            if (!ShouldRender(in renderingData, activeSettings))
            {
                pass?.ReleaseCompatibilityResources();
                renderTargets.Release();
                HoCharacterSpecializationRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    GetSkipReason(in renderingData, activeSettings));
                return;
            }

            EnsureMaterial(activeSettings);
            // 注意：UPR 17 fork 的 RenderGraph 主路径不调用 SetupRenderPasses，只在 AddRenderPasses 里能拿到每相机时机。
            eyeAngleTable?.UpdateForCamera(renderingData.cameraData.camera, activeSettings);
            if (compositeMaterial == null)
            {
                pass?.ReleaseCompatibilityResources();
                renderTargets.Release();
                HoCharacterSpecializationRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    "合成材质不可用。");
                return;
            }

            pass?.SetupRenderGraph(
                activeSettings,
                compositeMaterial,
                captureClearMaterial,
                faceHairDiffuseMaterial,
                subjectOutlineMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            renderTargets.Release();
            eyeAngleTable?.Dispose();
            eyeAngleTable = null;
            CoreUtils.Destroy(compositeMaterial);
            CoreUtils.Destroy(captureClearMaterial);
            CoreUtils.Destroy(faceHairDiffuseMaterial);
            CoreUtils.Destroy(subjectOutlineMaterial);
            compositeMaterial = null;
            captureClearMaterial = null;
            faceHairDiffuseMaterial = null;
            subjectOutlineMaterial = null;
            compositeShader = null;
            captureClearShader = null;
            faceHairDiffuseShader = null;
            subjectOutlineShader = null;
        }

        private bool ShouldRender(in RenderingData renderingData, HoCharacterSpecializationSettings activeSettings)
        {
            if (activeSettings == null || !activeSettings.enabled)
            {
                return false;
            }

            if (!activeSettings.eyeRevealEnabled
                && !activeSettings.hairDropShadowEnabled
                && !activeSettings.faceHairDiffuseEnabled
                && !activeSettings.subjectOutlineEnabled
                && !activeSettings.enhancedOutlineEnabled
                && activeSettings.debugMode == HoCharacterSpecializationDebugMode.Off)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private string GetSkipReason(in RenderingData renderingData, HoCharacterSpecializationSettings activeSettings)
        {
            if (activeSettings == null)
            {
                return "Volume 未启用或当前相机未激活角色特化。";
            }

            if (!activeSettings.enabled)
            {
                return "Feature 已关闭。";
            }

            if (!activeSettings.eyeRevealEnabled
                && !activeSettings.hairDropShadowEnabled
                && !activeSettings.faceHairDiffuseEnabled
                && !activeSettings.subjectOutlineEnabled
                && !activeSettings.enhancedOutlineEnabled
                && activeSettings.debugMode == HoCharacterSpecializationDebugMode.Off)
            {
                return "眼睛透过、前发投影、脸色扩散、主体轮廓、增强轮廓和 debug 均未启用。";
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView
                ? "未入队。"
                : "当前 camera type 不支持。";
        }

        private HoCharacterSpecializationSettings ResolveSettings(in RenderingData renderingData)
        {
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

            EnsureMaterial(
                ref compositeMaterial,
                ref compositeShader,
                shader,
                HoCharacterSpecializationShaderConstants.CompositeShaderName,
                ref warnedMissingCompositeShader,
                "HoCharacterSpecialization is unavailable because shader '{0}' could not be found.");

            EnsureMaterial(
                ref faceHairDiffuseMaterial,
                ref faceHairDiffuseShader,
                activeSettings != null && activeSettings.faceHairDiffuseShader != null
                    ? activeSettings.faceHairDiffuseShader
                    : Shader.Find(HoCharacterSpecializationShaderConstants.FaceHairDiffuseShaderName),
                HoCharacterSpecializationShaderConstants.FaceHairDiffuseShaderName,
                ref warnedMissingFaceHairDiffuseShader,
                "HoCharacterSpecialization face hair diffuse is unavailable because shader '{0}' could not be found.");

            EnsureMaterial(
                ref subjectOutlineMaterial,
                ref subjectOutlineShader,
                activeSettings != null && activeSettings.subjectOutlineShader != null
                    ? activeSettings.subjectOutlineShader
                    : Shader.Find(HoCharacterSpecializationShaderConstants.SubjectOutlineShaderName),
                HoCharacterSpecializationShaderConstants.SubjectOutlineShaderName,
                ref warnedMissingSubjectOutlineShader,
                "HoCharacterSpecialization outline field is unavailable because shader '{0}' could not be found.");

            Shader clearShader = Shader.Find(HoCharacterSpecializationShaderConstants.CaptureClearShaderName);
            EnsureMaterial(
                ref captureClearMaterial,
                ref captureClearShader,
                clearShader,
                HoCharacterSpecializationShaderConstants.CaptureClearShaderName,
                ref warnedMissingCaptureClearShader,
                "HoCharacterSpecialization capture clear falls back to CommandBuffer clear because shader '{0}' could not be found.");
        }

        private static void EnsureMaterial(
            ref Material material,
            ref Shader cachedShader,
            Shader shader,
            string shaderName,
            ref bool warnedMissingShader,
            string warningFormat)
        {
            if (material != null && cachedShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(material);
            material = null;
            cachedShader = shader;
            if (shader == null)
            {
                if (!warnedMissingShader)
                {
                    warnedMissingShader = true;
                    Debug.LogWarning(string.Format(warningFormat, shaderName));
                }

                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed partial class HoCharacterSpecializationPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-CharacterSpecialization");
        private const int FaceHairDiffuseBlurIterationCount = 2;
        private const int SubjectOutlineBlurIterationCount = 2;
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
        private Material faceHairDiffuseMaterial;
        private Material subjectOutlineMaterial;
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

        public HoCharacterSpecializationPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoCharacterSpecializationSettings settings,
            HoCharacterSpecializationRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material compositeMaterial,
            Material captureClearMaterial,
            Material faceHairDiffuseMaterial,
            Material subjectOutlineMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            ConfigurePass();
        }

        public void SetupRenderGraph(
            HoCharacterSpecializationSettings settings,
            Material compositeMaterial,
            Material captureClearMaterial,
            Material faceHairDiffuseMaterial,
            Material subjectOutlineMaterial)
        {
            this.settings = settings;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            ConfigurePass();
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            tempTexture?.Release();
            renderTargets?.Release();
            cameraColorTarget = null;
            tempTexture = null;
            renderTargets = null;
            for (int i = 0; i < captureColorTargets.Length; i++)
            {
                captureColorTargets[i] = null;
            }
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
            ReleaseCompatibilityResources();
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

            bool backBufferActive = resourceData.isActiveTargetBackBuffer;
            bool hasCameraColor = resourceData.activeColorTexture.IsValid();
            bool hasMetadataMaskId = metadataResources.maskIdTexture.IsValid();
            bool hasGeometryNormalDepth = geometryResources.normalDepthTexture.IsValid();
            bool hasGeometryDepth = geometryResources.depthTexture.IsValid();
            bool hasMetadataObjectCustom0 = metadataResources.objectCustom0Texture.IsValid();
            bool hasMetadataObjectCustom1 = metadataResources.objectCustom1Texture.IsValid();
            bool hasMetadataSurfaceColor = metadataResources.surfaceColorTexture.IsValid();
            bool requiresFaceHairDiffuseTextures = RequiresFaceHairDiffuseTextures(settings);
            bool requiresSubjectOutlineTextures = RequiresSubjectOutlineTextures(settings);
            bool requiresEnhancedOutlineTextures = RequiresEnhancedOutlineTextures(settings);
            bool requiresGeometryDepth = requiresSubjectOutlineTextures || requiresEnhancedOutlineTextures;
            HoCharacterSpecializationRuntimeDiagnostics.PublishRenderGraphInputs(
                cameraData.camera,
                "Composite",
                backBufferActive,
                hasCameraColor,
                hasMetadataMaskId,
                hasMetadataObjectCustom0,
                hasMetadataObjectCustom1,
                hasMetadataSurfaceColor,
                hasGeometryNormalDepth,
                hasGeometryDepth,
                requiresGeometryDepth,
                requiresFaceHairDiffuseTextures);

            if (backBufferActive
                || !hasCameraColor
                || !hasMetadataMaskId
                || !hasGeometryNormalDepth
                || (requiresGeometryDepth && !hasGeometryDepth)
                || !hasMetadataObjectCustom0
                || !hasMetadataObjectCustom1)
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

            TextureHandle faceHairDiffuseSourceColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseSourceDepthTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseTempColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseTempDepthTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseDepthTexture = TextureHandle.nullHandle;
            bool faceHairDiffuseReady = requiresFaceHairDiffuseTextures && hasMetadataSurfaceColor && faceHairDiffuseMaterial != null;
            if (faceHairDiffuseReady)
            {
                TextureDesc faceHairColorDesc = CreateFaceHairDiffuseTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetHdrGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.FaceHairDiffuseSourceColorTextureName);
                TextureDesc faceHairDepthDesc = CreateFaceHairDiffuseTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetDataGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.FaceHairDiffuseSourceDepthTextureName);

                faceHairDiffuseSourceColorTexture = renderGraph.CreateTexture(faceHairColorDesc);
                faceHairDepthDesc.name = HoCharacterSpecializationShaderConstants.FaceHairDiffuseSourceDepthTextureName;
                faceHairDiffuseSourceDepthTexture = renderGraph.CreateTexture(faceHairDepthDesc);
                faceHairColorDesc.name = HoCharacterSpecializationShaderConstants.FaceHairDiffuseTempColorTextureName;
                faceHairDiffuseTempColorTexture = renderGraph.CreateTexture(faceHairColorDesc);
                faceHairDepthDesc.name = HoCharacterSpecializationShaderConstants.FaceHairDiffuseTempDepthTextureName;
                faceHairDiffuseTempDepthTexture = renderGraph.CreateTexture(faceHairDepthDesc);
                faceHairColorDesc.name = HoCharacterSpecializationShaderConstants.FaceHairDiffuseColorTextureName;
                faceHairDiffuseColorTexture = renderGraph.CreateTexture(faceHairColorDesc);
                faceHairDepthDesc.name = HoCharacterSpecializationShaderConstants.FaceHairDiffuseDepthTextureName;
                faceHairDiffuseDepthTexture = renderGraph.CreateTexture(faceHairDepthDesc);

                using (var builder = renderGraph.AddRasterRenderPass<FaceHairDiffuseSourcePassData>("Ho-CharacterSpecialization FaceHair Source", out FaceHairDiffuseSourcePassData passData, ProfilingSampler))
                {
                    passData.source = source;
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataSurfaceColorTexture = metadataResources.surfaceColorTexture;
                    passData.geometryNormalDepthTexture = geometryResources.normalDepthTexture;
                    passData.material = faceHairDiffuseMaterial;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                    builder.UseTexture(passData.metadataSurfaceColorTexture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryNormalDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(faceHairDiffuseSourceColorTexture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(faceHairDiffuseSourceDepthTexture, 1, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (FaceHairDiffuseSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.metadataObjectCustom0Texture);
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, data.metadataSurfaceColorTexture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.geometryNormalDepthTexture);
                        context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    });
                }

                TextureHandle blurSourceColor = faceHairDiffuseSourceColorTexture;
                TextureHandle blurSourceDepth = faceHairDiffuseSourceDepthTexture;
                float iterationRadiusScale = 1.0f / Mathf.Sqrt(FaceHairDiffuseBlurIterationCount);
                for (int i = 0; i < FaceHairDiffuseBlurIterationCount; i++)
                {
                    bool writeFinal = i == FaceHairDiffuseBlurIterationCount - 1;
                    TextureHandle blurDestinationColor = writeFinal ? faceHairDiffuseColorTexture : faceHairDiffuseTempColorTexture;
                    TextureHandle blurDestinationDepth = writeFinal ? faceHairDiffuseDepthTexture : faceHairDiffuseTempDepthTexture;
                    Vector4 blurParams = CreateFaceHairDiffuseBlurParams(
                        settings,
                        cameraData.cameraTargetDescriptor,
                        blurSourceColor.GetDescriptor(renderGraph),
                        iterationRadiusScale,
                        i);
                    AddFaceHairDiffuseBlurPass(
                        renderGraph,
                        $"Ho-CharacterSpecialization FaceHair FastGaussian {i + 1}",
                        faceHairDiffuseMaterial,
                        blurSourceColor,
                        blurSourceDepth,
                        blurDestinationColor,
                        blurDestinationDepth,
                        blurParams);

                    blurSourceColor = blurDestinationColor;
                    blurSourceDepth = blurDestinationDepth;
                }
            }

            TextureHandle subjectOutlineSourceTexture = TextureHandle.nullHandle;
            TextureHandle subjectOutlineTempTexture = TextureHandle.nullHandle;
            TextureHandle subjectOutlineTexture = TextureHandle.nullHandle;
            bool subjectOutlineReady = requiresSubjectOutlineTextures && hasGeometryDepth && subjectOutlineMaterial != null;
            if (subjectOutlineReady)
            {
                TextureDesc subjectOutlineDesc = CreateSubjectOutlineTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetDataGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.SubjectOutlineSourceTextureName);
                subjectOutlineSourceTexture = renderGraph.CreateTexture(subjectOutlineDesc);
                subjectOutlineDesc.name = HoCharacterSpecializationShaderConstants.SubjectOutlineTempTextureName;
                subjectOutlineTempTexture = renderGraph.CreateTexture(subjectOutlineDesc);
                subjectOutlineDesc.name = HoCharacterSpecializationShaderConstants.SubjectOutlineTextureName;
                subjectOutlineTexture = renderGraph.CreateTexture(subjectOutlineDesc);

                using (var builder = renderGraph.AddRasterRenderPass<SubjectOutlineSourcePassData>("Ho-CharacterSpecialization SubjectOutline Source", out SubjectOutlineSourcePassData passData, ProfilingSampler))
                {
                    passData.source = source;
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataObjectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4((float)HoCharacterObjectCustomChannel.CharacterFull, 0.0f, 0.0f, 0.0f);

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(subjectOutlineSourceTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SubjectOutlineSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SubjectOutlineSourceParamsId, data.sourceParams);
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.metadataObjectCustom0Texture);
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.metadataObjectCustom1Texture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, data.geometryDepthTexture);
                        context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    });
                }

                TextureHandle blurSource = subjectOutlineSourceTexture;
                float iterationRadiusScale = 1.0f / Mathf.Sqrt(SubjectOutlineBlurIterationCount);
                for (int i = 0; i < SubjectOutlineBlurIterationCount; i++)
                {
                    bool writeFinal = i == SubjectOutlineBlurIterationCount - 1;
                    TextureHandle blurDestination = writeFinal ? subjectOutlineTexture : subjectOutlineTempTexture;
                    Vector4 blurParams = CreateSubjectOutlineBlurParams(
                        settings,
                        cameraData.cameraTargetDescriptor,
                        blurSource.GetDescriptor(renderGraph),
                        iterationRadiusScale,
                        i);
                    AddSubjectOutlineBlurPass(
                        renderGraph,
                        $"Ho-CharacterSpecialization SubjectOutline FastGaussian {i + 1}",
                        subjectOutlineMaterial,
                        blurSource,
                        blurDestination,
                        blurParams);

                    blurSource = blurDestination;
                }
            }

            TextureHandle enhancedOutlineSourceTexture = TextureHandle.nullHandle;
            TextureHandle enhancedOutlineTempTexture = TextureHandle.nullHandle;
            TextureHandle enhancedOutlineTexture = TextureHandle.nullHandle;
            bool enhancedOutlineReady = requiresEnhancedOutlineTextures && hasGeometryDepth && subjectOutlineMaterial != null;
            if (enhancedOutlineReady)
            {
                TextureDesc enhancedOutlineDesc = CreateSubjectOutlineTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetDataGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.EnhancedOutlineSourceTextureName);
                enhancedOutlineSourceTexture = renderGraph.CreateTexture(enhancedOutlineDesc);
                enhancedOutlineDesc.name = HoCharacterSpecializationShaderConstants.EnhancedOutlineTempTextureName;
                enhancedOutlineTempTexture = renderGraph.CreateTexture(enhancedOutlineDesc);
                enhancedOutlineDesc.name = HoCharacterSpecializationShaderConstants.EnhancedOutlineTextureName;
                enhancedOutlineTexture = renderGraph.CreateTexture(enhancedOutlineDesc);

                using (var builder = renderGraph.AddRasterRenderPass<SubjectOutlineSourcePassData>("Ho-CharacterSpecialization EnhancedOutline Source", out SubjectOutlineSourcePassData passData, ProfilingSampler))
                {
                    passData.source = source;
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataObjectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4(Mathf.Clamp((int)settings.enhancedOutlineSourceChannel, 0, 7), 0.0f, 0.0f, 0.0f);

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(enhancedOutlineSourceTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SubjectOutlineSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SubjectOutlineSourceParamsId, data.sourceParams);
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.metadataObjectCustom0Texture);
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.metadataObjectCustom1Texture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, data.geometryDepthTexture);
                        context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    });
                }

                TextureHandle blurSource = enhancedOutlineSourceTexture;
                float iterationRadiusScale = 1.0f / Mathf.Sqrt(SubjectOutlineBlurIterationCount);
                for (int i = 0; i < SubjectOutlineBlurIterationCount; i++)
                {
                    bool writeFinal = i == SubjectOutlineBlurIterationCount - 1;
                    TextureHandle blurDestination = writeFinal ? enhancedOutlineTexture : enhancedOutlineTempTexture;
                    Vector4 blurParams = CreateEnhancedOutlineBlurParams(
                        settings,
                        cameraData.cameraTargetDescriptor,
                        blurSource.GetDescriptor(renderGraph),
                        iterationRadiusScale,
                        i);
                    AddSubjectOutlineBlurPass(
                        renderGraph,
                        $"Ho-CharacterSpecialization EnhancedOutline FastGaussian {i + 1}",
                        subjectOutlineMaterial,
                        blurSource,
                        blurDestination,
                        blurParams);

                    blurSource = blurDestination;
                }
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
                passData.metadataMaskIdTexture = metadataResources.maskIdTexture;
                passData.geometryNormalDepthTexture = geometryResources.normalDepthTexture;
                passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                passData.metadataObjectCustom1Texture = metadataResources.objectCustom1Texture;
                passData.faceHairDiffuseSourceColorTexture = faceHairDiffuseSourceColorTexture;
                passData.faceHairDiffuseColorTexture = faceHairDiffuseColorTexture;
                passData.faceHairDiffuseDepthTexture = faceHairDiffuseDepthTexture;
                passData.subjectOutlineSourceTexture = subjectOutlineSourceTexture;
                passData.subjectOutlineTexture = subjectOutlineTexture;
                passData.enhancedOutlineSourceTexture = enhancedOutlineSourceTexture;
                passData.enhancedOutlineTexture = enhancedOutlineTexture;
                passData.eyeColorTexture = eyeColorTexture;
                passData.eyeDataTexture = eyeDataTexture;
                passData.material = compositeMaterial;
                passData.faceHairDiffuseReady = faceHairDiffuseReady;
                passData.subjectOutlineReady = subjectOutlineReady;
                passData.enhancedOutlineReady = enhancedOutlineReady;
                FillMaterialVectors(
                    settings,
                    faceHairDiffuseReady,
                    subjectOutlineReady,
                    enhancedOutlineReady,
                    out passData.eyeRevealParams,
                    out passData.eyeAngleParams,
                    out passData.hairShadowParams,
                    out passData.hairShadowParams1,
                    out passData.hairShadowParams2,
                    out passData.hairShadowColor,
                    out passData.faceHairDiffuseParams,
                    out passData.faceHairDiffuseLevels,
                    out passData.faceHairDiffuseTintColor,
                    out passData.faceHairDiffuseOptions,
                    out passData.subjectOutlineParams,
                    out passData.subjectOutlineLevels,
                    out passData.subjectOutlineColor,
                    out passData.subjectOutlineFogColor,
                    out passData.subjectOutlineFogParams,
                    out passData.subjectOutlineHeightFadeParams,
                    out passData.subjectOutlineOptions,
                    out passData.enhancedOutlineParams,
                    out passData.enhancedOutlineFogColor,
                    out passData.enhancedOutlineFogParams,
                    out passData.enhancedOutlineHeightFadeParams,
                    out passData.enhancedOutlineOptions,
                    out passData.options);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.metadataMaskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.geometryNormalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                builder.UseTexture(eyeColorTexture, AccessFlags.Read);
                builder.UseTexture(eyeDataTexture, AccessFlags.Read);
                if (faceHairDiffuseReady)
                {
                    builder.UseTexture(faceHairDiffuseSourceColorTexture, AccessFlags.Read);
                    builder.UseTexture(faceHairDiffuseColorTexture, AccessFlags.Read);
                    builder.UseTexture(faceHairDiffuseDepthTexture, AccessFlags.Read);
                }
                if (subjectOutlineReady)
                {
                    builder.UseTexture(subjectOutlineSourceTexture, AccessFlags.Read);
                    builder.UseTexture(subjectOutlineTexture, AccessFlags.Read);
                }
                if (enhancedOutlineReady)
                {
                    builder.UseTexture(enhancedOutlineSourceTexture, AccessFlags.Read);
                    builder.UseTexture(enhancedOutlineTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    ApplyMaterialProperties(
                        data.material,
                        data.eyeRevealParams,
                        data.eyeAngleParams,
                        data.hairShadowParams,
                        data.hairShadowParams1,
                        data.hairShadowParams2,
                        data.hairShadowColor,
                        data.faceHairDiffuseParams,
                        data.faceHairDiffuseLevels,
                        data.faceHairDiffuseTintColor,
                        data.faceHairDiffuseOptions,
                        data.subjectOutlineParams,
                        data.subjectOutlineLevels,
                        data.subjectOutlineColor,
                        data.subjectOutlineFogColor,
                        data.subjectOutlineFogParams,
                        data.subjectOutlineHeightFadeParams,
                        data.subjectOutlineOptions,
                        data.enhancedOutlineParams,
                        data.enhancedOutlineFogColor,
                        data.enhancedOutlineFogParams,
                        data.enhancedOutlineHeightFadeParams,
                        data.enhancedOutlineOptions,
                        data.options);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.metadataMaskIdTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.geometryNormalDepthTexture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.metadataObjectCustom0Texture);
                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.metadataObjectCustom1Texture);
                    if (data.faceHairDiffuseReady)
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.FaceHairDiffuseSourceColorTextureId, data.faceHairDiffuseSourceColorTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.FaceHairDiffuseColorTextureId, data.faceHairDiffuseColorTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.FaceHairDiffuseDepthTextureId, data.faceHairDiffuseDepthTexture);
                    }
                    if (data.subjectOutlineReady)
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SubjectOutlineSourceTextureId, data.subjectOutlineSourceTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SubjectOutlineTextureId, data.subjectOutlineTexture);
                    }
                    if (data.enhancedOutlineReady)
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EnhancedOutlineSourceTextureId, data.enhancedOutlineSourceTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EnhancedOutlineTextureId, data.enhancedOutlineTexture);
                    }

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
