using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    [DisallowMultipleRendererFeature("Ho-ImageProcess")]
    [ExecuteAlways]
    public sealed class ImageProcessRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private ImageProcessStackSettings settings = new ImageProcessStackSettings();

        private readonly ImageProcessMaterialCache materialCache = new ImageProcessMaterialCache();
        private readonly List<ImageProcessRuntimeLayer> afterPostProcessLayers = new List<ImageProcessRuntimeLayer>();
        private ImageProcessPass afterPostProcessPass;

        [Tooltip("Match HTrace-style setup: the renderer feature installs the pass, and Volume profiles provide the active settings.")]
        public bool UseVolumes = true;

        public static bool IsUseVolumes { get; private set; } = true;

        public ImageProcessStackSettings Settings => settings;

        public override void Create()
        {
            IsUseVolumes = UseVolumes;
            afterPostProcessPass = new ImageProcessPass("Ho-ImageProcess AfterURPPost");
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ImageProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                ImageProcessRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    GetSkipReason(in renderingData, volume));
                afterPostProcessPass?.ClearRuntimeLayers();
                if (ShouldReleaseRuntimeResources(in renderingData, volume))
                {
                    afterPostProcessPass?.ReleaseRuntimeResources();
                }

                return;
            }

            BuildRuntimeLayers(volume);
            SetupCompatibilityPass(afterPostProcessPass, renderer.cameraColorTargetHandle, afterPostProcessLayers, ScreenProcessRenderPassEvents.ImageProcessFinalStack);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ImageProcessStackVolume volume = GetVolumeComponent();
            if (!ShouldRender(in renderingData, volume))
            {
                afterPostProcessPass?.ClearRuntimeLayers();
                if (ShouldReleaseRuntimeResources(in renderingData, volume))
                {
                    afterPostProcessPass?.ReleaseRuntimeResources();
                }

                return;
            }

            BuildRuntimeLayers(volume);
            if (afterPostProcessLayers.Count == 0)
            {
                ImageProcessRuntimeDiagnostics.PublishSkipped(
                    renderingData.cameraData.camera,
                    "RendererFeature",
                    "没有可运行的 ImageProcess layer。");
            }

            EnqueueRenderGraphPass(renderer, afterPostProcessPass, afterPostProcessLayers, ScreenProcessRenderPassEvents.ImageProcessFinalStack);
        }

        protected override void Dispose(bool disposing)
        {
            afterPostProcessPass?.Dispose();
            afterPostProcessPass = null;
            materialCache.Dispose();
            afterPostProcessLayers.Clear();
        }

        private bool ShouldRender(in RenderingData renderingData, ImageProcessStackVolume volume)
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

        private string GetSkipReason(in RenderingData renderingData, ImageProcessStackVolume volume)
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
                return "未找到 ImageProcess Volume。";
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.SceneView && !volume.ShowInSceneView.value)
            {
                return "Scene View 渲染已关闭。";
            }

            if (!volume.IsActive())
            {
                return "ImageProcess Volume 未激活。";
            }

            return cameraType == CameraType.Game || cameraType == CameraType.SceneView
                ? "未入队。"
                : "当前 camera type 不支持。";
        }

        private bool ShouldReleaseRuntimeResources(in RenderingData renderingData, ImageProcessStackVolume volume)
        {
            if (settings == null || !settings.enabled || !UseVolumes || volume == null || !volume.IsActive())
            {
                return true;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game;
        }

        private void BuildRuntimeLayers(ImageProcessStackVolume volume)
        {
            ImageProcessRuntimeLayerBuilder.Build(volume, settings, materialCache, afterPostProcessLayers);
        }

        private void SetupCompatibilityPass(
            ImageProcessPass pass,
            RTHandle cameraColorTarget,
            List<ImageProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                pass?.ReleaseRuntimeResources();
                return;
            }

            pass.Setup(cameraColorTarget, layers, passEvent);
        }

        private void EnqueueRenderGraphPass(
            ScriptableRenderer renderer,
            ImageProcessPass pass,
            List<ImageProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            if (pass == null || layers.Count == 0)
            {
                pass?.ClearRuntimeLayers();
                pass?.ReleaseRuntimeResources();
                return;
            }

            pass.SetupRenderGraph(layers, passEvent);
            renderer.EnqueuePass(pass);
        }

        private static ImageProcessStackVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<ImageProcessStackVolume>() : null;
        }
    }
}
