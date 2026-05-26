using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.MetadataBuffer;
using lilToon.URP.Extensions.GeometryBuffer;
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

        private readonly Dictionary<Shader, Material> materialCache = new Dictionary<Shader, Material>();
        private readonly HashSet<string> warnedMissingShaders = new HashSet<string>();
        private readonly List<ScreenProcessRuntimeLayer> runtimeLayers = new List<ScreenProcessRuntimeLayer>();
        private Material subjectMaskMaterial;
        private Shader subjectMaskShader;
        private bool warnedMissingSubjectMaskShader;
        private ScreenProcessPass pass;

        [Tooltip("The renderer feature installs the pass, and Volume profiles provide the active ScreenProcess stack.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public ScreenProcessStackSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            pass = new ScreenProcessPass("Ho-ScreenProcess AfterURP BeforeImageProcess");
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ScreenProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
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
            pass?.Dispose();
            pass = null;

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
            List<ScreenProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (ScreenProcessLayer layer in layers)
            {
                if (layer == null || !layer.IsActive)
                {
                    continue;
                }

                Material material = ResolveMaterial(layer);
                if (material == null)
                {
                    continue;
                }

                runtimeLayers.Add(new ScreenProcessRuntimeLayer(layer, material));
            }
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

            pass.Setup(cameraColorTarget, cameraDepthTarget, layers, ScreenProcessRenderPassEvents.ScreenProcessStack, settings, null);
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
                return;
            }

            pass.SetupRenderGraph(layers, ScreenProcessRenderPassEvents.ScreenProcessStack);
            renderer.EnqueuePass(pass);
        }

        private static ScreenProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<ScreenProcessStackVolume>() : null;
        }

        private Material ResolveMaterial(ScreenProcessLayer layer)
        {
            if (layer.materialOverride != null)
            {
                return layer.materialOverride;
            }

            Shader shader = layer.shaderOverride;
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

            if (materialCache.TryGetValue(shader, out Material material) && material != null)
            {
                return material;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            materialCache[shader] = material;
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

        public ScreenProcessRuntimeLayer(ScreenProcessLayer settings, Material material)
        {
            this.settings = settings;
            this.material = material;
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
            public TextureHandle ruleMaskIdTexture;
            public TextureHandle ruleNormalDepthTexture;
            public TextureHandle ruleSurfaceDataTexture;
            public TextureHandle ruleCustom0Texture;
            public TextureHandle ruleObjectCustom0Texture;
            public TextureHandle ruleObjectCustom1Texture;
            public ScreenProcessLayer layer;
            public Material material;
            public int passIndex;
            public float dynamicFocusDistance;
            public bool isEdgeLight;
            public bool isDropShadow;
            public bool isPostLighting;
            public bool useRuleMaskTexture;
            public bool useRuleNormalDepth;
            public bool useRuleSurfaceData;
            public bool useRuleCustom0;
            public bool useRuleObjectCustom0;
            public bool useRuleObjectCustom1;
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
            RenderPassEvent passEvent)
        {
            ReleaseCompatibilityResources();
            CopyLayers(layers);
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
                }

                if (hasWritten)
                {
                    Blitter.BlitCameraTexture(cmd, source, cameraColorTarget, 0, true);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
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
                    false,
                    false,
                    false,
                    false);
                return;
            }

            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            int writtenLayerCount = 0;
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
                    passData.ruleMaskIdTexture = metadataResources.maskIdTexture;
                    passData.ruleNormalDepthTexture = geometryResources.normalDepthTexture;
                    passData.ruleSurfaceDataTexture = metadataResources.surfaceDataTexture;
                    passData.ruleCustom0Texture = metadataResources.custom0Texture;
                    passData.ruleObjectCustom0Texture = metadataResources.objectCustom0Texture;
                    passData.ruleObjectCustom1Texture = metadataResources.objectCustom1Texture;
                    passData.layer = runtimeLayer.settings;
                    passData.material = runtimeLayer.material;
                    passData.passIndex = Mathf.Max(0, runtimeLayer.settings.passIndex);
                    passData.dynamicFocusDistance = ResolveDepthOfFieldFocusDistance(runtimeLayer.settings, cameraData.camera);
                    passData.isEdgeLight = runtimeLayer.settings.effect == ScreenProcessEffect.EdgeLight;
                    passData.isDropShadow = runtimeLayer.settings.effect == ScreenProcessEffect.DropShadow;
                    passData.isPostLighting = runtimeLayer.settings.effect == ScreenProcessEffect.PostLighting;
                    bool needsRule = passData.isEdgeLight || passData.isDropShadow || passData.isPostLighting || runtimeLayer.settings.useRuleMask || runtimeLayer.settings.debugRuleMask;
                    bool needsRuleMaskResolve = passData.isDropShadow || runtimeLayer.settings.useRuleMask || runtimeLayer.settings.debugRuleMask;
                    passData.useRuleMaskTexture = needsRule && metadataResources.maskIdTexture.IsValid();
                    passData.useRuleNormalDepth = (passData.isEdgeLight || passData.isPostLighting) && geometryResources.normalDepthTexture.IsValid();
                    passData.useRuleSurfaceData = needsRuleMaskResolve && metadataResources.surfaceDataTexture.IsValid();
                    passData.useRuleCustom0 = needsRuleMaskResolve && metadataResources.custom0Texture.IsValid();
                    passData.useRuleObjectCustom0 = needsRuleMaskResolve && metadataResources.objectCustom0Texture.IsValid();
                    passData.useRuleObjectCustom1 = needsRuleMaskResolve && metadataResources.objectCustom1Texture.IsValid();

                    builder.UseTexture(source, AccessFlags.Read);
                    if (passData.useRuleMaskTexture)
                    {
                        builder.UseTexture(metadataResources.maskIdTexture, AccessFlags.Read);
                    }

                    if (passData.useRuleNormalDepth)
                    {
                        builder.UseTexture(geometryResources.normalDepthTexture, AccessFlags.Read);
                    }

                    if (passData.useRuleSurfaceData)
                    {
                        builder.UseTexture(metadataResources.surfaceDataTexture, AccessFlags.Read);
                    }

                    if (passData.useRuleCustom0)
                    {
                        builder.UseTexture(metadataResources.custom0Texture, AccessFlags.Read);
                    }

                    if (passData.useRuleObjectCustom0)
                    {
                        builder.UseTexture(metadataResources.objectCustom0Texture, AccessFlags.Read);
                    }

                    if (passData.useRuleObjectCustom1)
                    {
                        builder.UseTexture(metadataResources.objectCustom1Texture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ApplyLayerProperties(data.layer, data.material, data.dynamicFocusDistance);
                        if (data.isEdgeLight)
                        {
                            bool hasRule = data.useRuleMaskTexture && data.useRuleNormalDepth;
                            context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, hasRule ? 1.0f : 0.0f);
                            if (hasRule)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.ruleMaskIdTexture);
                                context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.ruleNormalDepthTexture);
                            }

                            if (data.layer.useRuleMask || data.layer.debugRuleMask)
                            {
                                if (data.useRuleSurfaceData)
                                {
                                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.ruleSurfaceDataTexture);
                                }

                                if (data.useRuleCustom0)
                                {
                                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.ruleCustom0Texture);
                                }

                                if (data.useRuleObjectCustom0)
                                {
                                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.ruleObjectCustom0Texture);
                                }

                                if (data.useRuleObjectCustom1)
                                {
                                    context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.ruleObjectCustom1Texture);
                                }
                            }
                        }
                        else if (data.isPostLighting)
                        {
                            bool hasRule = data.useRuleMaskTexture && data.useRuleNormalDepth;
                            context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, hasRule ? 1.0f : 0.0f);
                            if (hasRule)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.ruleMaskIdTexture);
                                context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.ruleNormalDepthTexture);
                            }

                            if (data.useRuleSurfaceData)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.ruleSurfaceDataTexture);
                            }

                            if (data.useRuleCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.ruleCustom0Texture);
                            }

                            if (data.useRuleObjectCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.ruleObjectCustom0Texture);
                            }

                            if (data.useRuleObjectCustom1)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.ruleObjectCustom1Texture);
                            }
                        }
                        else if (data.isDropShadow)
                        {
                            context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, data.useRuleMaskTexture ? 1.0f : 0.0f);
                            if (data.useRuleMaskTexture)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.ruleMaskIdTexture);
                            }

                            if (data.useRuleSurfaceData)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.ruleSurfaceDataTexture);
                            }

                            if (data.useRuleCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.ruleCustom0Texture);
                            }

                            if (data.useRuleObjectCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.ruleObjectCustom0Texture);
                            }

                            if (data.useRuleObjectCustom1)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.ruleObjectCustom1Texture);
                            }
                        }
                        else if (data.layer.useRuleMask || data.layer.debugRuleMask)
                        {
                            context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, data.useRuleMaskTexture ? 1.0f : 0.0f);
                            if (data.useRuleMaskTexture)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.ruleMaskIdTexture);
                            }

                            if (data.useRuleSurfaceData)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, data.ruleSurfaceDataTexture);
                            }

                            if (data.useRuleCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.ruleCustom0Texture);
                            }

                            if (data.useRuleObjectCustom0)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, data.ruleObjectCustom0Texture);
                            }

                            if (data.useRuleObjectCustom1)
                            {
                                context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, data.ruleObjectCustom1Texture);
                            }
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
                metadataResources.maskIdTexture.IsValid(),
                metadataResources.surfaceDataTexture.IsValid(),
                metadataResources.custom0Texture.IsValid(),
                metadataResources.objectCustom0Texture.IsValid(),
                metadataResources.objectCustom1Texture.IsValid(),
                geometryResources.normalDepthTexture.IsValid());
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

        private bool RequiresNormals()
        {
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ScreenProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (IsRuntimeLayerActive(runtimeLayer) && RequiresCameraNormals(runtimeLayer.settings.effect))
                {
                    return true;
                }
            }

            return false;
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
                if (effect == ScreenProcessEffect.Outline || effect == ScreenProcessEffect.DepthOfField || EffectRequiresSubjectMask(effect))
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

        private static bool RequiresCameraNormals(ScreenProcessEffect effect)
        {
            return effect == ScreenProcessEffect.Outline;
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
            material.SetFloat(ScreenProcessShaderConstants.LayerRuleMaskEnabledId, layer.useRuleMask ? 1.0f : 0.0f);
            material.SetFloat(ScreenProcessShaderConstants.LayerRuleSourceId, (float)layer.ruleSource);
            material.SetFloat(ScreenProcessShaderConstants.LayerRuleModeId, (float)layer.ruleMaskMode);
            material.SetVector(
                ScreenProcessShaderConstants.LayerRuleParamsId,
                new Vector4(
                    Mathf.Max(0.0f, layer.ruleThreshold),
                    0.0f,
                    layer.ruleMatchValue,
                    layer.invertRuleMask ? 1.0f : 0.0f));
            material.SetColor(ScreenProcessShaderConstants.LayerRuleMatchColorId, layer.ruleMatchColor);
            material.SetFloat(ScreenProcessShaderConstants.LayerRuleDebugOutputId, layer.debugRuleMask ? 1.0f : 0.0f);
            ScreenProcessRuleMaskRuntime.ApplyToMaterial(
                layer,
                material,
                ScreenProcessShaderConstants.LayerRuleMaskCountId,
                ScreenProcessShaderConstants.LayerRuleMaskData0Id,
                ScreenProcessShaderConstants.LayerRuleMaskData1Id,
                ScreenProcessShaderConstants.LayerRuleMaskData2Id,
                ScreenProcessShaderConstants.LayerRuleMaskColorId);
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
