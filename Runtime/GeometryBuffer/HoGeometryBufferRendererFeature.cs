#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    [DisallowMultipleRendererFeature("lilToon-GeometryBuffer")]
    public sealed class HoGeometryBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoGeometryBufferSettings settings = new HoGeometryBufferSettings();

        private readonly HoGeometryBufferRenderTargets renderTargets = new HoGeometryBufferRenderTargets();
        private HoGeometryBufferPass outputPass;
        private HoGeometryBufferDebugPass debugPass;
        private Material fallbackMaterial;
        private Material debugMaterial;
        private Shader fallbackShader;
        private Shader debugShader;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingDebugShader;

        public HoGeometryBufferSettings Settings => settings;

        public override void Create()
        {
            outputPass = new HoGeometryBufferPass();
            debugPass = new HoGeometryBufferDebugPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterials(ShouldDebug(in renderingData));
            outputPass?.Setup(settings, renderTargets, fallbackMaterial);
            debugPass?.Setup(settings, renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            bool shouldDebug = ShouldDebug(in renderingData);
            EnsureMaterials(shouldDebug);
            if (outputPass != null)
            {
                outputPass.Setup(settings, renderTargets, fallbackMaterial);
                renderer.EnqueuePass(outputPass);
            }

            if (debugPass != null && shouldDebug)
            {
                debugPass.SetupRenderGraph(settings, renderTargets, debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            renderTargets.Release();
            outputPass = null;
            debugPass?.Dispose();
            debugPass = null;
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(debugMaterial);
            fallbackMaterial = null;
            debugMaterial = null;
            fallbackShader = null;
            debugShader = null;
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
            if (settings == null || settings.debugMode == HoGeometryBufferDebugMode.Off)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return (cameraType == CameraType.SceneView && settings.debugInSceneView)
                || (cameraType == CameraType.Game && settings.debugInGameView);
        }

        private void EnsureMaterials(bool includeDebug)
        {
            EnsureFallbackMaterial();
            if (includeDebug)
            {
                EnsureDebugMaterial();
            }
        }

        private void EnsureFallbackMaterial()
        {
            Shader shader = settings != null && settings.fallbackShader != null
                ? settings.fallbackShader
                : Shader.Find(HoGeometryBufferShaderConstants.FallbackShaderName);

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
                    Debug.LogWarning($"GeometryBuffer fallback output is unavailable because shader '{HoGeometryBufferShaderConstants.FallbackShaderName}' could not be found.");
                }

                return;
            }

            fallbackMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureDebugMaterial()
        {
            Shader shader = settings != null && settings.debugShader != null
                ? settings.debugShader
                : Shader.Find(HoGeometryBufferShaderConstants.DebugShaderName);

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
                    Debug.LogWarning($"GeometryBuffer debug view is unavailable because shader '{HoGeometryBufferShaderConstants.DebugShaderName}' could not be found.");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
