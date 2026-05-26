#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    [DisallowMultipleRendererFeature("Ho-ShadowCast")]
    public sealed class HoShadowCastRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoShadowCastSettings settings = new HoShadowCastSettings();

        private readonly HoShadowCastRenderTargets renderTargets = new HoShadowCastRenderTargets();
        private readonly HoShadowCastDebugMaterial debugMaterialCache = new HoShadowCastDebugMaterial();
        private HoShadowCastPass pass;
        private HoShadowCastDebugPass debugPass;
        private bool registeredCameraReset;

        public HoShadowCastSettings Settings => settings;

        public override void Create()
        {
            settings?.Validate();
            RegisterCameraReset();
            pass = new HoShadowCastPass();
            debugPass = new HoShadowCastDebugPass();
        }

        private void OnValidate()
        {
            settings?.Validate();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            HoShadowCastFrameConfig config = HoShadowCastFrameConfig.Resolve(settings);
            if (!ShouldRender(in renderingData, config))
            {
                return;
            }

            pass?.Setup(settings, config, renderTargets);
            if (debugPass != null && ShouldDebug(config))
            {
                if (debugMaterialCache.Ensure())
                {
                    debugPass.Setup(config, renderTargets, renderer.cameraColorTargetHandle, debugMaterialCache.Material);
                }
            }
            else
            {
                debugPass?.ReleaseCompatibilityResources();
                debugMaterialCache.Release();
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoShadowCastFrameConfig config = HoShadowCastFrameConfig.Resolve(settings);
            if (!ShouldRender(in renderingData, config))
            {
                return;
            }

            if (pass == null)
            {
                return;
            }

            pass.SetupRenderGraph(settings, config);
            renderer.EnqueuePass(pass);

            if (debugPass != null && ShouldDebug(config))
            {
                if (debugMaterialCache.Ensure())
                {
                    debugPass.SetupRenderGraph(config, debugMaterialCache.Material);
                    renderer.EnqueuePass(debugPass);
                }
            }
            else
            {
                debugPass?.ReleaseCompatibilityResources();
                debugMaterialCache.Release();
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            pass = null;
            debugPass?.Dispose();
            debugPass = null;
            debugMaterialCache.Release();
        }

        private bool ShouldRender(in RenderingData renderingData, HoShadowCastFrameConfig config)
        {
            if (settings == null || !settings.enabled || config == null)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private static bool ShouldDebug(HoShadowCastFrameConfig config)
        {
            return config != null && config.debugMode != HoShadowCastDebugMode.Off;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetShadowCastState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetShadowCastState;
            registeredCameraReset = false;
        }

        private static void ResetShadowCastState(ScriptableRenderContext context, Camera camera)
        {
            HoShadowCastPublisher.ResetAllImmediate();
        }
    }
}
