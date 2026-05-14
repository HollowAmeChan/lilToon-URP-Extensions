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

        private sealed class PassData
        {
            public TextureHandle source;
            public ShoostPostProcessLayer layer;
            public Material material;
            public int passIndex;
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
        }

        public void SetupRenderGraph(List<ShoostPostProcessRuntimeLayer> layers, RenderPassEvent passEvent)
        {
            CopyLayers(layers);
            ConfigurePass(passEvent);
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
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
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
            material.SetFloat(ShoostPostProcessShaderConstants.IntensityId, layer.intensity);
            material.SetFloat(ShoostPostProcessShaderConstants.LayerBlendModeId, (float)layer.blendMode);
            material.SetColor(ShoostPostProcessShaderConstants.LayerColorId, layer.color);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams0Id, layer.parameters0);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams1Id, layer.parameters1);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams2Id, layer.parameters2);
            material.SetVector(ShoostPostProcessShaderConstants.LayerParams3Id, layer.parameters3);
            if (layer.texture != null)
            {
                material.SetTexture(ShoostPostProcessShaderConstants.LayerTextureId, layer.texture);
            }
        }
    }
}
