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
    [Serializable]
    public sealed class HoRTShadowSettings
    {
        [InspectorName("启用")] public bool enabled = true;
        [InspectorName("Scene View")] public bool inSceneView = true;
        [InspectorName("投射物层")] public LayerMask geometryLayers = -1;
        [InspectorName("排除投射材质")] public List<Material> excludedRayMaterials = new();
        [InspectorName("射线起点距离（米）"), Min(0.000001f)] public float rayBias = .0001f;
        [InspectorName("射线距离上限（米）"), Range(.01f, 65504)] public float maxDistance = 10000;
        [InspectorName("光源角直径（度）"), Range(0, 8)] public float angularDiameter;
        [InspectorName("同帧样本数"), Range(1, 4096)] public int samplesPerPixel = 1;
        [InspectorName("采样种子")] public int samplingSeed;
    }

    // Borrowed only during this graph. Raw RGBA = visibility, minimum actual
    // blocker distance among samples (65504 on all miss), status, SB owner.
    // Status 1 is valid; 0 unrequested; 2 invalid surface; 10+native = query error.
    // Multiple-sample distance is not an NRD frontend input statistic.
    internal sealed class HoRTShadowRenderGraphResources : ContextItem
    {
        internal TextureHandle raw;
        internal Camera camera; internal Light light;
        internal int lightId, width, height, samples;
        internal long sceneLifetime, renderSerial;
        internal uint sceneRevision;
        public override void Reset() { raw = TextureHandle.nullHandle; camera = null; light = null; lightId = width = height = samples = 0; sceneLifetime = renderSerial = 0; sceneRevision = 0; }
    }

    [DisallowMultipleRendererFeature("Ho-RTShadow")]
    public sealed class HoRTShadowRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoRTShadowSettings settings = new();
        [SerializeField] private RayTracingShader shader;
        [SerializeField] private Shader visibilityMaterialShader;
        private readonly Dictionary<(ScriptableRenderer, Camera), Output> outputs = new();
        private readonly List<(ScriptableRenderer, Camera)> dead = new();
        private ShadowPass pass; private object profileKey; private bool registered;
        private long serial;
        public HoRTShadowSettings Settings => settings;
        public string Status { get; private set; } = "未生产";
        internal Output LastOutput { get; private set; }
        internal sealed class Output : IDisposable
        {
            internal readonly int width, height;
            internal readonly RTHandle raw;
            internal readonly GraphicsBuffer receivers;
            internal HoRTView lastView;
            internal Light light; internal long renderSerial;
            private bool disposed;
            internal Output(int width, int height)
            {
                this.width = width; this.height = height;
                var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat, depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true, dimension = TextureDimension.Tex2D };
                raw = RTHandles.Alloc(descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "HoRTShadow Raw");
                receivers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2048, sizeof(uint)) { name = "HoRTShadow receiver identities" };
            }
            public void Dispose() { if (disposed) return; disposed = true; raw.Release(); receivers.Dispose(); }
            internal void ReleaseAfterUse() { lastView.ReleaseAfterUse(this); }
        }
        public override void Create()
        {
            ReleaseOutputs(); pass = new ShadowPass(); profileKey = new object();
            #if UNITY_EDITOR
            const string root = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/";
            if (shader == null) shader = UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(root + "RTShadow/Shaders/HoRTShadow.raytrace");
            if (visibilityMaterialShader == null) visibilityMaterialShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root + "RT/Shaders/HoRTVisibilityMaterial.shader");
            #endif
            if (!registered) { RenderPipelineManager.beginCameraRendering += ResetCamera; registered = true; }
        }
        private void ResetCamera(ScriptableRenderContext context, Camera camera)
        { LastOutput = null; Shader.SetGlobalFloat("_HoRTShadowActive", 0); Shader.SetGlobalTexture("_HoRTShadowRawTexture", Texture2D.blackTexture); Shader.SetGlobalTexture("_HoRTShadowVisibilityTexture", Texture2D.blackTexture); Shader.SetGlobalTexture("_HoRTShadowDepthTexture", Texture2D.blackTexture); }
        private bool CameraSupported(CameraData data) => settings.enabled && data.renderType == CameraRenderType.Base
            && (data.camera.cameraType == CameraType.Game || (settings.inSceneView && data.camera.cameraType == CameraType.SceneView));
        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData data)
        {
            if (!CameraSupported(data) || !HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null) return;
            var hitShader = visibilityMaterialShader;
            if (!HoRTRendererFeature.Declare(renderer, data.camera, this, new HoRTViewRequest(settings.geometryLayers.value,
                settings.excludedRayMaterials, profileKey: profileKey, createProfile: () => new HoRTVisibilityMaterials(hitShader), geometryPolicy: HoRTGeometryPolicy.ShadowCasters), out var reason)) Status = "Ho-RT：" + reason;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            LastOutput = null;
            if (!CameraSupported(data.cameraData)) { Status = "当前相机未请求"; return; }
            if (!HoRTScene.IsSupported || !SystemInfo.supportsRayTracing || shader == null || visibilityMaterialShader == null)
            { Status = "需要 D3D12 native RT 与阴影 Shader"; return; }
            if (!HoRTRendererFeature.TryAcquire(renderer, data.cameraData.camera, this, out var view, out var reason)) { Status = "Ho-RT：" + reason; return; }
            int main = data.lightData.mainLightIndex;
            var light = main >= 0 && main < data.lightData.visibleLights.Length ? data.lightData.visibleLights[main].light : null;
            if (light == null || light.type != LightType.Directional) { Status = "没有可用的主方向灯"; return; }
            // Until explicit linking is implemented, don't approximate a
            // restricted light with an all-world caster/receiver product.
            if (light.cullingMask != -1 || light.cookie != null) { Status = "主灯 layer linking / cookie 尚未支持"; return; }
            var descriptor = data.cameraData.cameraTargetDescriptor;
            if (descriptor.msaaSamples != 1 || descriptor.dimension != TextureDimension.Tex2D || data.cameraData.xr.enabled
                || data.cameraData.camera.rect != new Rect(0, 0, 1, 1) || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore))
            { Status = "需要 Mono 全视口单采样和 32F UAV"; return; }
            var mask = HoRTShadowReceiver.Capture(out int receiverCount);
            if (receiverCount == 0) { Status = "没有有效的 RTShadow 接收声明"; return; }
            dead.Clear(); foreach (var pair in outputs) if (pair.Key.Item2 == null) dead.Add(pair.Key);
            foreach (var key in dead) { outputs[key].ReleaseAfterUse(); outputs.Remove(key); }
            var current = (renderer, data.cameraData.camera);
            if (outputs.TryGetValue(current, out var output) && (output.width != descriptor.width || output.height != descriptor.height))
            { output.ReleaseAfterUse(); outputs.Remove(current); output = null; }
            if (output == null) { output = new Output(descriptor.width, descriptor.height); outputs.Add(current, output); }
            output.lastView = view; output.light = light; output.renderSerial = ++serial; LastOutput = output;
            HoAttributeCompositeConsumerRegistry.DeclareForCamera(data.cameraData.camera, this, "RTShadow primary identity and normals", Array.Empty<HoACQueryDescriptor>(), null, HoACDemandResources.Identity | HoACDemandResources.HighPrecisionSurfaceNormal);
            pass.Setup(output, view, shader, mask, settings, light); renderer.EnqueuePass(pass);
            Status = $"已安排主灯 {light.GetInstanceID()} / {receiverCount} 接收身份 / {output.width}×{output.height} / {Mathf.Clamp(settings.samplesPerPixel, 1, 4096)} spp";
        }
        private void ReleaseOutputs() { foreach (var output in outputs.Values) output.ReleaseAfterUse(); outputs.Clear(); LastOutput = null; HoAttributeCompositeConsumerRegistry.Remove(this); }
        protected override void Dispose(bool disposing)
        { if (registered) RenderPipelineManager.beginCameraRendering -= ResetCamera; registered = false; ResetCamera(default, null); ReleaseOutputs(); }

        private sealed class ShadowPass : ScriptableRenderPass
        {
            private Output output; private HoRTView view; private RayTracingShader shader; private uint[] receivers;
            private Vector4 lightDirection, parameters; private int samples, seed; private float strength;
            internal ShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }
            internal void Setup(Output output, HoRTView view, RayTracingShader shader, uint[] receivers, HoRTShadowSettings settings, Light light)
            {
                this.output = output; this.view = view; this.shader = shader; this.receivers = receivers;
                var direction = -light.transform.forward;
                float halfSine = Mathf.Sin(Mathf.Clamp(settings.angularDiameter, 0, 8) * Mathf.Deg2Rad * .25f);
                lightDirection = new Vector4(direction.x, direction.y, direction.z, 2 * halfSine * halfSine);
                parameters = new Vector4(output.width, output.height, Mathf.Clamp(settings.rayBias, .000001f, 1), Mathf.Clamp(settings.maxDistance, .01f, 65504));
                samples = Mathf.Clamp(settings.samplesPerPixel, 1, 4096); seed = settings.samplingSeed; strength = light.shadowStrength;
            }
            private sealed class Data
            {
                internal Output output; internal HoRTScene.Inputs scene; internal HoRTPrimarySurfaceInputs primary;
                internal RayTracingShader shader; internal TextureHandle raw; internal uint[] receivers;
                internal Vector4 direction, parameters; internal int samples, seed; internal float strength;
            }
            private Data Snapshot() => new Data { output = output, shader = shader, receivers = receivers, direction = lightDirection, parameters = parameters, samples = samples, seed = seed, strength = strength };
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                using (var builder = graph.AddUnsafePass<Data>("Ho-RTShadow raw main-light visibility", out var d))
                {
                    d.output = output; d.shader = shader; d.receivers = receivers; d.direction = lightDirection; d.parameters = parameters; d.samples = samples; d.seed = seed; d.strength = strength;
                    d.scene = view.Read(graph, builder); d.primary = HoRTPrimarySurfaceInputs.Read(graph, frame, builder, true);
                    d.raw = graph.ImportTexture(output.raw); builder.UseTexture(d.raw, AccessFlags.WriteAll); builder.UseBuffer(graph.ImportBuffer(output.receivers), AccessFlags.ReadWrite);
                    builder.SetGlobalTextureAfterPass(d.raw, Shader.PropertyToID("_HoRTShadowRawTexture"));
                    builder.SetGlobalTextureAfterPass(d.raw, Shader.PropertyToID("_HoRTShadowVisibilityTexture"));
                    var surfaceDepth = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>().depthTexture;
                    if (!surfaceDepth.IsValid()) surfaceDepth = graph.defaultResources.blackTexture;
                    builder.UseTexture(surfaceDepth); builder.SetGlobalTextureAfterPass(surfaceDepth, Shader.PropertyToID("_HoRTShadowDepthTexture"));
                    var resources = frame.GetOrCreate<HoRTShadowRenderGraphResources>();
                    resources.raw = d.raw; resources.camera = frame.Get<UniversalCameraData>().camera; resources.light = output.light; resources.lightId = output.light.GetInstanceID();
                    resources.width = output.width; resources.height = output.height; resources.samples = samples; resources.sceneLifetime = view.Scene.Lifetime; resources.sceneRevision = view.Scene.Revision; resources.renderSerial = output.renderSerial;
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                    { var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd); d.primary.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd)); Dispatch(cmd, d); });
                }
            }
            private static void Dispatch(CommandBuffer cmd, Data d)
            {
                d.scene.BuildAndBind(cmd, d.shader); cmd.SetBufferData(d.output.receivers, d.receivers);
                cmd.SetRayTracingShaderPass(d.shader, "HoRTVisibility"); cmd.SetRayTracingBufferParam(d.shader, "_HoRTShadowReceivers", d.output.receivers);
                cmd.SetRayTracingTextureParam(d.shader, "_HoRTShadowOutput", d.output.raw.nameID); cmd.SetRayTracingVectorParam(d.shader, "_HoRTShadowDirection", d.direction);
                cmd.SetRayTracingVectorParam(d.shader, "_HoRTShadowParameters", d.parameters); cmd.SetRayTracingIntParam(d.shader, "_HoRTShadowSamples", d.samples); cmd.SetRayTracingIntParam(d.shader, "_HoRTShadowSeed", d.seed);
                cmd.DispatchRays(d.shader, "Cast", (uint)d.output.width, (uint)d.output.height, 1);
                cmd.SetGlobalVector("_HoRTShadowSize", new Vector4(d.output.width, d.output.height, 1f / d.output.width, 1f / d.output.height));
                cmd.SetGlobalFloat("_HoRTShadowMainLightStrength", d.strength);
                cmd.SetGlobalFloat("_HoRTShadowActive", 1);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var d = Snapshot(); d.scene = view.ReadCompatibility(); var cmd = CommandBufferPool.Get("Ho-RTShadow raw main-light visibility");
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd, data.cameraData, true); Dispatch(cmd, d); cmd.SetGlobalTexture("_HoRTShadowRawTexture", output.raw.nameID);
                cmd.SetGlobalTexture("_HoRTShadowVisibilityTexture", output.raw.nameID);
                var depth = HoSurfaceBufferPass.CompatibilityProduced ? HoSurfaceBufferPass.CompatibilityDepthTexture : null;
                cmd.SetGlobalTexture("_HoRTShadowDepthTexture", depth?.rt != null ? depth.nameID : new RenderTargetIdentifier(Texture2D.blackTexture));
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
