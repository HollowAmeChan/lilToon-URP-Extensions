using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
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
        private Material objectSemanticMaterial;
        private HoCharacterEyeAngleTable eyeAngleTable;
        private Shader compositeShader;
        private Shader captureClearShader;
        private Shader faceHairDiffuseShader;
        private Shader subjectOutlineShader;
        private Shader objectSemanticShader;
        private bool warnedMissingCompositeShader;
        private bool warnedMissingCaptureClearShader;
        private bool warnedMissingFaceHairDiffuseShader;
        private bool warnedMissingSubjectOutlineShader;
        private bool warnedMissingObjectSemanticShader;

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
                objectSemanticMaterial);
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
                objectSemanticMaterial);
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
            CoreUtils.Destroy(objectSemanticMaterial);
            compositeMaterial = null;
            captureClearMaterial = null;
            faceHairDiffuseMaterial = null;
            subjectOutlineMaterial = null;
            objectSemanticMaterial = null;
            compositeShader = null;
            captureClearShader = null;
            faceHairDiffuseShader = null;
            subjectOutlineShader = null;
            objectSemanticShader = null;
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
                ref objectSemanticMaterial,
                ref objectSemanticShader,
                Shader.Find(HoCharacterSpecializationShaderConstants.ObjectSemanticShaderName),
                HoCharacterSpecializationShaderConstants.ObjectSemanticShaderName,
                ref warnedMissingObjectSemanticShader,
                "HoCharacterSpecialization object semantics are unavailable because shader '{0}' could not be found.");

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
        private Material objectSemanticMaterial;
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
            Material subjectOutlineMaterial,
            Material objectSemanticMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            this.objectSemanticMaterial = objectSemanticMaterial;
            ConfigurePass();
        }

        public void SetupRenderGraph(
            HoCharacterSpecializationSettings settings,
            Material compositeMaterial,
            Material captureClearMaterial,
            Material faceHairDiffuseMaterial,
            Material subjectOutlineMaterial,
            Material objectSemanticMaterial)
        {
            this.settings = settings;
            this.compositeMaterial = compositeMaterial;
            this.captureClearMaterial = captureClearMaterial;
            this.faceHairDiffuseMaterial = faceHairDiffuseMaterial;
            this.subjectOutlineMaterial = subjectOutlineMaterial;
            this.objectSemanticMaterial = objectSemanticMaterial;
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
                objectSemanticMaterial != null);
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

                bool objectSemanticReady = objectSemanticMaterial != null
                    && renderTargets.ObjectSemanticLowTexture != null
                    && renderTargets.ObjectSemanticHighTexture != null;
                if (objectSemanticReady)
                {
                    // 语义位平面：读 OB 身份池 + 覆盖率（这两张图是 OB feature 在兼容路径里设好的全局），
                    // 写两张 RGBA8 位平面。
                    captureColorIdentifiers[0] = renderTargets.ObjectSemanticLowTexture.nameID;
                    captureColorIdentifiers[1] = renderTargets.ObjectSemanticHighTexture.nameID;
                    cmd.SetRenderTarget(captureColorIdentifiers, renderTargets.CaptureDepthTexture.nameID);
                    Blitter.BlitTexture(cmd, tempTexture, new Vector4(1, 1, 0, 0), objectSemanticMaterial, 0);
                    cmd.SetGlobalVector(
                        HoCharacterSpecializationShaderConstants.ScreenTexelSizeId,
                        GetScreenTexelSize(renderingData.cameraData.cameraTargetDescriptor, settings));
                }

                ApplyMaterialProperties(compositeMaterial, settings);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeColorTextureId, renderTargets.EyeColorTexture.nameID);
                cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeDataTextureId, renderTargets.EyeDataTexture.nameID);
                if (objectSemanticReady)
                {
                    cmd.SetGlobalTexture(
                        HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureId,
                        renderTargets.ObjectSemanticLowTexture.nameID);
                    cmd.SetGlobalTexture(
                        HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureId,
                        renderTargets.ObjectSemanticHighTexture.nameID);
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
            HoObjectBufferRenderGraphResources objectBufferResources = frameData.GetOrCreate<HoObjectBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            bool backBufferActive = resourceData.isActiveTargetBackBuffer;
            bool hasCameraColor = resourceData.activeColorTexture.IsValid();
            bool hasObjectBufferIdentity = objectBufferResources.HasRequiredTextures;
            // 语义位平面这帧能不能产出：身份池在 + 打包材质在。它是合成与两条轮廓源的**共同输入**，
            // 所以它不成立时整支 no-op（没有 OB 就没有角色语义，退化成"什么都不做"而不是猜）。
            bool objectSemanticAvailable = hasObjectBufferIdentity && objectSemanticMaterial != null;
            bool hasGeometryNormalDepth = geometryResources.normalDepthTexture.IsValid();
            bool hasGeometryDepth = geometryResources.depthTexture.IsValid();
            bool requiresFaceHairDiffuseTextures = RequiresFaceHairDiffuseTextures(settings);
            bool requiresSubjectOutlineTextures = RequiresSubjectOutlineTextures(settings);
            bool requiresEnhancedOutlineTextures = RequiresEnhancedOutlineTextures(settings);
            bool requiresGeometryDepth = requiresSubjectOutlineTextures || requiresEnhancedOutlineTextures;
            HoCharacterSpecializationRuntimeDiagnostics.PublishRenderGraphInputs(
                cameraData.camera,
                "Composite",
                backBufferActive,
                hasCameraColor,
                hasObjectBufferIdentity,
                objectSemanticAvailable,
                hasGeometryNormalDepth,
                hasGeometryDepth,
                requiresGeometryDepth);

            if (backBufferActive
                || !hasCameraColor
                || !objectSemanticAvailable
                || !hasGeometryNormalDepth
                || (requiresGeometryDepth && !hasGeometryDepth))
            {
                return;
            }

            // 捕获支的门控：眼透（或它的 debug 模式）需要这张脸/眼的捕获。
            // 脸色扩散的底色输入就是同一次脸捕获的 MRT0（= 材质算完光照的 color），
            // 所以这条链要跑（效果开关打开，或它的 8 个 debug 视图任一）就必须有脸捕获；
            // 眼捕获仍然只服务眼透：脸捕获自己会把 MRT0 写成受光脸（ClearCaptureTargets 先清 0）。
            bool needsFaceCapture = RequiresCharacterCapture(settings) || requiresFaceHairDiffuseTextures;
            bool needsEyeCapture = RequiresCharacterCapture(settings);
            bool needsCharacterCapture = needsFaceCapture || needsEyeCapture;

            TextureHandle source = resourceData.activeColorTexture;
            // eyeColor 始终存在，有两个消费者：合成 shader 在 :621 无条件采样它（见 RequiresCharacterCapture
            // 的注释），以及脸色扩散源趟的"受光脸"输入（只在 needsFaceCapture 时才是真数据）。
            // 描述符带 clearBuffer，RDG 会在本帧首次使用（= 被谁当采样输入）时把它显式清成 0。
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

            // 共享的角色语义位平面：一趟、两个 MRT，给合成 / 脸色扩散源 / 两条轮廓源一起用。
            TextureHandle objectSemanticLowTexture = TextureHandle.nullHandle;
            TextureHandle objectSemanticHighTexture = TextureHandle.nullHandle;
            {
                TextureDesc objectSemanticDescriptor = CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    GetObjectSemanticGraphicsFormat(),
                    HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureName);
                objectSemanticLowTexture = renderGraph.CreateTexture(objectSemanticDescriptor);
                objectSemanticDescriptor.name = HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureName;
                objectSemanticHighTexture = renderGraph.CreateTexture(objectSemanticDescriptor);

                AddObjectSemanticPass(
                    renderGraph,
                    "Ho-CharacterSpecialization ObjectSemantic",
                    objectSemanticMaterial,
                    objectBufferResources.id0Texture,
                    objectBufferResources.id1Texture,
                    objectBufferResources.coverageTexture,
                    objectSemanticLowTexture,
                    objectSemanticHighTexture);
            }
            Vector4 screenTexelSize = GetScreenTexelSize(cameraData.cameraTargetDescriptor, settings);

            TextureHandle faceHairDiffuseSourceColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseSourceDepthTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseTempColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseTempDepthTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseColorTexture = TextureHandle.nullHandle;
            TextureHandle faceHairDiffuseDepthTexture = TextureHandle.nullHandle;
            bool faceHairDiffuseReady = requiresFaceHairDiffuseTextures && faceHairDiffuseMaterial != null;
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
                    passData.objectSemanticLowTexture = objectSemanticLowTexture;
                    passData.geometryNormalDepthTexture = geometryResources.normalDepthTexture;
                    passData.eyeColorTexture = eyeColorTexture;
                    passData.options = CreateCharacterOptions(settings);
                    passData.material = faceHairDiffuseMaterial;

                    // 相机颜色在这趟里从来没有被采样（HoCharacterFaceHairDiffuse.shader pass 0 的
                    // Frag:43-71 里没有 _BlitTexture），所以不再声明这条读；画面也不再用
                    // Blitter.BlitTexture 去绑 _BlitTexture，改成 DrawProcedural + 显式 _BlitScaleBias。
                    builder.UseTexture(passData.objectSemanticLowTexture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryNormalDepthTexture, AccessFlags.Read);
                    // 受光脸：这趟真的采它（Frag 里 SAMPLE _lilHoCharacterEyeColorTexture），
                    // 所以这条读是真依赖 —— 它把 CaptureFace 排到本趟之前，同时下面自己绑全局，
                    // 不复用上一帧合成趟留下的绑定。
                    builder.UseTexture(passData.eyeColorTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(faceHairDiffuseSourceColorTexture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(faceHairDiffuseSourceDepthTexture, 1, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (FaceHairDiffuseSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureId, data.objectSemanticLowTexture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.geometryNormalDepthTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.EyeColorTextureId, data.eyeColorTexture);
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.OptionsId, data.options);
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
                    passData.objectSemanticLowTexture = objectSemanticLowTexture;
                    passData.objectSemanticHighTexture = objectSemanticHighTexture;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4((float)HoCharacterSemanticChannel.CharacterFull, 0.0f, 0.0f, 0.0f);

                    // 相机颜色在这趟里从来没有被采样（HoCharacterSubjectOutline.shader pass 0 的
                    // Frag:91-112 里没有 _BlitTexture），所以不再声明这条读；画面也不再用
                    // Blitter.BlitTexture 去绑 _BlitTexture，改成 DrawProcedural + 显式 _BlitScaleBias。
                    builder.UseTexture(passData.objectSemanticLowTexture, AccessFlags.Read);
                    builder.UseTexture(passData.objectSemanticHighTexture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);

                    builder.SetRenderAttachment(subjectOutlineSourceTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SubjectOutlineSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SubjectOutlineSourceParamsId, data.sourceParams);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureId, data.objectSemanticLowTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureId, data.objectSemanticHighTexture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, data.geometryDepthTexture);

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
                    passData.objectSemanticLowTexture = objectSemanticLowTexture;
                    passData.objectSemanticHighTexture = objectSemanticHighTexture;
                    passData.geometryDepthTexture = geometryResources.depthTexture;
                    passData.material = subjectOutlineMaterial;
                    passData.sourceParams = new Vector4(Mathf.Clamp((int)settings.enhancedOutlineSourceChannel, 0, 7), 0.0f, 0.0f, 0.0f);

                    // 相机颜色在这趟里从来没有被采样（同 #7 的 shader）：去掉假读，改 DrawProcedural。
                    builder.UseTexture(passData.objectSemanticLowTexture, AccessFlags.Read);
                    builder.UseTexture(passData.objectSemanticHighTexture, AccessFlags.Read);
                    builder.UseTexture(passData.geometryDepthTexture, AccessFlags.Read);

                    builder.SetRenderAttachment(enhancedOutlineSourceTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SubjectOutlineSourcePassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.SubjectOutlineSourceParamsId, data.sourceParams);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureId, data.objectSemanticLowTexture);
                        context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureId, data.objectSemanticHighTexture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, data.geometryDepthTexture);

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

            // 语义位平面是合成本趟的常备输入：眼透 / 前发投影 / 脸色扩散 / 两条轮廓以及它们的
            // debug 视图都会采它，所以不做逐效果门控（它一定已经产出，上面已经检查过）。
            // 源色纹理在合成趟里只有两个采样点：debug 5（源遮罩）与 debug 18（① 捕获受光脸的原始采样）。
            // 别的时候不必让它活到最后一趟（RDG 的别名空间）。
            bool faceHairDiffuseSourceColorSampled =
                settings.debugMode == HoCharacterSpecializationDebugMode.FaceHairDiffuseSourceMask
                || settings.debugMode == HoCharacterSpecializationDebugMode.FaceHairDiffuseCapturedFaceLit;

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Ho-CharacterSpecialization Composite", out CompositePassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.objectBufferId0Texture = objectBufferResources.id0Texture;
                passData.geometryNormalDepthTexture = geometryResources.normalDepthTexture;
                passData.objectSemanticLowTexture = objectSemanticLowTexture;
                passData.objectSemanticHighTexture = objectSemanticHighTexture;
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
                passData.faceHairDiffuseSourceColorSampled = faceHairDiffuseSourceColorSampled;
                passData.screenTexelSize = screenTexelSize;
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
                builder.UseTexture(passData.objectBufferId0Texture, AccessFlags.Read);
                builder.UseTexture(passData.geometryNormalDepthTexture, AccessFlags.Read);
                // eyeColor 是**真读**：Composite.shader:621 在 Frag 开头无条件采样它
                // （debug 1 在 :640-642 直接返回它，:823 的 lerp 也拿它当目标色）。所以捕获支被门控掉时
                // 这条读和下面的全局绑定都保留 —— 那张纹理由 RDG 按描述符清成 0，而
                // revealMask 恒为 0（:243-246）→ :823 的 lerp 结果逐位等于 source。
                builder.UseTexture(eyeColorTexture, AccessFlags.Read);
                // eyeData 的 4 个采样点全部有门（:250 在 _HoCharacterOptions.x > 0.5 之后、
                // :275 在角度强度 > 0.0001 之后、:647/:665 在 debug 2/17 分支里），
                // 而 needsEyeCapture 就是这几个条件的并集，所以这里可以跟着门控。
                if (needsEyeCapture)
                {
                    builder.UseTexture(eyeDataTexture, AccessFlags.Read);
                }
                // 语义位平面：合成趟一直采它（眼透 / 前发投影 / 脸色扩散 / 两条轮廓的 source
                // 都由它派生），所以这条读是无条件的。
                builder.UseTexture(objectSemanticLowTexture, AccessFlags.Read);
                builder.UseTexture(objectSemanticHighTexture, AccessFlags.Read);
                if (faceHairDiffuseReady)
                {
                    // 源色只在 debug 5 / 18 被采（Composite.shader:683 与 :722，都要 options.y > 0.5），
                    // 别的时候不必让它活到最后一趟（RDG 的别名空间）。
                    if (faceHairDiffuseSourceColorSampled)
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
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.geometryNormalDepthTexture);
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

                    context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureId, data.objectSemanticLowTexture);
                    context.cmd.SetGlobalTexture(HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureId, data.objectSemanticHighTexture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.objectBufferId0Texture);
                    context.cmd.SetGlobalVector(HoCharacterSpecializationShaderConstants.ScreenTexelSizeId, data.screenTexelSize);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        // 捕获支（CaptureFace / CaptureEye 两趟几何 pass + captureDepth + eyeData）的消费者有两个：
        //   · 合成趟的眼睛透过路径（返回 true 的那组条件就是它的 shader 侧门）；
        //   · 脸色扩散源趟的"受光脸"输入（MRT0 = CaptureFace 写的材质受光 color）——
        //     它的条件不在这里，而是录制处的 `needsFaceCapture = RequiresCharacterCapture(settings)
        //     || requiresFaceHairDiffuseTextures`（那条链只消费脸捕获，不需要眼捕获）。
        // 下面这把门复制的是"眼透这条消费者到底会不会用到捕获"的 shader 侧条件：
        //   · ResolveEyeRevealMask 在 _HoCharacterOptions.x <= 0.5 时**采样前** return 0
        //     （Composite.shader:241-246），而 options.x = settings.eyeRevealEnabled（MaterialProperties.cs:233）；
        //   · ResolveEyeAngleFactor 在角度强度 <= 0.0001 时**采样前** return 1（:265-271），
        //     角度强度 = eyeRevealAngleEnabled ? Clamp01(eyeRevealAngleStrength) : 0（:177-181）——
        //     这一项不影响画面（revealMask=0 时 :823 的 lerp 权重恒为 0），但它会真的去采 eyeData，
        //     所以门里必须带上它，否则会读到没绑定的纹理；
        //   · debug 1/2/3/16/17 分别是 eyeColor / eyeData / revealMask / 角度因子 / 角度表，
        //     它们的采样点不受上面两个 gate 保护（:642、:647、:653、:659、:665），所以也要留在门内。
        // 不在门里的情形，:823 的 lerp(source.rgb, eyeColor.rgb, revealMask * eyeAngleFactor) 权重恒为 0，
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

        /// <summary>
        /// 屏幕空间采样用的 texel size：语义平面 / 眼捕获 / 几何都是同一个渲染分辨率（都由
        /// <see cref="CreateTextureDesc"/> 按 renderScale 缩过），所以一份就够。
        /// **不能用纹理自带的 _TexelSize**：全局纹理没有那一项，读出来是 0，
        /// 会让所有"按像素扩张 / 羽化"的半径静默失效。
        /// </summary>
        private static Vector4 GetScreenTexelSize(RenderTextureDescriptor cameraTextureDescriptor, HoCharacterSpecializationSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            float width = Mathf.Max(1, cameraTextureDescriptor.width / divisor);
            float height = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            return new Vector4(1.0f / width, 1.0f / height, width, height);
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
        private static GraphicsFormat GetObjectSemanticGraphicsFormat()
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
        private RTHandle objectSemanticLowTexture;
        private RTHandle objectSemanticHighTexture;

        public RTHandle EyeColorTexture => eyeColorTexture;
        public RTHandle EyeDataTexture => eyeDataTexture;
        public RTHandle CaptureDepthTexture => captureDepthTexture;
        public RTHandle ObjectSemanticLowTexture => objectSemanticLowTexture;
        public RTHandle ObjectSemanticHighTexture => objectSemanticHighTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoCharacterSpecializationSettings settings, bool allocateObjectSemantic)
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

            RenderTextureDescriptor objectSemanticDescriptor = descriptor;
            GraphicsFormat semanticMaskFormat = GetObjectSemanticGraphicsFormat();
            if (semanticMaskFormat != GraphicsFormat.None)
            {
                objectSemanticDescriptor.graphicsFormat = semanticMaskFormat;
            }

            // Releasing instead of allocating keeps "every effect unchecked" at zero cost without
            // reallocating the pair every frame.
            if (allocateObjectSemantic)
            {
                RenderingUtils.ReAllocateIfNeeded(ref objectSemanticLowTexture, objectSemanticDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.ObjectSemanticLowTextureName);
                RenderingUtils.ReAllocateIfNeeded(ref objectSemanticHighTexture, objectSemanticDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoCharacterSpecializationShaderConstants.ObjectSemanticHighTextureName);
            }
            else
            {
                ReleaseObjectSemanticTextures();
            }
        }

        private void ReleaseObjectSemanticTextures()
        {
            objectSemanticLowTexture?.Release();
            objectSemanticHighTexture?.Release();
            objectSemanticLowTexture = null;
            objectSemanticHighTexture = null;
        }

        public void Release()
        {
            eyeColorTexture?.Release();
            eyeDataTexture?.Release();
            captureDepthTexture?.Release();
            objectSemanticLowTexture?.Release();
            objectSemanticHighTexture?.Release();
            eyeColorTexture = null;
            eyeDataTexture = null;
            captureDepthTexture = null;
            objectSemanticLowTexture = null;
            objectSemanticHighTexture = null;
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

        private static GraphicsFormat GetObjectSemanticGraphicsFormat()
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
