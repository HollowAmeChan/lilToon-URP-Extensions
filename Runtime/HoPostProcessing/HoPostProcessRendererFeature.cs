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
                return;
            }

            BuildRuntimeLayers(volume);
            SetupCompatibilityPass(pass, renderer.cameraColorTargetHandle, runtimeLayers);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoPostProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
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

        private static void SetupCompatibilityPass(
            HoPostProcessPass pass,
            RTHandle cameraColorTarget,
            List<HoPostProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
                return;
            }

            pass.Setup(cameraColorTarget, layers, HoPostProcessRenderPassEvents.HoPostStack);
        }

        private static void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            HoPostProcessPass pass,
            List<HoPostProcessRuntimeLayer> layers)
        {
            if (pass == null || layers.Count == 0)
            {
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
        private readonly List<HoPostProcessRuntimeLayer> runtimeLayers = new List<HoPostProcessRuntimeLayer>();
        private readonly ProfilingSampler profilingSampler;
        private readonly string passName;
        private RTHandle cameraColorTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private bool warnedBackBuffer;

        private sealed class PassData
        {
            public TextureHandle source;
            public HoPostProcessLayer layer;
            public Material material;
            public int passIndex;
        }

        public HoPostProcessPass(string passName)
        {
            this.passName = passName;
            profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(RTHandle cameraColorTarget, List<HoPostProcessRuntimeLayer> layers, RenderPassEvent passEvent)
        {
            this.cameraColorTarget = cameraColorTarget;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(List<HoPostProcessRuntimeLayer> layers, RenderPassEvent passEvent)
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
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoPostProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoPostProcessShaderConstants.TempTextureBName);
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
                    HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material);
                    Blitter.BlitCameraTexture(cmd, source, destination, runtimeLayer.material, Mathf.Max(0, runtimeLayer.settings.passIndex));
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
                    Debug.LogWarning($"{passName} skipped because the active color target is the backbuffer. The HoPost stack requires an intermediate color texture.");
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
                HoPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = $"_lilHoPostProcessLayer{i}";
                destinationDesc.clearBuffer = false;
                destinationDesc.depthBufferBits = 0;
                EnsureHdrTextureDesc(ref destinationDesc);
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
            ConfigureInput(ScriptableRenderPassInput.Color);
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
            if (layer.texture != null)
            {
                material.SetTexture(HoPostProcessShaderConstants.LayerTextureId, layer.texture);
            }
        }
    }
}
