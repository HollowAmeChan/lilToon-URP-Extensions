using System.Collections.Generic;
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
        private readonly List<ShoostPostProcessRuntimeLayer> beforePostProcessLayers = new List<ShoostPostProcessRuntimeLayer>();
        private readonly List<ShoostPostProcessRuntimeLayer> afterPostProcessLayers = new List<ShoostPostProcessRuntimeLayer>();
        private readonly List<ShoostPostProcessRuntimeLayer> afterRenderingLayers = new List<ShoostPostProcessRuntimeLayer>();
        private ShoostPostProcessPass beforePostProcessPass;
        private ShoostPostProcessPass afterPostProcessPass;
        private ShoostPostProcessPass afterRenderingPass;

        [Tooltip("Match HTrace-style setup: the renderer feature installs the pass, and Volume profiles provide the active settings.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public ShoostPostProcessStackSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            beforePostProcessPass = new ShoostPostProcessPass("lilToon-Shoost Before URP Post");
            afterPostProcessPass = new ShoostPostProcessPass("lilToon-Shoost After URP Post");
            afterRenderingPass = new ShoostPostProcessPass("lilToon-Shoost After Rendering");
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ShoostPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                return;
            }

            BuildRuntimeLayers(renderingData.cameraData.cameraType, volume);
            SetupCompatibilityPass(beforePostProcessPass, renderer.cameraColorTargetHandle, beforePostProcessLayers, RenderPassEvent.BeforeRenderingPostProcessing);
            SetupCompatibilityPass(afterPostProcessPass, renderer.cameraColorTargetHandle, afterPostProcessLayers, RenderPassEvent.AfterRenderingPostProcessing);
            SetupCompatibilityPass(afterRenderingPass, renderer.cameraColorTargetHandle, afterRenderingLayers, RenderPassEvent.AfterRendering);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ShoostPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                return;
            }

            BuildRuntimeLayers(renderingData.cameraData.cameraType, volume);
            EnqueueRenderGraphPass(renderer, beforePostProcessPass, beforePostProcessLayers, RenderPassEvent.BeforeRenderingPostProcessing);
            EnqueueRenderGraphPass(renderer, afterPostProcessPass, afterPostProcessLayers, RenderPassEvent.AfterRenderingPostProcessing);
            EnqueueRenderGraphPass(renderer, afterRenderingPass, afterRenderingLayers, RenderPassEvent.AfterRendering);
        }

        protected override void Dispose(bool disposing)
        {
            beforePostProcessPass?.Dispose();
            afterPostProcessPass?.Dispose();
            afterRenderingPass?.Dispose();
            beforePostProcessPass = null;
            afterPostProcessPass = null;
            afterRenderingPass = null;

            foreach (Material material in materialCache.Values)
            {
                CoreUtils.Destroy(material);
            }

            materialCache.Clear();
            beforePostProcessLayers.Clear();
            afterPostProcessLayers.Clear();
            afterRenderingLayers.Clear();
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

        private void BuildRuntimeLayers(CameraType cameraType, ShoostPostProcessStackVolume volume)
        {
            beforePostProcessLayers.Clear();
            afterPostProcessLayers.Clear();
            afterRenderingLayers.Clear();
            List<ShoostPostProcessLayer> layers = volume != null && volume.layers != null ? volume.layers.value : null;
            if (layers == null)
            {
                return;
            }

            foreach (ShoostPostProcessLayer layer in layers)
            {
                if (layer == null || !layer.IsActive)
                {
                    continue;
                }

                if (cameraType == CameraType.SceneView && !layer.showInSceneView)
                {
                    continue;
                }

                Material material = ResolveMaterial(layer);
                if (material == null)
                {
                    continue;
                }

                ShoostPostProcessInjectionPoint injectionPoint = ResolveInjectionPoint(layer);
                ShoostPostProcessRuntimeLayer runtimeLayer = new ShoostPostProcessRuntimeLayer(layer, material);
                switch (injectionPoint)
                {
                    case ShoostPostProcessInjectionPoint.AfterURPPostProcessing:
                        afterPostProcessLayers.Add(runtimeLayer);
                        break;
                    case ShoostPostProcessInjectionPoint.AfterRendering:
                        afterRenderingLayers.Add(runtimeLayer);
                        break;
                    default:
                        beforePostProcessLayers.Add(runtimeLayer);
                        break;
                }
            }
        }

        private static void SetupCompatibilityPass(
            ShoostPostProcessPass pass,
            RTHandle cameraColorTarget,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                return;
            }

            pass.Setup(cameraColorTarget, layers, passEvent);
        }

        private static void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            ShoostPostProcessPass pass,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                return;
            }

            pass.SetupRenderGraph(layers, passEvent);
            renderer.EnqueuePass(pass);
        }

        private static ShoostPostProcessInjectionPoint ResolveInjectionPoint(ShoostPostProcessLayer layer)
        {
            if (layer.injectionPoint != ShoostPostProcessInjectionPoint.EffectDefault)
            {
                return layer.injectionPoint;
            }

            return ShoostPostProcessEffectRegistry.GetDefaultInjectionPoint(layer.effect);
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

            Debug.LogWarning($"lilToon-Shoost Post Process Stack skipped layer '{layer.name}' because shader '{shaderName}' was not found.");
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
        private readonly ProfilingSampler profilingSampler;
        private readonly string passName;
        private RTHandle cameraColorTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private RTHandle kawaseTextureA;
        private RTHandle kawaseTextureB;
        private RTHandle irisTextureA;
        private RTHandle irisTextureB;
        private RTHandle rgbBlurTextureA;
        private RTHandle rgbBlurTextureB;
        private bool warnedBackBuffer;
        private readonly Dictionary<int, ChangeFrameRateState> changeFrameRateStates = new Dictionary<int, ChangeFrameRateState>();

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle originalTexture;
            public ShoostPostProcessLayer layer;
            public Material material;
            public int passIndex;
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
            this.passName = passName;
            profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(RTHandle cameraColorTarget, List<ShoostPostProcessRuntimeLayer> layers, RenderPassEvent passEvent)
        {
            this.cameraColorTarget = cameraColorTarget;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(List<ShoostPostProcessRuntimeLayer> layers, RenderPassEvent passEvent)
        {
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            tempTextureA?.Release();
            tempTextureB?.Release();
            tempTextureA = null;
            tempTextureB = null;
            kawaseTextureA?.Release();
            kawaseTextureB?.Release();
            kawaseTextureA = null;
            kawaseTextureB = null;
            irisTextureA?.Release();
            irisTextureB?.Release();
            irisTextureA = null;
            irisTextureB = null;
            rgbBlurTextureA?.Release();
            rgbBlurTextureB?.Release();
            rgbBlurTextureA = null;
            rgbBlurTextureB = null;
            foreach (ChangeFrameRateState state in changeFrameRateStates.Values)
            {
                state.Release();
            }

            changeFrameRateStates.Clear();
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
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureBName);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (runtimeLayers.Count == 0 || cameraColorTarget == null || tempTextureA == null || tempTextureB == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                RTHandle source = cameraColorTarget;
                bool writeToA = true;

                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    if (runtimeLayer.settings.effect == ShoostPostProcessEffect.KawaseBlur)
                    {
                        ApplyKawaseBlurLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, destination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.IrisBlur)
                    {
                        ApplyIrisBlurLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, destination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.RGBBlurV2)
                    {
                        ApplyRgbBlurV2Layer(cmd, renderingData.cameraData.cameraTargetDescriptor, source, destination, runtimeLayer);
                    }
                    else if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ChangeFrameRate)
                    {
                        ApplyChangeFrameRateLayer(cmd, renderingData.cameraData.cameraTargetDescriptor, renderingData.cameraData.camera, source, destination, runtimeLayer);
                    }
                    else
                    {
                        ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material);
                        Blitter.BlitCameraTexture(cmd, source, destination, runtimeLayer.material, Mathf.Max(0, runtimeLayer.settings.passIndex));
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
                    Debug.LogWarning($"{passName} skipped because the active color target is the backbuffer. The Shoost post process stack requires an intermediate color texture.");
                    warnedBackBuffer = true;
                }
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                if (runtimeLayer.settings.effect == ShoostPostProcessEffect.KawaseBlur)
                {
                    source = RecordKawaseBlurLayer(renderGraph, source, runtimeLayer, i);
                    continue;
                }
                if (runtimeLayer.settings.effect == ShoostPostProcessEffect.IrisBlur)
                {
                    source = RecordIrisBlurLayer(renderGraph, source, runtimeLayer, i);
                    continue;
                }
                if (runtimeLayer.settings.effect == ShoostPostProcessEffect.RGBBlurV2)
                {
                    source = RecordRgbBlurV2Layer(renderGraph, source, runtimeLayer, i);
                    continue;
                }
                if (runtimeLayer.settings.effect == ShoostPostProcessEffect.ChangeFrameRate)
                {
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    source = RecordChangeFrameRateLayer(renderGraph, source, runtimeLayer, i, cameraData);
                    continue;
                }

                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = $"_lilShoostPostProcessLayer{i}";
                destinationDesc.clearBuffer = false;
                destinationDesc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{passName} Layer {i}", out PassData passData, profilingSampler))
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

        private void ApplyKawaseBlurLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            int resolutionType = Mathf.RoundToInt(layer.parameters0.x);
            Vector2Int customResolution = new Vector2Int(Mathf.RoundToInt(layer.parameters0.y), Mathf.RoundToInt(layer.parameters0.z));
            float radius = layer.parameters1.x > 0.0f ? layer.parameters1.x : 0.5f;
            int downScale = Mathf.Max(1, Mathf.RoundToInt(layer.parameters1.y > 0.0f ? layer.parameters1.y : 2.0f));
            int iterations = Mathf.Clamp(Mathf.RoundToInt(layer.parameters1.z > 0.0f ? layer.parameters1.z : 6.0f), 1, 10);

            int width = resolutionType == 1 && customResolution.x > 0 ? customResolution.x : sourceDescriptor.width;
            int height = resolutionType == 1 && customResolution.y > 0 ? customResolution.y : sourceDescriptor.height;
            width = Mathf.Max(1, width / downScale);
            height = Mathf.Max(1, height / downScale);

            RenderTextureDescriptor blurDescriptor = sourceDescriptor;
            blurDescriptor.width = width;
            blurDescriptor.height = height;
            blurDescriptor.depthBufferBits = 0;
            blurDescriptor.depthStencilFormat = GraphicsFormat.None;
            blurDescriptor.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(ref kawaseTextureA, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostKawaseBlurA");
            RenderingUtils.ReAllocateIfNeeded(ref kawaseTextureB, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostKawaseBlurB");

            float screenRatio = Mathf.Max(1.0f, sourceDescriptor.width) / Mathf.Max(1.0f, sourceDescriptor.height);
            material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, screenRatio);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);

            RTHandle current = kawaseTextureA;
            RTHandle next = kawaseTextureB;
            float iterationStep = 1.0f / downScale;

            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius);
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 0; i < iterations; i++)
            {
                material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius + (i * iterationStep));
                Blitter.BlitCameraTexture(cmd, current, next, material, 0);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius + (iterations * iterationStep));
            Blitter.BlitCameraTexture(cmd, current, destination, material, 0);
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

        private TextureHandle RecordKawaseBlurLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            int resolutionType = Mathf.RoundToInt(layer.parameters0.x);
            Vector2Int customResolution = new Vector2Int(Mathf.RoundToInt(layer.parameters0.y), Mathf.RoundToInt(layer.parameters0.z));
            float radius = layer.parameters1.x > 0.0f ? layer.parameters1.x : 0.5f;
            int downScale = Mathf.Max(1, Mathf.RoundToInt(layer.parameters1.y > 0.0f ? layer.parameters1.y : 2.0f));
            int iterations = Mathf.Clamp(Mathf.RoundToInt(layer.parameters1.z > 0.0f ? layer.parameters1.z : 6.0f), 1, 10);

            int width = resolutionType == 1 && customResolution.x > 0 ? customResolution.x : sourceDesc.width;
            int height = resolutionType == 1 && customResolution.y > 0 ? customResolution.y : sourceDesc.height;
            width = Mathf.Max(1, width / downScale);
            height = Mathf.Max(1, height / downScale);

            TextureDesc blurDesc = sourceDesc;
            blurDesc.name = $"_lilShoostKawaseBlur_{layerIndex}";
            blurDesc.width = width;
            blurDesc.height = height;
            blurDesc.clearBuffer = false;
            blurDesc.depthBufferBits = 0;

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            float screenRatio = Mathf.Max(1.0f, sourceDesc.width) / Mathf.Max(1.0f, sourceDesc.height);
            float iterationStep = 1.0f / downScale;

            AddKawasePass(renderGraph, source, blurA, material, 0, radius, screenRatio, runtimeLayer.settings, profilingSampler, passName);

            TextureHandle current = blurA;
            TextureHandle next = blurB;
            for (int i = 0; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                AddKawasePass(renderGraph, passSource, passDestination, material, 0, radius + (i * iterationStep), screenRatio, runtimeLayer.settings, profilingSampler, passName);
                current = passDestination;
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddKawasePass(renderGraph, current, destination, material, 0, radius + (iterations * iterationStep), screenRatio, runtimeLayer.settings, profilingSampler, passName);
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

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            float screenRatio = Mathf.Max(1.0f, sourceDesc.width) / Mathf.Max(1.0f, sourceDesc.height);

            TextureHandle current = AddIrisPass(renderGraph, source, blurA, material, 0, parameters, screenRatio, runtimeLayer.settings, profilingSampler, passName);
            TextureHandle next = blurB;
            for (int i = 1; i < parameters.iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddIrisPass(renderGraph, passSource, passDestination, material, 1, parameters, screenRatio, runtimeLayer.settings, profilingSampler, passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddIrisPass(renderGraph, source, destination, material, 2, parameters, screenRatio, runtimeLayer.settings, profilingSampler, passName, current);
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

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            TextureHandle current = AddRgbBlurV2Pass(renderGraph, source, blurA, material, 0, radius, runtimeLayer.settings, profilingSampler, passName);
            TextureHandle next = blurB;
            for (int i = 1; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddRgbBlurV2Pass(renderGraph, passSource, passDestination, material, 0, radius + i, runtimeLayer.settings, profilingSampler, passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddRgbBlurV2Pass(renderGraph, source, destination, material, 1, radius, runtimeLayer.settings, profilingSampler, passName, current);
        }

        private TextureHandle RecordChangeFrameRateLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex, UniversalCameraData cameraData)
        {
            int cameraId = cameraData.camera != null ? cameraData.camera.GetInstanceID() : 0;
            ChangeFrameRateState state = GetChangeFrameRateState(cameraId, cameraData.cameraTargetDescriptor);
            TextureHandle frozenFrameTexture = renderGraph.ImportTexture(state.frozenTexture);

            if (ShouldRefreshChangeFrameRateState(state, runtimeLayer.settings, out int targetFrameRate, out double now))
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{passName} Change Frame Rate Capture", out PassData passData, profilingSampler))
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
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{passName} Change Frame Rate", out PassData passData, profilingSampler))
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

        private TextureHandle AddKawasePass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float radius,
            float screenRatio,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} Kawase", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = radius;
                passData.screenRatio = screenRatio;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, data.screenRatio);
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
    }
}
