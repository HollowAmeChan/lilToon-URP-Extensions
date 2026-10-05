#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// **AC（Ho-AttributeComposite）= 语义遮罩与合成属性的唯一逻辑入口**（AC 架构 §1）。
    /// 发布 GB 覆盖范围、OB 身份引用、SB 数值引用，并合成固定 8 lane 的 Selection 池。
    /// 消费者经 typed query 选择遮罩与范围。精确 sample 合成与按需求裁剪尚未实现。
    /// </summary>
    [DisallowMultipleRendererFeature("Ho-AttributeComposite")]
    public sealed class HoAttributeCompositeRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoAttributeCompositeSettings settings = new HoAttributeCompositeSettings();

        private readonly HoAttributeCompositeSettings runtimeSettings = new HoAttributeCompositeSettings();
        private HoAttributeCompositePass pass;
        private HoAttributeCompositeDebugPass debugPass;
        private Material resolveMaterial;
        private Material debugMaterial;
        private Shader resolveShader;
        private Shader debugShader;
        private bool warnedMissingResolveShader;
        private bool registeredCameraReset;

        public HoAttributeCompositeSettings Settings => settings;

        public override void Create()
        {
            if (!registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering += ResetCameraState;
                registeredCameraReset = true;
            }
            pass = new HoAttributeCompositePass();
            debugPass = new HoAttributeCompositeDebugPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoAttributeCompositeSettings activeSettings = ResolveSettings(in renderingData);
            if (activeSettings == null || !activeSettings.enabled)
            {
                pass?.ReleaseCompatibilityResources();
                debugPass?.ReleaseCompatibilityResources();
                HoAttributeCompositePass.ResetGlobalState();
                return;
            }

            EnsureMaterials();
            if (resolveMaterial == null)
            {
                HoAttributeCompositePass.ResetGlobalState();
                return;
            }

            pass?.UploadLaneCatalogIfNeeded();
            pass?.Setup(activeSettings, resolveMaterial);
            renderer.EnqueuePass(pass);

            if (WantsDebugView(activeSettings, renderingData.cameraData.cameraType))
            {
                debugPass?.Setup(activeSettings, debugMaterial, renderer.cameraColorTargetHandle);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering -= ResetCameraState;
                registeredCameraReset = false;
            }
            pass?.Dispose();
            pass = null;
            debugPass?.ReleaseCompatibilityResources();
            debugPass = null;
            CoreUtils.Destroy(resolveMaterial);
            CoreUtils.Destroy(debugMaterial);
            resolveMaterial = null;
            debugMaterial = null;
            resolveShader = null;
            debugShader = null;
        }

        private HoAttributeCompositeSettings ResolveSettings(in RenderingData renderingData)
        {
            runtimeSettings.CopyFrom(settings);
            HoAttributeCompositeVolume volume = GetVolumeComponent();
            if (volume == null || !volume.active)
            {
                return runtimeSettings;
            }

            if (volume.enable.overrideState) runtimeSettings.enabled = volume.enable.value;
            if (!volume.IsActive()) return runtimeSettings;
            runtimeSettings.debugMode = volume.debugMode.value;
            runtimeSettings.debugSemanticName = volume.debugSemanticName.value;
            runtimeSettings.debugInSceneView = volume.debugInSceneView.value;
            runtimeSettings.debugInGameView = volume.debugInGameView.value;
            return runtimeSettings;
        }

        private static HoAttributeCompositeVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoAttributeCompositeVolume>() : null;
        }

        private static void ResetCameraState(ScriptableRenderContext context, Camera camera)
        {
            HoAttributeCompositePass.ResetGlobalState();
        }

        private static bool WantsDebugView(HoAttributeCompositeSettings activeSettings, CameraType cameraType)
        {
            if (activeSettings.debugMode == HoAttributeCompositeDebugMode.Off)
            {
                return false;
            }

            bool isSceneView = cameraType == CameraType.SceneView;
            return isSceneView ? activeSettings.debugInSceneView : activeSettings.debugInGameView;
        }

        private void EnsureMaterials()
        {
            EnsureMaterial(ref resolveMaterial, ref resolveShader, HoAttributeCompositeShaderConstants.ResolveShaderName, ref warnedMissingResolveShader);
            EnsureMaterial(ref debugMaterial, ref debugShader, HoAttributeCompositeShaderConstants.DebugShaderName, ref warnedMissingResolveShader);
        }

        private static void EnsureMaterial(ref Material material, ref Shader cachedShader, string shaderName, ref bool warned)
        {
            Shader shader = Shader.Find(shaderName);
            if (material != null && cachedShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(material);
            material = null;
            cachedShader = shader;
            if (shader == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"[Ho-AttributeComposite] 找不到 shader '{shaderName}'，相关步骤会跳过。");
                }

                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
