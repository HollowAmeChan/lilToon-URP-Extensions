using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [DisallowMultipleRendererFeature("lilToon-HoPost Process Stack")]
    [ExecuteAlways]
    public sealed class HoPostProcessRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoPostProcessStackSettings settings = new HoPostProcessStackSettings();

        private readonly Dictionary<Shader, Material> materialCache = new Dictionary<Shader, Material>();
        private readonly HashSet<string> warnedMissingShaders = new HashSet<string>();
        private readonly List<HoPostProcessRuntimeLayer> runtimeLayers = new List<HoPostProcessRuntimeLayer>();
        private Material subjectMaskMaterial;
        private Shader subjectMaskShader;
        private bool warnedMissingSubjectMaskShader;
        private HoPostProcessPass pass;

        [Tooltip("The renderer feature installs the pass, and Volume profiles provide the active HoPost stack.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public HoPostProcessStackSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            pass = new HoPostProcessPass("lilToon-HoPost After URP Before Shoost");
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            HoPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            BuildRuntimeLayers(volume);
            SetupCompatibilityPass(pass, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle, runtimeLayers);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            BuildRuntimeLayers(volume);
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

        private bool ShouldRender(in RenderingData renderingData, HoPostProcessStackVolume volume)
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

        private void BuildRuntimeLayers(HoPostProcessStackVolume volume)
        {
            runtimeLayers.Clear();
            List<HoPostProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (HoPostProcessLayer layer in layers)
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

                runtimeLayers.Add(new HoPostProcessRuntimeLayer(layer, material));
            }
        }

        private void SetupCompatibilityPass(
            HoPostProcessPass pass,
            RTHandle cameraColorTarget,
            RTHandle cameraDepthTarget,
            List<HoPostProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            pass.Setup(cameraColorTarget, cameraDepthTarget, layers, HoPostProcessRenderPassEvents.HoPostStack, settings, EnsureSubjectMaskMaterial());
        }

        private void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            HoPostProcessPass pass,
            List<HoPostProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            pass.SetupRenderGraph(layers, HoPostProcessRenderPassEvents.HoPostStack);
            renderer.EnqueuePass(pass);
        }

        private static HoPostProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoPostProcessStackVolume>() : null;
        }

        private Material ResolveMaterial(HoPostProcessLayer layer)
        {
            if (layer.materialOverride != null)
            {
                return layer.materialOverride;
            }

            Shader shader = layer.shaderOverride;
            if (shader == null && layer.effect == HoPostProcessEffect.CustomMaterial)
            {
                shader = settings.defaultLayerShader;
            }

            string shaderName = HoPostProcessEffectRegistry.GetDefaultShaderName(layer.effect);
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
                : Shader.Find(HoPostProcessShaderConstants.SubjectMaskShaderName);

            if (subjectMaskMaterial != null && subjectMaskShader == shader)
            {
                return subjectMaskMaterial;
            }

            if (shader == null)
            {
                if (!warnedMissingSubjectMaskShader)
                {
                    warnedMissingSubjectMaskShader = true;
                    Debug.LogWarning($"HoPost Drop Shadow was skipped because shader '{HoPostProcessShaderConstants.SubjectMaskShaderName}' could not be found.");
                }

                return null;
            }

            CoreUtils.Destroy(subjectMaskMaterial);
            subjectMaskShader = shader;
            subjectMaskMaterial = CoreUtils.CreateEngineMaterial(shader);
            return subjectMaskMaterial;
        }

        private static bool ContainsSubjectMaskLayer(List<HoPostProcessRuntimeLayer> layers)
        {
            if (layers == null)
            {
                return false;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                HoPostProcessRuntimeLayer runtimeLayer = layers[i];
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

        private static bool EffectRequiresSubjectMask(HoPostProcessEffect effect)
        {
            return effect == HoPostProcessEffect.DropShadow;
        }

        private void WarnMissingShader(HoPostProcessLayer layer, string shaderName)
        {
            string key = $"{layer.effect}:{shaderName}";
            if (!warnedMissingShaders.Add(key))
            {
                return;
            }

            Debug.LogWarning($"HoPost effect '{layer.effect}' was skipped because shader '{shaderName}' could not be found.");
        }
    }

    internal sealed class HoPostProcessRuntimeLayer
    {
        public readonly HoPostProcessLayer settings;
        public readonly Material material;

        public HoPostProcessRuntimeLayer(HoPostProcessLayer settings, Material material)
        {
            this.settings = settings;
            this.material = material;
        }
    }

    internal sealed class HoPostProcessPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> SubjectMaskShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly List<HoPostProcessRuntimeLayer> runtimeLayers = new List<HoPostProcessRuntimeLayer>();
        private readonly ProfilingSampler hoPostProfilingSampler;
        private readonly string hoPostPassName;
        private RTHandle cameraColorTarget;
        private RTHandle cameraDepthTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private RTHandle subjectMaskTexture;
        private HoPostProcessStackSettings settings;
        private Material subjectMaskMaterial;
        private FilteringSettings subjectMaskFilteringSettings;
        private RenderStateBlock subjectMaskRenderStateBlock;

        private sealed class PassData
        {
            public TextureHandle source;
            public HoPostProcessLayer layer;
            public Material material;
            public int passIndex;
        }

        public HoPostProcessPass(string passName)
        {
            hoPostPassName = passName;
            hoPostProfilingSampler = new ProfilingSampler(passName);
            subjectMaskRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            RTHandle cameraColorTarget,
            RTHandle cameraDepthTarget,
            List<HoPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent,
            HoPostProcessStackSettings settings,
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
            List<HoPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            this.cameraColorTarget = null;
            this.cameraDepthTarget = null;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            tempTextureA?.Release();
            tempTextureB?.Release();
            subjectMaskTexture?.Release();
            tempTextureA = null;
            tempTextureB = null;
            subjectMaskTexture = null;
            runtimeLayers.Clear();
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
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoPostProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoPostProcessShaderConstants.TempTextureBName);

            if (RequiresSubjectMask() && subjectMaskMaterial != null)
            {
                RenderTextureDescriptor maskDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                maskDescriptor.depthBufferBits = 0;
                maskDescriptor.depthStencilFormat = GraphicsFormat.None;
                maskDescriptor.msaaSamples = 1;
                maskDescriptor.graphicsFormat = GetSubjectMaskGraphicsFormat();
                RenderingUtils.ReAllocateIfNeeded(ref subjectMaskTexture, maskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoPostProcessShaderConstants.SubjectMaskTextureName);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!HasActiveRuntimeLayers() || cameraColorTarget == null || tempTextureA == null || tempTextureB == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, hoPostProfilingSampler))
            {
                RenderSubjectMask(context, cmd, ref renderingData);

                RTHandle source = cameraColorTarget;
                bool writeToA = true;

                bool hasWritten = false;
                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    if (!IsRuntimeLayerActive(runtimeLayer))
                    {
                        continue;
                    }

                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material);
                    if (EffectRequiresSubjectMask(runtimeLayer.settings.effect))
                    {
                        bool hasSubjectMask = subjectMaskTexture != null && subjectMaskMaterial != null;
                        runtimeLayer.material.SetFloat(HoPostProcessShaderConstants.SubjectMaskValidId, hasSubjectMask ? 1.0f : 0.0f);
                        runtimeLayer.material.SetTexture(
                            HoPostProcessShaderConstants.SubjectMaskTextureId,
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
            if (!HasActiveRuntimeLayers())
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            int writtenLayerCount = 0;
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (!IsRuntimeLayerActive(runtimeLayer))
                {
                    continue;
                }

                if (EffectRequiresSubjectMask(runtimeLayer.settings.effect))
                {
                    continue;
                }

                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = $"_lilHoPostProcessLayer{writtenLayerCount}";
                destinationDesc.clearBuffer = false;
                destinationDesc.depthBufferBits = 0;
                EnsureHdrTextureDesc(ref destinationDesc);
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{hoPostPassName} Layer {writtenLayerCount}", out PassData passData, hoPostProfilingSampler))
                {
                    passData.source = source;
                    passData.layer = runtimeLayer.settings;
                    passData.material = runtimeLayer.material;
                    passData.passIndex = Mathf.Max(0, runtimeLayer.settings.passIndex);

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ApplyLayerProperties(data.layer, data.material);
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
        }

        private void CopyLayers(List<HoPostProcessRuntimeLayer> layers)
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
                HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
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
                HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (!IsRuntimeLayerActive(runtimeLayer))
                {
                    continue;
                }

                HoPostProcessEffect effect = runtimeLayer.settings.effect;
                if (effect == HoPostProcessEffect.Outline || EffectRequiresSubjectMask(effect))
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
                HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                HoPostProcessLayer layer = runtimeLayer != null ? runtimeLayer.settings : null;
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

        private static bool EffectRequiresSubjectMask(HoPostProcessEffect effect)
        {
            return effect == HoPostProcessEffect.DropShadow;
        }

        private static bool RequiresCameraNormals(HoPostProcessEffect effect)
        {
            return effect == HoPostProcessEffect.EdgeLight || effect == HoPostProcessEffect.Outline;
        }

        private static bool IsRuntimeLayerActive(HoPostProcessRuntimeLayer runtimeLayer)
        {
            return runtimeLayer != null && runtimeLayer.material != null && IsLayerActive(runtimeLayer.settings);
        }

        private static bool IsLayerActive(HoPostProcessLayer layer)
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
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

        private static GraphicsFormat GetSubjectMaskGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R8_UNorm;
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
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

        private static void ApplyLayerProperties(HoPostProcessLayer layer, Material material)
        {
            material.SetFloat(HoPostProcessShaderConstants.IntensityId, layer.intensity);
            material.SetFloat(HoPostProcessShaderConstants.LayerBlendModeId, (float)layer.blendMode);
            material.SetColor(HoPostProcessShaderConstants.LayerColorId, layer.color);
            material.SetFloat(HoPostProcessShaderConstants.LayerTextureEnabledId, layer.texture != null ? 1.0f : 0.0f);
            material.SetVector(HoPostProcessShaderConstants.LayerParams0Id, layer.parameters0);
            material.SetVector(HoPostProcessShaderConstants.LayerParams1Id, layer.parameters1);
            material.SetVector(HoPostProcessShaderConstants.LayerParams2Id, layer.parameters2);
            material.SetVector(HoPostProcessShaderConstants.LayerParams3Id, layer.parameters3);
            material.SetVector(HoPostProcessShaderConstants.LayerParams4Id, layer.parameters4);
            material.SetVector(HoPostProcessShaderConstants.LayerParams5Id, layer.parameters5);
            material.SetFloat(HoPostProcessShaderConstants.SubjectMaskValidId, 0.0f);
            if (layer.texture != null)
            {
                material.SetTexture(HoPostProcessShaderConstants.LayerTextureId, layer.texture);
            }
        }
    }
}
