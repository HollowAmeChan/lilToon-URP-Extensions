#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.AttributeComposite;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// **Ho-SurfaceBuffer（SB）= 表面数值 buffer**：回答"表面是什么样"。
    /// 几何在 GB、身份在 OB、合成在 AC；SB 只发布五张数值图 + internal owner（SB 架构 §1）。
    /// <para>
    /// 本轮范围（数值面）：`Color / Normal / Material / Reflection / Classification` + owner，
    /// 由材质侧 `HoSurfaceBuffer` pass 一趟写全。**透明不生产**（队列上限压在不透明段末尾）。
    /// 语义 lane（`enableSemanticLanes`）由 `HoSurfaceBufferSemanticPass` 单采样另跑一趟，
    /// 只允许收窄 / 细化 OB 的物体位（SB 架构 §0.4）。
    /// </para>
    /// </summary>
    [DisallowMultipleRendererFeature("Ho-SurfaceBuffer")]
    public sealed class HoSurfaceBufferRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoSurfaceBufferSettings settings = new HoSurfaceBufferSettings();

        private readonly HoSurfaceBufferSettings runtimeSettings = new HoSurfaceBufferSettings();
        private HoSurfaceBufferPass pass;
        private HoSurfaceBufferSemanticPass semanticPass;
        private HoSurfaceBufferCorrelatedSemanticPass correlatedPass;
        private HoSurfaceBufferDebugPass debugPass;
        private Material debugMaterial;
        private Shader debugShader;
        private bool warnedMissingDebugShader;
        private static bool warnedMrtCapacity;
        private static bool warnedSemanticMrtCapacity;
        private bool registeredCameraReset;

        public HoSurfaceBufferSettings Settings => settings;

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (runtimeSettings.enabled && WantsDebugView(runtimeSettings, renderingData.cameraData.cameraType))
                debugPass?.Setup(runtimeSettings, debugMaterial, renderer.cameraColorTargetHandle);
        }

        public override void Create()
        {
            HoAttributeCompositeConsumerRegistry.EnsureInitialized();
            pass = new HoSurfaceBufferPass();
            semanticPass = new HoSurfaceBufferSemanticPass();
            correlatedPass?.Dispose();
            correlatedPass = new HoSurfaceBufferCorrelatedSemanticPass();
            debugPass = new HoSurfaceBufferDebugPass();
            if (!registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering += ResetCameraState;
                registeredCameraReset = true;
            }
            #if UNITY_EDITOR
            if (WantsCorrelatedVariants()) correlatedPass.PrepareWriterVariants(settings, null);
            #endif
        }

        #if UNITY_EDITOR
        private bool WantsCorrelatedVariants() => isActive && settings != null && settings.enabled &&
            settings.enableSemanticLanes && settings.enableCorrelatedSemantics &&
            (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12);

        #endif

        private void ResetCameraState(ScriptableRenderContext context, Camera camera)
        {
            HoSurfaceBufferPass.ResetGlobalState();
            HoSurfaceBufferSemanticPass.ResetGlobalState();
            #if UNITY_EDITOR
            // Renderer material variants are selected during culling; preparing in RecordRenderGraph is too late.
            if (WantsCorrelatedVariants()) correlatedPass?.PrepareWriterVariants(settings, camera);
            #endif
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            HoAttributeCompositeConsumerRegistry.Remove(this, renderingData.cameraData.camera);
            HoSurfaceBufferSettings activeSettings = ResolveSettings(in renderingData);
            if (activeSettings == null || !activeSettings.enabled)
            {
                pass?.ReleaseCompatibilityResources();
                semanticPass?.ReleaseCompatibilityResources();
                debugPass?.ReleaseCompatibilityResources();
                HoSurfaceBufferPass.ResetGlobalState();
                HoSurfaceBufferSemanticPass.ResetGlobalState();
                HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "SurfaceBuffer disabled");
                return;
            }

            int minQueue = activeSettings.minRenderQueue;
            int maxQueue = Mathf.Max(minQueue, activeSettings.maxRenderQueue);

            // 6 个 MRT 是硬要求（五张数值图 + owner）。平台不够就**整条不跑并报错**，不静默降级
            // （SB 架构 §5 的同一条纪律）：少绑附件时 D3D 会直接丢掉整个 draw，表现正是"什么都没写"。
            if (SystemInfo.supportedRenderTargetCount < HoSurfaceBufferShaderConstants.ValueAttachmentCount)
            {
                if (!warnedMrtCapacity)
                {
                    warnedMrtCapacity = true;
                    Debug.LogError($"[Ho-SurfaceBuffer] 本平台只支持 {SystemInfo.supportedRenderTargetCount} 个 MRT，" +
                                   $"数值面需要 {HoSurfaceBufferShaderConstants.ValueAttachmentCount} 个（五张数值图 + owner）：整条不跑。");
                }

                HoSurfaceBufferPass.ResetGlobalState();
                HoSurfaceBufferSemanticPass.ResetGlobalState();
                HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "Surface attachment capacity unsupported");
                return;
            }

            var filteringSettings = new FilteringSettings(
                new RenderQueueRange { lowerBound = minQueue, upperBound = maxQueue },
                activeSettings.layerMask.value);

            pass?.Setup(activeSettings, filteringSettings);
            renderer.EnqueuePass(pass);

            // 语义 lane：5 个 MRT（owner + 4 张 lane 图）。不够就只关这一趟，数值面照跑并报错
            // ——AC 会因为没有 lane 而回落到物体位，不是静默错值。
            if (activeSettings.enableSemanticLanes)
            {
                if (SystemInfo.supportedRenderTargetCount < HoSurfaceBufferShaderConstants.SemanticAttachmentCount)
                {
                    if (!warnedSemanticMrtCapacity)
                    {
                        warnedSemanticMrtCapacity = true;
                        Debug.LogError($"[Ho-SurfaceBuffer] 本平台只支持 {SystemInfo.supportedRenderTargetCount} 个 MRT，" +
                                       $"语义 lane 需要 {HoSurfaceBufferShaderConstants.SemanticAttachmentCount} 个（owner + 4 张 lane 图）：" +
                                       $"这一趟不跑，AC 的 surface 语义会全部回落到 OB 的物体位。");
                    }

                    HoSurfaceBufferSemanticPass.ResetGlobalState();
                    HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "Semantic attachment capacity unsupported");
                }
                else
                {
                    semanticPass?.Setup(activeSettings, filteringSettings);
                    renderer.EnqueuePass(semanticPass);
                    if (activeSettings.enableCorrelatedSemantics)
                    {
                        correlatedPass?.Setup(activeSettings, filteringSettings, renderingData.cameraData.camera);
                        renderer.EnqueuePass(correlatedPass);
                    }
                    else
                    {
                        HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "Correlated semantics disabled");
                    }
                }
            }
            else
            {
                HoSurfaceBufferSemanticPass.ResetGlobalState();
                HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "Surface semantics disabled");
            }

            if (WantsDebugView(activeSettings, renderingData.cameraData.cameraType))
            {
                EnsureDebugMaterial();
                if (debugMaterial != null)
                {
                    HoACDemandResources demand = HoACDemandResources.Identity;
                    if (activeSettings.debugMode == HoSurfaceBufferDebugMode.SemanticLanes || activeSettings.debugMode == HoSurfaceBufferDebugMode.SemanticOwner)
                        demand |= HoACDemandResources.LegacySemantic;
                    else demand |= HoACDemandResources.SurfaceAttributes | HoACDemandResources.SurfaceColor | HoACDemandResources.SurfaceNormal;
                    HoAttributeCompositeConsumerRegistry.DeclareForCamera(renderingData.cameraData.camera, this, "SurfaceBuffer Debug",
                        System.Array.Empty<HoACQueryDescriptor>(), null, demand, 0,
                        (demand & HoACDemandResources.LegacySemantic) != 0 ? 255u : 0u);
                    debugPass?.Setup(activeSettings, debugMaterial, null);
                    renderer.EnqueuePass(debugPass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            HoAttributeCompositeConsumerRegistry.Remove(this);
            if (registeredCameraReset)
            {
                RenderPipelineManager.beginCameraRendering -= ResetCameraState;
                registeredCameraReset = false;
            }
            pass?.Dispose();
            pass = null;
            semanticPass?.Dispose();
            semanticPass = null;
            correlatedPass?.Dispose();
            correlatedPass = null;
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
