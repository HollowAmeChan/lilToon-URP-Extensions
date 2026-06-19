// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.TransparentPass
{
    [DisallowMultipleRendererFeature("Ho-Transparent")]
    public sealed class HoTransparentRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoTransparentSettings settings = new HoTransparentSettings();

        private HoTransparentStatePass activatePass;
        private HoTransparentPass drawPass;
        private HoTransparentStatePass resetPass;
        private bool registeredCameraReset;

        public HoTransparentSettings Settings => settings;

        public override void Create()
        {
            settings?.EnsurePasses();
            RegisterCameraReset();
            activatePass = new HoTransparentStatePass(1.0f);
            drawPass = new HoTransparentPass();
            resetPass = new HoTransparentStatePass(0.0f);
        }

        private void OnValidate()
        {
            settings?.EnsurePasses();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                HoTransparentStatePass.ResetGlobalState();
                return;
            }

            drawPass?.Setup(settings, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                HoTransparentStatePass.ResetGlobalState();
                return;
            }

            if (settings.publishActiveFlag && activatePass != null)
            {
                activatePass.Setup(settings.activatePassEvent);
                renderer.EnqueuePass(activatePass);
            }

            if (drawPass != null)
            {
                drawPass.SetupRenderGraph(settings);
                renderer.EnqueuePass(drawPass);
            }

            if (settings.publishActiveFlag && resetPass != null)
            {
                resetPass.Setup(settings.resetPassEvent);
                renderer.EnqueuePass(resetPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            activatePass = null;
            drawPass = null;
            resetPass = null;
            HoTransparentStatePass.ResetGlobalState();
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetTransparentState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetTransparentState;
            registeredCameraReset = false;
        }

        private static void ResetTransparentState(ScriptableRenderContext context, Camera camera)
        {
            HoTransparentStatePass.ResetGlobalState();
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || !settings.HasActivePasses)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }
    }
}
