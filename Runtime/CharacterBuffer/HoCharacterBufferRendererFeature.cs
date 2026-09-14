#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// Ho-CharacterBuffer：用"ID + 覆盖率"取代 MetadataBuffer 的位掩码（规划 §5）。
    /// 与 MetadataBuffer **并存**（暂时不删），两者互不依赖：CB 不读 MetadataBuffer 的任何产物。
    /// </summary>
    [DisallowMultipleRendererFeature("Ho-CharacterBuffer")]
    public sealed class HoCharacterBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoCharacterBufferSettings settings = new HoCharacterBufferSettings();

        private readonly HoCharacterBufferRenderTargets renderTargets = new HoCharacterBufferRenderTargets();
        private HoCharacterBufferPass outputPass;
        private Material fallbackMaterial;
        private Material resolveMaterial;
        private Shader fallbackShader;
        private Shader resolveShader;
        private bool registeredCameraReset;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingResolveShader;
        private bool warnedUnsupportedPlatform;
        private bool warnedSelectionLayers;

        public HoCharacterBufferSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            outputPass = new HoCharacterBufferPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            EnsureMaterials();
            outputPass?.Setup(settings, renderTargets, fallbackMaterial, resolveMaterial, settings.RequestedSampleCount, ShouldProduceSelections());
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            EnsureMaterials();
            if (outputPass == null)
            {
                return;
            }

            outputPass.Setup(settings, renderTargets, fallbackMaterial, resolveMaterial, settings.RequestedSampleCount, ShouldProduceSelections());
            renderer.EnqueuePass(outputPass);
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            outputPass?.ReleaseCompatibilityResources();
            outputPass = null;
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(resolveMaterial);
            fallbackMaterial = null;
            resolveMaterial = null;
            fallbackShader = null;
            resolveShader = null;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetCharacterBufferState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetCharacterBufferState;
            registeredCameraReset = false;
        }

        private static void ResetCharacterBufferState(ScriptableRenderContext context, Camera camera)
        {
            HoCharacterBufferPass.ResetGlobalState();
        }

        private void ReleaseCompatibilityResources(bool resetGlobalState = false)
        {
            outputPass?.ReleaseCompatibilityResources(resetGlobalState);
            renderTargets.Release();
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return false;
            }

            if (!HoCharacterBufferRegistry.SupportsStructuredBuffer)
            {
                // 不静默降级：平台拿不到 StructuredBuffer 时 palette 根本读不了，宁可整条不跑并告警。
                if (!warnedUnsupportedPlatform)
                {
                    warnedUnsupportedPlatform = true;
                    Debug.LogWarning("[Ho-CharacterBuffer] 平台不支持 StructuredBuffer（shader level < 4.5），feature 已停用。");
                }

                return false;
            }

            if (settings.RequestedSelectionLayerCount > 2 && !warnedSelectionLayers)
            {
                warnedSelectionLayers = true;
                Debug.LogWarning("[Ho-CharacterBuffer] P1 只实现 2 个选择层/像素（一张选择图）；4 层配置暂按 2 跑。");
            }

            return true;
        }

        private bool ShouldProduceSelections()
        {
            return settings != null &&
                settings.RequestedSelectionLayerCount > 0 &&
                HoCharacterBufferRegistry.SelectionCount > 0;
        }

        private void EnsureMaterials()
        {
            EnsureFallbackMaterial();
            EnsureResolveMaterial();
        }

        private void EnsureFallbackMaterial()
        {
            Shader shader = settings != null && settings.fallbackShader != null
                ? settings.fallbackShader
                : Shader.Find(HoCharacterBufferShaderConstants.FallbackShaderName);

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
                    Debug.LogWarning($"[Ho-CharacterBuffer] 找不到 fallback shader '{HoCharacterBufferShaderConstants.FallbackShaderName}'，" +
                                     "未带 HoCharacterBuffer pass 的材质不会写 ID。");
                }

                return;
            }

            fallbackMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureResolveMaterial()
        {
            Shader shader = Shader.Find(HoCharacterBufferShaderConstants.ResolveShaderName);
            if (resolveMaterial != null && resolveShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(resolveMaterial);
            resolveMaterial = null;
            resolveShader = shader;
            if (shader == null)
            {
                if (!warnedMissingResolveShader)
                {
                    warnedMissingResolveShader = true;
                    Debug.LogWarning($"[Ho-CharacterBuffer] 找不到 resolve shader '{HoCharacterBufferShaderConstants.ResolveShaderName}'，" +
                                     "MSAA 下无法产生覆盖率。");
                }

                return;
            }

            resolveMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
