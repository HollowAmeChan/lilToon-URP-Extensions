using lilToon.URP.Extensions.Debugging;
#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTShadow
{
    public enum HoRTShadowReconstruction
    {
        [InspectorName("原始参考")] Raw,
        [InspectorName("SIGMA 无历史")] SigmaSingleFrame,
        [InspectorName("SIGMA 稳定预览")] SigmaPreview
    }
    public enum HoRTShadowLightSelection
    {
        [InspectorName("主方向灯")] Main,
        [InspectorName("场景声明灯")] Declared,
        [InspectorName("全部支持灯")] AllSupported
    }
    [Serializable]
    public sealed class HoRTShadowSettings
    {
        [InspectorName("启用")] public bool enabled = true;
        [InspectorName("Scene View")] public bool inSceneView = true;
        [InspectorName("请求灯范围")] public HoRTShadowLightSelection lightSelection = HoRTShadowLightSelection.Main;
        [InspectorName("灯所在层")] public LayerMask lightLayers = -1;
        [InspectorName("投射物层")] public LayerMask geometryLayers = -1;
        [InspectorName("排除投射材质")] public List<Material> excludedRayMaterials = new();
        [InspectorName("射线偏移（米）"), Min(0.000001f), Tooltip("沿出射侧网格法线移动起点，并作为沿射线的最小距离，减少掠射角自遮挡。过大会丢失紧贴表面的真实细影。")]
        public float rayBias = .0001f;
        [InspectorName("射线距离上限（米）"), Range(.01f, 65504)] public float maxDistance = 10000;
        [InspectorName("光源角直径（度）"), Range(0, 8)] public float angularDiameter;
        [InspectorName("原始参考样本数"), Range(1, 4096), Tooltip("只用于原始参考。SIGMA 按官方统计契约每像素输入一个真实光源样本。")]
        public int samplesPerPixel = 1;
        [InspectorName("采样种子")] public int samplingSeed;
        [InspectorName("阴影重建")] public HoRTShadowReconstruction reconstruction = HoRTShadowReconstruction.Raw;
        [InspectorName("无历史同帧估计数"), Range(1, 256), Tooltip("只用于 SIGMA 无历史模式。每次追单个灯样本并独立降噪，再平均线性可见度；原始遮挡统计约束薄影丢失，成本随次数增加，不等待后续帧。")]
        public int singleFrameEstimates = 1;
        [InspectorName("调试模式")] public HoRTShadowDebugMode debugMode;
        [InspectorName("调试灯槽"), Min(0)] public int debugLight;
        [InspectorName("距离显示上限（米）"), Min(.0001f)] public float debugDistance = 1;
        [InspectorName("Scene View 调试")] public bool debugInSceneView = true;
        [InspectorName("Game View 调试")] public bool debugInGameView;
        internal HoRTShadowSettings Copy()
        {
            var copy = (HoRTShadowSettings)MemberwiseClone();
            copy.excludedRayMaterials = excludedRayMaterials != null ? new List<Material>(excludedRayMaterials) : new List<Material>();
            return copy;
        }
    }

    [DisallowMultipleRendererFeature("Ho-RTShadow")]
    public sealed class HoRTShadowRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoRTShadowSettings settings = new();
        [SerializeField] private RayTracingShader shader;
        [SerializeField] private Shader visibilityMaterialShader;
        [SerializeField] private ComputeShader sigmaShader;
        [SerializeField] private ComputeShader[] sigmaPipelines;
        [SerializeField] private Shader debugShader;

        private HoRTShadowProducer producer;
        private HoRTShadowProducer Producer => producer ??= new HoRTShadowProducer(this);

        public HoRTShadowSettings Settings => settings;
        public HoRTShadowSettings LastEffectiveSettings => producer?.LastEffectiveSettings;
        public Camera LastCamera => producer?.LastCamera;
        public int VolumeOverrideCount => producer?.VolumeOverrideCount ?? 0;
        public IEnumerable<HoRTShadowLightDiagnostics> LightDiagnostics => producer?.LightDiagnostics ?? Array.Empty<HoRTShadowLightDiagnostics>();
        public IReadOnlyList<HoRTShadowProduct> UnavailableProducts => producer?.UnavailableProducts ?? Array.Empty<HoRTShadowProduct>();
        public string Status => producer?.Status ?? "未生产";
        internal HoRTShadowProducer.Output LastOutput => producer?.LastOutput;
        internal HoRTShadowProducer.Output OutputForLight(Light light) => producer?.OutputForLight(light);
        internal int ScheduledLightCount => producer?.ScheduledLightCount ?? 0;
        internal int ActiveOutputCount => producer?.ActiveOutputCount ?? 0;

        public bool TryGetCompatibilityProduct(Camera camera, Light light, out HoRTShadowCompatibilityProduct product)
        {
            if (producer != null) return producer.TryGetCompatibilityProduct(camera, light, out product);
            product = default;
            return false;
        }

        public override void Create() => Producer.CreateLegacy(settings, ref shader, ref visibilityMaterialShader,
            ref sigmaShader, ref sigmaPipelines, ref debugShader);

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData data) =>
            producer?.OnCameraPreCull(renderer, in data);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) =>
            producer?.AddRenderPasses(renderer, ref data);

        protected override void Dispose(bool disposing) => Producer.Dispose();
    }

    internal sealed class HoRTShadowProducer : IDisposable
    {
        private readonly UnityEngine.Object requestOwner;
        private HoRTShadowSettings settings;
        private RayTracingShader shader;
        private Shader visibilityMaterialShader;
        private ComputeShader sigmaShader;
        private ComputeShader[] sigmaPipelines;
        private Shader debugShader;
        private Material debugMaterial;
        private HoRTShadowDebugPass debugPass;
        private ConsumerDebugPass consumerDebugPass;
        private HoRTShadowSettings lastEffective;
        public HoRTShadowSettings LastEffectiveSettings => lastEffective?.Copy();
        public Camera LastCamera => currentCamera;
        public int VolumeOverrideCount { get; private set; }
        public IEnumerable<HoRTShadowLightDiagnostics> LightDiagnostics
        {
            get
            {
                foreach (var output in scheduled)
                    yield return new HoRTShadowLightDiagnostics(output.light, HoRTLightIdentity.Get(output.light), output.slot,
                        output.width, output.height, output.reconstruction, output.sigma?.LastReset ?? false, output.sigma?.LastFrame ?? 0,
                        output.actualAngle, output.samples, output.receiverCount, output.sigma?.Estimates ?? 1);
            }
        }
        private readonly Dictionary<(ScriptableRenderer renderer, Camera camera, Light light), Output> outputs = new();
        private readonly List<(ScriptableRenderer renderer, Camera camera, Light light)> dead = new();
        private object profileKey; private bool registered;
        private long serial;
        public string Status { get; private set; } = "未生产";
        internal Output LastOutput { get; private set; }
        private readonly List<Output> scheduled = new();
        private readonly List<HoRTShadowProduct> rejected = new();
        private Camera currentCamera;
        private bool managed;
        private bool publishLegacyMainAlias = true;
        private HoRTManagedViewPlanner managedPlanner;
        private VolumeStack managedVolumeStack;
        private Camera managedPlanCamera;
        private ScriptableRenderer managedPlanRenderer;
        private readonly List<(Light light, bool main, long id)> managedCandidates = new();
        private readonly List<PlannedLight> plannedLights = new();
        private int managedRequestedCount;
        private int managedMaximumLights;

        private readonly struct PlannedLight
        {
            internal readonly Light light;
            internal readonly bool main;
            internal readonly HoRTShadowReconstruction reconstruction;
            internal readonly uint[] receiverMask;
            internal readonly int receiverCount;

            internal PlannedLight(Light light, bool main, HoRTShadowReconstruction reconstruction, uint[] receiverMask, int receiverCount)
            {
                this.light = light;
                this.main = main;
                this.reconstruction = reconstruction;
                this.receiverMask = receiverMask;
                this.receiverCount = receiverCount;
            }
        }

        public IReadOnlyList<HoRTShadowProduct> UnavailableProducts => rejected;
        public bool TryGetCompatibilityProduct(Camera camera, Light light, out HoRTShadowCompatibilityProduct product)
        {
            product=default;if(camera!=currentCamera||light==null)return false;
            foreach(var output in scheduled)if(output.light==light&&output.lastView.IsValid&&output.producedSerial==output.renderSerial)
            {product=new HoRTShadowCompatibilityProduct(light,HoRTLightIdentity.Get(light),output.renderSerial,output.raw,output.Visibility,output.reconstruction,output.slot);return true;}
            return false;
        }
        internal Output OutputForLight(Light light){foreach(var output in scheduled)if(output.light==light)return output;return null;}
        internal int ScheduledLightCount=>scheduled.Count;
        internal int ActiveOutputCount => outputs.Count;

        internal HoRTShadowProducer(UnityEngine.Object requestOwner)
        {
            this.requestOwner = requestOwner ?? throw new ArgumentNullException(nameof(requestOwner));
        }

        internal void CreateLegacy(HoRTShadowSettings legacySettings, ref RayTracingShader rayShader, ref Shader hitShader,
            ref ComputeShader reconstructionShader, ref ComputeShader[] reconstructionPipelines, ref Shader diagnosticShader)
        {
            settings = legacySettings;
            shader = rayShader;
            visibilityMaterialShader = hitShader;
            sigmaShader = reconstructionShader;
            sigmaPipelines = reconstructionPipelines;
            debugShader = diagnosticShader;
            Create();
            // Existing legacy assets persist the Editor defaults filled by Create.
            rayShader = shader;
            hitShader = visibilityMaterialShader;
            reconstructionShader = sigmaShader;
            reconstructionPipelines = sigmaPipelines;
            diagnosticShader = debugShader;
        }

        internal void ConfigureForManaged(HoRTShadowSettings managedSettings, bool publishLegacyMainAlias = false, int maximumLights = 4)
        {
            settings = managedSettings ?? throw new ArgumentNullException(nameof(managedSettings));
            managed = true;
            this.publishLegacyMainAlias = publishLegacyMainAlias;
            managedMaximumLights = Mathf.Max(0, maximumLights);
            if (profileKey == null)
                Create();
        }

        internal void ConfigureManagedShaders(RayTracingShader rayShader, Shader hitShader, ComputeShader reconstructionShader,
            ComputeShader[] reconstructionPipelines, Shader diagnosticShader)
        {
            if (rayShader != null) shader = rayShader;
            if (hitShader != null && hitShader != visibilityMaterialShader)
            {
                visibilityMaterialShader = hitShader;
                profileKey = new object();
                ReleaseOutputs();
            }
            if (reconstructionShader != null) sigmaShader = reconstructionShader;
            if (reconstructionPipelines != null && reconstructionPipelines.Length != 0) sigmaPipelines = reconstructionPipelines;
            if (diagnosticShader != null) debugShader = diagnosticShader;
        }

        internal void RetireManagedOutputs()
        {
            ClearManagedPlan();
            ReleaseOutputs();
            DisposeManagedVolumeStack();
        }

        internal sealed class Output : IDisposable
        {
            internal readonly int width, height;
            internal readonly RTHandle raw;
            internal readonly GraphicsBuffer receivers;
            internal readonly HoRTShadowReconstruction reconstruction;
            internal readonly HoRTShadowSigmaContext sigma;
            internal RTHandle Visibility => sigma?.visibility ?? raw;
            internal HoRTView lastView;
            internal Light light; internal long renderSerial, producedSerial;
            internal int slot;
            internal float actualAngle;
            internal int samples, receiverCount;
            internal readonly ShadowPass pass = new();
            private bool disposed;
            internal Output(int width, int height, HoRTShadowReconstruction reconstruction, ComputeShader[] pipelines, int estimates)
            {
                this.width = width; this.height = height; this.reconstruction = reconstruction;
                try
                {
                    if (reconstruction != HoRTShadowReconstruction.Raw) sigma = new HoRTShadowSigmaContext(width, height, pipelines, estimates);
                    var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat, depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true, dimension = TextureDimension.Tex2D };
                    raw = RTHandles.Alloc(descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "HoRTShadow Raw");
                    receivers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2048, sizeof(uint)) { name = "HoRTShadow receiver identities" };
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() { if (disposed) return; disposed = true; sigma?.Dispose(); raw?.Release(); receivers?.Dispose(); }
            internal void ReleaseAfterUse() { lastView.ReleaseAfterUse(this); }
        }
        private void Create()
        {
            ReleaseOutputs(); profileKey = new object();
            // Create can be called by a pass while the current graph is still
            // executing. Keep the diagnostic pass/material alive for that graph.
            debugPass ??= new HoRTShadowDebugPass();
            consumerDebugPass ??= new ConsumerDebugPass();
            #if UNITY_EDITOR
            const string root = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/";
            if (shader == null) shader = UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(root + "RTShadow/Shaders/HoRTShadow.raytrace");
            if (visibilityMaterialShader == null) visibilityMaterialShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "RT/Shaders/HoRTVisibilityMaterial.shader");
            if (sigmaShader == null) sigmaShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "RTShadow/Shaders/HoRTShadowSigma.compute");
            if (debugShader == null) debugShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "RTShadow/Shaders/HoRTShadowDebug.shader");
            if (sigmaPipelines == null || sigmaPipelines.Length != HoRTShadowSigmaManifest.Identifiers.Length) Array.Resize(ref sigmaPipelines, HoRTShadowSigmaManifest.Identifiers.Length);
            for (int i = 0; i < sigmaPipelines.Length; i++) if (sigmaPipelines[i] == null) sigmaPipelines[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>($"{root}RTShadow/Shaders/NRD/HoRTShadowSigma{i:00}.compute");
            #endif
            if (!registered) { RenderPipelineManager.beginCameraRendering += ResetCamera; registered = true; }
        }
        private void ResetCamera(ScriptableRenderContext context, Camera camera)
        {
            foreach (var pair in outputs) if (pair.Key.camera == camera) pair.Value.sigma?.CameraBegin();
            LastOutput = null; scheduled.Clear(); rejected.Clear(); currentCamera = camera;
            if (!publishLegacyMainAlias) return;
            Shader.SetGlobalFloat("_HoRTShadowActive", 0);
            Shader.SetGlobalFloat("_HoRTShadowConsumerDebugMode", 0);
            Shader.SetGlobalTexture("_HoRTShadowRawTexture", Texture2D.blackTexture);
            Shader.SetGlobalTexture("_HoRTShadowVisibilityTexture", Texture2D.blackTexture);
            Shader.SetGlobalTexture("_HoRTShadowDepthTexture", Texture2D.blackTexture);
        }
        private bool CameraSupported(CameraData data, HoRTShadowSettings effective) => settings.enabled && effective.enabled && data.renderType == CameraRenderType.Base
            && (data.camera.cameraType == CameraType.Game || (settings.inSceneView && data.camera.cameraType == CameraType.SceneView));
        private HoRTShadowSettings ResolveSettings(out int overrides)
        {
            var stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return HoRTShadowVolume.Resolve(settings, stack?.GetComponent<HoRTShadowVolume>(), out overrides);
        }
        internal void OnCameraPreCull(ScriptableRenderer renderer, in CameraData data)
        {
            if (managed)
            {
                ClearManagedPlan();
                if (!settings.enabled || managedMaximumLights == 0)
                {
                    if (outputs.Count != 0 || managedVolumeStack != null)
                        RetireManagedOutputs();
                    Status = "RTShadow Renderer 已关闭";
                    return;
                }

                if (!CameraSupported(data, settings) || !CameraTargetSupported(data))
                {
                    Status = "RTShadow 当前相机不支持";
                    return;
                }
                if (!CanTrace())
                {
                    Status = "需要 D3D12 native RT 与阴影 Shader";
                    return;
                }

                managedPlanner ??= PlanManagedRequest;
                if (!HoRTRendererFeature.RegisterManagedPlanner(renderer, data.camera, requestOwner, managedPlanner, out var managedReason))
                    Status = "Ho-RT：" + managedReason;
                return;
            }

            // VolumeManager's stack is updated for the camera during the
            // regular AddRenderPasses path. At pre-cull it may still describe
            // the previous camera (SceneView -> GameView is the common case),
            // so never use the resolved Volume value to decide whether the Ho-
            // RT host request exists. The production path will apply the
            // camera's effective Volume settings later.
            if (!settings.enabled || data.renderType != CameraRenderType.Base
                || (data.camera.cameraType != CameraType.Game && (data.camera.cameraType != CameraType.SceneView || !settings.inSceneView))
                || !HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null) return;
            var hitShader = visibilityMaterialShader;
            if (!HoRTRendererFeature.Declare(renderer, data.camera, requestOwner, new HoRTViewRequest(settings.geometryLayers.value,
                settings.excludedRayMaterials, profileKey: profileKey, createProfile: () => new HoRTVisibilityMaterials(hitShader), geometryPolicy: HoRTGeometryPolicy.ShadowCasters), out var reason)) Status = "Ho-RT：" + reason;
        }

        private void ClearManagedPlan()
        {
            managedPlanCamera = null;
            managedPlanRenderer = null;
            managedRequestedCount = 0;
            managedCandidates.Clear();
            plannedLights.Clear();
            lastEffective = null;
            VolumeOverrideCount = 0;
        }

        private void DisposeManagedVolumeStack()
        {
            if (managedVolumeStack == null) return;
            VolumeManager.instance.DestroyStack(managedVolumeStack);
            managedVolumeStack = null;
        }

        private HoRTShadowSettings ResolveManagedSettings(CameraData data, out int overrides)
        {
            var manager = VolumeManager.instance;
            var camera = data.camera;
            VolumeStack stack = null;
            var updateMode = camera.GetVolumeFrameworkUpdateMode();
            if (updateMode == VolumeFrameworkUpdateMode.UsePipelineSettings && UniversalRenderPipeline.asset != null)
                updateMode = UniversalRenderPipeline.asset.volumeFrameworkUpdateMode;
            bool useCachedStack = updateMode == VolumeFrameworkUpdateMode.ViaScripting && camera.cameraType != CameraType.SceneView;
#if UNITY_EDITOR
            useCachedStack &= Application.isPlaying;
#endif
            if (useCachedStack && camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalData)
                && additionalData.volumeStack?.isValid == true)
                stack = additionalData.volumeStack;

            if (stack == null)
            {
                if (managedVolumeStack != null && !managedVolumeStack.isValid)
                    DisposeManagedVolumeStack();
                managedVolumeStack ??= manager.CreateStack();
                LayerMask mask = data.volumeLayerMask;
                Transform trigger = data.volumeTrigger != null ? data.volumeTrigger : camera.transform;
                if (camera.cameraType == CameraType.SceneView && Camera.main != null
                    && Camera.main.TryGetComponent<UniversalAdditionalCameraData>(out var mainData))
                {
                    mask = mainData.volumeLayerMask;
                    if (mainData.volumeTrigger != null)
                        trigger = mainData.volumeTrigger;
                }

                // SingleCameraRequest can leave the pipeline's global stack on another camera.
                manager.Update(managedVolumeStack, trigger, mask);
                stack = managedVolumeStack;
            }

            return HoRTShadowVolume.Resolve(settings, stack.GetComponent<HoRTShadowVolume>(), out overrides);
        }

        private bool CanTrace() => HoRTScene.IsSupported && SystemInfo.supportsRayTracing
            && shader != null && visibilityMaterialShader != null;

        private static bool CameraTargetSupported(CameraData data)
        {
            var descriptor = data.cameraTargetDescriptor;
            return descriptor.msaaSamples == 1 && descriptor.dimension == TextureDimension.Tex2D && !data.xr.enabled
                && data.camera.rect == new Rect(0, 0, 1, 1)
                && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore);
        }

        private HoRTViewRequest PlanManagedRequest(ref RenderingData data)
        {
            var cameraData = data.cameraData;
            managedPlanCamera = cameraData.camera;
            managedPlanRenderer = cameraData.renderer;
            if (!settings.enabled || managedMaximumLights == 0)
                return null;

            lastEffective = ResolveManagedSettings(cameraData, out int overrides);
            VolumeOverrideCount = overrides;
            if (!CameraSupported(cameraData, lastEffective))
            {
                Status = "RTShadow Volume 已关闭当前相机";
                return null;
            }

            int main = data.lightData.mainLightIndex;
            var visibleLights = data.lightData.visibleLights;
            var descriptor = cameraData.cameraTargetDescriptor;
            for (int index = 0; index < visibleLights.Length; index++)
            {
                var visible = visibleLights[index];
                var light = visible.light;
                if (light == null || !light.isActiveAndEnabled || light.shadows == LightShadows.None || light.shadowStrength <= 0
                    || (lastEffective.lightLayers.value & (1 << light.gameObject.layer)) == 0)
                    continue;

                bool isMain = index == main && visible.lightType == LightType.Directional;
                bool isLocal = visible.lightType == LightType.Point || visible.lightType == LightType.Spot;
                if (!(isMain && data.shadowData.supportsMainLightShadows)
                    && !(isLocal && data.shadowData.supportsAdditionalLightShadows))
                    continue;
                if (lastEffective.lightSelection == HoRTShadowLightSelection.Main && !isMain)
                    continue;
                if (lastEffective.lightSelection == HoRTShadowLightSelection.Declared && !IsDeclaredLight(light))
                    continue;

                managedCandidates.Add((light, isMain, HoRTLightIdentity.Get(light)));
            }

            // Native atlas allocation follows during graph recording. Rank the camera-visible
            // candidates independently and let the screen resolver match the actual raster slots.
            managedCandidates.Sort(static (a, b) => a.main == b.main ? a.id.CompareTo(b.id) : a.main ? -1 : 1);
            managedRequestedCount = managedCandidates.Count;
            foreach (var candidate in managedCandidates)
            {
                if (plannedLights.Count >= managedMaximumLights)
                    break;
                Light light = candidate.light;
                bool isMain = candidate.main;

                bool baked = light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked;
#if UNITY_EDITOR
                baked = light.lightmapBakeType == LightmapBakeType.Baked;
#endif
                if (baked || light.cookie != null || light.cullingMask != -1)
                {
                    RejectManagedLight(light, descriptor, HoRTShadowProductStatus.Unsupported,
                        "Only realtime directional / delta point / delta spot without cookie or restricted linking");
                    continue;
                }

                var mode = isMain ? lastEffective.reconstruction : HoRTShadowReconstruction.Raw;
                if (mode != HoRTShadowReconstruction.Raw && (sigmaShader == null || sigmaPipelines == null
                    || sigmaPipelines.Length != 9 || Array.Exists(sigmaPipelines, item => item == null)))
                {
                    RejectManagedLight(light, descriptor, HoRTShadowProductStatus.Unsupported, "Complete SIGMA shader library missing", mode);
                    continue;
                }

                uint[] mask = HoRTShadowReceiver.Capture(out int receiverCount, light);
                if (receiverCount == 0)
                {
                    RejectManagedLight(light, descriptor, HoRTShadowProductStatus.NoReceivers, "No receiver declaration for this light");
                    continue;
                }

                plannedLights.Add(new PlannedLight(light, isMain, mode, mask, receiverCount));
            }
            managedCandidates.Clear();

            if (plannedLights.Count == 0)
            {
                Status = $"请求 {managedRequestedCount} 灯 / 生产 0 / 不可用 {rejected.Count}";
                return null;
            }

            var hitShader = visibilityMaterialShader;
            return new HoRTViewRequest(lastEffective.geometryLayers.value, lastEffective.excludedRayMaterials,
                profileKey: profileKey, createProfile: () => new HoRTVisibilityMaterials(hitShader), geometryPolicy: HoRTGeometryPolicy.ShadowCasters);
        }

        private static bool IsDeclaredLight(Light light)
        {
            foreach (var declaration in HoRTShadowLight.Active)
                if (declaration != null && declaration.isActiveAndEnabled && declaration.Light == light)
                    return true;
            return false;
        }

        private void RejectManagedLight(Light light, RenderTextureDescriptor descriptor, HoRTShadowProductStatus status,
            string reason, HoRTShadowReconstruction mode = HoRTShadowReconstruction.Raw)
        {
            rejected.Add(new HoRTShadowProduct(light, HoRTLightIdentity.Get(light), -1, ++serial, descriptor.width, descriptor.height,
                0, 0, 0, 0, mode, TextureHandle.nullHandle, TextureHandle.nullHandle, status, reason));
        }

        private void AddManagedRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            Camera camera = data.cameraData.camera;
            currentCamera = camera;
            LastOutput = null;
            scheduled.Clear();
            if (!settings.enabled || managedMaximumLights == 0)
            {
                if (outputs.Count != 0 || managedVolumeStack != null) RetireManagedOutputs();
                return;
            }

            bool plannedForCamera = managedPlanCamera == camera && ReferenceEquals(managedPlanRenderer, renderer);
            if (plannedForCamera && plannedLights.Count != 0)
            {
                if (HoRTRendererFeature.TryAcquire(renderer, camera, requestOwner, out var view, out var reason))
                {
                    var descriptor = data.cameraData.cameraTargetDescriptor;
                    foreach (var plan in plannedLights)
                        ScheduleLight(renderer, camera, descriptor, view, lastEffective, plan.light, plan.main,
                            plan.reconstruction, plan.receiverMask, plan.receiverCount);
                    Status = $"请求 {managedRequestedCount} 灯 / 生产 {scheduled.Count} / 不可用 {rejected.Count}";
                }
                else
                    Status = "Ho-RT：" + reason;
            }

            RetireUnscheduledOutputs(renderer, camera);
            if (plannedForCamera && (scheduled.Count != 0 || rejected.Count != 0))
                EnqueueProducts(renderer, camera);
            if (plannedForCamera && lastEffective != null && CameraSupported(data.cameraData, lastEffective))
                EnqueueDebugPasses(renderer, data.cameraData);
            plannedLights.Clear();
        }
        internal void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (managed)
            {
                AddManagedRenderPasses(renderer, ref data);
                return;
            }

            lastEffective = ResolveSettings(out int overrides); VolumeOverrideCount = overrides;
            try { AddProductionPasses(renderer, ref data, lastEffective); }
            finally { EnqueueDebugPasses(renderer, data.cameraData); }
        }

        private void EnqueueDebugPasses(ScriptableRenderer renderer, CameraData data)
        {
            var camera = data.camera;
            bool show = camera.cameraType == CameraType.SceneView ? lastEffective.debugInSceneView
                : camera.cameraType == CameraType.Game && lastEffective.debugInGameView;
            if (!show || lastEffective.debugMode == HoRTShadowDebugMode.Off || data.renderType != CameraRenderType.Base)
                return;

            if (lastEffective.debugMode == HoRTShadowDebugMode.Consumption ||
                lastEffective.debugMode == HoRTShadowDebugMode.ForceLit ||
                lastEffective.debugMode == HoRTShadowDebugMode.ForceShadow)
            {
                if (!publishLegacyMainAlias) return;
                consumerDebugPass.mode = (int)lastEffective.debugMode;
                HoDebugPerformanceRegistry.EnqueuePass(renderer, camera, consumerDebugPass, HoRTShadowDebugViewInfo.Performance);
                return;
            }

            if (debugMaterial == null && debugShader != null) debugMaterial = CoreUtils.CreateEngineMaterial(debugShader);
            if (debugMaterial == null) return;
            Output selected = scheduled.Find(item => item.slot == lastEffective.debugLight);
            debugPass.Setup(debugMaterial, lastEffective, selected);
            debugPass.Enqueue(renderer, camera, HoRTShadowDebugViewInfo.Performance);
        }
        private sealed class ConsumerDebugPass : ScriptableRenderPass
        {
            internal int mode;
            private sealed class Data { internal int mode; }
            internal ConsumerDebugPass() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                using var builder = graph.AddRasterRenderPass<Data>("Ho-RTShadow actual lilToon consumer debug", out var passData);
                passData.mode = mode;
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data data, RasterGraphContext context) =>
                    context.cmd.SetGlobalFloat("_HoRTShadowConsumerDebugMode", data.mode));
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get("Ho-RTShadow consumer debug");
                cmd.SetGlobalFloat("_HoRTShadowConsumerDebugMode", mode);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
        private void AddProductionPasses(ScriptableRenderer renderer, ref RenderingData data, HoRTShadowSettings settings)
        {
            LastOutput = null; scheduled.Clear(); rejected.Clear(); currentCamera=data.cameraData.camera;
            if (!CameraSupported(data.cameraData, settings)) { Status = "当前相机未请求（检查 Feature 总开关 / Volume 启用 / Scene View）"; return; }
            if (!HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null)
            { Status = "需要 D3D12 native RT 与阴影 Shader"; return; }
            if (!HoRTRendererFeature.TryAcquire(renderer, data.cameraData.camera, requestOwner, out var view, out var reason)) { Status = "Ho-RT：" + reason; return; }
            int main = data.lightData.mainLightIndex;
            var light = main >= 0 && main < data.lightData.visibleLights.Length ? data.lightData.visibleLights[main].light : null;
            var descriptor = data.cameraData.cameraTargetDescriptor;
            if (descriptor.msaaSamples != 1 || descriptor.dimension != TextureDimension.Tex2D || data.cameraData.xr.enabled
                || data.cameraData.camera.rect != new Rect(0, 0, 1, 1) || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore))
            { Status = "需要 Mono 全视口单采样和 32F UAV"; return; }
            var requested=new List<Light>();
            if(settings.lightSelection==HoRTShadowLightSelection.Main){if(light!=null)requested.Add(light);}
            else if(settings.lightSelection==HoRTShadowLightSelection.Declared)
            {foreach(var declaration in HoRTShadowLight.Active)if(declaration!=null&&declaration.isActiveAndEnabled&&declaration.Light!=null&&!requested.Contains(declaration.Light))requested.Add(declaration.Light);}
            else foreach(var candidate in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))requested.Add(candidate);
            requested.RemoveAll(item=>!item.enabled||!item.gameObject.activeInHierarchy||(settings.lightLayers.value&(1<<item.gameObject.layer))==0);
            requested.Sort((a,b)=>HoRTLightIdentity.Get(a).CompareTo(HoRTLightIdentity.Get(b)));
            var selected=new HashSet<Light>(requested);
            dead.Clear();foreach(var pair in outputs)if(pair.Key.camera==null||pair.Key.light==null||(ReferenceEquals(pair.Key.renderer,renderer)&&pair.Key.camera==data.cameraData.camera&&!selected.Contains(pair.Key.light)))dead.Add(pair.Key);
            foreach(var key in dead){outputs[key].ReleaseAfterUse();outputs.Remove(key);}
            foreach(var requestedLight in requested)
            {
                long id=HoRTLightIdentity.Get(requestedLight);
                bool baked=requestedLight.bakingOutput.lightmapBakeType==LightmapBakeType.Baked;
                #if UNITY_EDITOR
                baked=requestedLight.lightmapBakeType==LightmapBakeType.Baked;
                #endif
                if((requestedLight.type!=LightType.Directional&&requestedLight.type!=LightType.Point&&requestedLight.type!=LightType.Spot)||requestedLight.cookie!=null||requestedLight.cullingMask!=-1||baked)
                {rejected.Add(new HoRTShadowProduct(requestedLight,id,-1,++serial,descriptor.width,descriptor.height,0,0,view.Scene.Lifetime,view.Scene.Revision,HoRTShadowReconstruction.Raw,TextureHandle.nullHandle,TextureHandle.nullHandle,HoRTShadowProductStatus.Unsupported,"Only realtime directional / delta point / delta spot without cookie or restricted linking"));continue;}
                var mask=HoRTShadowReceiver.Capture(out int receiverCount,requestedLight);
                if(receiverCount==0){rejected.Add(new HoRTShadowProduct(requestedLight,id,-1,++serial,descriptor.width,descriptor.height,0,0,view.Scene.Lifetime,view.Scene.Revision,HoRTShadowReconstruction.Raw,TextureHandle.nullHandle,TextureHandle.nullHandle,HoRTShadowProductStatus.NoReceivers,"No receiver declaration for this light"));continue;}
                var mode=requestedLight.type==LightType.Directional?settings.reconstruction:HoRTShadowReconstruction.Raw;
                if(mode!=HoRTShadowReconstruction.Raw&&(sigmaShader==null||sigmaPipelines==null||sigmaPipelines.Length!=9||Array.Exists(sigmaPipelines,item=>item==null)))
                {rejected.Add(new HoRTShadowProduct(requestedLight,id,-1,++serial,descriptor.width,descriptor.height,0,0,view.Scene.Lifetime,view.Scene.Revision,mode,TextureHandle.nullHandle,TextureHandle.nullHandle,HoRTShadowProductStatus.Unsupported,"Complete SIGMA shader library missing"));continue;}
                bool isMain=requestedLight==light&&requestedLight.type==LightType.Directional;
                ScheduleLight(renderer, data.cameraData.camera, descriptor, view, settings, requestedLight, isMain, mode, mask, receiverCount);
            }
            RetireUnscheduledOutputs(renderer, data.cameraData.camera);
            EnqueueProducts(renderer, data.cameraData.camera);
            Status=$"请求 {requested.Count} 灯 / 生产 {scheduled.Count} / 不可用 {rejected.Count}";
        }

        private void ScheduleLight(ScriptableRenderer renderer, Camera camera, RenderTextureDescriptor descriptor, HoRTView view,
            HoRTShadowSettings effective, Light light, bool isMain, HoRTShadowReconstruction mode, uint[] mask, int receiverCount)
        {
            var key = (renderer, camera, light);
            int estimates = mode == HoRTShadowReconstruction.SigmaSingleFrame ? Mathf.Clamp(effective.singleFrameEstimates, 1, 256) : 1;
            if (outputs.TryGetValue(key, out var output) && (output.width != descriptor.width || output.height != descriptor.height
                || output.reconstruction != mode || (output.sigma != null && output.sigma.Estimates != estimates)))
            {
                output.ReleaseAfterUse();
                outputs.Remove(key);
                output = null;
            }
            if (output == null)
            {
                output = new Output(descriptor.width, descriptor.height, mode, sigmaPipelines, estimates);
                outputs.Add(key, output);
            }

            output.lastView = view;
            output.light = light;
            output.renderSerial = ++serial;
            output.producedSerial = 0;
            output.receiverCount = receiverCount;
            output.slot = scheduled.Count;
            if (isMain)
                LastOutput = output;
            output.pass.Setup(output, view, shader, sigmaShader, mask, effective, light,
                isMain && publishLegacyMainAlias, managed, output.slot);
            scheduled.Add(output);
        }

        private void RetireUnscheduledOutputs(ScriptableRenderer renderer, Camera camera)
        {
            dead.Clear();
            foreach(var pair in outputs)
                if (pair.Key.camera == null || pair.Key.light == null || (ReferenceEquals(pair.Key.renderer, renderer)
                    && pair.Key.camera == camera && !scheduled.Contains(pair.Value)))
                    dead.Add(pair.Key);
            foreach(var key in dead){outputs[key].ReleaseAfterUse();outputs.Remove(key);}
        }

        private void EnqueueProducts(ScriptableRenderer renderer, Camera camera)
        {
            if(scheduled.Count>0)HoAttributeCompositeConsumerRegistry.DeclareForCamera(camera,requestOwner,"RTShadow primary identity and normals",Array.Empty<HoACQueryDescriptor>(),null,HoACDemandResources.Identity|HoACDemandResources.HighPrecisionSurfaceNormal);
            // Metadata pass is separate so rejected products are observable even
            // when there is nothing to trace. No invalid product masquerades as V=1.
            var publish=new ProductPass();publish.camera=camera;publish.products=rejected.ToArray();renderer.EnqueuePass(publish);
            foreach(var output in scheduled)HoDebugPerformanceRegistry.EnqueuePass(renderer, camera, output.pass, HoRTShadowDebugViewInfo.Performance);
        }
        private sealed class ProductPass:ScriptableRenderPass
        {
            internal Camera camera;internal HoRTShadowProduct[] products;
            internal ProductPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {var resources=frame.GetOrCreate<HoRTShadowRenderGraphResources>();resources.Camera=camera;foreach(var product in products)resources.Add(product);}
            public override void Execute(ScriptableRenderContext context,ref RenderingData data){}
        }
        private void ReleaseOutputs() { foreach (var output in outputs.Values) output.ReleaseAfterUse(); outputs.Clear(); scheduled.Clear(); rejected.Clear(); LastOutput = null; HoAttributeCompositeConsumerRegistry.Remove(requestOwner); }
        public void Dispose()
        { if (registered) RenderPipelineManager.beginCameraRendering -= ResetCamera; registered = false; ResetCamera(default, null); RetireManagedOutputs(); debugPass?.ReleaseDebugResources(); CoreUtils.Destroy(debugMaterial); debugMaterial = null; }

        internal sealed class ShadowPass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler ProfilingSampler = lilToon.URP.Extensions.Debugging.HoDebugPerformanceRegistry.GetSampler("Ho-RTShadow");
            private Output output; private HoRTView view; private RayTracingShader shader; private uint[] receivers;
            private ComputeShader sigmaShader;
            private Vector4 lightDirection, parameters, localPosition, localForward; private bool main, requireSourceDepth; private int slot; private int samples, seed, signature; private float strength, tanRadius;
            internal ShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }
            internal void Setup(Output output, HoRTView view, RayTracingShader shader, ComputeShader sigmaShader, uint[] receivers,
                HoRTShadowSettings settings, Light light, bool main, bool requireSourceDepth, int slot)
            {
                this.output = output; this.view = view; this.shader = shader; this.receivers = receivers;
                this.sigmaShader = sigmaShader; this.main=main;this.requireSourceDepth=requireSourceDepth;this.slot=slot;
                var position=light.transform.position;localPosition=new Vector4(position.x,position.y,position.z,Mathf.Max(.0001f,light.range));
                var forward=light.transform.forward;localForward=new Vector4(forward.x,forward.y,forward.z,Mathf.Cos(Mathf.Clamp(light.spotAngle,0,179)*Mathf.Deg2Rad*.5f));
                var direction = -light.transform.forward;
                float angle=settings.angularDiameter;foreach(var declaration in HoRTShadowLight.Active)if(declaration!=null&&declaration.isActiveAndEnabled&&declaration.Light==light){angle=declaration.angularDiameter;break;}
                float halfSine = Mathf.Sin(Mathf.Clamp(angle, 0, 8) * Mathf.Deg2Rad * .25f);
                lightDirection = new Vector4(direction.x, direction.y, direction.z, 2 * halfSine * halfSine);
                parameters = new Vector4(output.width, output.height, Mathf.Clamp(settings.rayBias, .000001f, 1), Mathf.Clamp(settings.maxDistance, .01f, 65504));
                samples = output.sigma == null && light.type==LightType.Directional ? Mathf.Clamp(settings.samplesPerPixel, 1, 4096) : 1; seed = settings.samplingSeed; strength = light.shadowStrength;
                output.actualAngle = light.type == LightType.Directional ? Mathf.Clamp(angle, 0, 8) : 0; output.samples = samples;
                tanRadius = Mathf.Tan(Mathf.Clamp(angle, 0, 8) * Mathf.Deg2Rad * .5f);
                unchecked { signature = view.MaterialSignature ^ lightDirection.GetHashCode() ^ parameters.GetHashCode() ^ seed ^ localPosition.GetHashCode() ^ localForward.GetHashCode(); foreach (uint bits in receivers) signature = signature * 397 ^ (int)bits; }
            }
            private sealed class Data
            {
                internal Output output; internal HoRTScene.Inputs scene; internal HoRTPrimarySurfaceInputs primary;
                internal RayTracingShader shader; internal TextureHandle raw; internal uint[] receivers;
                internal Vector4 direction, parameters, localPosition, localForward; internal int lightType, slot;internal bool main; internal int samples, seed; internal float strength;
                internal ComputeShader sigmaShader; internal Matrix4x4 cameraView, projection; internal long lifetime; internal uint revision; internal int signature, lightId; internal float tanRadius; internal double time;
            }
            private void Snapshot(Data d, Camera camera, Matrix4x4 cameraView, Matrix4x4 projection)
            {
                d.output = output; d.shader = shader; d.receivers = receivers; d.direction = lightDirection; d.parameters = parameters; d.samples = samples; d.seed = seed; d.strength = strength; d.main=main;d.slot=slot;d.localPosition=localPosition;d.localForward=localForward;d.lightType=output.light.type==LightType.Directional?0:output.light.type==LightType.Point?1:2;
                d.sigmaShader = sigmaShader; d.cameraView = cameraView; d.projection = projection; d.lifetime = view.Scene.Lifetime; d.revision = view.Scene.Revision; d.signature = signature; d.lightId = output.light.GetInstanceID(); d.tanRadius = tanRadius;
                d.time = Time.unscaledTimeAsDouble;
                #if UNITY_EDITOR
                if (!Application.isPlaying && camera.cameraType == CameraType.SceneView) d.time = UnityEditor.EditorApplication.timeSinceStartup;
                #endif
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var sourceDepth = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>().depthTexture;
                var resources = frame.GetOrCreate<HoRTShadowRenderGraphResources>();
                if (requireSourceDepth && !sourceDepth.IsValid())
                {
                    resources.Add(new HoRTShadowProduct(output.light, HoRTLightIdentity.Get(output.light), slot, output.renderSerial,
                        output.width, output.height, samples, output.sigma?.Estimates ?? 1, view.Scene.Lifetime, view.Scene.Revision,
                        output.reconstruction, TextureHandle.nullHandle, TextureHandle.nullHandle, HoRTShadowProductStatus.Unsupported,
                        "Matching SurfaceBuffer depth is unavailable"));
                    return;
                }

                string passName = requireSourceDepth ? "Ho-RTShadow raw light visibility" : "Ho-RTShadow raw main-light visibility";
                using (var builder = graph.AddUnsafePass<Data>(passName, out var d))
                {
                    var camera = frame.Get<UniversalCameraData>(); Snapshot(d, camera.camera, camera.GetViewMatrix(), camera.GetProjectionMatrix());
                    d.scene = view.Read(graph, builder); d.primary = HoRTPrimarySurfaceInputs.Read(graph, frame, builder, true);
                    d.raw = graph.ImportTexture(output.raw); builder.UseTexture(d.raw, output.sigma == null ? AccessFlags.WriteAll : AccessFlags.ReadWrite); builder.UseBuffer(graph.ImportBuffer(output.receivers), AccessFlags.ReadWrite);
                    if(main)builder.SetGlobalTextureAfterPass(d.raw, Shader.PropertyToID("_HoRTShadowRawTexture"));
                    var visibility = graph.ImportTexture(output.Visibility);
                    if (output.sigma != null) output.sigma.Declare(graph, builder);
                    if(main)builder.SetGlobalTextureAfterPass(visibility, Shader.PropertyToID("_HoRTShadowVisibilityTexture"));
                    var surfaceDepth = sourceDepth.IsValid() ? sourceDepth : graph.defaultResources.blackTexture;
                    builder.UseTexture(surfaceDepth); if(main)builder.SetGlobalTextureAfterPass(surfaceDepth, Shader.PropertyToID("_HoRTShadowDepthTexture"));
                    resources.Add(new HoRTShadowProduct(output.light,HoRTLightIdentity.Get(output.light),slot,output.renderSerial,output.width,output.height,samples,output.sigma?.Estimates??1,view.Scene.Lifetime,view.Scene.Revision,output.reconstruction,d.raw,visibility,
                        sourceDepth: sourceDepth, lightShadowStrength: d.strength));
                    if(main)
                    {
                        resources.raw = d.raw; resources.visibility = visibility; resources.reconstruction = output.reconstruction; resources.camera = frame.Get<UniversalCameraData>().camera; resources.light = output.light; resources.lightId = output.light.GetInstanceID();
                        resources.width = output.width; resources.height = output.height; resources.samples = samples; resources.estimates = output.sigma?.Estimates ?? 1; resources.sceneLifetime = view.Scene.Lifetime; resources.sceneRevision = view.Scene.Revision; resources.renderSerial = output.renderSerial;
                    }
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd); d.primary.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd));
                        if (d.output.sigma != null) d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd), d.sigmaShader, d.sigmaShader.FindKernel("Prepare"));
                        using (new ProfilingScope(cmd, ProfilingSampler)) Produce(cmd, d);
                    });
                }
            }
            private static void Produce(CommandBuffer cmd, Data d)
            {
                int baseSeed = d.seed, estimates = d.output.sigma?.Estimates ?? 1;
                for (int estimate = 0; estimate < estimates; estimate++)
                {
                    d.seed = unchecked(baseSeed ^ estimate * (int)0x85ebca6bu);
                    Dispatch(cmd, d);
                    if (d.output.sigma != null) Reconstruct(cmd, d, estimate);
                }
                d.seed = baseSeed; d.output.producedSerial=d.output.renderSerial;
            }
            private static void Dispatch(CommandBuffer cmd, Data d)
            {
                if (d.output.sigma != null)
                {
                    int frame = d.output.sigma.Begin(d.output.reconstruction == HoRTShadowReconstruction.SigmaPreview, d.lifetime, d.revision, d.lightId, d.signature, d.cameraView, d.projection, d.time);
                    d.seed = unchecked(d.seed ^ frame * (int)0x9e3779b9u);
                }
                d.scene.BuildAndBind(cmd, d.shader); cmd.SetBufferData(d.output.receivers, d.receivers);
                cmd.SetRayTracingShaderPass(d.shader, "HoRTVisibility"); cmd.SetRayTracingBufferParam(d.shader, "_HoRTShadowReceivers", d.output.receivers);
                cmd.SetRayTracingTextureParam(d.shader, "_HoRTShadowOutput", d.output.raw.nameID); cmd.SetRayTracingVectorParam(d.shader, "_HoRTShadowDirection", d.direction);
                cmd.SetRayTracingVectorParam(d.shader, "_HoRTShadowParameters", d.parameters); cmd.SetRayTracingIntParam(d.shader, "_HoRTShadowSamples", d.samples); cmd.SetRayTracingIntParam(d.shader, "_HoRTShadowSeed", d.seed);
                cmd.SetRayTracingVectorParam(d.shader,"_HoRTShadowLocalPosition",d.localPosition);cmd.SetRayTracingVectorParam(d.shader,"_HoRTShadowLocalForward",d.localForward);cmd.SetRayTracingIntParam(d.shader,"_HoRTShadowLightType",d.lightType);
                cmd.DispatchRays(d.shader, "Cast", (uint)d.output.width, (uint)d.output.height, 1);
                if(!d.main)return;
                cmd.SetGlobalVector("_HoRTShadowSize", new Vector4(d.output.width, d.output.height, 1f / d.output.width, 1f / d.output.height));
                cmd.SetGlobalFloat("_HoRTShadowMainLightStrength", d.strength);
                cmd.SetGlobalFloat("_HoRTShadowActive", 1);
            }
            private static void Reconstruct(CommandBuffer cmd, Data d, int estimate)
            { d.output.sigma.Record(cmd, d.sigmaShader, d.output.raw, d.direction, d.tanRadius, d.cameraView, d.projection, d.output.reconstruction == HoRTShadowReconstruction.SigmaPreview, estimate); }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var d = new Data(); Snapshot(d, data.cameraData.camera, data.cameraData.GetViewMatrix(), data.cameraData.GetProjectionMatrix()); d.scene = view.ReadCompatibility(); var cmd = CommandBufferPool.Get("Ho-RTShadow raw main-light visibility");
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd, data.cameraData, true);
                if (output.sigma != null) HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd, sigmaShader, sigmaShader.FindKernel("Prepare"), data.cameraData, true);
                using (new ProfilingScope(cmd, ProfilingSampler)) Produce(cmd, d);
                if(main)cmd.SetGlobalTexture("_HoRTShadowRawTexture", output.raw.nameID);
                if(main)cmd.SetGlobalTexture("_HoRTShadowVisibilityTexture", output.Visibility.nameID);
                var depth = HoSurfaceBufferPass.CompatibilityProduced ? HoSurfaceBufferPass.CompatibilityDepthTexture : null;
                if(main)cmd.SetGlobalTexture("_HoRTShadowDepthTexture", depth?.rt != null ? depth.nameID : new RenderTargetIdentifier(Texture2D.blackTexture));
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
