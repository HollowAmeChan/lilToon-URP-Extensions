#pragma warning disable CS0618, CS0672
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Atmosphere
{
    [DisallowMultipleRendererFeature("Ho-Atmosphere")]
    public sealed class HoAtmosphereRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoAtmosphereSettings settings = new HoAtmosphereSettings();
        private HoAtmosphereCache cache;
        private EnvironmentPass environmentPass;
        private HoAtmosphereMediumPass mediumPass;
        private HoAtmosphereDebugPass debugPass;
        private Material debugMaterial;
        private bool registered;
        public HoAtmosphereSettings Settings => settings;
        public uint CaptureCount => cache != null ? cache.CaptureCount : 0;
        public string Status { get; private set; } = "未生产";
        public HoAtmosphereMediumSnapshot Medium { get; private set; }
        public string MediumStatus => Medium.status ?? "未生产";
        public void RefreshEnvironment() => cache?.Refresh();

        public override void Create()
        {
            cache ??= new HoAtmosphereCache();
            environmentPass = new EnvironmentPass(cache);
            mediumPass?.Dispose();
            mediumPass = new HoAtmosphereMediumPass();
            debugPass?.ReleaseDebugResources();
            debugPass = new HoAtmosphereDebugPass();
            if (!registered)
            {
                RenderPipelineManager.beginCameraRendering += ResetCamera;
                registered = true;
            }
        }

        private void ResetCamera(ScriptableRenderContext context, Camera camera)
        {
            HoAtmosphereBindings.ResetGlobals();
            HoAtmosphereMediumBindings.ResetGlobals();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            var volume = VolumeManager.instance.stack.GetComponent<HoAtmosphereVolume>();
            bool enabled = settings.enabled && (volume == null || !volume.enable.overrideState || volume.enable.value);
            if (!settings.enabled || renderingData.cameraData.renderType != CameraRenderType.Base ||
                (camera.cameraType != CameraType.Game && (camera.cameraType != CameraType.SceneView || !settings.inSceneView)))
            {
                Status = "本相机未生产";
                Medium = default;
                return;
            }
            Medium = HoAtmosphereRegionRegistry.Capture();
            mediumPass.Setup(Medium);
            renderer.EnqueuePass(mediumPass);
            if (!enabled)
            {
                Status = "环境输入关闭";
                EnqueueDebug(renderer, camera, volume);
                return;
            }
            Light sun = RenderSettings.sun;
            int main = renderingData.lightData.mainLightIndex;
            if (main >= 0 && main < renderingData.lightData.visibleLights.Length) sun = renderingData.lightData.visibleLights[main].light;
            var input = cache.Prepare(settings, sun);
            float gain = volume != null && volume.intensity.overrideState ? volume.intensity.value : settings.intensity;
            float yaw = volume != null && volume.rotationY.overrideState ? volume.rotationY.value : settings.rotationY;
            Color tint = volume != null && volume.tint.overrideState ? volume.tint.value : settings.tint;
            yaw = HoAtmosphereCache.Finite(yaw, 0) * Mathf.Deg2Rad;
            Vector4 parameters = new Vector4(Mathf.Sin(yaw), Mathf.Cos(yaw), Mathf.Max(0, HoAtmosphereCache.Finite(gain, 0)), input.valid ? 1 : 0);
            Vector4 color = HoAtmosphereCache.RGB(tint);
            Vector4 constant = HoAtmosphereCache.RGB(settings.constantColor);
            constant.w = settings.source == HoAtmosphereSource.Constant ? 1 : 0;
            uint revision = unchecked((uint)HoAtmosphereCache.Hash((int)input.content,
                HoAtmosphereCache.Hash(parameters.GetHashCode(), HoAtmosphereCache.Hash(color.GetHashCode(), constant.GetHashCode()))));
            environmentPass.Setup(input, parameters, color, constant, input.valid ? revision : 0);
            renderer.EnqueuePass(environmentPass);
            Status = input.valid ? (input.capture ? "已安排天空捕获" : "复用环境输入") : "天空来源缺失或不支持，输出无效/黑色";
            EnqueueDebug(renderer, camera, volume);
        }
        private void EnqueueDebug(ScriptableRenderer renderer, Camera camera, HoAtmosphereVolume volume)
        {
            if (volume != null && volume.debugMode.value != HoAtmosphereDebugMode.Off &&
                (camera.cameraType == CameraType.SceneView ? volume.debugInSceneView.value : volume.debugInGameView.value))
            {
                if (debugMaterial == null)
                {
                    var shader = Shader.Find("Hidden/lilToon/URP/Atmosphere/Debug");
                    if (shader != null) debugMaterial = CoreUtils.CreateEngineMaterial(shader);
                }
                if (debugMaterial != null)
                {
                    debugPass.Setup(volume.debugMode.value, debugMaterial);
                    debugPass.Enqueue(renderer);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (registered) RenderPipelineManager.beginCameraRendering -= ResetCamera;
            registered = false;
            HoAtmosphereBindings.ResetGlobals();
            cache?.Dispose(); cache = null;
            mediumPass?.Dispose(); mediumPass = null; Medium = default;
            debugPass?.ReleaseDebugResources(); debugPass = null;
            CoreUtils.Destroy(debugMaterial); debugMaterial = null;
            environmentPass = null;
        }

        private sealed class EnvironmentPass : ScriptableRenderPass
        {
            private readonly HoAtmosphereCache cache;
            private new HoAtmosphereCache.Input input;
            private Vector4 parameters, tint, constant;
            private uint revision;
            public EnvironmentPass(HoAtmosphereCache cache) { this.cache = cache; renderPassEvent = RenderPassEvent.AfterRenderingPrePasses; }
            public void Setup(HoAtmosphereCache.Input input, Vector4 parameters, Vector4 tint, Vector4 constant, uint revision)
            {
                this.input = input; this.parameters = parameters; this.tint = tint; this.constant = constant; this.revision = revision;
            }
            private sealed class FaceData
            {
                public RendererListHandle list; public int size, face; public bool last; public HoAtmosphereCache cache; public HoAtmosphereCache.Input input;
                public Matrix4x4 view, projection, restoreView, restoreProjection;
            }
            private sealed class PublishData { public HoAtmosphereRenderGraphResources resources; }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                TextureHandle texture = graph.ImportTexture(input.texture);
                if (input.capture)
                {
                    var cameraData = frameData.Get<UniversalCameraData>();
                    for (int face = 0; face < 6; ++face)
                    {
                        var faceCamera = cache.FaceCamera(face);
                        var list = graph.CreateSkyboxRendererList(faceCamera);
                        // The RG raster attachment fallback does not select all cube faces correctly on D3D11.
                        // Explicit CubemapFace binding also matches the compatibility path.
                        using (var builder = graph.AddUnsafePass<FaceData>("HoAtmosphere Capture " + (CubemapFace)face, out var data))
                        {
                            data.list = list; data.size = input.resolution; data.face = face; data.last = face == 5; data.cache = cache; data.input = input;
                            data.view = faceCamera.worldToCameraMatrix; data.projection = faceCamera.projectionMatrix;
                            data.restoreView = cameraData.GetViewMatrix(); data.restoreProjection = cameraData.GetProjectionMatrix();
                            builder.UseRendererList(list);
                            builder.UseTexture(texture, AccessFlags.Write);
                            builder.AllowPassCulling(false);
                            builder.AllowGlobalStateModification(true);
                            builder.SetRenderFunc(static (FaceData d, UnsafeGraphContext context) =>
                            {
                                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                                cmd.SetRenderTarget(d.input.texture.nameID, 0, (CubemapFace)d.face);
                                cmd.SetViewport(new Rect(0, 0, d.size, d.size));
                                cmd.ClearRenderTarget(false, true, Color.clear);
                                cmd.SetViewProjectionMatrices(d.view, d.projection);
                                cmd.DrawRendererList(d.list);
                                cmd.SetViewProjectionMatrices(d.restoreView, d.restoreProjection);
                                if (d.last) d.cache.Complete(d.input);
                            });
                        }
                    }
                }
                var resources = frameData.GetOrCreate<HoAtmosphereRenderGraphResources>();
                resources.radianceTexture = texture; resources.parameters = parameters; resources.tint = tint;
                resources.constantColor = constant; resources.revision = revision;
                using (var builder = graph.AddRasterRenderPass<PublishData>("HoAtmosphere Publish", out var data))
                {
                    data.resources = resources;
                    builder.UseTexture(texture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(texture, HoAtmosphereBindings.RadianceId);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PublishData d, RasterGraphContext context) => HoAtmosphereBindings.Bind(context.cmd, d.resources));
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("HoAtmosphere");
                if (input.capture)
                {
                    for (int face = 0; face < 6; ++face)
                    {
                        var faceCamera = cache.FaceCamera(face);
                        var list = context.CreateSkyboxRendererList(faceCamera);
                        cmd.SetRenderTarget(input.texture.nameID, 0, (CubemapFace)face);
                        cmd.SetViewport(new Rect(0, 0, input.resolution, input.resolution));
                        cmd.ClearRenderTarget(false, true, Color.clear);
                        cmd.SetViewProjectionMatrices(faceCamera.worldToCameraMatrix, faceCamera.projectionMatrix);
                        cmd.DrawRendererList(list);
                    }
                    cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), renderingData.cameraData.GetProjectionMatrix());
                    CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle,
                        renderingData.cameraData.renderer.cameraDepthTargetHandle);
                    var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                    cmd.SetViewport(renderingData.cameraData.isDefaultViewport
                        ? new Rect(0, 0, descriptor.width, descriptor.height)
                        : renderingData.cameraData.camera.pixelRect);
                    cache.Complete(input);
                }
                cmd.SetGlobalTexture(HoAtmosphereBindings.RadianceId, input.texture.nameID);
                cmd.SetGlobalVector(HoAtmosphereBindings.ParametersId, parameters);
                cmd.SetGlobalVector(HoAtmosphereBindings.TintId, tint);
                cmd.SetGlobalVector(HoAtmosphereBindings.ConstantId, constant);
                HoAtmosphereBindings.compatibilityTexture = input.texture;
                HoAtmosphereBindings.compatibilityParameters = parameters;
                HoAtmosphereBindings.compatibilityTint = tint;
                HoAtmosphereBindings.compatibilityConstant = constant;
                HoAtmosphereBindings.compatibilityRevision = revision;
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
