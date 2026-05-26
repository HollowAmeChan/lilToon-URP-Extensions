#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    [DisallowMultipleRendererFeature("Ho-MetadataBuffer")]
    public sealed class HoMetadataBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoMetadataBufferSettings settings = new HoMetadataBufferSettings();

        private readonly HoMetadataBufferRenderTargets renderTargets = new HoMetadataBufferRenderTargets();
        private HoMetadataBufferPass outputPass;
        private HoMetadataBufferDebugPass debugPass;
        private Material clearMaterial;
        private Material fallbackMaterial;
        private Material debugMaterial;
        private Shader clearShader;
        private Shader fallbackShader;
        private Shader debugShader;
        private bool registeredCameraReset;
        private bool warnedMissingClearShader;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingDebugShader;

        public HoMetadataBufferSettings Settings => settings;

        public override void Create()
        {
            settings?.ClampCustomChannels();
            RegisterCameraReset();
            outputPass = new HoMetadataBufferPass();
            debugPass = new HoMetadataBufferDebugPass();
        }

        private void OnValidate()
        {
            settings?.ClampCustomChannels();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            EnsureMaterials(ShouldDebug(in renderingData));
            outputPass?.Setup(settings, renderTargets, clearMaterial, fallbackMaterial);
            debugPass?.Setup(settings, renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            bool shouldDebug = ShouldDebug(in renderingData);
            EnsureMaterials(shouldDebug);
            if (outputPass != null)
            {
                outputPass.SetupRenderGraph(settings, renderTargets, clearMaterial, fallbackMaterial);
                renderer.EnqueuePass(outputPass);
            }

            if (debugPass != null && shouldDebug)
            {
                debugPass.SetupRenderGraph(settings, renderTargets, debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
            else
            {
                debugPass?.ReleaseCompatibilityResources();
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            outputPass = null;
            debugPass?.Dispose();
            debugPass = null;
            CoreUtils.Destroy(clearMaterial);
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(debugMaterial);
            clearMaterial = null;
            fallbackMaterial = null;
            debugMaterial = null;
            clearShader = null;
            fallbackShader = null;
            debugShader = null;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetMetadataBufferState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetMetadataBufferState;
            registeredCameraReset = false;
        }

        private static void ResetMetadataBufferState(ScriptableRenderContext context, Camera camera)
        {
            HoMetadataBufferPass.ResetGlobalState();
        }

        private void ReleaseCompatibilityResources(bool resetGlobalState = false)
        {
            outputPass?.ReleaseCompatibilityResources(resetGlobalState);
            debugPass?.ReleaseCompatibilityResources();
            renderTargets.Release();
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private bool ShouldDebug(in RenderingData renderingData)
        {
            if (settings == null || settings.debugMode == HoMetadataBufferDebugMode.Off)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return (cameraType == CameraType.SceneView && settings.debugInSceneView)
                || (cameraType == CameraType.Game && settings.debugInGameView);
        }

        private void EnsureMaterials(bool includeDebug)
        {
            EnsureClearMaterial();
            EnsureFallbackMaterial();
            if (includeDebug)
            {
                EnsureDebugMaterial();
            }
        }

        private void EnsureClearMaterial()
        {
            Shader shader = Shader.Find(HoMetadataBufferShaderConstants.ClearShaderName);

            if (clearMaterial != null && clearShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(clearMaterial);
            clearMaterial = null;
            clearShader = shader;
            if (shader == null)
            {
                if (!warnedMissingClearShader)
                {
                    warnedMissingClearShader = true;
                    Debug.LogWarning($"MetadataBuffer clear pass is unavailable because shader '{HoMetadataBufferShaderConstants.ClearShaderName}' could not be found.");
                }

                return;
            }

            clearMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureFallbackMaterial()
        {
            Shader shader = settings != null && settings.fallbackShader != null
                ? settings.fallbackShader
                : Shader.Find(HoMetadataBufferShaderConstants.FallbackShaderName);

            if (fallbackMaterial != null && fallbackShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(fallbackMaterial);
            fallbackMaterial = null;
            fallbackShader = shader;
            if (shader == null)
            {
                if (!warnedMissingFallbackShader)
                {
                    warnedMissingFallbackShader = true;
                    Debug.LogWarning($"MetadataBuffer fallback output is unavailable because shader '{HoMetadataBufferShaderConstants.FallbackShaderName}' could not be found.");
                }

                return;
            }

            fallbackMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureDebugMaterial()
        {
            Shader shader = settings != null && settings.debugShader != null
                ? settings.debugShader
                : Shader.Find(HoMetadataBufferShaderConstants.DebugShaderName);

            if (debugMaterial != null && debugShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugShader = shader;
            if (shader == null)
            {
                if (!warnedMissingDebugShader)
                {
                    warnedMissingDebugShader = true;
                    Debug.LogWarning($"MetadataBuffer debug view is unavailable because shader '{HoMetadataBufferShaderConstants.DebugShaderName}' could not be found.");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
