#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// Ho-ObjectBuffer：用"ID + 覆盖率"取代 MetadataBuffer 的位掩码（规划 §5）。
    /// 与 MetadataBuffer **并存**（暂时不删），两者互不依赖：CB 不读 MetadataBuffer 的任何产物。
    /// </summary>
    [DisallowMultipleRendererFeature("Ho-ObjectBuffer")]
    public sealed class HoObjectBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoObjectBufferSettings settings = new HoObjectBufferSettings();

        private readonly HoObjectBufferRenderTargets renderTargets = new HoObjectBufferRenderTargets();
        private HoObjectBufferPass outputPass;
        private HoObjectBufferDebugPass debugPass;
        private Material fallbackMaterial;
        private Material resolveMaterial;
        private Material debugMaterial;
        private Shader fallbackShader;
        private Shader resolveShader;
        private Shader debugShader;
        private bool registeredCameraReset;
        private bool warnedMissingFallbackShader;
        private bool warnedMissingResolveShader;
        private bool warnedMissingDebugShader;
        private bool warnedUnsupportedPlatform;
        private bool warnedSelectionLayers;

        public HoObjectBufferSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            outputPass = new HoObjectBufferPass();
            debugPass = new HoObjectBufferDebugPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            ResolveVolume();
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            bool shouldDebug = ShouldDebug(in renderingData);
            EnsureMaterials(shouldDebug);
            outputPass?.Setup(settings, renderTargets, fallbackMaterial, resolveMaterial, settings.RequestedSampleCount, ShouldProduceSelections());
            debugPass?.Setup(settings, renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ResolveVolume();
            if (!ShouldRender(in renderingData))
            {
                ReleaseCompatibilityResources(true);
                return;
            }

            bool shouldDebug = ShouldDebug(in renderingData);
            EnsureMaterials(shouldDebug);
            if (outputPass == null)
            {
                return;
            }

            outputPass.Setup(settings, renderTargets, fallbackMaterial, resolveMaterial, settings.RequestedSampleCount, ShouldProduceSelections());
            renderer.EnqueuePass(outputPass);

            if (debugPass != null && shouldDebug)
            {
                debugPass.Setup(settings, renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            outputPass?.ReleaseCompatibilityResources();
            outputPass = null;
            debugPass?.ReleaseCompatibilityResources();
            debugPass = null;
            CoreUtils.Destroy(fallbackMaterial);
            CoreUtils.Destroy(resolveMaterial);
            CoreUtils.Destroy(debugMaterial);
            fallbackMaterial = null;
            resolveMaterial = null;
            debugMaterial = null;
            fallbackShader = null;
            resolveShader = null;
            debugShader = null;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetObjectBufferState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetObjectBufferState;
            registeredCameraReset = false;
        }

        private static void ResetObjectBufferState(ScriptableRenderContext context, Camera camera)
        {
            HoObjectBufferPass.ResetGlobalState();
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

            if (!HoObjectBufferRegistry.SupportsStructuredBuffer)
            {
                // 不静默降级：平台拿不到 StructuredBuffer 时 palette 根本读不了，宁可整条不跑并告警。
                if (!warnedUnsupportedPlatform)
                {
                    warnedUnsupportedPlatform = true;
                    Debug.LogWarning("[Ho-ObjectBuffer] 平台不支持 StructuredBuffer（shader level < 4.5），feature 已停用。");
                }

                return false;
            }

            if (settings.RequestedSelectionLayerCount > 2 && !warnedSelectionLayers)
            {
                warnedSelectionLayers = true;
                Debug.LogWarning("[Ho-ObjectBuffer] P1 只实现 2 个选择层/像素（一张选择图）；4 层配置暂按 2 跑。");
            }

            return true;
        }

        private void ResolveVolume()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            HoObjectBufferVolume volume = stack != null ? stack.GetComponent<HoObjectBufferVolume>() : null;
            if (volume == null || settings == null)
            {
                return;
            }

            if (volume.enable.overrideState) settings.enabled = volume.enable.value;
            if (volume.debugMode.overrideState) settings.debugMode = volume.debugMode.value;
            if (volume.debugInSceneView.overrideState) settings.debugInSceneView = volume.debugInSceneView.value;
            if (volume.debugInGameView.overrideState) settings.debugInGameView = volume.debugInGameView.value;
        }

        private bool ShouldProduceSelections()
        {
            return settings != null &&
                settings.RequestedSelectionLayerCount > 0 &&
                HoObjectBufferRegistry.SelectionCount > 0;
        }

        private bool ShouldDebug(in RenderingData renderingData)
        {
            if (settings == null || settings.debugMode == HoObjectBufferDebugMode.Off)
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
            EnsureResolveMaterial();
            if (includeDebug)
            {
                EnsureDebugMaterial();
            }
        }

        private void EnsureFallbackMaterial()
        {
            Shader shader = settings != null && settings.fallbackShader != null
                ? settings.fallbackShader
                : Shader.Find(HoObjectBufferShaderConstants.FallbackShaderName);

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
                    Debug.LogWarning($"[Ho-ObjectBuffer] 找不到 fallback shader '{HoObjectBufferShaderConstants.FallbackShaderName}'，" +
                                     "未带 HoObjectBuffer pass 的材质不会写 ID。");
                }

                return;
            }

            fallbackMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureResolveMaterial()
        {
            Shader shader = Shader.Find(HoObjectBufferShaderConstants.ResolveShaderName);
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
                    Debug.LogWarning($"[Ho-ObjectBuffer] 找不到 resolve shader '{HoObjectBufferShaderConstants.ResolveShaderName}'，" +
                                     "MSAA 下无法产生覆盖率。");
                }

                return;
            }

            resolveMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private void EnsureDebugMaterial()
        {
            Shader shader = settings != null && settings.debugShader != null
                ? settings.debugShader
                : Shader.Find(HoObjectBufferShaderConstants.DebugShaderName);

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
                    Debug.LogWarning($"[Ho-ObjectBuffer] 找不到 debug shader '{HoObjectBufferShaderConstants.DebugShaderName}'，调试视图不可用。");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
