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
        private Material fallbackMaterial;
        private Shader fallbackShader;
        private bool warnedMissingFallbackShader;

        public HoGeometryBufferSettings Settings => settings;

        public override void Create()
        {
            outputPass = new HoGeometryBufferPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureFallbackMaterial();
            outputPass?.Setup(settings, renderTargets, fallbackMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureFallbackMaterial();
            if (outputPass != null)
            {
                outputPass.Setup(settings, renderTargets, fallbackMaterial);
                renderer.EnqueuePass(outputPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            renderTargets.Release();
            outputPass = null;
            CoreUtils.Destroy(fallbackMaterial);
            fallbackMaterial = null;
            fallbackShader = null;
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
    }
}
