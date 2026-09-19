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
        private Material semanticMaskBlurMaterial;
        private HoCharacterEyeAngleTable eyeAngleTable;
        private Shader compositeShader;
        private Shader captureClearShader;
        private Shader faceHairDiffuseShader;
        private Shader subjectOutlineShader;
        private Shader semanticMaskBlurShader;
        private bool warnedMissingCompositeShader;
        private bool warnedMissingCaptureClearShader;
        private bool warnedMissingFaceHairDiffuseShader;
        private bool warnedMissingSubjectOutlineShader;
        private bool warnedMissingSemanticMaskBlurShader;

        public HoCharacterSpecializationSettings Settings => settings;

        public override void Create()
        {
            pass = new HoCharacterSpecializationPass();
            // Create 可能在同一 feature 实例上被重复调用（编辑器重载、资产重导入），
            // 先释放旧表，避免上一批"每相机一张"的纹理成为永久泄漏。
            eyeAngleTable?.Dispose();
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
                subjectOutlineMaterial,
                semanticMaskBlurMaterial);
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
                subjectOutlineMaterial,
                semanticMaskBlurMaterial);
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
            CoreUtils.Destroy(semanticMaskBlurMaterial);
            compositeMaterial = null;
            captureClearMaterial = null;
            faceHairDiffuseMaterial = null;
            subjectOutlineMaterial = null;
            semanticMaskBlurMaterial = null;
            compositeShader = null;
            captureClearShader = null;
            faceHairDiffuseShader = null;
            subjectOutlineShader = null;
            semanticMaskBlurShader = null;
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
            volume.CopyEffectsTo(runtimeSettings);
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

            EnsureMaterial(
                ref semanticMaskBlurMaterial,
                ref semanticMaskBlurShader,
                Shader.Find(HoCharacterSpecializationShaderConstants.SemanticMaskBlurShaderName),
                HoCharacterSpecializationShaderConstants.SemanticMaskBlurShaderName,
                ref warnedMissingSemanticMaskBlurShader,
                "HoCharacterSpecialization semantic mask anti-aliasing is unavailable because shader '{0}' could not be found.");

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
        private Material semanticMaskBlurMaterial;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;

        private sealed class SemanticMaskBlurPassData
        {
            public TextureHandle metadataObjectCustom0Texture;
            public TextureHandle metadataObjectCustom1Texture;
            public TextureHandle destinationLowTexture;
            public TextureHandle destinationHighTexture;
            public Material material;
            public Vector4 blurParams;
        }

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
            Material subjectOutlineMaterial,
            Material semanticMaskBlurMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            this.semanticMaskBlurMaterial = semanticMaskBlurMaterial;
            ConfigurePass();
        }

        public void SetupRenderGraph(
            HoCharacterSpecializationSettings settings,
            Material compositeMaterial,
            Material captureClearMaterial,
            Material faceHairDiffuseMaterial,
            Material subjectOutlineMaterial,
            Material semanticMaskBlurMaterial)
        {
            this.settings = settings;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            this.semanticMaskBlurMaterial = semanticMaskBlurMaterial;
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

            renderTargets.ReAllocateIfNeeded(
                renderingData.cameraData.cameraTargetDescriptor,
                settings,
                RequiresSemanticMaskBlurTextures(settings));
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

                bool semanticMaskBlurReady = RequiresSemanticMaskBlurTextures(settings)
                    && semanticMaskBlurMaterial != null
                    && renderTargets.SemanticMaskBlurredLowTexture != null
                    && renderTargets.SemanticMaskBlurredHighTexture != null;
                if (semanticMaskBlurReady)
                {
                    captureColorIdentifiers[0] = renderTargets.SemanticMaskBlurredLowTexture.nameID;
                    captureColorIdentifiers[1] = renderTargets.SemanticMaskBlurredHighTexture.nameID;
                    cmd.SetRenderTarget(captureColorIdentifiers, renderTargets.CaptureDepthTexture.nameID);
                    cmd.SetGlobalVector(
                        HoCharacterSpecializationShaderConstants.SemanticMaskBlurParamsId,
                        CreateSemanticMaskBlurParams(settings));
                    Blitter.BlitTexture(cmd, tempTexture, new Vector4(1, 1, 0, 0), semanticMaskBlurMaterial, 0);
                }

                ApplyMaterialProperties(compositeMaterial, settings);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeColorTextureId, renderTargets.EyeColorTexture.nameID);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeDataTextureId, renderTargets.EyeDataTexture.nameID);
                cmd.SetGlobalFloat(
                    HoCharacterSpecializationShaderConstants.SemanticMaskBlurValidId,
                    semanticMaskBlurReady ? 1.0f : 0.0f);
                cmd.SetGlobalVector(
                    HoCharacterSpecializationShaderConstants.SemanticMaskOptionsId,
                    CreateSemanticMaskOptions(settings, semanticMaskBlurReady));
                if (semanticMaskBlurReady)
                {
                    cmd.SetGlobalTexture(
                        HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureId,
                        renderTargets.SemanticMaskBlurredLowTexture.nameID);
                    cmd.SetGlobalTexture(
                        HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureId,
                        renderTargets.SemanticMaskBlurredHighTexture.nameID);
                }

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

            // 捕获支的门控：眼透（或它的 debug 模式）需要这张脸/眼的捕获。
            // 注意：脸色扩散的"底色"输入以后也会消费脸捕获 —— 到时候把条件加到这里，别散在录制点里。
            bool needsFaceCapture = RequiresCharacterCapture(settings);
            bool needsEyeCapture = RequiresCharacterCapture(settings);
            bool needsCharacterCapture = needsFaceCapture || needsEyeCapture;

            TextureHandle source = resourceData.activeColorTexture;
            // eyeColor 始终存在：合成 shader 在 :619 无条件采样它（见 RequiresCharacterCapture 的注释），
            // 描述符带 clearBuffer，RDG 会在本帧首次使用（= 合成趟当采样输入）时把它显式清成 0。
            TextureHandle eyeColorTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, GetHdrGraphicsFormat(), HoCharacterSpecializationShaderConstants.EyeColorTextureName));
            // eyeData 是 CaptureFace 清屏材质的第二个 MRT（HoCharacterCaptureClear.shader:41,57），
            // 所以只要有一趟捕获在录，它就得存在；但只有眼透路径会采样它。
            TextureHandle eyeDataTexture = needsCharacterCapture
                ? renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, GetDataGraphicsFormat(), HoCharacterSpecializationShaderConstants.EyeDataTextureName))
                : TextureHandle.nullHandle;
            // captureDepth 全仓库只有这两趟捕获自己用（深度测试 + 清屏），没有采样者。
            TextureHandle captureDepthTexture = needsCharacterCapture
                ? UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    HoCharacterSpecializationRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                    HoCharacterSpecializationShaderConstants.CaptureDepthTextureName,
                    true,
                    FilterMode.Point,
                    TextureWrapMode.Clamp)
                : TextureHandle.nullHandle;

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                CaptureShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            if (needsFaceCapture)
            {
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
            }

            if (needsEyeCapture)
            {
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
            }

            // Shared anti-aliased semantic masks: one pass, two MRTs, for every consumer of the
            // MetadataBuffer semantic bits. The raw bits stay untouched.
            TextureHandle semanticMaskBlurredLowTexture = TextureHandle.nullHandle;
            TextureHandle semanticMaskBlurredHighTexture = TextureHandle.nullHandle;
            bool semanticMaskBlurReady = RequiresSemanticMaskBlurTextures(settings)
                && hasMetadataObjectCustom0
                && hasMetadataObjectCustom1
                && semanticMaskBlurMaterial != null;
            if (semanticMaskBlurReady)
            {
                TextureDesc semanticMaskDesc = CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetSemanticMaskGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureName);
                semanticMaskBlurredLowTexture = renderGraph.CreateTexture(semanticMaskDesc);
                semanticMaskDesc.name = HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureName;
                semanticMaskBlurredHighTexture = renderGraph.CreateTexture(semanticMaskDesc);

                AddSemanticMaskBlurPass(
                    renderGraph,
                    "Ho-CharacterSpecialization SemanticMask Blur",
                    semanticMaskBlurMaterial,
                    metadataResources.objectCustom0Texture,
                    metadataResources.objectCustom1Texture,
                    semanticMaskBlurredLowTexture,
                    semanticMaskBlurredHighTexture,
                    CreateSemanticMaskBlurParams(settings));
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
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataSurfaceColorTexture = metadataResources.surfaceColorTexture;
                    passData.geometryNormalDepthTexture = geometryResources.normalDepthTexture;
                    passData.material = faceHairDiffuseMaterial;

                    // 相机颜色在这趟里从来没有被采样（HoCharacterFaceHairDiffuse.shader pass 0 的
                    // Frag:38-54 里没有 _BlitTexture），所以不再声明这条读；画面也不再用
                    // Blitter.BlitTexture 去绑 _BlitTexture，改成 DrawProcedural + 显式 _BlitScaleBias。
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
                        // Blit.hlsl 的 Vert:50 用 _BlitScaleBias 算 UV，这趟不再是 Blitter.BlitTexture
                        // （它会替我们设），所以要自己设成整张纹理：scale=1, bias=0。
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
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
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataObjectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.semanticMaskBlurredLowTexture = semanticMaskBlurredLowTexture;
                    passData.semanticMaskBlurredHighTexture = semanticMaskBlurredHighTexture;
                    passData.semanticMaskBlurReady = semanticMaskBlurReady;
                    passData.useSemanticMaskAntiAliasing = settings.semanticMaskBlurSubjectOutline;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4((float)HoCharacterObjectCustomChannel.CharacterFull, 0.0f, 0.0f, 0.0f);

                    // 相机颜色在这趟里从来没有被采样（HoCharacterSubjectOutline.shader pass 0 的
                    // Frag:91-112 里没有 _BlitTexture），所以不再声明这条读；画面也不再用
                    // Blitter.BlitTexture 去绑 _BlitTexture，改成 DrawProcedural + 显式 _BlitScaleBias。
                    builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);
                    // shader 只在 _HoCharacterSemanticMaskBlurValid > 0.5 时读模糊对
                    // （SubjectOutline.shader:43-52），而这个全局量就是下面的
                    // "semanticMaskBlurReady && 本效果自己的开关"（render func 里写死同一个表达式）。
                    bool subjectOutlineSamplesSemanticMaskBlur = semanticMaskBlurReady && settings.semanticMaskBlurSubjectOutline;
                    if (subjectOutlineSamplesSemanticMaskBlur)
                    {
                        builder.UseTexture(semanticMaskBlurredLowTexture, AccessFlags.Read);
                        builder.UseTexture(semanticMaskBlurredHighTexture, AccessFlags.Read);
                    }

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
                        // The outline's own switch: it stays on the raw bits by default.
                        bool useSemanticMaskAntiAliasing = data.semanticMaskBlurReady && data.useSemanticMaskAntiAliasing;
                        context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.SemanticMaskBlurValidId, useSemanticMaskAntiAliasing ? 1.0f : 0.0f);
                        if (useSemanticMaskAntiAliasing)
                        {
                            context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureId, data.semanticMaskBlurredLowTexture);
                            context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureId, data.semanticMaskBlurredHighTexture);
                        }

                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
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
                    passData.metadataObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.metadataObjectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.semanticMaskBlurredLowTexture = semanticMaskBlurredLowTexture;
                    passData.semanticMaskBlurredHighTexture = semanticMaskBlurredHighTexture;
                    passData.semanticMaskBlurReady = semanticMaskBlurReady;
                    passData.useSemanticMaskAntiAliasing = settings.semanticMaskBlurEnhancedOutline;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4(Mathf.Clamp((int)settings.enhancedOutlineSourceChannel, 0, 7), 0.0f, 0.0f, 0.0f);

                    // 相机颜色在这趟里从来没有被采样（同 #7 的 shader）：去掉假读，改 DrawProcedural。
                    builder.UseTexture(passData.metadataObjectCustom0Texture, AccessFlags.Read);
                    builder.UseTexture(passData.metadataObjectCustom1Texture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);
                    // 同 #7：模糊对只在 _HoCharacterSemanticMaskBlurValid > 0.5 时被采
                    // （SubjectOutline.shader:43-52），这里用的就是 render func 里写的那个表达式。
                    bool enhancedOutlineSamplesSemanticMaskBlur = semanticMaskBlurReady && settings.semanticMaskBlurEnhancedOutline;
                    if (enhancedOutlineSamplesSemanticMaskBlur)
                    {
                        builder.UseTexture(semanticMaskBlurredLowTexture, AccessFlags.Read);
                        builder.UseTexture(semanticMaskBlurredHighTexture, AccessFlags.Read);
                    }

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
                        // The outline's own switch: it stays on the raw bits by default.
                        bool useSemanticMaskAntiAliasing = data.semanticMaskBlurReady && data.useSemanticMaskAntiAliasing;
                        context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.SemanticMaskBlurValidId, useSemanticMaskAntiAliasing ? 1.0f : 0.0f);
                        if (useSemanticMaskAntiAliasing)
                        {
                            context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureId, data.semanticMaskBlurredLowTexture);
                            context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureId, data.semanticMaskBlurredHighTexture);
                        }

                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
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

            // 语义副本（模糊对）在合成里只有三条采样路径，全部要求 _HoCharacterSemanticMaskBlurValid > 0.5
            // （= ready）且该效果自己的开关位 > 0.5（Composite.shader:92 的 SampleSemanticBit），
            // 而各路径自己还有前置门：眼透 :243-246、前发投影 :309-312、脸色扩散 :364-367。
            // 三个"效果开着 && 它勾了读取抗锯齿掩码"的或，就是这张模糊对在合成趟里的全部可达条件。
            bool compositeSamplesSemanticMaskBlur = semanticMaskBlurReady
                && ((settings.eyeRevealEnabled && settings.semanticMaskBlurEyeReveal)
                    || (settings.hairDropShadowEnabled && settings.semanticMaskBlurHairShadow)
                    || (faceHairDiffuseReady && settings.semanticMaskBlurFaceHairDiffuse));

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
                passData.eyeDataSampled = needsEyeCapture;
                passData.semanticMaskBlurredLowTexture = semanticMaskBlurredLowTexture;
                passData.semanticMaskBlurredHighTexture = semanticMaskBlurredHighTexture;
                passData.semanticMaskBlurSampled = compositeSamplesSemanticMaskBlur;
                passData.faceHairDiffuseSourceColorSampled = settings.debugMode == HoCharacterSpecializationDebugMode.FaceHairDiffuseSourceMask;
                passData.material = compositeMaterial;
                passData.faceHairDiffuseReady = faceHairDiffuseReady;
                passData.subjectOutlineReady = subjectOutlineReady;
                passData.enhancedOutlineReady = enhancedOutlineReady;
                passData.semanticMaskBlurReady = semanticMaskBlurReady;
                passData.semanticMaskOptions = CreateSemanticMaskOptions(settings, semanticMaskBlurReady);
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
                // eyeColor 是**真读**：Composite.shader:619 在 Frag 开头无条件采样它
                // （debug 1 在 :640 直接返回它，:771 的 lerp 也拿它当目标色）。所以捕获支被门控掉时
                // 这条读和下面的全局绑定都保留 —— 那张纹理由 RDG 按描述符清成 0，而
                // revealMask 恒为 0（:243-246）→ :771 的 lerp 结果逐位等于 source。
                builder.UseTexture(eyeColorTexture, AccessFlags.Read);
                // eyeData 的 4 个采样点全部有门（:250 在 _HoCharacterOptions.x > 0.5 之后、
                // :275 在角度强度 > 0.0001 之后、:645/:663 在 debug 2/17 分支里），
                // 而 needsEyeCapture 就是这几个条件的并集，所以这里可以跟着门控。
                if (needsEyeCapture)
                {
                    builder.UseTexture(eyeDataTexture, AccessFlags.Read);
                }
                // 语义副本（模糊对）的读声明见上面 compositeSamplesSemanticMaskBlur 的推导。
                if (compositeSamplesSemanticMaskBlur)
                {
                    builder.UseTexture(semanticMaskBlurredLowTexture, AccessFlags.Read);
                    builder.UseTexture(semanticMaskBlurredHighTexture, AccessFlags.Read);
                }
                if (faceHairDiffuseReady)
                {
                    // 源色只在 debug 5 被采（Composite.shader:674-683，还要 options.y > 0.5），
                    // 别的时候不必让它活到最后一趟（RDG 的别名空间）。
                    if (settings.debugMode == HoCharacterSpecializationDebugMode.FaceHairDiffuseSourceMask)
                    {
                        builder.UseTexture(faceHairDiffuseSourceColorTexture, AccessFlags.Read);
                    }

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
                        if (data.faceHairDiffuseSourceColorSampled)
                        {
                            context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.FaceHairDiffuseSourceColorTextureId, data.faceHairDiffuseSourceColorTexture);
                        }

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
                    // eyeData 只有 needsEyeCapture 时才存在（否则它连纹理都不是），
                    // 而 shader 也只在同一组门下才会采它。
                    if (data.eyeDataSampled)
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeDataTextureId, data.eyeDataTexture);
                    }

                    // The anti-aliased semantic masks are optional: when the shared pass did not run,
                    // the consumers fall back to reading the raw bits. Which effect reads the copy is
                    // the user's per-effect choice, carried in the options vector.
                    // 注意 _HoCharacterSemanticMaskBlurValid 必须照旧无条件写：它为 0 正是"读原始 bit"
                    // 那条分支的条件（Composite.shader:92），跟这里绑不绑模糊对无关。
                    context.cmd.SetGlobalFloat(HoCharacterSpecializationShaderConstants.SemanticMaskBlurValidId, data.semanticMaskBlurReady ? 1.0f : 0.0f);
                    context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SemanticMaskOptionsId, data.semanticMaskOptions);
                    if (data.semanticMaskBlurSampled)
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureId, data.semanticMaskBlurredLowTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureId, data.semanticMaskBlurredHighTexture);
                    }

                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        // 捕获支（CaptureFace / CaptureEye 两趟几何 pass + captureDepth + eyeData）的唯一消费者是
        // 合成趟的眼睛透过路径。这把门复制的是"这个消费者到底会不会用到捕获"的 shader 侧条件：
        //   · ResolveEyeRevealMask 在 _HoCharacterOptions.x <= 0.5 时**采样前** return 0
        //     （Composite.shader:241-246），而 options.x = settings.eyeRevealEnabled（MaterialProperties.cs:226）；
        //   · ResolveEyeAngleFactor 在角度强度 <= 0.0001 时**采样前** return 1（:265-271），
        //     角度强度 = eyeRevealAngleEnabled ? Clamp01(eyeRevealAngleStrength) : 0（:177-181）——
        //     这一项不影响画面（revealMask=0 时 :771 的 lerp 权重恒为 0），但它会真的去采 eyeData，
        //     所以门里必须带上它，否则会读到没绑定的纹理；
        //   · debug 1/2/3/16/17 分别是 eyeColor / eyeData / revealMask / 角度因子 / 角度表，
        //     它们的采样点不受上面两个 gate 保护（:640、:645、:651、:657、:663），所以也要留在门内。
        // 不在门里的情形，:771 的 lerp(source.rgb, eyeColor.rgb, revealMask * eyeAngleFactor) 权重恒为 0，
        // 输出逐位等于 source.rgb：eyeColor 即使在门控帧里也是被 RDG 清成 0 的合法纹理（见下面录制处）。
        private static bool RequiresCharacterCapture(HoCharacterSpecializationSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (settings.eyeRevealEnabled)
            {
                return true;
            }

            if (settings.eyeRevealAngleEnabled && settings.eyeRevealAngleStrength > 0.0001f)
            {
                return true;
            }

            switch (settings.debugMode)
            {
                case HoCharacterSpecializationDebugMode.EyeColor:
                case HoCharacterSpecializationDebugMode.EyeAlpha:
                case HoCharacterSpecializationDebugMode.EyeRevealMask:
                case HoCharacterSpecializationDebugMode.EyeAngleFactor:
                case HoCharacterSpecializationDebugMode.EyeAngleTable:
                    return true;
                default:
                    return false;
            }
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
            // Capture textures are sampled later as screen-space data. Keep them single-sampled
            // so camera MSAA coverage is not resolved into the captured eye color.
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = false;
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

        // The blurred semantic masks only carry 0..1 coverage, so 8 bits per channel is plenty and
        // cheaper than the 16F metadata textures they are derived from.
        private static GraphicsFormat GetSemanticMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8G8B8A8_UNorm;
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
        private RTHandle semanticMaskBlurredLowTexture;
        private RTHandle semanticMaskBlurredHighTexture;

        public RTHandle EyeColorTexture => eyeColorTexture;
        public RTHandle EyeDataTexture => eyeDataTexture;
        public RTHandle CaptureDepthTexture => captureDepthTexture;
        public RTHandle SemanticMaskBlurredLowTexture => semanticMaskBlurredLowTexture;
        public RTHandle SemanticMaskBlurredHighTexture => semanticMaskBlurredHighTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoCharacterSpecializationSettings settings, bool allocateSemanticMaskBlur)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
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

            RenderTextureDescriptor semanticMaskDescriptor = descriptor;
            GraphicsFormat semanticMaskFormat = GetSemanticMaskGraphicsFormat();
            if (semanticMaskFormat != GraphicsFormat.None)
            {
                semanticMaskDescriptor.graphicsFormat = semanticMaskFormat;
            }

            // Releasing instead of allocating keeps "every effect unchecked" at zero cost without
            // reallocating the pair every frame.
            if (allocateSemanticMaskBlur)
            {
                RenderingUtils.ReAllocateIfNeeded(ref semanticMaskBlurredLowTexture, semanticMaskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.SemanticMaskBlurredLowTextureName);
                RenderingUtils.ReAllocateIfNeeded(ref semanticMaskBlurredHighTexture, semanticMaskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.SemanticMaskBlurredHighTextureName);
            }
            else
            {
                ReleaseSemanticMaskBlurTextures();
            }
        }

        private void ReleaseSemanticMaskBlurTextures()
        {
            semanticMaskBlurredLowTexture?.Release();
            semanticMaskBlurredHighTexture?.Release();
            semanticMaskBlurredLowTexture = null;
            semanticMaskBlurredHighTexture = null;
        }

        public void Release()
        {
            eyeColorTexture?.Release();
            eyeDataTexture?.Release();
            captureDepthTexture?.Release();
            semanticMaskBlurredLowTexture?.Release();
            semanticMaskBlurredHighTexture?.Release();
            eyeColorTexture = null;
            eyeDataTexture = null;
            captureDepthTexture = null;
            semanticMaskBlurredLowTexture = null;
            semanticMaskBlurredHighTexture = null;
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
            descriptor.msaaSamples = 1;
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

        private static GraphicsFormat GetSemanticMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8G8B8A8_UNorm;
            if (SystemInfo.IsFormatSupported(preferredFormat, GraphicsFormatUsage.Render))
            {
                return preferredFormat;
            }

            GraphicsFormat format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            return format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)
                ? format
                : GraphicsFormat.B8G8R8A8_UNorm;
        }
    }
}
