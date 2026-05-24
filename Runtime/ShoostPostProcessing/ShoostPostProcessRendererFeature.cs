using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [DisallowMultipleRendererFeature("lilToon-Shoost Post Process Stack")]
    [ExecuteAlways]
    public sealed class ShoostPostProcessRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private ShoostPostProcessStackSettings settings = new ShoostPostProcessStackSettings();

        private readonly ShoostPostProcessMaterialCache materialCache = new ShoostPostProcessMaterialCache();
        private readonly List<ShoostPostProcessRuntimeLayer> afterPostProcessLayers = new List<ShoostPostProcessRuntimeLayer>();
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
            materialCache.Dispose();
            afterPostProcessLayers.Clear();
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
            ShoostPostProcessRuntimeLayerBuilder.Build(volume, settings, materialCache, afterPostProcessLayers);
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

            pass.Setup(cameraColorTarget, layers, passEvent);
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

            pass.SetupRenderGraph(layers, passEvent);
            renderer.EnqueuePass(pass);
        }

        private static ShoostPostProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<ShoostPostProcessStackVolume>() : null;
        }
    }
}
