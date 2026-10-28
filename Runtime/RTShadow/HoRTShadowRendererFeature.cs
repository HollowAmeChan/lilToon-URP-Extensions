#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryData;
using lilToon.URP.Extensions.ObjectBuffer;
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
        [InspectorName("阴影终止线校正"), Range(0,1), Tooltip("用顶点切平面重建平滑曲面的射线起点，减轻低模曲面逐三角面的阴影终止线。0 为原三角网格参考；1 为完整校正。会改变自影几何近似，并额外检查位移路径以保留附近真实遮挡。")]
        public float terminatorCorrection = 1;
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
        private Material debugMaterial;
        private HoRTShadowDebugPass debugPass;
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
                        output.actualAngle, output.samples, output.receiverCount, output.sigma?.Estimates ?? 1, output.terminatorCorrection);
            }
        }
        private readonly Dictionary<(ScriptableRenderer renderer, Camera camera, Light light), Output> outputs = new();
        private readonly List<(ScriptableRenderer renderer, Camera camera, Light light)> dead = new();
        private object profileKey; private bool registered;
        private long serial;
        public HoRTShadowSettings Settings => settings;
        public string Status { get; private set; } = "未生产";
        internal Output LastOutput { get; private set; }
        private readonly List<Output> scheduled = new();
        private readonly List<HoRTShadowProduct> rejected = new();
        private Camera currentCamera;
        private readonly List<HoRTScene.Instance> originInstances = new();
        public IReadOnlyList<HoRTShadowProduct> UnavailableProducts => rejected;
        public bool TryGetCompatibilityProduct(Camera camera, Light light, out HoRTShadowCompatibilityProduct product)
        {
            product=default;if(camera!=currentCamera||light==null)return false;
            foreach(var output in scheduled)if(output.light==light&&output.lastView.IsValid&&output.producedSerial==output.renderSerial)
            {product=new HoRTShadowCompatibilityProduct(light,HoRTLightIdentity.Get(light),output.renderSerial,output.raw,output.Visibility,output.reconstruction,output.slot,output.terminatorCorrection);return true;}
            return false;
        }
        internal Output OutputForLight(Light light){foreach(var output in scheduled)if(output.light==light)return output;return null;}
        internal int ScheduledLightCount=>scheduled.Count;

        internal sealed class Output : IDisposable
        {
            internal readonly int width, height;
            internal readonly RTHandle raw;
            internal readonly GraphicsBuffer receivers;
            internal readonly GraphicsBuffer instanceOwners;
            internal readonly HoRTShadowReconstruction reconstruction;
            internal readonly HoRTShadowSigmaContext sigma;
            internal RTHandle Visibility => sigma?.visibility ?? raw;
            internal HoRTView lastView;
            internal Light light; internal long renderSerial, producedSerial;
            internal int slot;
            internal float actualAngle;
            internal float terminatorCorrection;
            internal int samples, receiverCount;
            internal readonly ShadowPass pass = new();
            private bool disposed;
            internal Output(int width, int height, HoRTShadowReconstruction reconstruction, ComputeShader[] pipelines, int estimates, int ownerCapacity)
            {
                this.width = width; this.height = height; this.reconstruction = reconstruction;
                try
                {
                    if (reconstruction != HoRTShadowReconstruction.Raw) sigma = new HoRTShadowSigmaContext(width, height, pipelines, estimates);
                    var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat, depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true, dimension = TextureDimension.Tex2D };
                    raw = RTHandles.Alloc(descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "HoRTShadow Raw");
                    receivers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2048, sizeof(uint)) { name = "HoRTShadow receiver identities" };
                    instanceOwners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ownerCapacity, sizeof(uint)) { name = "HoRTShadow AS owner identities" };
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() { if (disposed) return; disposed = true; sigma?.Dispose(); raw?.Release(); receivers?.Dispose(); instanceOwners?.Dispose(); }
            internal void ReleaseAfterUse() { lastView.ReleaseAfterUse(this); }
        }
        public override void Create()
        {
            ReleaseOutputs(); profileKey = new object();
            // Create can be called by a pass while the current graph is still
            // executing. Keep the diagnostic pass/material alive for that graph.
            debugPass ??= new HoRTShadowDebugPass();
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
        { foreach (var pair in outputs) if (pair.Key.Item2 == camera) pair.Value.sigma?.CameraBegin(); LastOutput = null; scheduled.Clear(); rejected.Clear(); currentCamera=camera; Shader.SetGlobalFloat("_HoRTShadowActive", 0); Shader.SetGlobalTexture("_HoRTShadowRawTexture", Texture2D.blackTexture); Shader.SetGlobalTexture("_HoRTShadowVisibilityTexture", Texture2D.blackTexture); Shader.SetGlobalTexture("_HoRTShadowDepthTexture", Texture2D.blackTexture); }
        private bool CameraSupported(CameraData data, HoRTShadowSettings effective) => settings.enabled && effective.enabled && data.renderType == CameraRenderType.Base
            && (data.camera.cameraType == CameraType.Game || (settings.inSceneView && data.camera.cameraType == CameraType.SceneView));
        private HoRTShadowSettings ResolveSettings(out int overrides)
        {
            var stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return HoRTShadowVolume.Resolve(settings, stack?.GetComponent<HoRTShadowVolume>(), out overrides);
        }
        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData data)
        {
            var effective = ResolveSettings(out _);
            if (!CameraSupported(data, effective) || !HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null) return;
            var hitShader = visibilityMaterialShader;
            if (!HoRTRendererFeature.Declare(renderer, data.camera, this, new HoRTViewRequest(settings.geometryLayers.value,
                settings.excludedRayMaterials, profileKey: profileKey, createProfile: () => new HoRTVisibilityMaterials(hitShader), geometryPolicy: HoRTGeometryPolicy.ShadowCasters), out var reason)) Status = "Ho-RT：" + reason;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            lastEffective = ResolveSettings(out int overrides); VolumeOverrideCount = overrides;
            try { AddProductionPasses(renderer, ref data, lastEffective); }
            finally
            {
                var camera = data.cameraData.camera;
                bool show = camera.cameraType == CameraType.SceneView ? lastEffective.debugInSceneView
                    : camera.cameraType == CameraType.Game && lastEffective.debugInGameView;
                if (show && lastEffective.debugMode != HoRTShadowDebugMode.Off && data.cameraData.renderType == CameraRenderType.Base)
                {
                    if (debugMaterial == null && debugShader != null) debugMaterial = CoreUtils.CreateEngineMaterial(debugShader);
                    if (debugMaterial != null)
                    {
                        Output selected = scheduled.Find(item => item.slot == lastEffective.debugLight);
                        debugPass.Setup(debugMaterial, lastEffective, selected);
                        debugPass.Enqueue(renderer);
                    }
                }
            }
        }
        private void AddProductionPasses(ScriptableRenderer renderer, ref RenderingData data, HoRTShadowSettings settings)
        {
            LastOutput = null; scheduled.Clear(); rejected.Clear(); currentCamera=data.cameraData.camera;
            if (!CameraSupported(data.cameraData, settings)) { Status = "当前相机未请求（检查 Feature 总开关 / Volume 启用 / Scene View）"; return; }
            if (!HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null)
            { Status = "需要 D3D12 native RT 与阴影 Shader"; return; }
            if (!HoRTRendererFeature.TryAcquire(renderer, data.cameraData.camera, this, out var view, out var reason)) { Status = "Ho-RT：" + reason; return; }
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
            HoObjectBufferRegistry.EnsureBuilt();
            view.Scene.CopyInstances(originInstances); uint highestIdentity = 0;
            foreach (var instance in originInstances) highestIdentity = Math.Max(highestIdentity, instance.identity);
            var instanceOwners = new uint[Mathf.NextPowerOfTwo(Math.Max(16, checked((int)highestIdentity + 1)))];
            foreach (var instance in originInstances) instanceOwners[instance.identity] = HoGeometryRendererBinding.GetManagedValue(instance.renderer) & 65535u;
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
                var key=(renderer,data.cameraData.camera,requestedLight);
                int estimates=mode==HoRTShadowReconstruction.SigmaSingleFrame?Mathf.Clamp(settings.singleFrameEstimates,1,256):1;
                if(outputs.TryGetValue(key,out var output)&&(output.width!=descriptor.width||output.height!=descriptor.height||output.reconstruction!=mode||output.instanceOwners.count!=instanceOwners.Length||(output.sigma!=null&&output.sigma.Estimates!=estimates)))
                {output.ReleaseAfterUse();outputs.Remove(key);output=null;}
                if(output==null){output=new Output(descriptor.width,descriptor.height,mode,sigmaPipelines,estimates,instanceOwners.Length);outputs.Add(key,output);}
                output.lastView=view;output.light=requestedLight;output.renderSerial=++serial;output.producedSerial=0;
                bool isMain=requestedLight==light&&requestedLight.type==LightType.Directional;
                if(isMain)LastOutput=output;
                output.pass.Setup(output,view,shader,sigmaShader,mask,settings,requestedLight,isMain,scheduled.Count,instanceOwners);
                output.receiverCount = receiverCount;
                output.slot=scheduled.Count;
                scheduled.Add(output);
            }
            dead.Clear();foreach(var pair in outputs)if(ReferenceEquals(pair.Key.renderer,renderer)&&pair.Key.camera==data.cameraData.camera&&!scheduled.Contains(pair.Value))dead.Add(pair.Key);
            foreach(var key in dead){outputs[key].ReleaseAfterUse();outputs.Remove(key);}
            if(scheduled.Count>0)HoAttributeCompositeConsumerRegistry.DeclareForCamera(data.cameraData.camera,this,"RTShadow primary identity and normals",Array.Empty<HoACQueryDescriptor>(),null,HoACDemandResources.Identity|HoACDemandResources.HighPrecisionSurfaceNormal);
            // Metadata pass is separate so rejected products are observable even
            // when there is nothing to trace. No invalid product masquerades as V=1.
            var publish=new ProductPass();publish.camera=data.cameraData.camera;publish.products=rejected.ToArray();renderer.EnqueuePass(publish);
            foreach(var output in scheduled)renderer.EnqueuePass(output.pass);
            Status=$"请求 {requested.Count} 灯 / 生产 {scheduled.Count} / 不可用 {rejected.Count}";
        }
        private sealed class ProductPass:ScriptableRenderPass
        {
            internal Camera camera;internal HoRTShadowProduct[] products;
            internal ProductPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {var resources=frame.GetOrCreate<HoRTShadowRenderGraphResources>();resources.Camera=camera;foreach(var product in products)resources.Add(product);}
            public override void Execute(ScriptableRenderContext context,ref RenderingData data){}
        }
        private void ReleaseOutputs() { foreach (var output in outputs.Values) output.ReleaseAfterUse(); outputs.Clear(); scheduled.Clear(); rejected.Clear(); LastOutput = null; HoAttributeCompositeConsumerRegistry.Remove(this); }
        protected override void Dispose(bool disposing)
        { if (registered) RenderPipelineManager.beginCameraRendering -= ResetCamera; registered = false; ResetCamera(default, null); ReleaseOutputs(); debugPass?.ReleaseDebugResources(); CoreUtils.Destroy(debugMaterial); debugMaterial = null; }

        internal sealed class ShadowPass : ScriptableRenderPass
        {
            private Output output; private HoRTView view; private RayTracingShader shader; private uint[] receivers, instanceOwners;
            private ComputeShader sigmaShader;
            private float terminatorCorrection;
            private Vector4 lightDirection, parameters, localPosition, localForward; private bool main; private int slot; private int samples, seed, signature; private float strength, tanRadius;
            internal ShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }
            internal void Setup(Output output, HoRTView view, RayTracingShader shader, ComputeShader sigmaShader, uint[] receivers, HoRTShadowSettings settings, Light light, bool main, int slot, uint[] instanceOwners)
            {
                this.output = output; this.view = view; this.shader = shader; this.receivers = receivers;
                this.instanceOwners = instanceOwners;
                terminatorCorrection = Mathf.Clamp01(settings.terminatorCorrection);
                output.terminatorCorrection = terminatorCorrection;
                this.sigmaShader = sigmaShader; this.main=main;this.slot=slot;
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
                unchecked { foreach(uint identity in instanceOwners) signature=signature*397^(int)identity; signature=signature*397^terminatorCorrection.GetHashCode(); }
            }
            private sealed class Data
            {
                internal Output output; internal HoRTScene.Inputs scene; internal HoRTPrimarySurfaceInputs primary;
                internal RayTracingShader shader; internal TextureHandle raw; internal uint[] receivers, instanceOwners;
                internal float terminatorCorrection;
                internal Vector4 direction, parameters, localPosition, localForward; internal int lightType, slot;internal bool main; internal int samples, seed; internal float strength;
                internal ComputeShader sigmaShader; internal Matrix4x4 cameraView, projection; internal long lifetime; internal uint revision; internal int signature, lightId; internal float tanRadius; internal double time;
            }
            private void Snapshot(Data d, Camera camera, Matrix4x4 cameraView, Matrix4x4 projection)
            {
                d.output = output; d.shader = shader; d.receivers = receivers; d.direction = lightDirection; d.parameters = parameters; d.samples = samples; d.seed = seed; d.strength = strength; d.main=main;d.slot=slot;d.localPosition=localPosition;d.localForward=localForward;d.lightType=output.light.type==LightType.Directional?0:output.light.type==LightType.Point?1:2;
                d.instanceOwners = instanceOwners;
                d.terminatorCorrection = terminatorCorrection;
                d.sigmaShader = sigmaShader; d.cameraView = cameraView; d.projection = projection; d.lifetime = view.Scene.Lifetime; d.revision = view.Scene.Revision; d.signature = signature; d.lightId = output.light.GetInstanceID(); d.tanRadius = tanRadius;
                d.time = Time.unscaledTimeAsDouble;
                #if UNITY_EDITOR
                if (!Application.isPlaying && camera.cameraType == CameraType.SceneView) d.time = UnityEditor.EditorApplication.timeSinceStartup;
                #endif
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<Data>("Ho-RTShadow raw main-light visibility", out var d))
                {
                    var camera = frame.Get<UniversalCameraData>(); Snapshot(d, camera.camera, camera.GetViewMatrix(), camera.GetProjectionMatrix());
                    d.scene = view.Read(graph, builder); d.primary = HoRTPrimarySurfaceInputs.Read(graph, frame, builder, true);
                    d.raw = graph.ImportTexture(output.raw); builder.UseTexture(d.raw, output.sigma == null ? AccessFlags.WriteAll : AccessFlags.ReadWrite); builder.UseBuffer(graph.ImportBuffer(output.receivers), AccessFlags.ReadWrite);
                    builder.UseBuffer(graph.ImportBuffer(output.instanceOwners),AccessFlags.ReadWrite);
                    if(main)builder.SetGlobalTextureAfterPass(d.raw, Shader.PropertyToID("_HoRTShadowRawTexture"));
                    var visibility = graph.ImportTexture(output.Visibility);
                    if (output.sigma != null) output.sigma.Declare(graph, builder);
                    if(main)builder.SetGlobalTextureAfterPass(visibility, Shader.PropertyToID("_HoRTShadowVisibilityTexture"));
                    var surfaceDepth = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>().depthTexture;
                    if (!surfaceDepth.IsValid()) surfaceDepth = graph.defaultResources.blackTexture;
                    builder.UseTexture(surfaceDepth); if(main)builder.SetGlobalTextureAfterPass(surfaceDepth, Shader.PropertyToID("_HoRTShadowDepthTexture"));
                    var resources = frame.GetOrCreate<HoRTShadowRenderGraphResources>();
                    resources.Add(new HoRTShadowProduct(output.light,HoRTLightIdentity.Get(output.light),slot,output.renderSerial,output.width,output.height,samples,output.sigma?.Estimates??1,view.Scene.Lifetime,view.Scene.Revision,output.reconstruction,d.raw,visibility,terminatorCorrection:terminatorCorrection));
                    if(main)
                    {
                        resources.raw = d.raw; resources.visibility = visibility; resources.reconstruction = output.reconstruction; resources.camera = frame.Get<UniversalCameraData>().camera; resources.light = output.light; resources.lightId = output.light.GetInstanceID();
                        resources.width = output.width; resources.height = output.height; resources.samples = samples; resources.estimates = output.sigma?.Estimates ?? 1; resources.sceneLifetime = view.Scene.Lifetime; resources.sceneRevision = view.Scene.Revision; resources.renderSerial = output.renderSerial;
                    }
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd); d.primary.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd));
                        if (d.output.sigma != null) d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd), d.sigmaShader, d.sigmaShader.FindKernel("Prepare"));
                        Produce(cmd, d);
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
                cmd.SetBufferData(d.output.instanceOwners,d.instanceOwners);
                cmd.SetRayTracingBufferParam(d.shader,"_HoRTShadowInstanceOwners",d.output.instanceOwners);
                cmd.SetRayTracingIntParam(d.shader,"_HoRTShadowInstanceOwnerCount",d.instanceOwners.Length);
                cmd.SetRayTracingFloatParam(d.shader,"_HoRTShadowTerminatorCorrection",d.terminatorCorrection);
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
                Produce(cmd, d); if(main)cmd.SetGlobalTexture("_HoRTShadowRawTexture", output.raw.nameID);
                if(main)cmd.SetGlobalTexture("_HoRTShadowVisibilityTexture", output.Visibility.nameID);
                var depth = HoSurfaceBufferPass.CompatibilityProduced ? HoSurfaceBufferPass.CompatibilityDepthTexture : null;
                if(main)cmd.SetGlobalTexture("_HoRTShadowDepthTexture", depth?.rt != null ? depth.nameID : new RenderTargetIdentifier(Texture2D.blackTexture));
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
