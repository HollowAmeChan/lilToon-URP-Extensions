#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Debugging
{
    // Keep this type and script GUID so existing renderer assets remain valid.
    [DisallowMultipleRendererFeature("Ho-DebugPerformance")]
    public sealed class HoDebugTileRendererFeature : ScriptableRendererFeature
    {
        // Legacy constants/field are retained for serialized renderer compatibility.
        public const string NoneViewId = "";
        public const string AllRegisteredViewId = "__AllRegistered";
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool enabledForGameView = true;
        [SerializeField] private bool enabledForSceneView = true;
        [SerializeField, HideInInspector] private string selectedDebugViewId = NoneViewId;
        [SerializeField] private HoDebugTilePerformanceMode performanceMode = HoDebugTilePerformanceMode.Off;
        private HoDebugPerformanceOverlayPass performancePass;

        public override void Create()
        {
            performancePass = new HoDebugPerformanceOverlayPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ResolveVolume(out bool resolvedEnabled, out bool resolvedGameView, out bool resolvedSceneView,
                out HoDebugTilePerformanceMode resolvedPerformanceMode);
            if (resolvedPerformanceMode == HoDebugTilePerformanceMode.Off)
            {
                HoDebugPerformanceRegistry.SetRecordingEnabled(false);
            }

            if (!resolvedEnabled)
            {
                return;
            }

            CameraType type = renderingData.cameraData.cameraType;
            bool cameraEnabled = (resolvedGameView && type == CameraType.Game)
                || (resolvedSceneView && type == CameraType.SceneView);
            if (!cameraEnabled || resolvedPerformanceMode == HoDebugTilePerformanceMode.Off)
            {
                return;
            }

            HoDebugPerformanceRegistry.EnsureInitialized();
            HoDebugPerformanceRegistry.SetRecordingEnabled(true);
            HoDebugPerformanceRegistry.Update();
            performancePass.Setup(HoDebugPerformanceRegistry.BuildLines(resolvedPerformanceMode));
            renderer.EnqueuePass(performancePass);
        }

        protected override void Dispose(bool disposing)
        {
            performancePass = null;
            HoDebugPerformanceRegistry.SetRecordingEnabled(false);
        }

        private void ResolveVolume(out bool resolvedEnabled, out bool resolvedGameView, out bool resolvedSceneView,
            out HoDebugTilePerformanceMode resolvedPerformanceMode)
        {
            resolvedEnabled = enabled;
            resolvedGameView = enabledForGameView;
            resolvedSceneView = enabledForSceneView;
            resolvedPerformanceMode = performanceMode;

            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            HoDebugTileVolume volume = stack != null ? stack.GetComponent<HoDebugTileVolume>() : null;
            if (volume == null)
            {
                return;
            }

            if (volume.enable.overrideState) resolvedEnabled = volume.enable.value;
            if (volume.performance.overrideState) resolvedPerformanceMode = volume.performance.value;
            if (volume.debugInSceneView.overrideState) resolvedSceneView = volume.debugInSceneView.value;
            if (volume.debugInGameView.overrideState) resolvedGameView = volume.debugInGameView.value;
        }
    }
}
