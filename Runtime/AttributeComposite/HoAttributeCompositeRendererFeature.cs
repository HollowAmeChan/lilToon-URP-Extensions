#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// **AC（Ho-AttributeComposite）= 语义遮罩与合成属性的唯一逻辑入口**（规划 §1）。
    /// 本轮（R3-obj）只落 object 来源：`SemanticResolve` 把 OB 身份池 + 部件行标签解压成
    /// 固定 lane 的 Selection 池，消费者经 `HoAC_*` 查询，不再自己解码 OB 的 packing。
    /// <para>
    /// 还没落地（不要假装有）：surface 来源与五种 sourceMode 的合成（等 SB，R4b）、
    /// 合成数值属性（`constant &lt; surface`，R4c）、Selection 池的 16 lane MRT 分批。
    /// </para>
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

        public HoAttributeCompositeSettings Settings => settings;

        public override void Create()
        {
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
            if (volume == null || !volume.IsActive())
            {
                return runtimeSettings;
            }

            runtimeSettings.debugMode = volume.debugMode.value;
            runtimeSettings.debugInSceneView = volume.debugInSceneView.value;
            runtimeSettings.debugInGameView = volume.debugInGameView.value;
            return runtimeSettings;
        }

        private static HoAttributeCompositeVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoAttributeCompositeVolume>() : null;
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
