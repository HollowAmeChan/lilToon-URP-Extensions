using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.AttributeComposite;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [DisallowMultipleRendererFeature("Ho-ScreenProcess")]
    [ExecuteAlways]
    public sealed class ScreenProcessRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private ScreenProcessStackSettings settings = new ScreenProcessStackSettings();

        // Compatibility command buffers retain material references, so each active layer slot owns its material.
        private readonly Dictionary<(Shader shader, int slot), Material> materialCache = new Dictionary<(Shader, int), Material>();
        private readonly HashSet<string> warnedMissingShaders = new HashSet<string>();
        private readonly List<ScreenProcessRuntimeLayer> runtimeLayers = new List<ScreenProcessRuntimeLayer>();
        private Material subjectMaskMaterial;
        private Shader subjectMaskShader;
        private bool warnedMissingSubjectMaskShader;
        private ScreenProcessPass pass;
        private ScreenProcessSemanticBufferReleasePass semanticBufferReleasePass;
        private bool registeredCameraReset;

        [Tooltip("The renderer feature installs the pass, and Volume profiles provide the active ScreenProcess stack.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public ScreenProcessStackSettings Settings => settings;
        private string ConsumerName => "ScreenProcess " + GetInstanceID();

        public override void Create()
        {
            if (!registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering += ResetConsumerDeclaration;
                registeredCameraReset = true;
            }
            IsUseVolumes = UseVolumes;
            pass = new ScreenProcessPass("Ho-ScreenProcess AfterURP BeforeImageProcess");
            semanticBufferReleasePass = new ScreenProcessSemanticBufferReleasePass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ScreenProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
                pass?.ClearRuntimeLayers();
                pass?.ReleaseCompatibilityResources();
                return;
            }

            BuildRuntimeLayers(volume);
            SetupCompatibilityPass(pass, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle, runtimeLayers);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ScreenProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
                ScreenProcessRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    GetSkipReason(in renderingData, volume));
                pass?.ClearRuntimeLayers();
                pass?.ReleaseCompatibilityResources();
                return;
            }

            BuildRuntimeLayers(volume);
            if (runtimeLayers.Count == 0)
            {
                ScreenProcessRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    "没有可运行的 ScreenProcess layer。");
            }

            EnqueueRenderGraphPass(renderer, pass, runtimeLayers);
        }

        protected override void Dispose(bool disposing)
        {
            if (registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering -= ResetConsumerDeclaration;
                registeredCameraReset = false;
            }
            HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
            pass?.Dispose();
            pass = null;
            semanticBufferReleasePass = null;

            foreach (Material material in materialCache.Values)
            {
                CoreUtils.Destroy(material);
            }

            CoreUtils.Destroy(subjectMaskMaterial);
            subjectMaskMaterial = null;
            subjectMaskShader = null;
            materialCache.Clear();
            runtimeLayers.Clear();
            warnedMissingShaders.Clear();
        }

        private void ResetConsumerDeclaration(ScriptableRenderContext context, Camera camera)
        {
            HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
        }

        private bool ShouldRender(in RenderingData renderingData, ScreenProcessStackVolume volume)
        {
            IsUseVolumes = UseVolumes;
            if (settings == null || !settings.enabled || !UseVolumes)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.SceneView)
            {
                return volume != null && volume.ShowInSceneView.value && volume.IsActive();
            }

            return cameraType == CameraType.Game && volume != null && volume.IsActive();
        }

        private string GetSkipReason(in RenderingData renderingData, ScreenProcessStackVolume volume)
        {
            if (settings == null || !settings.enabled)
            {
                return "Feature 已关闭。";
            }

            if (!UseVolumes)
            {
                return "Volume 模式已关闭。";
            }

            if (volume == null)
            {
                return "未找到 ScreenProcess Volume。";
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.SceneView && !volume.ShowInSceneView.value)
            {
                return "Scene View 渲染已关闭。";
            }

            if (!volume.IsActive())
            {
                return "ScreenProcess Volume 未激活。";
            }

            return cameraType == CameraType.Game || cameraType == CameraType.SceneView
                ? "未入队。"
                : "当前 camera type 不支持。";
        }

        private void BuildRuntimeLayers(ScreenProcessStackVolume volume)
        {
            runtimeLayers.Clear();
            var semanticNames = new HashSet<string>();
            List<ScreenProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
                return;
            }

            foreach (ScreenProcessLayer layer in layers)
            {
                if (layer == null || !layer.IsActive)
                {
                    continue;
                }

                Material material = ResolveMaterial(layer, runtimeLayers.Count);
                if (material == null)
                {
                    continue;
                }

                runtimeLayers.Add(new ScreenProcessRuntimeLayer(layer, material));
                if ((layer.useMask || layer.debugMask) && layer.maskSource == HoACQueryKind.Semantic)
                    semanticNames.Add(layer.maskSemanticName ?? string.Empty);
            }
            if (runtimeLayers.Count > 0)
                HoAttributeCompositeConsumerRegistry.Declare(ConsumerName, new List<string>(semanticNames).ToArray());
            else HoAttributeCompositeConsumerRegistry.Remove(ConsumerName);
        }

        private void SetupCompatibilityPass(
            ScreenProcessPass pass,
            RTHandle cameraColorTarget,
            RTHandle cameraDepthTarget,
            List<ScreenProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                pass?.ReleaseCompatibilityResources();
                return;
            }

            pass.Setup(
                cameraColorTarget,
                cameraDepthTarget,
                layers,
                ScreenProcessRenderPassEvents.ScreenProcessStack,
                settings,
                EnsureSubjectMaskMaterial());
        }

        private void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            ScreenProcessPass pass,
            List<ScreenProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                pass?.ReleaseCompatibilityResources();
                EnqueueSemanticBufferReleasePass(renderer);
                return;
            }

            pass.SetupRenderGraph(
                layers,
                ScreenProcessRenderPassEvents.ScreenProcessStack,
                settings,
                EnsureSubjectMaskMaterial());
            renderer.EnqueuePass(pass);
            EnqueueSemanticBufferReleasePass(renderer);
        }

        private void EnqueueSemanticBufferReleasePass(ScriptableRenderer renderer)
        {
            semanticBufferReleasePass?.Setup(ScreenProcessRenderPassEvents.ScreenProcessStack);
            if (semanticBufferReleasePass != null)
            {
                renderer.EnqueuePass(semanticBufferReleasePass);
            }
        }

        private static ScreenProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<ScreenProcessStackVolume>() : null;
        }

        private Material ResolveMaterial(ScreenProcessLayer layer, int slot)
        {
            Shader shader = layer.materialOverride != null ? layer.materialOverride.shader : layer.shaderOverride;
            if (shader == null && layer.effect == ScreenProcessEffect.CustomMaterial)
            {
                shader = settings.defaultLayerShader;
            }

            string shaderName = ScreenProcessEffectRegistry.GetDefaultShaderName(layer.effect);
            if (shader == null)
            {
                shader = Shader.Find(shaderName);
            }

            if (shader == null)
            {
                WarnMissingShader(layer, shaderName);
                return null;
            }

            var key = (shader, slot);
            if (materialCache.TryGetValue(key, out Material material) && material != null)
            {
                return material;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            materialCache[key] = material;
            return material;
        }

        private Material EnsureSubjectMaskMaterial()
        {
            if (!ContainsSubjectMaskLayer(runtimeLayers))
            {
                return null;
            }

            Shader shader = settings.subjectMaskShader != null
                ? settings.subjectMaskShader
                : Shader.Find(ScreenProcessShaderConstants.SubjectMaskShaderName);

            if (subjectMaskMaterial != null && subjectMaskShader == shader)
            {
                return subjectMaskMaterial;
            }

            if (shader == null)
            {
                if (!warnedMissingSubjectMaskShader)
                {
                    warnedMissingSubjectMaskShader = true;
                    Debug.LogWarning($"ScreenProcess Drop Shadow was skipped because shader '{ScreenProcessShaderConstants.SubjectMaskShaderName}' could not be found.");
                }

                return null;
            }

            CoreUtils.Destroy(subjectMaskMaterial);
            subjectMaskShader = shader;
            subjectMaskMaterial = CoreUtils.CreateEngineMaterial(shader);
            return subjectMaskMaterial;
        }

        private static bool ContainsSubjectMaskLayer(List<ScreenProcessRuntimeLayer> layers)
        {
            if (layers == null)
            {
                return false;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = layers[i];
                if (runtimeLayer != null
                    && runtimeLayer.settings != null
                    && runtimeLayer.settings.IsActive
                    && EffectRequiresSubjectMask(runtimeLayer.settings.effect))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EffectRequiresSubjectMask(ScreenProcessEffect effect)
        {
            return effect == ScreenProcessEffect.DropShadow;
        }

        private void WarnMissingShader(ScreenProcessLayer layer, string shaderName)
        {
            string key = $"{layer.effect}:{shaderName}";
            if (!warnedMissingShaders.Add(key))
            {
                return;
            }

            Debug.LogWarning($"ScreenProcess effect '{layer.effect}' was skipped because shader '{shaderName}' could not be found.");
        }
    }

    internal sealed class ScreenProcessRuntimeLayer
    {
        public readonly ScreenProcessLayer settings;
        public readonly Material material;
        public readonly HoACQueryDescriptor maskQuery;

        public ScreenProcessRuntimeLayer(ScreenProcessLayer settings, Material material)
        {
            this.settings = settings;
            this.material = material;
            maskQuery = settings.ResolveMaskQuery();
        }
    }

    internal sealed class ScreenProcessSemanticBufferReleasePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ScreenProcess Release Semantic Buffers");

        private sealed class PassData
        {
            public TextureHandle blackTexture;
        }

        public ScreenProcessSemanticBufferReleasePass()
        {
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Setup(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                ResetSemanticBufferGlobals(cmd);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            TextureHandle blackTexture = renderGraph.defaultResources.blackTexture;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-ScreenProcess Release Semantic Buffers", out PassData passData, ProfilingSampler))
            {
                passData.blackTexture = blackTexture;
                // MB 的 maskId / custom0 / objectCustom 兜底已删：遮罩来源现在是 AC（OB 覆盖率），
                // 由 OB 的 pass 自己发布，SP 只在每层明确置 _lilHoSPMaskValid。
                builder.SetGlobalTextureAfterPass(blackTexture, HoGeometryBufferShaderConstants.NormalDepthTextureId);
                builder.SetGlobalTextureAfterPass(blackTexture, HoGeometryBufferShaderConstants.DepthTextureId);
                builder.SetGlobalTextureAfterPass(blackTexture, HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId);
                builder.SetGlobalTextureAfterPass(blackTexture, HoGeometryBufferShaderConstants.SkyTextureId);
                builder.SetGlobalTextureAfterPass(blackTexture, ScreenProcessShaderConstants.SubjectMaskTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.blackTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, data.blackTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, data.blackTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, data.blackTexture);
                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 0.0f);
                    context.cmd.SetGlobalTexture(ScreenProcessShaderConstants.SubjectMaskTextureId, data.blackTexture);
                    context.cmd.SetGlobalFloat(ScreenProcessShaderConstants.SubjectMaskValidId, 0.0f);
                    ResetSemanticBufferFlags(context.cmd);
                });
            }
        }

        private static void ResetSemanticBufferGlobals(CommandBuffer cmd)
        {
            Texture fallback = Texture2D.blackTexture;
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, fallback);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, fallback);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, fallback);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, fallback);
            cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 0.0f);
            cmd.SetGlobalTexture(ScreenProcessShaderConstants.SubjectMaskTextureId, fallback);
            ResetSemanticBufferFlags(cmd);
            cmd.SetGlobalFloat(ScreenProcessShaderConstants.SubjectMaskValidId, 0.0f);
        }

        private static void ResetSemanticBufferFlags(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(ScreenProcessShaderConstants.MaskValidId, 0.0f);
            cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 0.0f);
            cmd.SetGlobalFloat(ScreenProcessShaderConstants.SubjectMaskValidId, 0.0f);
        }

        private static void ResetSemanticBufferFlags(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalFloat(ScreenProcessShaderConstants.MaskValidId, 0.0f);
            cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 0.0f);
            cmd.SetGlobalFloat(ScreenProcessShaderConstants.SubjectMaskValidId, 0.0f);
        }
    }

    internal sealed class ScreenProcessPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> SubjectMaskShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly List<ScreenProcessRuntimeLayer> runtimeLayers = new List<ScreenProcessRuntimeLayer>();
        private readonly ProfilingSampler screenProcessProfilingSampler;
        private readonly string screenProcessPassName;
        private RTHandle cameraColorTarget;
        private RTHandle cameraDepthTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private RTHandle subjectMaskTexture;
        private ScreenProcessStackSettings settings;
        private Material subjectMaskMaterial;
        private FilteringSettings subjectMaskFilteringSettings;
        private RenderStateBlock subjectMaskRenderStateBlock;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle subjectMaskTexture;
            public TextureHandle outlineNormalDepthTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle skyTexture;
            public ScreenProcessLayer layer;
            public Material material;
            public int passIndex;
            public float dynamicFocusDistance;
            public bool isEdgeLight;
            public bool isDropShadow;
            public bool isOutline;
            public bool isDepthOfField;
            public bool isPostLighting;
            public bool isSkyTyndall;
            public bool isDepthFog;
            public bool useMaskTexture;
            public bool intrinsicCoverage;
            public HoAttributeCompositeRenderGraphResources acResources;
            public HoACQueryDescriptor maskQuery;
            public bool useNormalDepth;
            public bool useSkyTexture;
            public bool useSubjectMask;
            public bool useOutlineNormalDepth;
            /// <summary>遮罩来源的 texel / 尺寸（xy = texel、zw = 尺寸）：给"按像素扩张 / 羽化"用。</summary>
            public Vector4 maskTexelSize;
        }

        private sealed class SubjectMaskPassData
        {
            public RendererListHandle rendererList;
        }

        public ScreenProcessPass(string passName)
        {
            screenProcessPassName = passName;
            screenProcessProfilingSampler = new ProfilingSampler(passName);
            subjectMaskRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            RTHandle cameraColorTarget,
            RTHandle cameraDepthTarget,
            List<ScreenProcessRuntimeLayer> layers,
            RenderPassEvent passEvent,
            ScreenProcessStackSettings settings,
            Material subjectMaskMaterial)
        {
            this.cameraColorTarget = cameraColorTarget;
            this.cameraDepthTarget = cameraDepthTarget;
            this.settings = settings;
            this.subjectMaskMaterial = subjectMaskMaterial;
            CopyLayers(layers);
            ConfigureSubjectMaskFiltering();
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(
            List<ScreenProcessRuntimeLayer> layers,
            RenderPassEvent passEvent,
            ScreenProcessStackSettings settings,
            Material subjectMaskMaterial)
        {
            ReleaseCompatibilityResources();
            this.settings = settings;
            this.subjectMaskMaterial = subjectMaskMaterial;
            CopyLayers(layers);
            ConfigureSubjectMaskFiltering();
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
            runtimeLayers.Clear();
        }

        public void ReleaseCompatibilityResources()
        {
            tempTextureA?.Release();
            tempTextureB?.Release();
            subjectMaskTexture?.Release();
            cameraColorTarget = null;
            cameraDepthTarget = null;
            tempTextureA = null;
            tempTextureB = null;
            subjectMaskTexture = null;
        }

        public void ClearRuntimeLayers()
        {
            runtimeLayers.Clear();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!HasActiveRuntimeLayers())
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref descriptor);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ScreenProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ScreenProcessShaderConstants.TempTextureBName);

            if (RequiresSubjectMask() && subjectMaskMaterial != null)
            {
                RenderTextureDescriptor maskDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                maskDescriptor.depthBufferBits = 0;
                maskDescriptor.depthStencilFormat = GraphicsFormat.None;
                maskDescriptor.msaaSamples = 1;
                maskDescriptor.graphicsFormat = GetSubjectMaskGraphicsFormat();
                RenderingUtils.ReAllocateIfNeeded(ref subjectMaskTexture, maskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ScreenProcessShaderConstants.SubjectMaskTextureName);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!HasActiveRuntimeLayers() || cameraColorTarget == null || tempTextureA == null || tempTextureB == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            bool maskInputsAvailable = true;
            int writtenLayerCount = 0;
            var queryErrors = new List<string>();
            using (new ProfilingScope(cmd, screenProcessProfilingSampler))
            {
                RenderSubjectMask(context, cmd, ref renderingData);

                RTHandle source = cameraColorTarget;
                bool writeToA = true;

                bool hasWritten = false;
                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    if (!IsRuntimeLayerActive(runtimeLayer))
                    {
                        continue;
                    }

                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    float dynamicFocusDistance = ResolveDepthOfFieldFocusDistance(runtimeLayer.settings, renderingData.cameraData.camera);
                    ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material, dynamicFocusDistance);
                    bool published = HoAttributeCompositePass.IsCompatibilityPublished(renderingData.cameraData.camera);
                    Vector4 flags = published ? HoAttributeCompositePass.CompatibilityInputFlags : Vector4.zero;
                    HoACQueryDescriptor query = runtimeLayer.settings.useMask || runtimeLayer.settings.debugMask ? runtimeLayer.maskQuery :
                        HoACQueryDescriptor.Resolve(HoACQueryKind.TotalCoverage, null, 0, HoACMaskDomain.Screen);
                    string queryError = query.DescribeMissingInput(published, flags);
                    bool queryValid = queryError == null;
                    bool needsMask = EffectRequiresSubjectMask(runtimeLayer.settings.effect) ||
                        runtimeLayer.settings.effect == ScreenProcessEffect.EdgeLight ||
                        runtimeLayer.settings.effect == ScreenProcessEffect.PostLighting ||
                        runtimeLayer.settings.useMask || runtimeLayer.settings.debugMask;
                    if (needsMask && !queryValid)
                    {
                        maskInputsAvailable = false;
                        queryErrors.Add(runtimeLayer.settings.name + ": " + queryError);
                    }
                    cmd.SetGlobalFloat(ScreenProcessShaderConstants.MaskValidId, queryValid ? 1 : 0);
                    cmd.SetGlobalFloat(ScreenProcessShaderConstants.CoverageValidId, flags.x > 0.5f ? 1 : 0);
                    cmd.SetGlobalVector(ScreenProcessShaderConstants.MaskTexelSizeId, new Vector4(
                        1f / renderingData.cameraData.cameraTargetDescriptor.width,
                        1f / renderingData.cameraData.cameraTargetDescriptor.height,
                        renderingData.cameraData.cameraTargetDescriptor.width, renderingData.cameraData.cameraTargetDescriptor.height));
                    runtimeLayer.material.SetVector(ScreenProcessShaderConstants.MaskQueryId, query.ShaderValue);
                    if (EffectRequiresSubjectMask(runtimeLayer.settings.effect))
                    {
                        bool hasSubjectMask = subjectMaskTexture != null && subjectMaskMaterial != null;
                        runtimeLayer.material.SetFloat(ScreenProcessShaderConstants.SubjectMaskValidId, hasSubjectMask ? 1.0f : 0.0f);
                        runtimeLayer.material.SetTexture(
                            ScreenProcessShaderConstants.SubjectMaskTextureId,
                            hasSubjectMask ? subjectMaskTexture : Texture2D.blackTexture);
                    }

                    Blitter.BlitCameraTexture(cmd, source, destination, runtimeLayer.material, Mathf.Max(0, runtimeLayer.settings.passIndex));
                    source = destination;
                    writeToA = !writeToA;
                    hasWritten = true;
                    writtenLayerCount++;
                }

                if (hasWritten)
                {
                    Blitter.BlitCameraTexture(cmd, source, cameraColorTarget, 0, true);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            ScreenProcessRuntimeDiagnostics.PublishRenderGraphInputs(renderingData.cameraData.camera, "Compatibility",
                ScreenProcessRuntimeDiagnostics.AnalyzeRequirements(runtimeLayers), writtenLayerCount, false, true,
                maskInputsAvailable, HoGeometryBufferPass.CompatibilityTargets != null,
                HoGeometryBufferPass.CompatibilityTargets != null && HoGeometryBufferPass.CompatibilityTargets.SkyTexture != null,
                string.Join("\n", queryErrors));
        }

        private void RenderSubjectMask(ScriptableRenderContext context, CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!RequiresSubjectMask() || subjectMaskMaterial == null || subjectMaskTexture == null)
            {
                return;
            }

            if (CanUseDepthTarget(subjectMaskTexture, cameraDepthTarget))
            {
                CoreUtils.SetRenderTarget(cmd, subjectMaskTexture, cameraDepthTarget, ClearFlag.Color, Color.clear);
            }
            else
            {
                CoreUtils.SetRenderTarget(cmd, subjectMaskTexture, ClearFlag.Color, Color.clear);
            }

            DrawingSettings drawingSettings = CreateDrawingSettings(SubjectMaskShaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
            drawingSettings.overrideMaterial = subjectMaskMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref subjectMaskFilteringSettings, ref subjectMaskRenderStateBlock);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!HasActiveRuntimeLayers())
            {
                ScreenProcessRuntimeDiagnostics.PublishSkipped(
                    cameraData.camera,
                    "RenderGraph",
                    "没有可运行的 ScreenProcess layer。");
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            ScreenProcessRuntimeResourceRequirements requirements = ScreenProcessRuntimeDiagnostics.AnalyzeRequirements(runtimeLayers);
            if (resourceData.isActiveTargetBackBuffer)
            {
                ScreenProcessRuntimeDiagnostics.PublishRenderGraphInputs(
                    cameraData.camera,
                    "Stack",
                    requirements,
                    0,
                    true,
                    false,
                    false,
                    false,
                    false);
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                ScreenProcessRuntimeDiagnostics.PublishRenderGraphInputs(
                    cameraData.camera,
                    "Stack",
                    requirements,
                    0,
                    false,
                    false,
                    false,
                    false,
                    false);
                return;
            }

            // 遮罩来源 = OB 的四层身份覆盖率（AC 门面读的就是它）：MB 的 maskId 在 SP 这条链上退出。
            HoAttributeCompositeRenderGraphResources acResources = frameData.GetOrCreate<HoAttributeCompositeRenderGraphResources>();
            TextureHandle maskSourceTexture = acResources.identityCoverageTexture;
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            bool useSubjectMask = RequiresSubjectMask() && subjectMaskMaterial != null;
            TextureHandle subjectMaskTexture = default;
            if (useSubjectMask)
            {
                TextureDesc subjectMaskDesc = renderGraph.GetTextureDesc(source);
                subjectMaskDesc.name = ScreenProcessShaderConstants.SubjectMaskTextureName;
                subjectMaskDesc.depthBufferBits = 0;
                subjectMaskDesc.clearBuffer = true;
                subjectMaskDesc.clearColor = Color.clear;
                GraphicsFormat subjectMaskFormat = GetSubjectMaskGraphicsFormat();
                if (subjectMaskFormat != GraphicsFormat.None)
                {
                    subjectMaskDesc.format = subjectMaskFormat;
                }

                subjectMaskTexture = renderGraph.CreateTexture(subjectMaskDesc);
                DrawingSettings subjectMaskDrawingSettings = RenderingUtils.CreateDrawingSettings(
                    SubjectMaskShaderTagIds,
                    frameData.Get<UniversalRenderingData>(),
                    cameraData,
                    frameData.Get<UniversalLightData>(),
                    SortingCriteria.CommonOpaque);
                subjectMaskDrawingSettings.overrideMaterial = subjectMaskMaterial;
                subjectMaskDrawingSettings.overrideMaterialPassIndex = 0;
                RendererListParams subjectMaskRendererListParams = new RendererListParams(
                    frameData.Get<UniversalRenderingData>().cullResults,
                    subjectMaskDrawingSettings,
                    subjectMaskFilteringSettings);

                using (var builder = renderGraph.AddRasterRenderPass<SubjectMaskPassData>(
                    "Ho-ScreenProcess Subject Mask",
                    out SubjectMaskPassData subjectMaskPassData,
                    screenProcessProfilingSampler))
                {
                    subjectMaskPassData.rendererList = renderGraph.CreateRendererList(subjectMaskRendererListParams);
                    if (subjectMaskPassData.rendererList.IsValid())
                    {
                        builder.UseRendererList(subjectMaskPassData.rendererList);
                    }
                    builder.SetRenderAttachment(subjectMaskTexture, 0, AccessFlags.WriteAll);
                    if (resourceData.activeDepthTexture.IsValid())
                    {
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                    }

                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SubjectMaskPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                        if (data.rendererList.IsValid())
                        {
                            context.cmd.DrawRendererList(data.rendererList);
                        }
                    });
                }
            }

            int writtenLayerCount = 0;
            var queryErrors = new List<string>();
            bool maskInputsAvailable = true;
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (!IsRuntimeLayerActive(runtimeLayer))
                {
                    continue;
                }

                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = $"_lilScreenProcessLayer{writtenLayerCount}";
                destinationDesc.clearBuffer = false;
                destinationDesc.depthBufferBits = 0;
                EnsureHdrTextureDesc(ref destinationDesc);
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{screenProcessPassName} Layer {writtenLayerCount}", out PassData passData, screenProcessProfilingSampler))
                {
                    passData.source = source;
                    passData.subjectMaskTexture = subjectMaskTexture;
                    passData.outlineNormalDepthTexture = geometryResources.outlineNormalDepthTexture;
                    passData.normalDepthTexture = geometryResources.normalDepthTexture;
                    passData.skyTexture = geometryResources.skyTexture;
                    passData.layer = runtimeLayer.settings;
                    passData.material = runtimeLayer.material;
                    passData.passIndex = Mathf.Max(0, runtimeLayer.settings.passIndex);
                    passData.dynamicFocusDistance = ResolveDepthOfFieldFocusDistance(runtimeLayer.settings, cameraData.camera);
                    passData.isEdgeLight = runtimeLayer.settings.effect == ScreenProcessEffect.EdgeLight;
                    passData.isDropShadow = runtimeLayer.settings.effect == ScreenProcessEffect.DropShadow;
                    passData.isOutline = runtimeLayer.settings.effect == ScreenProcessEffect.Outline;
                    passData.isDepthOfField = runtimeLayer.settings.effect == ScreenProcessEffect.DepthOfField;
                    passData.isPostLighting = runtimeLayer.settings.effect == ScreenProcessEffect.PostLighting;
                    passData.isSkyTyndall = runtimeLayer.settings.effect == ScreenProcessEffect.SkyTyndall;
                    passData.isDepthFog = runtimeLayer.settings.effect == ScreenProcessEffect.DepthFog;
                    bool needsMask = passData.isEdgeLight || passData.isDropShadow || passData.isPostLighting || runtimeLayer.settings.useMask || runtimeLayer.settings.debugMask;
                    passData.acResources = acResources;
                    passData.maskQuery = runtimeLayer.maskQuery;
                    passData.intrinsicCoverage = passData.isEdgeLight || passData.isDropShadow || passData.isPostLighting;
                    bool optionalMask = runtimeLayer.settings.useMask || runtimeLayer.settings.debugMask;
                    passData.intrinsicCoverage = passData.intrinsicCoverage && !optionalMask;
                    if (!optionalMask)
                        passData.maskQuery = HoACQueryDescriptor.Resolve(HoACQueryKind.TotalCoverage, null, 0, HoACMaskDomain.Screen);
                    string queryError = optionalMask ? acResources.DescribeMissingInput(passData.maskQuery) : null;
                    if (queryError != null) queryErrors.Add(runtimeLayer.settings.name + ": " + queryError);
                    passData.useMaskTexture = needsMask && (optionalMask ? queryError == null : acResources.published && acResources.HasIdentityPool);
                    if (needsMask && (!passData.useMaskTexture ||
                        (passData.intrinsicCoverage && !acResources.HasIdentityPool))) maskInputsAvailable = false;
                    passData.useNormalDepth = (passData.isEdgeLight || passData.isPostLighting || passData.isSkyTyndall || passData.isOutline || passData.isDepthOfField || passData.isDepthFog) && geometryResources.normalDepthTexture.IsValid();
                    passData.useSkyTexture = passData.isSkyTyndall && geometryResources.skyTexture.IsValid();
                    passData.useSubjectMask = passData.isDropShadow && useSubjectMask;
                    passData.useOutlineNormalDepth = passData.isDepthOfField && geometryResources.outlineNormalDepthTexture.IsValid();
                    TextureHandle queryTexture = passData.maskQuery.NeedsSelection ? acResources.selectionTextures[0] :
                        passData.maskQuery.NeedsGeometry ? acResources.geometryNormalDepthTexture :
                        passData.maskQuery.NeedsOutline ? acResources.outlineNormalDepthTexture : maskSourceTexture;
                    passData.maskTexelSize = ResolveMaskTexelSize(renderGraph, queryTexture.IsValid() ? queryTexture : source);

                    builder.UseTexture(source, AccessFlags.Read);
                    if (passData.useMaskTexture)
                    {
                        HoAttributeCompositeBindings.ReadQuery(builder, acResources, passData.maskQuery, passData.intrinsicCoverage);
                    }

                    if (passData.useNormalDepth)
                    {
                        builder.UseTexture(geometryResources.normalDepthTexture, AccessFlags.Read);
                    }

                    if (passData.useSkyTexture)
                    {
                        builder.UseTexture(geometryResources.skyTexture, AccessFlags.Read);
                    }

                    if (passData.useSubjectMask)
                    {
                        builder.UseTexture(subjectMaskTexture, AccessFlags.Read);
                    }

                    if (passData.useOutlineNormalDepth)
                    {
                        builder.UseTexture(geometryResources.outlineNormalDepthTexture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ApplyLayerProperties(data.layer, data.material, data.dynamicFocusDistance);
                        data.material.SetVector(ScreenProcessShaderConstants.MaskQueryId, data.maskQuery.ShaderValue);
                        context.cmd.SetGlobalFloat(ScreenProcessShaderConstants.MaskValidId, data.useMaskTexture ? 1 : 0);
                        context.cmd.SetGlobalFloat(ScreenProcessShaderConstants.CoverageValidId,
                            data.acResources.published && data.acResources.HasIdentityPool ? 1 : 0);
                        if (data.useMaskTexture)
                            HoAttributeCompositeBindings.BindQuery(context.cmd, data.acResources, data.maskQuery, data.intrinsicCoverage);
                        context.cmd.SetGlobalVector(ScreenProcessShaderConstants.MaskTexelSizeId, data.maskTexelSize);
                        context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, data.useNormalDepth ? 1.0f : 0.0f);
                        context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 0.0f);
                        context.cmd.SetGlobalFloat(ScreenProcessShaderConstants.SubjectMaskValidId, data.useSubjectMask ? 1.0f : 0.0f);
                        if (data.useSubjectMask)
                        {
                            context.cmd.SetGlobalTexture(ScreenProcessShaderConstants.SubjectMaskTextureId, data.subjectMaskTexture);
                        }

                        if (data.useNormalDepth)
                        {
                            context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                        }

                        if (data.useOutlineNormalDepth)
                        {
                            context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, data.outlineNormalDepthTexture);
                        }

                        if ((data.isEdgeLight || data.isPostLighting) && !data.layer.debugMask && !data.useNormalDepth)
                            context.cmd.SetGlobalFloat(ScreenProcessShaderConstants.MaskValidId, 0);
                        if (data.isSkyTyndall)
                        {
                            context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, data.useSkyTexture ? 1 : 0);
                            if (data.useSkyTexture)
                                context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, data.skyTexture);
                        }

                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                    });
                }

                source = destination;
                writtenLayerCount++;
            }

            if (writtenLayerCount > 0)
            {
                resourceData.cameraColor = source;
            }

            ScreenProcessRuntimeDiagnostics.PublishRenderGraphInputs(
                cameraData.camera,
                "Stack",
                requirements,
                writtenLayerCount,
                false,
                true,
                maskInputsAvailable,
                geometryResources.normalDepthTexture.IsValid(),
                geometryResources.skyTexture.IsValid(),
                string.Join("\n", queryErrors));
        }

        private static Vector4 ResolveMaskTexelSize(RenderGraph renderGraph, TextureHandle maskTexture)
        {
            if (!maskTexture.IsValid())
            {
                return Vector4.zero;
            }

            TextureDesc descriptor = renderGraph.GetTextureDesc(maskTexture);
            float width = Mathf.Max(1, descriptor.width);
            float height = Mathf.Max(1, descriptor.height);
            return new Vector4(1.0f / width, 1.0f / height, width, height);
        }

        private void CopyLayers(List<ScreenProcessRuntimeLayer> layers)
        {
            runtimeLayers.Clear();
            if (layers == null)
            {
                return;
            }

            runtimeLayers.AddRange(layers);
        }

        private void ConfigurePass(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
            ScriptableRenderPassInput input = ScriptableRenderPassInput.Color;
            if (RequiresDepth())
            {
                input |= ScriptableRenderPassInput.Depth;
            }

            if (RequiresNormals())
            {
                input |= ScriptableRenderPassInput.Normal;
            }

            ConfigureInput(input);
        }

        private void ConfigureSubjectMaskFiltering()
        {
            int minQueue = settings != null ? settings.subjectMinRenderQueue : 0;
            int maxQueue = settings != null ? settings.subjectMaxRenderQueue : (int)RenderQueue.GeometryLast;
            if (maxQueue < minQueue)
            {
                maxQueue = minQueue;
            }

            RenderQueueRange renderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = maxQueue
            };

            int layerMask = settings != null ? settings.subjectLayerMask.value : -1;
            subjectMaskFilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
        }

        private bool RequiresDepth()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (!IsRuntimeLayerActive(runtimeLayer))
                {
                    continue;
                }

                ScreenProcessEffect effect = runtimeLayer.settings.effect;
                if (effect == ScreenProcessEffect.Outline || effect == ScreenProcessEffect.DepthOfField ||
                    effect == ScreenProcessEffect.DepthFog || EffectRequiresSubjectMask(effect))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RequiresNormals()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (IsRuntimeLayerActive(runtimeLayer) && runtimeLayer.settings.effect == ScreenProcessEffect.Outline)
                {
                    return true;
                }
            }

            return false;
        }

        private bool RequiresSubjectMask()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                ScreenProcessLayer layer = runtimeLayer != null ? runtimeLayer.settings : null;
                if (IsLayerActive(layer) && EffectRequiresSubjectMask(layer.effect))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasActiveRuntimeLayers()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                if (IsRuntimeLayerActive(runtimeLayers[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EffectRequiresSubjectMask(ScreenProcessEffect effect)
        {
            return effect == ScreenProcessEffect.DropShadow;
        }

        private static bool IsRuntimeLayerActive(ScreenProcessRuntimeLayer runtimeLayer)
        {
            return runtimeLayer != null && runtimeLayer.material != null && IsLayerActive(runtimeLayer.settings);
        }

        private static bool IsLayerActive(ScreenProcessLayer layer)
        {
            return layer != null && layer.IsActive;
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
            return SystemInfo.IsFormatSupported(preferredFormat, GraphicsFormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

        private static GraphicsFormat GetSubjectMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8_UNorm;
            return SystemInfo.IsFormatSupported(preferredFormat, GraphicsFormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

        private static bool CanUseDepthTarget(RTHandle colorTarget, RTHandle depthTarget)
        {
            RenderTexture color = colorTarget != null ? colorTarget.rt : null;
            RenderTexture depth = depthTarget != null ? depthTarget.rt : null;
            if (color == null || depth == null)
            {
                return false;
            }

            return color.width == depth.width &&
                   color.height == depth.height &&
                   color.volumeDepth == depth.volumeDepth &&
                   color.antiAliasing == depth.antiAliasing;
        }

        private static void ApplyLayerProperties(ScreenProcessLayer layer, Material material, float dynamicFocusDistance = -1.0f)
        {
            if (layer.materialOverride != null) material.CopyPropertiesFromMaterial(layer.materialOverride);
            material.SetFloat(ScreenProcessShaderConstants.IntensityId, layer.intensity);
            material.SetFloat(ScreenProcessShaderConstants.LayerBlendModeId, (float)layer.blendMode);
            material.SetColor(ScreenProcessShaderConstants.LayerColorId, layer.color);
            material.SetFloat(ScreenProcessShaderConstants.LayerTextureEnabledId, layer.texture != null ? 1.0f : 0.0f);
            Vector4 parameters0 = layer.parameters0;
            if (dynamicFocusDistance > 0.0f)
            {
                parameters0.y = dynamicFocusDistance;
            }

            material.SetVector(ScreenProcessShaderConstants.LayerParams0Id, parameters0);
            material.SetVector(ScreenProcessShaderConstants.LayerParams1Id, layer.parameters1);
            material.SetVector(ScreenProcessShaderConstants.LayerParams2Id, layer.parameters2);
            material.SetVector(ScreenProcessShaderConstants.LayerParams3Id, layer.parameters3);
            material.SetVector(ScreenProcessShaderConstants.LayerParams4Id, layer.parameters4);
            material.SetVector(ScreenProcessShaderConstants.LayerParams5Id, layer.parameters5);
            material.SetFloat(ScreenProcessShaderConstants.LayerMaskEnabledId, layer.useMask ? 1.0f : 0.0f);
            material.SetFloat(ScreenProcessShaderConstants.LayerMaskInvertId, layer.invertMask ? 1.0f : 0.0f);
            material.SetFloat(ScreenProcessShaderConstants.LayerMaskDebugOutputId, layer.debugMask ? 1.0f : 0.0f);
            material.SetFloat(ScreenProcessShaderConstants.SubjectMaskValidId, 0.0f);
            if (layer.texture != null)
            {
                material.SetTexture(ScreenProcessShaderConstants.LayerTextureId, layer.texture);
            }
        }

        private static float ResolveDepthOfFieldFocusDistance(ScreenProcessLayer layer, Camera camera)
        {
            if (layer == null ||
                layer.effect != ScreenProcessEffect.DepthOfField ||
                camera == null ||
                Mathf.RoundToInt(layer.parameters0.x) != 2)
            {
                return -1.0f;
            }

            Transform target = ResolveDepthOfFieldFocusTarget(layer);
            if (target == null)
            {
                return -1.0f;
            }

            Transform cameraTransform = camera.transform;
            Vector3 targetPosition = ResolveDepthOfFieldFocusTargetPosition(target);
            float distance = Vector3.Dot(targetPosition - cameraTransform.position, cameraTransform.forward);
            return Mathf.Max(0.001f, distance + layer.depthOfFieldFocusOffset);
        }

        private static Transform ResolveDepthOfFieldFocusTarget(ScreenProcessLayer layer)
        {
            if (layer.depthOfFieldFocusTarget != null)
            {
                return layer.depthOfFieldFocusTarget;
            }

            if (string.IsNullOrEmpty(layer.depthOfFieldFocusTargetPath))
            {
                return null;
            }

            GameObject target = GameObject.Find(layer.depthOfFieldFocusTargetPath);
            return target != null ? target.transform : null;
        }

        private static Vector3 ResolveDepthOfFieldFocusTargetPosition(Transform target)
        {
            Renderer renderer = target.GetComponentInChildren<Renderer>();
            return renderer != null ? renderer.bounds.center : target.position;
        }
    }
}
