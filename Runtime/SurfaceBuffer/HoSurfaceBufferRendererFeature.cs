#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// **Ho-SurfaceBuffer（SB）= 表面数值 buffer**：回答"表面是什么样"。
    /// 几何在 GB、身份在 OB、合成在 AC；SB 只发布五张数值图 + internal owner（规划 §1）。
    /// <para>
    /// 本轮范围（数值面）：`Color / Normal / Material / Reflection / Classification` + owner，
    /// 由材质侧 `HoSurfaceBuffer` pass 一趟写全。**透明不生产**（队列上限压在不透明段末尾）。
    /// 还没落地：SB 的 MSAA semantic lane pass（喂 AC 的 surface 来源），见规划 §0.4/§3。
    /// </para>
    /// </summary>
    [DisallowMultipleRendererFeature("Ho-SurfaceBuffer")]
    public sealed class HoSurfaceBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoSurfaceBufferSettings settings = new HoSurfaceBufferSettings();

        private readonly HoSurfaceBufferSettings runtimeSettings = new HoSurfaceBufferSettings();
        private HoSurfaceBufferPass pass;
        private HoSurfaceBufferDebugPass debugPass;
        private Material debugMaterial;
        private Shader debugShader;
        private bool warnedMissingDebugShader;

        public HoSurfaceBufferSettings Settings => settings;

        public override void Create()
        {
            pass = new HoSurfaceBufferPass();
            debugPass = new HoSurfaceBufferDebugPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoSurfaceBufferSettings activeSettings = ResolveSettings(in renderingData);
            if (activeSettings == null || !activeSettings.enabled)
            {
                pass?.ReleaseCompatibilityResources();
                debugPass?.ReleaseCompatibilityResources();
                HoSurfaceBufferPass.ResetGlobalState();
                return;
            }

            int minQueue = activeSettings.minRenderQueue;
            int maxQueue = Mathf.Max(minQueue, activeSettings.maxRenderQueue);
            var filteringSettings = new FilteringSettings(
                new RenderQueueRange { lowerBound = minQueue, upperBound = maxQueue },
                activeSettings.layerMask.value);

            pass?.Setup(activeSettings, filteringSettings);
            renderer.EnqueuePass(pass);

            if (WantsDebugView(activeSettings, renderingData.cameraData.cameraType))
            {
                EnsureDebugMaterial();
                if (debugMaterial != null)
                {
                    debugPass?.Setup(activeSettings, debugMaterial, renderer.cameraColorTargetHandle);
                    renderer.EnqueuePass(debugPass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            debugPass?.ReleaseCompatibilityResources();
            debugPass = null;
            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugShader = null;
        }

        private HoSurfaceBufferSettings ResolveSettings(in RenderingData renderingData)
        {
            runtimeSettings.CopyFrom(settings);
            HoSurfaceBufferVolume volume = GetVolumeComponent();
            if (volume == null || !volume.IsActive())
            {
                return runtimeSettings;
            }

            runtimeSettings.debugMode = volume.debugMode.value;
            runtimeSettings.debugInSceneView = volume.debugInSceneView.value;
            runtimeSettings.debugInGameView = volume.debugInGameView.value;
            return runtimeSettings;
        }

        private static HoSurfaceBufferVolume GetVolumeComponent()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<HoSurfaceBufferVolume>() : null;
        }

        private static bool WantsDebugView(HoSurfaceBufferSettings activeSettings, CameraType cameraType)
        {
            if (activeSettings.debugMode == HoSurfaceBufferDebugMode.Off)
            {
                return false;
            }

            bool isSceneView = cameraType == CameraType.SceneView;
            return isSceneView ? activeSettings.debugInSceneView : activeSettings.debugInGameView;
        }

        private void EnsureDebugMaterial()
        {
            Shader shader = Shader.Find(HoSurfaceBufferShaderConstants.DebugShaderName);
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
                    Debug.LogWarning($"[Ho-SurfaceBuffer] 找不到 shader '{HoSurfaceBufferShaderConstants.DebugShaderName}'，调试视图会跳过。");
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }
}
