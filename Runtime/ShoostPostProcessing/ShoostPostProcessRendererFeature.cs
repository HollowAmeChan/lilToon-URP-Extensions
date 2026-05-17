using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.AOV;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [DisallowMultipleRendererFeature("lilToon-Shoost Post Process Stack")]
    [ExecuteAlways]
    public sealed class ShoostPostProcessRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private ShoostPostProcessStackSettings settings = new ShoostPostProcessStackSettings();

        private readonly Dictionary<Shader, Material> materialCache = new Dictionary<Shader, Material>();
        private readonly HashSet<string> warnedMissingShaders = new HashSet<string>();
        private readonly List<ShoostPostProcessRuntimeLayer> afterPostProcessLayers = new List<ShoostPostProcessRuntimeLayer>();
        private Material aovCompositeMaterial;
        private Shader aovCompositeShader;
        private bool warnedMissingAovCompositeShader;
        private ShoostPostProcessPass afterPostProcessPass;

        [Tooltip("Match HTrace-style setup: the renderer feature installs the pass, and Volume profiles provide the active settings.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public ShoostPostProcessStackSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            afterPostProcessPass = new ShoostPostProcessPass("lilToon-Shoost After URP Post");
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ShoostPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                afterPostProcessPass?.ClearRuntimeLayers();
                return;
            }

            BuildRuntimeLayers(volume);
            SetupCompatibilityPass(afterPostProcessPass, renderer.cameraColorTargetHandle, afterPostProcessLayers, HoPostProcessRenderPassEvents.ShoostFinalStack);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ShoostPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                afterPostProcessPass?.ClearRuntimeLayers();
                return;
            }

            BuildRuntimeLayers(volume);
            EnqueueRenderGraphPass(renderer, afterPostProcessPass, afterPostProcessLayers, HoPostProcessRenderPassEvents.ShoostFinalStack);
        }

        protected override void Dispose(bool disposing)
        {
            afterPostProcessPass?.Dispose();
            afterPostProcessPass = null;

            foreach (Material material in materialCache.Values)
            {
                CoreUtils.Destroy(material);
            }

            CoreUtils.Destroy(aovCompositeMaterial);
            aovCompositeMaterial = null;
            aovCompositeShader = null;
            materialCache.Clear();
            afterPostProcessLayers.Clear();
            warnedMissingShaders.Clear();
        }

        private bool ShouldRender(in RenderingData renderingData, ShoostPostProcessStackVolume volume)
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

        private void BuildRuntimeLayers(ShoostPostProcessStackVolume volume)
        {
            afterPostProcessLayers.Clear();
            List<ShoostPostProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (ShoostPostProcessLayer layer in layers)
            {
                if (layer == null ||
                    !layer.IsActive ||
                    IsRemovedEffectSlot(layer.effect))
                {
                    continue;
                }

                Material material = ResolveMaterial(layer);
                if (material == null)
                {
                    continue;
                }

                ShoostPostProcessRuntimeLayer runtimeLayer = new ShoostPostProcessRuntimeLayer(layer, material);
                afterPostProcessLayers.Add(runtimeLayer);
            }

            afterPostProcessLayers.Sort(CompareRuntimeLayerOrder);
        }

        private static bool IsRemovedEffectSlot(ShoostPostProcessEffect effect)
        {
            return effect == ShoostPostProcessEffect.RemovedEffectSlot13 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot30 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot31 ||
                   effect == ShoostPostProcessEffect.RemovedEffectSlot32;
        }

        private static int CompareRuntimeLayerOrder(ShoostPostProcessRuntimeLayer a, ShoostPostProcessRuntimeLayer b)
        {
            int orderA = GetRuntimeEffectOrder(a.settings.effect);
            int orderB = GetRuntimeEffectOrder(b.settings.effect);
            return orderA.CompareTo(orderB);
        }

        private static int GetRuntimeEffectOrder(ShoostPostProcessEffect effect)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.SharpenBefore: return 0;
                case ShoostPostProcessEffect.AutoWhiteBalance: return 1;
                case ShoostPostProcessEffect.LevelAdjustment: return 2;
                case ShoostPostProcessEffect.ColorGradingCustom: return 3;
                case ShoostPostProcessEffect.Gradient: return 7;
                case ShoostPostProcessEffect.Lighting: return 8;
                case ShoostPostProcessEffect.CenterColorCorrection: return 9;
                case ShoostPostProcessEffect.Kuwahara: return 10;
                case ShoostPostProcessEffect.LED: return 11;
                case ShoostPostProcessEffect.Weather: return 12;
                case ShoostPostProcessEffect.Particle: return 13;
                case ShoostPostProcessEffect.CameraSwitcher: return 14;
                case ShoostPostProcessEffect.TransparentBackground: return 15;
                case ShoostPostProcessEffect.FilmBreathGateWeave: return 16;
                case ShoostPostProcessEffect.Tube: return 17;
                case ShoostPostProcessEffect.VHS: return 18;
                case ShoostPostProcessEffect.CRTEffects: return 19;
                case ShoostPostProcessEffect.DitheringCustom: return 20;
                case ShoostPostProcessEffect.IrisBlur: return 21;
                case ShoostPostProcessEffect.RGBBlurV2: return 22;
                case ShoostPostProcessEffect.RGBSplit: return 23;
                case ShoostPostProcessEffect.RGBChannelSeparator: return 24;
                case ShoostPostProcessEffect.BokehZoomBlur: return 25;
                case ShoostPostProcessEffect.ApertureBokeh: return 26;
                case ShoostPostProcessEffect.LensFlare: return 27;
                case ShoostPostProcessEffect.Glow: return 28;
                case ShoostPostProcessEffect.ToonMap: return 29;
                case ShoostPostProcessEffect.GrainCustom: return 30;
                case ShoostPostProcessEffect.VignetteCustom: return 31;
                case ShoostPostProcessEffect.Pixelize: return 32;
                case ShoostPostProcessEffect.ChangeFrameRate: return 33;
                case ShoostPostProcessEffect.Distortion: return 34;
                case ShoostPostProcessEffect.Fisheye: return 35;
                case ShoostPostProcessEffect.CameraFlash: return 36;
                case ShoostPostProcessEffect.CustomMaterial: return 37;
                case ShoostPostProcessEffect.GateWeave: return 38;
                case ShoostPostProcessEffect.LensDistortionCustom: return 39;
                case ShoostPostProcessEffect.MotionTrail: return 40;
                case ShoostPostProcessEffect.RGBBlur: return 41;
                case ShoostPostProcessEffect.SharpenAfter: return 42;
                case ShoostPostProcessEffect.RetroLookProBleedCustom: return 43;
                case ShoostPostProcessEffect.RetroLookProNoise2Custom: return 44;
                case ShoostPostProcessEffect.RetroLookProOldFilm2Custom: return 45;
                case ShoostPostProcessEffect.RetroLookProTVEffectCustom: return 46;
                default: return int.MaxValue;
            }
        }

        private void SetupCompatibilityPass(
            ShoostPostProcessPass pass,
            RTHandle cameraColorTarget,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            pass.Setup(cameraColorTarget, layers, passEvent, EnsureAovCompositeMaterial(layers));
        }

        private void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            ShoostPostProcessPass pass,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                return;
            }

            pass.SetupRenderGraph(layers, passEvent, EnsureAovCompositeMaterial(layers));
            renderer.EnqueuePass(pass);
        }

        private Material EnsureAovCompositeMaterial(List<ShoostPostProcessRuntimeLayer> layers)
        {
            if (!ContainsAovMaskedLayer(layers))
            {
                return null;
            }

            Shader shader = Shader.Find(ShoostPostProcessShaderConstants.AovCompositeShaderName);
            if (aovCompositeMaterial != null && aovCompositeShader == shader)
            {
                return aovCompositeMaterial;
            }

            if (shader == null)
            {
                if (!warnedMissingAovCompositeShader)
                {
                    warnedMissingAovCompositeShader = true;
                    Debug.LogWarning($"Shoost AOV 遮罩已跳过：找不到 Shader '{ShoostPostProcessShaderConstants.AovCompositeShaderName}'。");
                }

                return null;
            }

            CoreUtils.Destroy(aovCompositeMaterial);
            aovCompositeShader = shader;
            aovCompositeMaterial = CoreUtils.CreateEngineMaterial(shader);
            return aovCompositeMaterial;
        }

        private static bool ContainsAovMaskedLayer(List<ShoostPostProcessRuntimeLayer> layers)
        {
            if (layers == null)
            {
                return false;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ShoostPostProcessLayer layer = layers[i]?.settings;
                if (layer != null && (layer.useAovMask || layer.debugAovMask))
                {
                    return true;
                }
            }

            return false;
        }

        private static ShoostPostProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<ShoostPostProcessStackVolume>() : null;
        }

        private Material ResolveMaterial(ShoostPostProcessLayer layer)
        {
            if (layer.materialOverride != null)
            {
                return layer.materialOverride;
            }

            Shader shader = layer.shaderOverride;
            if (shader == null && layer.effect == ShoostPostProcessEffect.CustomMaterial)
            {
                shader = settings.defaultLayerShader;
            }

            string shaderName = ShoostPostProcessEffectRegistry.GetDefaultShaderName(layer.effect);
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

        private void WarnMissingShader(ShoostPostProcessLayer layer, string shaderName)
        {
            string key = $"{layer.effect}:{shaderName}";
            if (!warnedMissingShaders.Add(key))
            {
                return;
            }

            Debug.LogWarning($"lilToon-Shoost 后处理图层 '{layer.name}' 已跳过：找不到 Shader '{shaderName}'。");
        }
    }

    internal sealed class ShoostPostProcessRuntimeLayer
    {
        public readonly ShoostPostProcessLayer settings;
        public readonly Material material;

        public ShoostPostProcessRuntimeLayer(ShoostPostProcessLayer settings, Material material)
        {
            this.settings = settings;
            this.material = material;
        }
    }

    internal sealed class ShoostPostProcessPass : ScriptableRenderPass
    {
        private readonly List<ShoostPostProcessRuntimeLayer> runtimeLayers = new List<ShoostPostProcessRuntimeLayer>();
        private readonly ProfilingSampler _profilingSampler;
        private readonly string _passName;
        private RTHandle cameraColorTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private RTHandle tempTextureC;
        private RTHandle irisTextureA;
        private RTHandle irisTextureB;
        private RTHandle rgbBlurTextureA;
        private RTHandle rgbBlurTextureB;
        private RTHandle glowTextureA;
        private RTHandle glowTextureB;
        private RTHandle apertureBokehTextureA;
        private RTHandle apertureBokehTextureB;
        private Material aovCompositeMaterial;
        private bool warnedBackBuffer;
        private readonly Dictionary<int, ChangeFrameRateState> changeFrameRateStates = new Dictionary<int, ChangeFrameRateState>();

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle originalTexture;
            public TextureHandle layerResultTexture;
            public TextureHandle aovMaskIdTexture;
            public TextureHandle aovSurfaceDataTexture;
            public TextureHandle aovCustom0Texture;
            public TextureHandle aovObjectCustom0Texture;
            public TextureHandle aovObjectCustom1Texture;
            public ShoostPostProcessLayer layer;
            public Material material;
            public int passIndex;
            public bool useAovMaskTexture;
            public bool useAovSurfaceData;
            public bool useAovCustom0;
            public bool useAovObjectCustom0;
            public bool useAovObjectCustom1;
            public float radius;
            public float screenRatio;
            public TextureHandle blurredTexture;
            public Vector2 center;
            public float centerSize;
            public float smoothness;
            public float distance;
            public float angle;
            public float blurOffsetR;
            public float blurOffsetG;
            public float blurOffsetB;
            public bool enableRgbSplit;
            public TextureHandle frozenFrameTexture;
            public TextureHandle bloomTexture;
        }

        private sealed class ChangeFrameRateState
        {
            public RTHandle frozenTexture;
            public int width;
            public int height;
            public int volumeDepth;
            public int msaaSamples;
            public TextureDimension dimension;
            public GraphicsFormat graphicsFormat;
            public bool isValid;
            public int targetFrameRate;
            public double nextUpdateTime;

            public void Release()
            {
                frozenTexture?.Release();
                frozenTexture = null;
                isValid = false;
            }
        }

        private struct IrisBlurParameters
        {
            public int resolutionType;
            public Vector2Int customResolution;
            public float radius;
            public int downScale;
            public int iterations;
            public Vector2 center;
            public float centerSize;
            public float smoothness;
            public bool enableRgbSplit;
            public float blurRadiusR;
            public float blurRadiusG;
            public float blurRadiusB;
            public float distance;
            public float angleRadians;
        }

        public ShoostPostProcessPass(string passName)
        {
            _passName = passName;
            _profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            RTHandle cameraColorTarget,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent,
            Material aovCompositeMaterial)
        {
            this.cameraColorTarget = cameraColorTarget;
            this.aovCompositeMaterial = aovCompositeMaterial;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent,
            Material aovCompositeMaterial)
        {
            this.cameraColorTarget = null;
            this.aovCompositeMaterial = aovCompositeMaterial;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            tempTextureA?.Release();
            tempTextureB?.Release();
            tempTextureC?.Release();
            tempTextureA = null;
            tempTextureB = null;
            tempTextureC = null;
            irisTextureA?.Release();
            irisTextureB?.Release();
            irisTextureA = null;
            irisTextureB = null;
            rgbBlurTextureA?.Release();
            rgbBlurTextureB?.Release();
            rgbBlurTextureA = null;
            rgbBlurTextureB = null;
            glowTextureA?.Release();
            glowTextureB?.Release();
            glowTextureA = null;
            glowTextureB = null;
            apertureBokehTextureA?.Release();
            apertureBokehTextureB?.Release();
            apertureBokehTextureA = null;
            apertureBokehTextureB = null;
            foreach (ChangeFrameRateState state in changeFrameRateStates.Values)
            {
                state.Release();
            }

            changeFrameRateStates.Clear();
            runtimeLayers.Clear();
        }

        public void ClearRuntimeLayers()
        {
            runtimeLayers.Clear();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (runtimeLayers.Count == 0)
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref descriptor);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureBName);
            if (RequiresAovComposite())
            {
                RenderingUtils.ReAllocateIfNeeded(ref tempTextureC, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureCName);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (runtimeLayers.Count == 0 || cameraColorTarget == null || tempTextureA == null || tempTextureB == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                RTHandle source = cameraColorTarget;
                bool writeToA = true;

                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    RTHandle effectDestination = RequiresAovComposite(runtimeLayer.settings) && tempTextureC != null
                        ? tempTextureC
                        : destination;

                    if (runtimeLayer.settings.effect == ShoostPostProcessEffect.IrisBlur)
                    {
                        ApplyIrisBlurLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, effectDestination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.RGBBlurV2)
                    {
                        ApplyRgbBlurV2Layer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, effectDestination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.Glow)
                    {
                        ApplyGlowLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, effectDestination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ApertureBokeh)
                    {
                        ApplyApertureBokehLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, effectDestination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ChangeFrameRate)
                    {
                        ApplyChangeFrameRateLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, renderingData.cameraData.camera, source, effectDestination, runtimeLayer);
                    }
                    else
                    {
                        ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material);
                        Blitter.BlitCameraTexture(cmd, source, effectDestination, runtimeLayer.material, Mathf.Max(0, runtimeLayer.settings.passIndex));
                    }

                    if (effectDestination != destination)
                    {
                        ApplyShoostAovCompositeProperties(runtimeLayer.settings, aovCompositeMaterial);
                        aovCompositeMaterial.SetTexture(ShoostPostProcessShaderConstants.LayerResultTextureId, effectDestination);
                        Blitter.BlitCameraTexture(cmd, source, destination, aovCompositeMaterial, 0);
                    }

                    source = destination;
                    writeToA = !writeToA;
                }

                Blitter.BlitCameraTexture(cmd, source, cameraColorTarget, 0, true);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (runtimeLayers.Count == 0)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                if (!warnedBackBuffer)
                {
                    Debug.LogWarning($"{_passName} skipped because the active color target is the backbuffer. The Shoost post process stack requires an intermediate color texture.");
                    warnedBackBuffer = true;
                }
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                TextureHandle layerInput = source;
                TextureHandle effectResult;
                if (runtimeLayer.settings.effect == ShoostPostProcessEffect.IrisBlur)
                {
                    effectResult = RecordIrisBlurLayer(renderGraph, source, runtimeLayer, i);
                }
                else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.RGBBlurV2)
                {
                    effectResult = RecordRgbBlurV2Layer(renderGraph, source, runtimeLayer, i);
                }
                else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.Glow)
                {
                    effectResult = RecordGlowLayer(renderGraph, source, runtimeLayer, i);
                }
                else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ApertureBokeh)
                {
                    effectResult = RecordApertureBokehLayer(renderGraph, source, runtimeLayer, i);
                }
                else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ChangeFrameRate)
                {
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    effectResult = RecordChangeFrameRateLayer(renderGraph, source, runtimeLayer, i, cameraData);
                }
                else
                {
                    TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                    destinationDesc.name = $"_lilShoostPostProcessLayer{i}";
                    destinationDesc.clearBuffer = false;
                    destinationDesc.depthBufferBits = 0;
                    EnsureHdrTextureDesc(ref destinationDesc);
                    TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                    using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Layer {i}", out PassData passData, _profilingSampler))
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

                    effectResult = destination;
                }

                source = RecordAovCompositeIfNeeded(renderGraph, layerInput, effectResult, runtimeLayer.settings, i, aovResources);
            }

            resourceData.cameraColor = source;
        }

        private void CopyLayers(List<ShoostPostProcessRuntimeLayer> layers)
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
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private bool RequiresAovComposite()
        {
            if (aovCompositeMaterial == null)
            {
                return false;
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                if (RequiresAovComposite(runtimeLayers[i]?.settings))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RequiresAovComposite(ShoostPostProcessLayer layer)
        {
            return aovCompositeMaterial != null && layer != null && (layer.useAovMask || layer.debugAovMask);
        }

        private TextureHandle RecordAovCompositeIfNeeded(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle layerResult,
            ShoostPostProcessLayer layer,
            int layerIndex,
            HoAovRenderGraphResources aovResources)
        {
            if (!RequiresAovComposite(layer) || !source.IsValid() || !layerResult.IsValid())
            {
                return layerResult;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"_lilShoostPostProcessLayer{layerIndex}_AOV";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref destinationDesc);
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} AOV Mask {layerIndex}", out PassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.layerResultTexture = layerResult;
                passData.layer = layer;
                passData.material = aovCompositeMaterial;
                passData.aovMaskIdTexture = aovResources.maskIdTexture;
                passData.aovSurfaceDataTexture = aovResources.surfaceDataTexture;
                passData.aovCustom0Texture = aovResources.custom0Texture;
                passData.aovObjectCustom0Texture = aovResources.objectCustom0Texture;
                passData.aovObjectCustom1Texture = aovResources.objectCustom1Texture;
                passData.useAovMaskTexture = aovResources.maskIdTexture.IsValid();
                passData.useAovSurfaceData = aovResources.surfaceDataTexture.IsValid();
                passData.useAovCustom0 = aovResources.custom0Texture.IsValid();
                passData.useAovObjectCustom0 = aovResources.objectCustom0Texture.IsValid();
                passData.useAovObjectCustom1 = aovResources.objectCustom1Texture.IsValid();

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(layerResult, AccessFlags.Read);
                if (passData.useAovMaskTexture)
                {
                    builder.UseTexture(aovResources.maskIdTexture, AccessFlags.Read);
                }

                if (passData.useAovSurfaceData)
                {
                    builder.UseTexture(aovResources.surfaceDataTexture, AccessFlags.Read);
                }

                if (passData.useAovCustom0)
                {
                    builder.UseTexture(aovResources.custom0Texture, AccessFlags.Read);
                }

                if (passData.useAovObjectCustom0)
                {
                    builder.UseTexture(aovResources.objectCustom0Texture, AccessFlags.Read);
                }

                if (passData.useAovObjectCustom1)
                {
                    builder.UseTexture(aovResources.objectCustom1Texture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyShoostAovCompositeProperties(data.layer, data.material);
                    context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.LayerResultTextureId, data.layerResultTexture);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, data.useAovMaskTexture ? 1.0f : 0.0f);
                    if (data.useAovMaskTexture)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.aovMaskIdTexture);
                    }

                    if (data.useAovSurfaceData)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.aovSurfaceDataTexture);
                    }

                    if (data.useAovCustom0)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, data.aovCustom0Texture);
                    }

                    if (data.useAovObjectCustom0)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, data.aovObjectCustom0Texture);
                    }

                    if (data.useAovObjectCustom1)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, data.aovObjectCustom1Texture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            return destination;
        }

        private static void EnsureHdrDescriptor(ref RenderTextureDescriptor descriptor)
        {
            GraphicsFormat hdrFormat = GetShoostHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.graphicsFormat = hdrFormat;
            }
        }

        private static void EnsureHdrTextureDesc(ref TextureDesc descriptor)
        {
            GraphicsFormat hdrFormat = GetShoostHdrGraphicsFormat();
            if (hdrFormat != GraphicsFormat.None)
            {
                descriptor.format = hdrFormat;
            }
        }

        private static GraphicsFormat GetShoostHdrGraphicsFormat()
        {
            const GraphicsFormat preferredFormat = GraphicsFormat.R16G16B16A16_SFloat;
            return SystemInfo.IsFormatSupported(preferredFormat, FormatUsage.Render)
                ? preferredFormat
                : GraphicsFormat.None;
        }

        private static void ApplyLayerProperties(ShoostPostProcessLayer layer, Material material)
        {
            float sharpness = layer.parameters0.x;
            if ((layer.effect == ShoostPostProcessEffect.SharpenBefore || layer.effect == ShoostPostProcessEffect.SharpenAfter) && sharpness <= 0.0f)
            {
                sharpness = 0.2f;
            }

            material.SetFloat(ShoostPostProcessShaderConstants.IntensityId, layer.intensity);
            material.SetFloat(ShoostPostProcessShaderConstants.SharpnessId, sharpness);
            material.SetFloat(ShoostPostProcessShaderConstants.ModeId, layer.parameters0.x);
            material.SetFloat(ShoostPostProcessShaderConstants.AngleId, layer.parameters0.z * Mathf.Deg2Rad);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerBlendModeId, (float)layer.blendMode);
            Color layerColor = layer.effect == ShoostPostProcessEffect.Fisheye ? Color.black : layer.color;
            material.SetColor(ShoostPostProcessShaderConstants.LayerColorId, layerColor);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerTextureEnabledId, layer.texture != null ? 1.0f : 0.0f);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams0Id, layer.parameters0);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams1Id, layer.parameters1);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams2Id, layer.parameters2);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams3Id, layer.parameters3);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams4Id, layer.parameters4);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams5Id, layer.parameters5);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams6Id, layer.parameters6);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams7Id, layer.parameters7);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams8Id, layer.parameters8);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams9Id, layer.parameters9);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams10Id, layer.parameters10);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams11Id, layer.parameters11);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams12Id, layer.parameters12);
            if (layer.texture != null)
            {
                material.SetTexture(ShoostPostProcessShaderConstants.LayerTextureId, layer.texture);
            }
        }

        private static void ApplyShoostAovCompositeProperties(ShoostPostProcessLayer layer, Material material)
        {
            if (layer == null || material == null)
            {
                return;
            }

            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovMaskEnabledId, layer.useAovMask ? 1.0f : 0.0f);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovSourceId, (float)layer.aovSource);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovModeId, (float)layer.aovMaskMode);
            material.SetVector(
                ShoostPostProcessShaderConstants.LayerAovParamsId,
                new Vector4(
                    Mathf.Max(0.0f, layer.aovThreshold),
                    0.0f,
                    layer.aovMatchValue,
                    layer.invertAovMask ? 1.0f : 0.0f));
            material.SetColor(ShoostPostProcessShaderConstants.LayerAovMatchColorId, layer.aovMatchColor);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerAovDebugOutputId, layer.debugAovMask ? 1.0f : 0.0f);
            HoPostAovMaskRuntime.ApplyToMaterial(
                layer,
                material,
                ShoostPostProcessShaderConstants.LayerAovRuleCountId,
                ShoostPostProcessShaderConstants.LayerAovRuleData0Id,
                ShoostPostProcessShaderConstants.LayerAovRuleData1Id,
                ShoostPostProcessShaderConstants.LayerAovRuleData2Id,
                ShoostPostProcessShaderConstants.LayerAovRuleColorId);
        }

        private static int GetChangeFrameRateTargetFrameRate(ShoostPostProcessLayer layer)
        {
            float value = layer.parameters0.x > 0.0f ? layer.parameters0.x : 12.0f;
            return Mathf.Clamp(Mathf.RoundToInt(value), 1, 60);
        }

        private ChangeFrameRateState GetChangeFrameRateState(int cameraId, RenderTextureDescriptor descriptor)
        {
            if (!changeFrameRateStates.TryGetValue(cameraId, out ChangeFrameRateState state))
            {
                state = new ChangeFrameRateState();
                changeFrameRateStates.Add(cameraId, state);
            }

            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref descriptor);

            bool descriptorChanged = state.frozenTexture == null
                || state.width != descriptor.width
                || state.height != descriptor.height
                || state.volumeDepth != descriptor.volumeDepth
                || state.msaaSamples != descriptor.msaaSamples
                || state.dimension != descriptor.dimension
                || state.graphicsFormat != descriptor.graphicsFormat;

            if (descriptorChanged)
            {
                state.Release();
                state.width = descriptor.width;
                state.height = descriptor.height;
                state.volumeDepth = descriptor.volumeDepth;
                state.msaaSamples = descriptor.msaaSamples;
                state.dimension = descriptor.dimension;
                state.graphicsFormat = descriptor.graphicsFormat;
            }

            RenderingUtils.ReAllocateIfNeeded(ref state.frozenTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_lilShoostChangeFrameRate_{cameraId}");
            return state;
        }

        private static bool ShouldRefreshChangeFrameRateState(ChangeFrameRateState state, ShoostPostProcessLayer layer, out int targetFrameRate, out double now)
        {
            targetFrameRate = GetChangeFrameRateTargetFrameRate(layer);
            now = Time.realtimeSinceStartupAsDouble;
            if (state.targetFrameRate != targetFrameRate)
            {
                state.targetFrameRate = targetFrameRate;
                state.nextUpdateTime = 0.0;
                state.isValid = false;
            }

            return !state.isValid || now >= state.nextUpdateTime;
        }

        private static void MarkChangeFrameRateStateRefreshed(ChangeFrameRateState state, int targetFrameRate, double now)
        {
            state.isValid = true;
            state.nextUpdateTime = now + (1.0 / Mathf.Max(1, targetFrameRate));
        }

        private static IrisBlurParameters GetIrisBlurParameters(ShoostPostProcessLayer layer)
        {
            Vector4 parameters0 = layer.parameters0;
            Vector4 parameters1 = layer.parameters1;
            Vector4 parameters2 = layer.parameters2;
            Vector4 parameters3 = layer.parameters3;

            return new IrisBlurParameters
            {
                resolutionType = Mathf.Clamp(Mathf.RoundToInt(parameters0.x), 0, 1),
                customResolution = new Vector2Int(Mathf.RoundToInt(parameters0.y), Mathf.RoundToInt(parameters0.z)),
                radius = parameters1.x > 0.0f ? parameters1.x : 1.0f,
                downScale = Mathf.Clamp(Mathf.RoundToInt(parameters1.y > 0.0f ? parameters1.y : 2.0f), 1, 4),
                iterations = Mathf.Clamp(Mathf.RoundToInt(parameters1.z > 0.0f ? parameters1.z : 3.0f), 1, 8),
                center = new Vector2(parameters2.x, parameters2.y),
                centerSize = parameters2.z > 0.0f ? parameters2.z : 0.8f,
                smoothness = parameters2.w > 0.0f ? parameters2.w : 0.1f,
                enableRgbSplit = parameters3.x > 0.5f,
                blurRadiusR = Mathf.Max(0.0f, parameters3.y),
                blurRadiusG = Mathf.Max(0.0f, parameters3.z),
                blurRadiusB = Mathf.Max(0.0f, parameters3.w),
                distance = Mathf.Max(0.0f, parameters0.w),
                angleRadians = parameters1.w * Mathf.Deg2Rad
            };
        }

        private static void ApplyIrisBlurProperties(Material material, IrisBlurParameters parameters, float screenRatio)
        {
            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, parameters.radius * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, screenRatio);
            material.SetVector(ShoostPostProcessShaderConstants.CenterId, new Vector4(parameters.center.x, parameters.center.y, 0.0f, 0.0f));
            material.SetFloat(ShoostPostProcessShaderConstants.CenterSizeId, 1.0f - parameters.centerSize);
            material.SetFloat(ShoostPostProcessShaderConstants.SmoothnessId, parameters.smoothness);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetRId, parameters.blurRadiusR * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetGId, parameters.blurRadiusG * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetBId, parameters.blurRadiusB * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.DistanceId, parameters.distance * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.AngleId, parameters.angleRadians);

            if (parameters.enableRgbSplit)
            {
                material.EnableKeyword("ENABLE_RGBSPLIT");
            }
            else
            {
                material.DisableKeyword("ENABLE_RGBSPLIT");
            }
        }

        private void ApplyIrisBlurLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            IrisBlurParameters parameters = GetIrisBlurParameters(layer);

            int width = parameters.resolutionType == 1 && parameters.customResolution.x > 0 ? parameters.customResolution.x : sourceDescriptor.width;
            int height = parameters.resolutionType == 1 && parameters.customResolution.y > 0 ? parameters.customResolution.y : sourceDescriptor.height;
            width = Mathf.Max(1, width / parameters.downScale);
            height = Mathf.Max(1, height / parameters.downScale);

            RenderTextureDescriptor blurDescriptor = sourceDescriptor;
            blurDescriptor.width = width;
            blurDescriptor.height = height;
            blurDescriptor.depthBufferBits = 0;
            blurDescriptor.depthStencilFormat = GraphicsFormat.None;
            blurDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref blurDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref irisTextureA, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostIrisBlurA");
            RenderingUtils.ReAllocateIfNeeded(ref irisTextureB, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostIrisBlurB");

            float screenRatio = Mathf.Max(1.0f, sourceDescriptor.width) / Mathf.Max(1.0f, sourceDescriptor.height);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            ApplyLayerProperties(layer, material);
            ApplyIrisBlurProperties(material, parameters, screenRatio);

            RTHandle current = irisTextureA;
            RTHandle next = irisTextureB;
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 1; i < parameters.iterations; i++)
            {
                Blitter.BlitCameraTexture(cmd, current, next, material, 1);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 2);
        }

        private void ApplyRgbBlurV2Layer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float maxChannelBlur = Mathf.Clamp01(Mathf.Max(layer.parameters0.x, layer.parameters0.y, layer.parameters0.z) * layer.intensity);
            int downScale = maxChannelBlur > 0.0001f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(maxChannelBlur * 4.0f), 2, 6);
            float radius = Mathf.Lerp(0.75f, 9.0f, maxChannelBlur);

            RenderTextureDescriptor blurDescriptor = sourceDescriptor;
            blurDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            blurDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            blurDescriptor.depthBufferBits = 0;
            blurDescriptor.depthStencilFormat = GraphicsFormat.None;
            blurDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref blurDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref rgbBlurTextureA, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostRGBBlurV2A");
            RenderingUtils.ReAllocateIfNeeded(ref rgbBlurTextureB, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostRGBBlurV2B");

            RTHandle current = rgbBlurTextureA;
            RTHandle next = rgbBlurTextureB;
            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius);
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 1; i < iterations; i++)
            {
                material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius + i);
                Blitter.BlitCameraTexture(cmd, current, next, material, 0);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 1);
        }

        private void ApplyGlowLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float radius = Mathf.Clamp(layer.parameters0.z, 0.0f, 6.0f);
            int mode = Mathf.Clamp(Mathf.RoundToInt(layer.parameters0.w), 0, 2);
            int downScale = radius > 0.75f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(radius * 1.25f), 2, 10);

            RenderTextureDescriptor glowDescriptor = sourceDescriptor;
            glowDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            glowDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            glowDescriptor.depthBufferBits = 0;
            glowDescriptor.depthStencilFormat = GraphicsFormat.None;
            glowDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref glowDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref glowTextureA, glowDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostGlowA");
            RenderingUtils.ReAllocateIfNeeded(ref glowTextureB, glowDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostGlowB");

            RTHandle current = glowTextureA;
            RTHandle next = glowTextureB;

            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, Mathf.Max(0.75f, radius));
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 0; i < iterations; i++)
            {
                material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, Mathf.Lerp(0.75f, 2.5f + radius, (i + 1.0f) / iterations));
                Blitter.BlitCameraTexture(cmd, current, next, material, 1);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            if (mode != 0)
            {
                float angle = mode == 2 ? layer.parameters2.y : 0.0f;
                material.SetFloat(ShoostPostProcessShaderConstants.AngleId, angle);
                material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, Mathf.Max(1.0f, radius * 1.75f));
                Blitter.BlitCameraTexture(cmd, current, next, material, 2);
                current = next;
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BloomTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 3);
        }

        private void ApplyApertureBokehLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float apertureSize = Mathf.Clamp01(layer.parameters0.x);
            float radius = Mathf.Lerp(2.0f, 24.0f, apertureSize);
            int downScale = apertureSize > 0.35f ? 2 : 1;

            RenderTextureDescriptor bokehDescriptor = sourceDescriptor;
            bokehDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            bokehDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            bokehDescriptor.depthBufferBits = 0;
            bokehDescriptor.depthStencilFormat = GraphicsFormat.None;
            bokehDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref bokehDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref apertureBokehTextureA, bokehDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostApertureBokehA");
            RenderingUtils.ReAllocateIfNeeded(ref apertureBokehTextureB, bokehDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostApertureBokehB");

            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius);
            Blitter.BlitCameraTexture(cmd, source, apertureBokehTextureA, material, 0);
            Blitter.BlitCameraTexture(cmd, apertureBokehTextureA, apertureBokehTextureB, material, 1);
            Blitter.BlitCameraTexture(cmd, apertureBokehTextureB, apertureBokehTextureA, material, 2);

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BloomTexId, apertureBokehTextureA);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 3);
        }

        private void ApplyChangeFrameRateLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, Camera camera, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            int cameraId = camera != null ? camera.GetInstanceID() : 0;
            ChangeFrameRateState state = GetChangeFrameRateState(cameraId, sourceDescriptor);

            ApplyLayerProperties(layer, material);
            if (ShouldRefreshChangeFrameRateState(state, layer, out int targetFrameRate, out double now))
            {
                Blitter.BlitCameraTexture(cmd, source, state.frozenTexture, material, 0);
                MarkChangeFrameRateStateRefreshed(state, targetFrameRate, now);
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.FrozenFrameTexId, state.frozenTexture);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 1);
        }

        private TextureHandle RecordIrisBlurLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            IrisBlurParameters parameters = GetIrisBlurParameters(layer);

            int width = parameters.resolutionType == 1 && parameters.customResolution.x > 0 ? parameters.customResolution.x : sourceDesc.width;
            int height = parameters.resolutionType == 1 && parameters.customResolution.y > 0 ? parameters.customResolution.y : sourceDesc.height;
            width = Mathf.Max(1, width / parameters.downScale);
            height = Mathf.Max(1, height / parameters.downScale);

            TextureDesc blurDesc = sourceDesc;
            blurDesc.name = $"_lilShoostIrisBlur_{layerIndex}";
            blurDesc.width = width;
            blurDesc.height = height;
            blurDesc.clearBuffer = false;
            blurDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref blurDesc);

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            float screenRatio = Mathf.Max(1.0f, sourceDesc.width) / Mathf.Max(1.0f, sourceDesc.height);

            TextureHandle current = AddIrisPass(renderGraph, source, blurA, material, 0, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = blurB;
            for (int i = 1; i < parameters.iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddIrisPass(renderGraph, passSource, passDestination, material, 1, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddIrisPass(renderGraph, source, destination, material, 2, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle RecordRgbBlurV2Layer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;

            float maxChannelBlur = Mathf.Clamp01(Mathf.Max(layer.parameters0.x, layer.parameters0.y, layer.parameters0.z) * layer.intensity);
            int downScale = maxChannelBlur > 0.0001f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(maxChannelBlur * 4.0f), 2, 6);
            float radius = Mathf.Lerp(0.75f, 9.0f, maxChannelBlur);

            TextureDesc blurDesc = sourceDesc;
            blurDesc.name = $"_lilShoostRGBBlurV2_{layerIndex}";
            blurDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            blurDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            blurDesc.clearBuffer = false;
            blurDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref blurDesc);

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            TextureHandle current = AddRgbBlurV2Pass(renderGraph, source, blurA, material, 0, radius, runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = blurB;
            for (int i = 1; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddRgbBlurV2Pass(renderGraph, passSource, passDestination, material, 0, radius + i, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddRgbBlurV2Pass(renderGraph, source, destination, material, 1, radius, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle RecordGlowLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;

            float radius = Mathf.Clamp(layer.parameters0.z, 0.0f, 6.0f);
            int mode = Mathf.Clamp(Mathf.RoundToInt(layer.parameters0.w), 0, 2);
            int downScale = radius > 0.75f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(radius * 1.25f), 2, 10);

            TextureDesc glowDesc = sourceDesc;
            glowDesc.name = $"_lilShoostGlow_{layerIndex}";
            glowDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            glowDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            glowDesc.clearBuffer = false;
            glowDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref glowDesc);

            TextureHandle glowA = renderGraph.CreateTexture(glowDesc);
            TextureHandle glowB = renderGraph.CreateTexture(glowDesc);
            TextureHandle current = AddGlowPass(renderGraph, source, glowA, material, 0, Mathf.Max(0.75f, radius), runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = glowB;

            for (int i = 0; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                float passRadius = Mathf.Lerp(0.75f, 2.5f + radius, (i + 1.0f) / iterations);
                current = AddGlowPass(renderGraph, passSource, passDestination, material, 1, passRadius, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            if (mode != 0)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                float angle = mode == 2 ? layer.parameters2.y : 0.0f;
                current = AddGlowPass(renderGraph, passSource, passDestination, material, 2, Mathf.Max(1.0f, radius * 1.75f), runtimeLayer.settings, _profilingSampler, _passName, default, angle);
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddGlowPass(renderGraph, source, destination, material, 3, radius, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle RecordApertureBokehLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            float apertureSize = Mathf.Clamp01(layer.parameters0.x);
            float radius = Mathf.Lerp(2.0f, 24.0f, apertureSize);
            int downScale = apertureSize > 0.35f ? 2 : 1;

            TextureDesc bokehDesc = sourceDesc;
            bokehDesc.name = $"_lilShoostApertureBokeh_{layerIndex}";
            bokehDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            bokehDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            bokehDesc.clearBuffer = false;
            bokehDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref bokehDesc);

            TextureHandle bokehA = renderGraph.CreateTexture(bokehDesc);
            bokehDesc.name = $"_lilShoostApertureBokehTmp_{layerIndex}";
            TextureHandle bokehB = renderGraph.CreateTexture(bokehDesc);

            TextureHandle current = AddGlowPass(renderGraph, source, bokehA, material, 0, radius, layer, _profilingSampler, _passName);
            current = AddGlowPass(renderGraph, current, bokehB, material, 1, radius, layer, _profilingSampler, _passName);
            current = AddGlowPass(renderGraph, current, bokehA, material, 2, radius, layer, _profilingSampler, _passName);

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddGlowPass(renderGraph, source, destination, material, 3, radius, layer, _profilingSampler, _passName, current);
        }

        private TextureHandle RecordChangeFrameRateLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex, UniversalCameraData cameraData)
        {
            int cameraId = cameraData.camera != null ? cameraData.camera.GetInstanceID() : 0;
            ChangeFrameRateState state = GetChangeFrameRateState(cameraId, cameraData.cameraTargetDescriptor);
            TextureHandle frozenFrameTexture = renderGraph.ImportTexture(state.frozenTexture);

            if (ShouldRefreshChangeFrameRateState(state, runtimeLayer.settings, out int targetFrameRate, out double now))
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Change Frame Rate Capture", out PassData passData, _profilingSampler))
                {
                    passData.source = source;
                    passData.layer = runtimeLayer.settings;
                    passData.material = runtimeLayer.material;
                    passData.passIndex = 0;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(frozenFrameTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ApplyLayerProperties(data.layer, data.material);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                    });
                }

                MarkChangeFrameRateStateRefreshed(state, targetFrameRate, now);
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref destinationDesc);
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Change Frame Rate", out PassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.frozenFrameTexture = frozenFrameTexture;
                passData.layer = runtimeLayer.settings;
                passData.material = runtimeLayer.material;
                passData.passIndex = 1;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(frozenFrameTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.FrozenFrameTexId, data.frozenFrameTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

        private TextureHandle AddIrisPass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            IrisBlurParameters parameters,
            float screenRatio,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle blurredTexture = default)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} Iris", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = parameters.radius;
                passData.screenRatio = screenRatio;
                passData.center = parameters.center;
                passData.centerSize = parameters.centerSize;
                passData.smoothness = parameters.smoothness;
                passData.distance = parameters.distance;
                passData.angle = parameters.angleRadians;
                passData.blurOffsetR = parameters.blurRadiusR;
                passData.blurOffsetG = parameters.blurRadiusG;
                passData.blurOffsetB = parameters.blurRadiusB;
                passData.enableRgbSplit = parameters.enableRgbSplit;
                passData.blurredTexture = blurredTexture;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                if (blurredTexture.IsValid())
                {
                    builder.UseTexture(blurredTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(blurredTexture, ShoostPostProcessShaderConstants.BlurredTexId);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, data.screenRatio);
                    data.material.SetVector(ShoostPostProcessShaderConstants.CenterId, new Vector4(data.center.x, data.center.y, 0.0f, 0.0f));
                    data.material.SetFloat(ShoostPostProcessShaderConstants.CenterSizeId, 1.0f - data.centerSize);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.SmoothnessId, data.smoothness);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetRId, data.blurOffsetR * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetGId, data.blurOffsetG * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetBId, data.blurOffsetB * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.DistanceId, data.distance * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.AngleId, data.angle);

                    if (data.enableRgbSplit)
                    {
                        data.material.EnableKeyword("ENABLE_RGBSPLIT");
                    }
                    else
                    {
                        data.material.DisableKeyword("ENABLE_RGBSPLIT");
                    }

                    if (data.blurredTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, data.blurredTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

        private TextureHandle AddRgbBlurV2Pass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float radius,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle blurredTexture = default)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} RGB Blur V2", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.originalTexture = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = radius;
                passData.blurredTexture = blurredTexture;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                if (blurredTexture.IsValid())
                {
                    builder.UseTexture(blurredTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(blurredTexture, ShoostPostProcessShaderConstants.BlurredTexId);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius);
                    if (data.blurredTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, data.originalTexture);
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, data.blurredTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

        private TextureHandle AddGlowPass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float radius,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle bloomTexture = default,
            float angle = 0.0f)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} Glow", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.originalTexture = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = radius;
                passData.angle = angle;
                passData.bloomTexture = bloomTexture;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                if (bloomTexture.IsValid())
                {
                    builder.UseTexture(bloomTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(bloomTexture, ShoostPostProcessShaderConstants.BloomTexId);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.AngleId, data.angle);
                    if (data.bloomTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, data.originalTexture);
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BloomTexId, data.bloomTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }
    }
}
