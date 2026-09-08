#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.GeometryBuffer;

namespace lilToon.URP.Extensions.GTAO
{
    [DisallowMultipleRendererFeature("Ho-GTAO")]
    public sealed class HoGTAORendererFeature : ScriptableRendererFeature
    {
        [SerializeField, InspectorName("Ho-GTAO 设置")]
        private HoGTAOSettings settings = new HoGTAOSettings();

        private HoGTAOPass pass;
        private HoGTAODebugPass debugPass;
        private Material material;
        private Shader shader;
        private Material debugMaterial;
        private Shader debugShader;
        private HoGTAOHistory history;
        private HoGTAOQuality lastAppliedQuality = (HoGTAOQuality)(-1);
        private bool resetRegistered;

        public HoGTAOSettings Settings => settings;

        public override void Create()
        {
            pass = new HoGTAOPass();
            debugPass = new HoGTAODebugPass();
            history = new HoGTAOHistory();
            RenderPipelineManager.beginCameraRendering += ResetGlobalState;
            resetRegistered = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

            ResolveVolume();
            if (settings.quality != lastAppliedQuality)
            {
                HoGTAOQualityPresets.Apply(settings.quality, settings);
                lastAppliedQuality = settings.quality;
            }

            Shader currentShader = settings.shader != null ? settings.shader : Shader.Find(HoGTAOShaderConstants.ShaderName);
            if (currentShader == null)
            {
                return;
            }

            if (material == null || shader != currentShader)
            {
                CoreUtils.Destroy(material);
                shader = currentShader;
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            Shader currentDebugShader = Shader.Find(HoGTAOShaderConstants.DebugShaderName);
            if (debugMaterial == null || debugShader != currentDebugShader)
            {
                CoreUtils.Destroy(debugMaterial);
                debugShader = currentDebugShader;
                debugMaterial = debugShader != null ? CoreUtils.CreateEngineMaterial(debugShader) : null;
            }

            pass.Setup(settings, material, debugMaterial, history);
            renderer.EnqueuePass(pass);
            if (settings.debugMode != HoGTAODebugMode.Off && debugMaterial != null)
            {
                debugPass.Setup(debugMaterial, settings.debugMode, settings.debugIntensity);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (resetRegistered)
            {
                RenderPipelineManager.beginCameraRendering -= ResetGlobalState;
                resetRegistered = false;
            }

            CoreUtils.Destroy(material);
            CoreUtils.Destroy(debugMaterial);
            material = null;
            shader = null;
            debugMaterial = null;
            debugShader = null;
            history?.Dispose();
            history = null;
            pass = null;
            debugPass = null;
        }

        private static void ResetGlobalState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(HoGTAOShaderConstants.AOTextureId, Texture2D.whiteTexture);
        }

        private void ResolveVolume()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            HoGTAOVolume volume = stack != null ? stack.GetComponent<HoGTAOVolume>() : null;
            if (volume == null || settings == null)
            {
                return;
            }

            if (volume.quality.overrideState)
            {
                settings.quality = volume.quality.value;
                HoGTAOQualityPresets.Apply(volume.quality.value, settings);
                lastAppliedQuality = volume.quality.value;
            }
            if (volume.resolution.overrideState) settings.resolution = volume.resolution.value;
            if (volume.worldSpaceRadius.overrideState) settings.worldSpaceRadius = volume.worldSpaceRadius.value;
            if (volume.screenSpaceRadius.overrideState) settings.screenSpaceRadius = volume.screenSpaceRadius.value;
            if (volume.thickness.overrideState) settings.thickness = volume.thickness.value;
            if (volume.sliceCount.overrideState) settings.sliceCount = volume.sliceCount.value;
            if (volume.stepCount.overrideState) settings.stepCount = volume.stepCount.value;
            if (volume.useAttenuation.overrideState) settings.useAttenuation = volume.useAttenuation.value;
            if (volume.temporalFrameCount.overrideState) settings.temporalFrameCount = volume.temporalFrameCount.value;
            if (volume.temporalRejection.overrideState) settings.temporalRejection = volume.temporalRejection.value;
            if (volume.spatialFilter.overrideState) settings.spatialFilter = volume.spatialFilter.value;
            if (volume.filterRadius.overrideState) settings.filterRadius = volume.filterRadius.value;
            if (volume.filterAdaptivity.overrideState) settings.filterAdaptivity = volume.filterAdaptivity.value;
            if (volume.boxPassCount.overrideState) settings.boxPassCount = volume.boxPassCount.value;
        }
    }

    internal sealed class HoGTAOHistory : System.IDisposable
    {
        private RTHandle previous;
        private RTHandle next;
        private RTHandle previousDepth;
        private RTHandle nextDepth;
        private bool valid;

        public RTHandle Previous => previous;
        public RTHandle Next => next;
        public RTHandle PreviousDepth => previousDepth;
        public RTHandle NextDepth => nextDepth;
        public bool Valid => valid;

        public void Ensure(int width, int height)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };

            RenderingUtils.ReAllocateIfNeeded(ref previous, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevTex");
            RenderingUtils.ReAllocateIfNeeded(ref next, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextTex");
            RenderTextureDescriptor depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref previousDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevDepthTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextDepthTex");
            valid = false;
        }

        public void MarkValid() => valid = true;

        public void Swap()
        {
            RTHandle temp = previous;
            previous = next;
            next = temp;
            temp = previousDepth;
            previousDepth = nextDepth;
            nextDepth = temp;
        }

        public void Dispose()
        {
            previous?.Release();
            next?.Release();
            previousDepth?.Release();
            nextDepth?.Release();
            previous = null;
            next = null;
            previousDepth = null;
            nextDepth = null;
            valid = false;
        }
    }

    internal sealed class HoGTAODebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GTAO Debug Output");

        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle cameraColor;
            public TextureHandle destination;
            public float displayIntensity;
            public float displayInvert;
        }

        private Material material;
        private HoGTAODebugMode debugMode;
        private float debugIntensity = 3.672f;

        public void Setup(Material material, HoGTAODebugMode debugMode, float debugIntensity)
        {
            this.material = material;
            this.debugMode = debugMode;
            this.debugIntensity = Mathf.Clamp(debugIntensity, 0.1f, 8.0f);
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            HoGTAORenderGraphResources gtao = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!gtao.HasAO || !cameraColor.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            destinationDesc.name = "_HoGTAODebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-GTAO Debug Output", out PassData data, ProfilingSampler))
            {
                data.material = material;
                data.source = gtao.aoTexture;
                data.cameraColor = cameraColor;
                data.destination = destination;
                // HTrace profile: Intensity=3.06, output exponent=Intensity*1.2.
                data.displayIntensity = debugMode == HoGTAODebugMode.AO ? debugIntensity : 1.0f;
                data.displayInvert = debugMode == HoGTAODebugMode.AO ? 1.0f : 0.0f;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.cameraColor, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.DebugIntensityId, passData.displayIntensity);
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.DebugInvertId, passData.displayInvert);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }

    internal sealed class HoGTAOPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GTAO");
        private static readonly int DebugModeId = Shader.PropertyToID("_HoGTAODebugMode");
        private static readonly int HistoryBlendId = Shader.PropertyToID("_HoGTAOHistoryBlend");
        private static readonly int HistoryValidId = Shader.PropertyToID("_HoGTAOHistoryValid");
        private static readonly int TemporalMaxFramesId = Shader.PropertyToID("_HoGTAOTemporalMaxFrames");
        private static readonly int TemporalRejectionId = Shader.PropertyToID("_HoGTAOTemporalRejection");
        private static readonly int HistoryPrevTexelSizeId = Shader.PropertyToID("_HoGTAOHistoryPrevTex_TexelSize");
        private static readonly int WorldRadiusId = Shader.PropertyToID("_HoGTAOWorldSpaceRadius");
        private static readonly int ScreenRadiusId = Shader.PropertyToID("_HoGTAOScreenSpaceRadius");
        private static readonly int ThicknessId = Shader.PropertyToID("_HoGTAOThickness");
        private static readonly int UseAttenuationId = Shader.PropertyToID("_HoGTAOUseAttenuation");
        private static readonly int LinearThicknessId = Shader.PropertyToID("_HoGTAOUseLinearThickness");
        private static readonly int SliceCountId = Shader.PropertyToID("_HoGTAOSliceCount");
        private static readonly int StepCountId = Shader.PropertyToID("_HoGTAOStepCount");
        private static readonly int FrameIndexId = Shader.PropertyToID("_HoGTAOFrameIndex");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_HoGTAOViewMatrix");
        private static readonly int ProjMatrixId = Shader.PropertyToID("_HoGTAOProjMatrix");
        private static readonly int InvProjMatrixId = Shader.PropertyToID("_HoGTAOInvProjMatrix");
        private static readonly int DepthToViewParamsId = Shader.PropertyToID("_HoGTAODepthToViewParams");
        private static readonly int OrthographicId = Shader.PropertyToID("_HoGTAOOrthographic");
        private static readonly int GeometryInputId = Shader.PropertyToID("_HoGTAOGeometryInput");
        private static readonly int SpatialRadiusId = Shader.PropertyToID("_HoGTAOSpatialRadius");
        private static readonly int SpatialAdaptivityId = Shader.PropertyToID("_HoGTAOSpatialAdaptivity");
        private static readonly int SpatialResolutionId = Shader.PropertyToID("_HoGTAOSpatialResolution");
        private static readonly int SpatialFilterId = Shader.PropertyToID("_HoGTAOSpatialFilter");
        private static readonly int SpatialStepId = Shader.PropertyToID("_HoGTAOSpatialStep");
        private static readonly int PixelSpreadMultiplierId = Shader.PropertyToID("_HoGTAOPixelSpreadMultiplier");
        private static readonly int HistoryDepthPrevId = Shader.PropertyToID("_HoGTAOHistoryPrevDepthTex");
        private static readonly int DepthInputId = Shader.PropertyToID("_HoGTAODepthInput");
        private static readonly int DepthInputTexelSizeId = Shader.PropertyToID("_HoGTAODepthInputTexelSize");
        private static readonly int DepthMip0Id = Shader.PropertyToID("_HoGTAODepthMip0");
        private static readonly int DepthMip1Id = Shader.PropertyToID("_HoGTAODepthMip1");
        private static readonly int DepthMip2Id = Shader.PropertyToID("_HoGTAODepthMip2");
        private static readonly int DepthMip3Id = Shader.PropertyToID("_HoGTAODepthMip3");
        private static readonly int MotionVectorTextureId = Shader.PropertyToID("_MotionVectorTexture");
        private static readonly int UseMotionVectorsId = Shader.PropertyToID("_HoGTAOUseMotionVectors");

        private sealed class GenerateData
        {
            public Material material;
            public TextureHandle normalDepth;
            public TextureHandle output;
            public int debugMode;
            public float worldRadius;
            public float screenRadius;
            public float thickness;
            public float useAttenuation;
            public float linearThickness;
            public int sliceCount;
            public int stepCount;
            public float frameIndex;
            public Matrix4x4 view;
            public Matrix4x4 proj;
            public Matrix4x4 invProj;
            public Vector4 depthToViewParams;
            public float orthographic;
            public TextureHandle depthMip0;
            public TextureHandle depthMip1;
            public TextureHandle depthMip2;
            public TextureHandle depthMip3;
        }

        private sealed class TemporalData
        {
            public Material material;
            public TextureHandle current;
            public TextureHandle previous;
            public TextureHandle geometry;
            public TextureHandle previousDepth;
            public TextureHandle motionVectors;
            public TextureHandle output;
            public bool useHistory;
            public bool useMotionVectors;
            public bool debugDisocclusion;
            public int maxFrames;
            public float rejection;
            public Vector4 historyTexelSize;
        }

        private sealed class BlitData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle destination;
            public int passIndex;
        }

        private sealed class SpatialData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle geometry;
            public TextureHandle destination;
            public float radius;
            public float adaptivity;
            public float resolution;
            public int filterType;
            public float step;
            public float pixelSpreadMultiplier;
        }

        private sealed class DepthHistoryData
        {
            public Material material;
            public TextureHandle geometry;
            public TextureHandle destination;
        }

        private sealed class DepthData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle destination;
            public Vector4 sourceTexelSize;
            public int passIndex;
            public bool sourceIsGeometry;
            public int outputId;
        }

        private HoGTAOSettings settings;
        private Material material;
        private Material debugMaterial;
        private HoGTAOHistory history;
        private int historyWidth;
        private int historyHeight;
        private int historyCameraId;
        private HoGTAOResolution historyResolution;

        public void Setup(HoGTAOSettings settings, Material material, Material debugMaterial, HoGTAOHistory history)
        {
            this.settings = settings;
            this.material = material;
            this.debugMaterial = debugMaterial;
            this.history = history;
            // Same-event order is intentional: HoUrp's explicit enqueue-order
            // tie-breaker follows the Renderer Feature list (GeometryBuffer must
            // be listed above Ho-GTAO).
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            // Motion vectors are produced by URP's built-in MotionVectorRenderPass.
            // Request depth as well: Ho-GTAO runs before opaques, so URP must build
            // a full prepass and schedule motion-vector production immediately after
            // it. Without the Depth bit, URP defers the motion pass until after the
            // skybox and this pass would only ever see the cleared texture.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null || history == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            if (!resourceData.activeColorTexture.IsValid() || !geometry.normalDepthTexture.IsValid())
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            // Keep the first quality baseline full-resolution. HTrace's Half and
            // Quarter modes use checkerboard addressing into full-resolution
            // geometry; a plain smaller RT with unchanged UVs is not equivalent
            // and produces visibly wrong AO silhouettes.
            int divisor = 1;
            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            int cameraId = cameraData.camera != null ? cameraData.camera.GetInstanceID() : 0;
            if (history.Previous == null || history.PreviousDepth == null || width != historyWidth || height != historyHeight || cameraId != historyCameraId || historyResolution != settings.resolution)
            {
                history.Ensure(width, height);
                historyWidth = width;
                historyHeight = height;
                historyCameraId = cameraId;
                historyResolution = settings.resolution;
            }

            HoGTAORenderGraphResources gtao = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            TextureHandle[] depthMips = CreateDepthPyramid(renderGraph, frameData, geometry.normalDepthTexture, width, height);
            TextureDesc currentDesc = new TextureDesc(width, height)
            {
                name = "_HoGTAOCurrentTex",
                format = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            TextureHandle current = renderGraph.CreateTexture(currentDesc);

            using (var builder = renderGraph.AddRasterRenderPass<GenerateData>("Ho-GTAO Generate", out GenerateData data, ProfilingSampler))
            {
                data.material = material;
                data.normalDepth = geometry.normalDepthTexture;
                data.output = current;
                data.debugMode = (int)settings.debugMode;
                data.worldRadius = settings.worldSpaceRadius;
                data.screenRadius = settings.screenSpaceRadius;
                data.thickness = settings.thickness;
                data.useAttenuation = settings.useAttenuation ? 1.0f : 0.0f;
                data.linearThickness = settings.useLinearThickness ? 1.0f : 0.0f;
                data.sliceCount = settings.sliceCount;
                data.stepCount = settings.stepCount;
                data.frameIndex = Time.frameCount;
                data.view = cameraData.GetViewMatrix();
                data.proj = cameraData.GetProjectionMatrix();
                data.invProj = data.proj.inverse;
                data.depthMip0 = depthMips[0];
                data.depthMip1 = depthMips[1];
                data.depthMip2 = depthMips[2];
                data.depthMip3 = depthMips[3];
                float fovRadians = cameraData.camera.fieldOfView * Mathf.Deg2Rad;
                float halfHeight = cameraData.camera.orthographic
                    ? cameraData.camera.orthographicSize
                    : Mathf.Tan(fovRadians * 0.5f);
                float renderAspect = (float)cameraData.cameraTargetDescriptor.width / Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
                float halfWidth = halfHeight * renderAspect;
                data.depthToViewParams = new Vector4(2.0f * halfWidth, 2.0f * halfHeight, -halfWidth, -halfHeight);
                data.orthographic = cameraData.camera.orthographic ? 1.0f : 0.0f;
                builder.UseTexture(data.normalDepth, AccessFlags.Read);
                builder.UseTexture(data.depthMip0, AccessFlags.Read);
                builder.UseTexture(data.depthMip1, AccessFlags.Read);
                builder.UseTexture(data.depthMip2, AccessFlags.Read);
                builder.UseTexture(data.depthMip3, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (GenerateData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(DebugModeId, passData.debugMode);
                    context.cmd.SetGlobalFloat(WorldRadiusId, passData.worldRadius);
                    context.cmd.SetGlobalFloat(ScreenRadiusId, passData.screenRadius);
                    context.cmd.SetGlobalFloat(ThicknessId, passData.thickness);
                    context.cmd.SetGlobalFloat(UseAttenuationId, passData.useAttenuation);
                    context.cmd.SetGlobalFloat(LinearThicknessId, passData.linearThickness);
                    context.cmd.SetGlobalFloat(SliceCountId, passData.sliceCount);
                    context.cmd.SetGlobalFloat(StepCountId, passData.stepCount);
                    context.cmd.SetGlobalFloat(FrameIndexId, passData.frameIndex);
                    context.cmd.SetGlobalMatrix(ViewMatrixId, passData.view);
                    context.cmd.SetGlobalMatrix(ProjMatrixId, passData.proj);
                    context.cmd.SetGlobalMatrix(InvProjMatrixId, passData.invProj);
                    context.cmd.SetGlobalVector(DepthToViewParamsId, passData.depthToViewParams);
                    context.cmd.SetGlobalFloat(OrthographicId, passData.orthographic);
                    context.cmd.SetGlobalTexture(DepthMip0Id, passData.depthMip0);
                    context.cmd.SetGlobalTexture(DepthMip1Id, passData.depthMip1);
                    context.cmd.SetGlobalTexture(DepthMip2Id, passData.depthMip2);
                    context.cmd.SetGlobalTexture(DepthMip3Id, passData.depthMip3);
                    Blitter.BlitTexture(context.cmd, passData.normalDepth, new Vector4(1, 1, 0, 0), passData.material, 2);
                });
            }

            bool debugTemporal = settings.debugMode == HoGTAODebugMode.Temporal;
            bool debugRawGeometry = settings.debugMode == HoGTAODebugMode.Depth
                || settings.debugMode == HoGTAODebugMode.Normal;
            if (debugRawGeometry)
            {
                // Publish the generated AO for the later feature-local debug pass.
                // The presentation pass must run after opaques/post-processing so
                // opaque rendering cannot overwrite its camera-color output.
                gtao.aoTexture = current;
                return;
            }

            TextureHandle previous = renderGraph.ImportTexture(history.Previous);
            TextureHandle next = renderGraph.ImportTexture(history.Next);
            TextureHandle previousDepth = renderGraph.ImportTexture(history.PreviousDepth);
            TextureHandle nextDepth = renderGraph.ImportTexture(history.NextDepth);
            TextureHandle motionVectors = resourceData.motionVectorColor;
            using (var builder = renderGraph.AddRasterRenderPass<TemporalData>("Ho-GTAO Temporal", out TemporalData data, ProfilingSampler))
            {
                data.material = material;
                data.current = current;
                data.previous = previous;
                data.geometry = geometry.normalDepthTexture;
                data.previousDepth = previousDepth;
                data.motionVectors = motionVectors;
                data.output = next;
                data.useHistory = history.Valid;
                data.useMotionVectors = motionVectors.IsValid();
                data.debugDisocclusion = debugTemporal;
                // HTrace's SampleCountTemporal=8 gates motion-vector work but
                // accumulates up to g_HTemporalSamplecountAO*2 = 12 frames.
                data.maxFrames = settings.temporalFrameCount > 0 ? 12 : 1;
                data.rejection = settings.temporalRejection;
                data.historyTexelSize = new Vector4(1.0f / Mathf.Max(1, historyWidth), 1.0f / Mathf.Max(1, historyHeight), historyWidth, historyHeight);
                builder.UseTexture(data.current, AccessFlags.Read);
                builder.UseTexture(data.previous, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.previousDepth, AccessFlags.Read);
                if (data.motionVectors.IsValid())
                    builder.UseTexture(data.motionVectors, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (TemporalData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(DebugModeId, passData.debugDisocclusion ? 5.0f : 0.0f);
                    context.cmd.SetGlobalFloat(HistoryBlendId, passData.useHistory ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(HistoryValidId, passData.useHistory ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(TemporalMaxFramesId, passData.maxFrames);
                    context.cmd.SetGlobalFloat(TemporalRejectionId, passData.rejection);
                    context.cmd.SetGlobalVector(HistoryPrevTexelSizeId, passData.historyTexelSize);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.AoInputTexId, passData.current);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.HistoryPrevTexId, passData.previous);
                    context.cmd.SetGlobalTexture(GeometryInputId, passData.geometry);
                    context.cmd.SetGlobalTexture(HistoryDepthPrevId, passData.previousDepth);
                    context.cmd.SetGlobalFloat(UseMotionVectorsId, passData.useMotionVectors ? 1.0f : 0.0f);
                    if (passData.motionVectors.IsValid())
                        context.cmd.SetGlobalTexture(MotionVectorTextureId, passData.motionVectors);
                    Blitter.BlitTexture(context.cmd, passData.current, new Vector4(1, 1, 0, 0), passData.material, 3);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<DepthHistoryData>("Ho-GTAO Depth History", out DepthHistoryData depthData, ProfilingSampler))
            {
                depthData.material = material;
                depthData.geometry = geometry.normalDepthTexture;
                depthData.destination = nextDepth;
                builder.UseTexture(depthData.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(depthData.destination, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DepthHistoryData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.geometry, new Vector4(1, 1, 0, 0), data.material, 6);
                });
            }

            history.Swap();
            history.MarkValid();

            if (debugTemporal)
            {
                // Temporal debug intentionally stops before spatial denoising and
                // final composition so the inspector shows the actual history
                // reprojection/rejection result.
                gtao.aoTexture = next;
                return;
            }

            int spatialPassCount = settings.spatialFilter == HoGTAOSpatialFilter.Box
                ? Mathf.Clamp(settings.boxPassCount, 1, 3)
                : 1;
            TextureHandle spatialSource = next;
            TextureHandle spatial = TextureHandle.nullHandle;
            for (int spatialPass = 0; spatialPass < spatialPassCount; spatialPass++)
            {
                TextureDesc spatialDesc = new TextureDesc(width, height)
                {
                    name = "_HoGTAOSpatialTex" + spatialPass,
                    format = GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = 0,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                spatial = renderGraph.CreateTexture(spatialDesc);
                using (var builder = renderGraph.AddRasterRenderPass<SpatialData>("Ho-GTAO Spatial " + spatialPass, out SpatialData data, ProfilingSampler))
                {
                    data.material = material;
                    data.source = spatialSource;
                    data.geometry = geometry.normalDepthTexture;
                    data.destination = spatial;
                    data.radius = settings.filterRadius;
                    data.adaptivity = settings.filterAdaptivity;
                    data.resolution = 1.0f;
                    data.filterType = (int)settings.spatialFilter;
                data.step = settings.spatialFilter == HoGTAOSpatialFilter.Box
                        ? (spatialPass == 0 ? (settings.boxPassCount >= 3 ? 4.0f : settings.boxPassCount == 2 ? 2.0f : 1.0f) : spatialPass == 1 ? 2.0f : 1.0f)
                        : 1.0f;
                float baselineSpread = 2.0f * Mathf.Tan(60.0f * Mathf.Deg2Rad * 0.5f) / 1080.0f;
                float actualSpread = 2.0f * Mathf.Tan(cameraData.camera.fieldOfView * Mathf.Deg2Rad * 0.5f)
                    / Mathf.Max(1.0f, cameraData.cameraTargetDescriptor.height);
                data.pixelSpreadMultiplier = actualSpread / Mathf.Max(baselineSpread, 1.0e-6f);
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseTexture(data.geometry, AccessFlags.Read);
                    builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SpatialData passData, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalTexture(GeometryInputId, passData.geometry);
                        context.cmd.SetGlobalFloat(SpatialRadiusId, passData.radius);
                        context.cmd.SetGlobalFloat(SpatialAdaptivityId, passData.adaptivity);
                        context.cmd.SetGlobalFloat(SpatialResolutionId, passData.resolution);
                        context.cmd.SetGlobalFloat(SpatialFilterId, passData.filterType);
                    context.cmd.SetGlobalFloat(SpatialStepId, passData.step);
                    context.cmd.SetGlobalFloat(PixelSpreadMultiplierId, passData.pixelSpreadMultiplier);
                        Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 5);
                    });
                }
                spatialSource = spatial;
            }

            if (settings.debugMode == HoGTAODebugMode.AO)
            {
                // HTrace's AO debug view is taken after temporal and spatial
                // denoising. Keep those passes active so the debug image
                // converges instead of showing the raw animated march noise.
                gtao.aoTexture = spatial;
                return;
            }

            gtao.aoTexture = RecordBlit(renderGraph, frameData, spatial, resourceData.activeColorTexture, material, "Ho-GTAO Output");
        }

        private TextureHandle[] CreateDepthPyramid(RenderGraph renderGraph, ContextContainer frameData, TextureHandle geometry, int width, int height)
        {
            TextureHandle[] mips = new TextureHandle[4];
            TextureHandle source = geometry;
            TextureDesc geometryDesc = renderGraph.GetTextureDesc(geometry);
            int sourceWidth = Mathf.Max(1, geometryDesc.width);
            int sourceHeight = Mathf.Max(1, geometryDesc.height);
            int[] ids = { DepthMip0Id, DepthMip1Id, DepthMip2Id, DepthMip3Id };

            for (int i = 0; i < mips.Length; i++)
            {
                int mipWidth = Mathf.Max(1, sourceWidth >> i);
                int mipHeight = Mathf.Max(1, sourceHeight >> i);
                TextureDesc desc = new TextureDesc(mipWidth, mipHeight)
                {
                    name = "_HoGTAODepthMip" + i,
                    format = GraphicsFormat.R32_SFloat,
                    depthBufferBits = 0,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                TextureHandle destination = renderGraph.CreateTexture(desc);
                mips[i] = destination;

                using (var builder = renderGraph.AddRasterRenderPass<DepthData>("Ho-GTAO Depth Pyramid " + i, out DepthData data, ProfilingSampler))
                {
                    data.material = material;
                    data.source = source;
                    data.destination = destination;
                    int sourceMip = Mathf.Max(0, i - 1);
                    data.sourceTexelSize = new Vector4(
                        1.0f / Mathf.Max(1, sourceWidth >> sourceMip),
                        1.0f / Mathf.Max(1, sourceHeight >> sourceMip),
                        0,
                        0);
                    data.passIndex = i == 0 ? 0 : 1;
                    data.sourceIsGeometry = i == 0;
                    data.outputId = ids[i];
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(data.destination, data.outputId);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (DepthData passData, RasterGraphContext context) =>
                    {
                        if (!passData.sourceIsGeometry)
                        {
                            context.cmd.SetGlobalTexture(DepthInputId, passData.source);
                            context.cmd.SetGlobalVector(DepthInputTexelSizeId, passData.sourceTexelSize);
                        }
                        Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, passData.passIndex);
                    });
                }

                source = destination;
            }

            return mips;
        }

        private TextureHandle RecordBlit(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source, TextureHandle cameraColor, Material blitMaterial, string passName)
        {
            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            bool isDebug = passName == "Ho-GTAO Debug Output";
            destinationDesc.name = isDebug ? "_HoGTAODebugColor" : "_HoAOTexture";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<BlitData>(passName, out BlitData data, ProfilingSampler))
            {
                data.material = blitMaterial;
                data.source = source;
                data.destination = destination;
                data.passIndex = isDebug ? 0 : 4;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                if (!isDebug)
                {
                    builder.SetGlobalTextureAfterPass(data.destination, HoGTAOShaderConstants.AOTextureId);
                }
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BlitData passData, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, passData.passIndex);
                });
            }

            if (isDebug)
            {
                frameData.Get<UniversalResourceData>().cameraColor = destination;
            }
            return destination;
        }
    }
}
