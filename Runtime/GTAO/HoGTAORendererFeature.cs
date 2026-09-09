#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
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
        private Material motionMaterial;
        private Shader motionShader;
        private Material debugMaterial;
        private Shader debugShader;
        private readonly Dictionary<int, HoGTAOHistory> cameraHistories = new Dictionary<int, HoGTAOHistory>();
        private HoGTAOQuality lastAppliedQuality = (HoGTAOQuality)(-1);
        private bool resetRegistered;

        public HoGTAOSettings Settings => settings;

        public override void Create()
        {
            pass = new HoGTAOPass();
            debugPass = new HoGTAODebugPass();
            RenderPipelineManager.beginCameraRendering += ResetGlobalState;
            resetRegistered = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null)
            {
                return;
            }

            ResolveVolume();
            if (!settings.enabled)
            {
                return;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

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

            Shader currentMotionShader = Shader.Find(HoGTAOShaderConstants.MotionShaderName);
            if (motionMaterial == null || motionShader != currentMotionShader)
            {
                CoreUtils.Destroy(motionMaterial);
                motionShader = currentMotionShader;
                motionMaterial = motionShader != null ? CoreUtils.CreateEngineMaterial(motionShader) : null;
            }

            Shader currentDebugShader = Shader.Find(HoGTAOShaderConstants.DebugShaderName);
            if (debugMaterial == null || debugShader != currentDebugShader)
            {
                CoreUtils.Destroy(debugMaterial);
                debugShader = currentDebugShader;
                debugMaterial = debugShader != null ? CoreUtils.CreateEngineMaterial(debugShader) : null;
            }

            int cameraId = renderingData.cameraData.camera != null
                ? renderingData.cameraData.camera.GetInstanceID()
                : 0;
            if (!cameraHistories.TryGetValue(cameraId, out HoGTAOHistory cameraHistory))
            {
                cameraHistory = new HoGTAOHistory();
                cameraHistories.Add(cameraId, cameraHistory);
            }

            pass.Setup(settings, material, motionMaterial, debugMaterial, cameraHistory);
            renderer.EnqueuePass(pass);
            bool debugEnabledForCamera = settings.debugMode != HoGTAODebugMode.Off
                && ((cameraType == CameraType.SceneView && settings.debugInSceneView)
                    || (cameraType == CameraType.Game && settings.debugInGameView));
            if (debugEnabledForCamera && debugMaterial != null)
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
            CoreUtils.Destroy(motionMaterial);
            CoreUtils.Destroy(debugMaterial);
            material = null;
            shader = null;
            motionMaterial = null;
            motionShader = null;
            debugMaterial = null;
            debugShader = null;
            foreach (HoGTAOHistory cameraHistory in cameraHistories.Values)
            {
                cameraHistory.Dispose();
            }
            cameraHistories.Clear();
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

            if (volume.enable.overrideState) settings.enabled = volume.enable.value;
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
            if (volume.useLinearThickness.overrideState) settings.useLinearThickness = volume.useLinearThickness.value;
            if (volume.temporalFrameCount.overrideState) settings.temporalFrameCount = volume.temporalFrameCount.value;
            if (volume.temporalRejection.overrideState) settings.temporalRejection = volume.temporalRejection.value;
            if (volume.spatialFilter.overrideState) settings.spatialFilter = volume.spatialFilter.value;
            if (volume.filterRadius.overrideState) settings.filterRadius = volume.filterRadius.value;
            if (volume.filterAdaptivity.overrideState) settings.filterAdaptivity = volume.filterAdaptivity.value;
            if (volume.boxPassCount.overrideState) settings.boxPassCount = volume.boxPassCount.value;
            if (volume.debugMode.overrideState) settings.debugMode = volume.debugMode.value;
            if (volume.debugInSceneView.overrideState) settings.debugInSceneView = volume.debugInSceneView.value;
            if (volume.debugInGameView.overrideState) settings.debugInGameView = volume.debugInGameView.value;
            if (volume.debugIntensity.overrideState) settings.debugIntensity = volume.debugIntensity.value;
        }
    }

    internal sealed class HoGTAOHistory : System.IDisposable
    {
        private RTHandle previous;
        private RTHandle next;
        private RTHandle previousDepth;
        private RTHandle nextDepth;
        private RTHandle previousNormal;
        private RTHandle nextNormal;
        private bool valid;
        private Matrix4x4 previousView = Matrix4x4.identity;
        private Vector4 previousDepthToViewParams;
        private Vector4 previousZBufferParams;
        private bool previousOrthographic;
        private bool cameraStateValid;
        private int allocatedWidth;
        private int allocatedHeight;
        private HoGTAOResolution allocatedResolution = (HoGTAOResolution)(-1);

        public RTHandle Previous => previous;
        public RTHandle Next => next;
        public RTHandle PreviousDepth => previousDepth;
        public RTHandle NextDepth => nextDepth;
        public RTHandle PreviousNormal => previousNormal;
        public RTHandle NextNormal => nextNormal;
        public bool Valid => valid;
        public bool CameraStateValid => cameraStateValid;
        public Matrix4x4 PreviousView => previousView;
        public Vector4 PreviousDepthToViewParams => previousDepthToViewParams;
        public Vector4 PreviousZBufferParams => previousZBufferParams;
        public bool PreviousOrthographic => previousOrthographic;

        public bool NeedsEnsure(int width, int height, HoGTAOResolution resolution)
        {
            return previous == null
                || previousDepth == null
                || previousNormal == null
                || allocatedWidth != width
                || allocatedHeight != height
                || allocatedResolution != resolution;
        }

        public void Ensure(int width, int height, HoGTAOResolution resolution)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height)
            {
                // HTrace keeps AO and hit velocity in a floating-point history
                // buffer. The sample count is normalized into B; normals are
                // carried by the separate normal history below.
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = 0,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };

            RenderingUtils.ReAllocateIfNeeded(ref previous, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevTex");
            RenderingUtils.ReAllocateIfNeeded(ref next, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextTex");
            RenderTextureDescriptor depthDescriptor = descriptor;
            // Keep the same raw device-depth representation as HTrace.  The
            // geometry buffer alpha is fp16 linear eye depth, which loses
            // precision at the far end of the frustum when converted back to
            // raw depth for temporal rejection.
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref previousDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevDepthTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextDepthTex");
            RenderTextureDescriptor normalDescriptor = descriptor;
            normalDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref previousNormal, normalDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryPrevNormalTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextNormal, normalDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoGTAOHistoryNextNormalTex");
            allocatedWidth = width;
            allocatedHeight = height;
            allocatedResolution = resolution;
            valid = false;
            cameraStateValid = false;
        }

        public void MarkValid() => valid = true;

        public void SetCameraState(
            Matrix4x4 view,
            Vector4 depthToViewParams,
            Vector4 zBufferParams,
            bool orthographic)
        {
            previousView = view;
            previousDepthToViewParams = depthToViewParams;
            previousZBufferParams = zBufferParams;
            previousOrthographic = orthographic;
            cameraStateValid = true;
        }

        public void Swap()
        {
            RTHandle temp = previous;
            previous = next;
            next = temp;
            temp = previousDepth;
            previousDepth = nextDepth;
            nextDepth = temp;
            temp = previousNormal;
            previousNormal = nextNormal;
            nextNormal = temp;
        }

        public void Dispose()
        {
            previous?.Release();
            next?.Release();
            previousDepth?.Release();
            nextDepth?.Release();
            previousNormal?.Release();
            nextNormal?.Release();
            previous = null;
            next = null;
            previousDepth = null;
            nextDepth = null;
            previousNormal = null;
            nextNormal = null;
            valid = false;
            previousView = Matrix4x4.identity;
            previousDepthToViewParams = Vector4.zero;
            previousZBufferParams = Vector4.zero;
            previousOrthographic = false;
            cameraStateValid = false;
            allocatedWidth = 0;
            allocatedHeight = 0;
            allocatedResolution = (HoGTAOResolution)(-1);
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
            public float displayMode;
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
                data.displayMode = (float)debugMode;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.cameraColor, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.DebugIntensityId, passData.displayIntensity);
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.DebugInvertId, passData.displayInvert);
                    context.cmd.SetGlobalFloat(HoGTAOShaderConstants.DebugViewModeId, passData.displayMode);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }

    internal sealed class HoGTAOPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GTAO");
        private static readonly ProfilingSampler MotionMaskProfilingSampler = new ProfilingSampler("Ho-GTAO Motion Mask");
        private static readonly ProfilingSampler MotionDeltaProfilingSampler = new ProfilingSampler("Ho-GTAO Motion Delta");
        private static readonly ProfilingSampler CameraMotionProfilingSampler = new ProfilingSampler("Ho-GTAO Camera Motion");
        private static readonly int DebugModeId = Shader.PropertyToID("_HoGTAODebugMode");
        private static readonly int HistoryBlendId = Shader.PropertyToID("_HoGTAOHistoryBlend");
        private static readonly int HistoryValidId = Shader.PropertyToID("_HoGTAOHistoryValid");
        private static readonly int TemporalMaxFramesId = Shader.PropertyToID("_HoGTAOTemporalMaxFrames");
        private static readonly int TemporalRejectionId = Shader.PropertyToID("_HoGTAOTemporalRejection");
        private static readonly int HistoryPrevTexelSizeId = Shader.PropertyToID("_HoGTAOHistoryPrevTex_TexelSize");
        private static readonly int HistoryPrevNormalTexId = Shader.PropertyToID("_HoGTAOHistoryPrevNormalTex");
        private static readonly int WorldRadiusId = Shader.PropertyToID("_HoGTAOWorldSpaceRadius");
        private static readonly int ScreenRadiusId = Shader.PropertyToID("_HoGTAOScreenSpaceRadius");
        private static readonly int ThicknessId = Shader.PropertyToID("_HoGTAOThickness");
        private static readonly int UseAttenuationId = Shader.PropertyToID("_HoGTAOUseAttenuation");
        private static readonly int LinearThicknessId = Shader.PropertyToID("_HoGTAOUseLinearThickness");
        private static readonly int SliceCountId = Shader.PropertyToID("_HoGTAOSliceCount");
        private static readonly int StepCountId = Shader.PropertyToID("_HoGTAOStepCount");
        private static readonly int FrameIndexId = Shader.PropertyToID("_HoGTAOFrameIndex");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_HoGTAOViewMatrix");
        private static readonly int InvViewMatrixId = Shader.PropertyToID("_HoGTAOInvViewMatrix");
        private static readonly int PreviousViewMatrixId = Shader.PropertyToID("_HoGTAOPreviousViewMatrix");
        private static readonly int ProjMatrixId = Shader.PropertyToID("_HoGTAOProjMatrix");
        private static readonly int InvProjMatrixId = Shader.PropertyToID("_HoGTAOInvProjMatrix");
        private static readonly int DepthToViewParamsId = Shader.PropertyToID("_HoGTAODepthToViewParams");
        private static readonly int OrthographicId = Shader.PropertyToID("_HoGTAOOrthographic");
        private static readonly int PreviousOrthographicId = Shader.PropertyToID("_HoGTAOPreviousOrthographic");
        private static readonly int PreviousDepthToViewParamsId = Shader.PropertyToID("_HoGTAOPreviousDepthToViewParams");
        private static readonly int ZBufferParamsId = Shader.PropertyToID("_ZBufferParams");
        private static readonly int PreviousZBufferParamsId = Shader.PropertyToID("_HoGTAOPreviousZBufferParams");
        private static readonly int GeometryInputId = Shader.PropertyToID("_HoGTAOGeometryInput");
        private static readonly int GeometryDepthInputId = HoGeometryBufferShaderConstants.DepthTextureId;
        private static readonly int SpatialDepthInputId = Shader.PropertyToID("_HoGTAOSpatialDepthTexture");
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
        private static readonly int UseCameraMotionId = Shader.PropertyToID("_HoGTAOUseCameraMotion");
        private static readonly int UseObjectMotionId = Shader.PropertyToID("_HoGTAOUseObjectMotion");
        private static readonly ShaderTagId MotionVectorsShaderTagId = new ShaderTagId("MotionVectors");

        private sealed class GenerateData
        {
            public Material material;
            public TextureHandle normalDepth;
            public TextureHandle geometryDepth;
            public TextureHandle motionVectors;
            public TextureHandle motionMask;
            public TextureHandle motionDelta;
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
            public Matrix4x4 inverseView;
            public Matrix4x4 previousView;
            public Matrix4x4 proj;
            public Matrix4x4 invProj;
            public Vector4 depthToViewParams;
            public Vector4 previousDepthToViewParams;
            public float orthographic;
            public float previousOrthographic;
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
            public TextureHandle geometryDepth;
            public TextureHandle previousDepth;
            public TextureHandle previousNormal;
            public TextureHandle motionVectors;
            public TextureHandle motionMask;
            public TextureHandle motionDelta;
            public TextureHandle output;
            public TextureHandle normalOutput;
            public TextureHandle debugOutput;
            public bool useHistory;
            public bool useMotionVectors;
            public bool useCameraMotion;
            public bool debugDisocclusion;
            public int maxFrames;
            public float rejection;
            public float pixelSpreadMultiplier;
            public Vector4 historyTexelSize;
            public Matrix4x4 currentView;
            public Matrix4x4 inverseCurrentView;
            public Matrix4x4 previousView;
            public Vector4 currentDepthToViewParams;
            public Vector4 previousDepthToViewParams;
            public Vector4 previousZBufferParams;
            public bool currentOrthographic;
            public bool previousOrthographic;
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
            public TextureHandle geometryDepth;
            public TextureHandle depth;
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
            public TextureHandle geometryDepth;
            public TextureHandle geometryNormalDepth;
            public TextureHandle destination;
        }

        private sealed class DepthData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle coverage;
            public TextureHandle destination;
            public Vector4 sourceTexelSize;
            public int passIndex;
            public bool sourceIsGeometry;
            public int outputId;
        }

        private sealed class ObjectMotionData
        {
            public RendererListHandle rendererList;
            public TextureHandle destination;
        }

        private sealed class CameraMotionData
        {
            public Material material;
            public TextureHandle geometryDepth;
            public TextureHandle destination;
            public Matrix4x4 currentView;
            public Matrix4x4 inverseCurrentView;
            public Matrix4x4 previousView;
            public Vector4 currentDepthToViewParams;
            public Vector4 previousDepthToViewParams;
            public Vector4 currentZBufferParams;
            public bool currentOrthographic;
            public bool previousOrthographic;
        }

        private HoGTAOSettings settings;
        private Material material;
        private Material motionMaterial;
        private Material debugMaterial;
        private HoGTAOHistory history;

        private static Vector4 GetCameraZBufferParams(Camera camera)
        {
            float near = Mathf.Max(camera.nearClipPlane, 1.0e-4f);
            float far = Mathf.Max(camera.farClipPlane, near + 1.0e-4f);
            if (SystemInfo.usesReversedZBuffer)
            {
                return new Vector4(
                    far / near - 1.0f,
                    1.0f,
                    1.0f / near - 1.0f / far,
                    1.0f / far);
            }

            return new Vector4(
                1.0f - far / near,
                far / near,
                1.0f / far - 1.0f / near,
                1.0f / near);
        }

        public void Setup(HoGTAOSettings settings, Material material, Material motionMaterial, Material debugMaterial, HoGTAOHistory history)
        {
            this.settings = settings;
            this.material = material;
            this.motionMaterial = motionMaterial;
            this.debugMaterial = debugMaterial;
            this.history = history;
            // Same-event order is intentional: HoUrp's explicit enqueue-order
            // tie-breaker follows the Renderer Feature list (GeometryBuffer must
            // be listed above Ho-GTAO).
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            // Motion vectors are produced by URP's built-in MotionVectorRenderPass.
            // GTAO consumes the raw depth attachment published by Ho-GeometryBuffer,
            // so do not request URP's CameraDepthTexture here.
            ConfigureInput(ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null || history == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            if (!resourceData.activeColorTexture.IsValid()
                || !geometry.normalDepthTexture.IsValid()
                || !geometry.depthTexture.IsValid())
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            // Keep the first quality baseline full-resolution. HTrace's Half and
            // Quarter modes use checkerboard addressing into full-resolution
            // geometry; a plain smaller RT with unchanged UVs is not equivalent
            // and produces visibly wrong AO silhouettes.
            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            Matrix4x4 currentView = cameraData.GetViewMatrix();
            bool currentOrthographic = cameraData.camera.orthographic;
            float fovRadians = cameraData.camera.fieldOfView * Mathf.Deg2Rad;
            float halfHeight = currentOrthographic
                ? cameraData.camera.orthographicSize
                : Mathf.Tan(fovRadians * 0.5f);
            float renderAspect = (float)cameraData.cameraTargetDescriptor.width / Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            float halfWidth = halfHeight * renderAspect;
            Vector4 currentDepthToViewParams = new Vector4(2.0f * halfWidth, 2.0f * halfHeight, -halfWidth, -halfHeight);
            Vector4 currentZBufferParams = GetCameraZBufferParams(cameraData.camera);
            if (history.NeedsEnsure(width, height, settings.resolution))
            {
                history.Ensure(width, height, settings.resolution);
            }
            Matrix4x4 previousView = history.CameraStateValid ? history.PreviousView : currentView;
            Vector4 previousDepthToViewParams = history.CameraStateValid
                ? history.PreviousDepthToViewParams
                : currentDepthToViewParams;
            Vector4 previousZBufferParams = history.CameraStateValid
                ? history.PreviousZBufferParams
                : currentZBufferParams;
            bool previousOrthographic = history.CameraStateValid && history.PreviousOrthographic;

            HoGTAORenderGraphResources gtao = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            bool gameCameraMotion = cameraData.cameraType == CameraType.Game;
            TextureHandle temporalMotionVectors = gameCameraMotion
                ? resourceData.motionVectorColor
                : RecordCameraMotionPass(
                    renderGraph,
                    geometry.depthTexture,
                    currentView,
                    currentView.inverse,
                    previousView,
                    currentDepthToViewParams,
                    previousDepthToViewParams,
                    currentZBufferParams,
                    currentOrthographic,
                    previousOrthographic);
            TextureHandle generateMotionVectors = temporalMotionVectors;
            TextureHandle motionMask = TextureHandle.nullHandle;
            TextureHandle motionDelta = TextureHandle.nullHandle;
            if (motionMaterial != null)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                RecordObjectMotionPasses(
                    renderGraph,
                    renderingData,
                    cameraData,
                    geometry.depthTexture,
                    out motionMask,
                    out motionDelta);
            }
            TextureHandle[] depthMips = CreateDepthPyramid(
                renderGraph,
                frameData,
                geometry.depthTexture,
                geometry.normalDepthTexture,
                width,
                height);
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
                data.geometryDepth = geometry.depthTexture;
                data.motionVectors = generateMotionVectors;
                data.motionMask = motionMask;
                data.motionDelta = motionDelta;
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
                data.view = currentView;
                data.inverseView = currentView.inverse;
                data.previousView = previousView;
                data.proj = cameraData.GetProjectionMatrix();
                data.invProj = data.proj.inverse;
                data.depthMip0 = depthMips[0];
                data.depthMip1 = depthMips[1];
                data.depthMip2 = depthMips[2];
                data.depthMip3 = depthMips[3];
                data.depthToViewParams = currentDepthToViewParams;
                data.previousDepthToViewParams = previousDepthToViewParams;
                data.orthographic = currentOrthographic ? 1.0f : 0.0f;
                data.previousOrthographic = previousOrthographic ? 1.0f : 0.0f;
                builder.UseTexture(data.normalDepth, AccessFlags.Read);
                builder.UseTexture(data.geometryDepth, AccessFlags.Read);
                if (data.motionVectors.IsValid())
                {
                    builder.UseTexture(data.motionVectors, AccessFlags.Read);
                }
                if (data.motionMask.IsValid())
                {
                    builder.UseTexture(data.motionMask, AccessFlags.Read);
                    builder.UseTexture(data.motionDelta, AccessFlags.Read);
                }
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
                    context.cmd.SetGlobalMatrix(InvViewMatrixId, passData.inverseView);
                    context.cmd.SetGlobalMatrix(PreviousViewMatrixId, passData.previousView);
                    context.cmd.SetGlobalMatrix(ProjMatrixId, passData.proj);
                    context.cmd.SetGlobalMatrix(InvProjMatrixId, passData.invProj);
                    context.cmd.SetGlobalVector(DepthToViewParamsId, passData.depthToViewParams);
                    context.cmd.SetGlobalVector(PreviousDepthToViewParamsId, passData.previousDepthToViewParams);
                    context.cmd.SetGlobalFloat(OrthographicId, passData.orthographic);
                    context.cmd.SetGlobalFloat(PreviousOrthographicId, passData.previousOrthographic);
                    context.cmd.SetGlobalFloat(UseMotionVectorsId, passData.motionVectors.IsValid() ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(UseCameraMotionId, passData.motionVectors.IsValid() ? 0.0f : 1.0f);
                    context.cmd.SetGlobalFloat(UseObjectMotionId, passData.motionMask.IsValid() ? 1.0f : 0.0f);
                    context.cmd.SetGlobalTexture(GeometryDepthInputId, passData.geometryDepth);
                    if (passData.motionVectors.IsValid())
                    {
                        context.cmd.SetGlobalTexture(MotionVectorTextureId, passData.motionVectors);
                    }
                    if (passData.motionMask.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoGTAOShaderConstants.MotionMaskId, passData.motionMask);
                        context.cmd.SetGlobalTexture(HoGTAOShaderConstants.MotionDeltaId, passData.motionDelta);
                    }
                    context.cmd.SetGlobalTexture(DepthMip0Id, passData.depthMip0);
                    context.cmd.SetGlobalTexture(DepthMip1Id, passData.depthMip1);
                    context.cmd.SetGlobalTexture(DepthMip2Id, passData.depthMip2);
                    context.cmd.SetGlobalTexture(DepthMip3Id, passData.depthMip3);
                    Blitter.BlitTexture(context.cmd, passData.normalDepth, new Vector4(1, 1, 0, 0), passData.material, 2);
                });
            }

            bool debugTemporal = settings.debugMode == HoGTAODebugMode.Temporal;
            bool debugRawGeometry = settings.debugMode == HoGTAODebugMode.Depth
                || settings.debugMode == HoGTAODebugMode.Normal
                || settings.debugMode == HoGTAODebugMode.Motion;
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
            TextureHandle previousNormal = renderGraph.ImportTexture(history.PreviousNormal);
            TextureHandle nextNormal = renderGraph.ImportTexture(history.NextNormal);
            TextureDesc temporalDebugDesc = new TextureDesc(width, height)
            {
                name = "_HoGTAOTemporalDebug",
                format = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            TextureHandle temporalDebug = renderGraph.CreateTexture(temporalDebugDesc);
            // SceneView does not provide HTrace's stable camera-motion pass;
            // using the built-in texture there makes a stationary editor view
            // reproject through stale values every frame.
            TextureHandle motionVectors = temporalMotionVectors;
            using (var builder = renderGraph.AddRasterRenderPass<TemporalData>("Ho-GTAO Temporal", out TemporalData data, ProfilingSampler))
            {
                data.material = material;
                data.current = current;
                data.previous = previous;
                data.geometry = geometry.normalDepthTexture;
                data.geometryDepth = geometry.depthTexture;
                data.previousDepth = previousDepth;
                data.previousNormal = previousNormal;
                data.motionVectors = motionVectors;
                data.motionMask = motionMask;
                data.motionDelta = motionDelta;
                data.output = next;
                data.normalOutput = nextNormal;
                data.debugOutput = temporalDebug;
                data.useHistory = history.Valid;
                data.useMotionVectors = motionVectors.IsValid();
                data.useCameraMotion = !data.useMotionVectors && cameraData.cameraType == CameraType.SceneView;
                data.debugDisocclusion = debugTemporal;
                // HTrace's SampleCountTemporal=8 gates motion-vector work but
                // accumulates up to g_HTemporalSamplecountAO*2 = 12 frames.
                data.maxFrames = settings.temporalFrameCount > 0 ? 12 : 1;
                data.rejection = settings.temporalRejection;
                float temporalBaselineSpread = 2.0f * Mathf.Tan(60.0f * Mathf.Deg2Rad * 0.5f) / 1080.0f;
                float temporalActualSpread = 2.0f * Mathf.Tan(cameraData.camera.fieldOfView * Mathf.Deg2Rad * 0.5f)
                    / Mathf.Max(1.0f, cameraData.cameraTargetDescriptor.height);
                data.pixelSpreadMultiplier = temporalActualSpread / Mathf.Max(temporalBaselineSpread, 1.0e-6f);
                data.historyTexelSize = new Vector4(1.0f / Mathf.Max(1, width), 1.0f / Mathf.Max(1, height), width, height);
                data.currentView = currentView;
                data.inverseCurrentView = currentView.inverse;
                data.previousView = previousView;
                data.currentDepthToViewParams = currentDepthToViewParams;
                data.previousDepthToViewParams = previousDepthToViewParams;
                data.previousZBufferParams = previousZBufferParams;
                data.currentOrthographic = currentOrthographic;
                data.previousOrthographic = previousOrthographic;
                builder.UseTexture(data.current, AccessFlags.Read);
                builder.UseTexture(data.previous, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.geometryDepth, AccessFlags.Read);
                builder.UseTexture(data.previousDepth, AccessFlags.Read);
                builder.UseTexture(data.previousNormal, AccessFlags.Read);
                if (data.motionVectors.IsValid())
                    builder.UseTexture(data.motionVectors, AccessFlags.Read);
                if (data.motionMask.IsValid())
                {
                    builder.UseTexture(data.motionMask, AccessFlags.Read);
                    builder.UseTexture(data.motionDelta, AccessFlags.Read);
                }
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.normalOutput, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.debugOutput, 2, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (TemporalData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(DebugModeId, passData.debugDisocclusion ? 5.0f : 0.0f);
                    context.cmd.SetGlobalFloat(HistoryBlendId, passData.useHistory ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(HistoryValidId, passData.useHistory ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(TemporalMaxFramesId, passData.maxFrames);
                    context.cmd.SetGlobalFloat(TemporalRejectionId, passData.rejection);
                    context.cmd.SetGlobalFloat(PixelSpreadMultiplierId, passData.pixelSpreadMultiplier);
                    context.cmd.SetGlobalMatrix(ViewMatrixId, passData.currentView);
                    context.cmd.SetGlobalMatrix(InvViewMatrixId, passData.inverseCurrentView);
                    context.cmd.SetGlobalMatrix(PreviousViewMatrixId, passData.previousView);
                    context.cmd.SetGlobalVector(DepthToViewParamsId, passData.currentDepthToViewParams);
                    context.cmd.SetGlobalVector(PreviousDepthToViewParamsId, passData.previousDepthToViewParams);
                    context.cmd.SetGlobalVector(PreviousZBufferParamsId, passData.previousZBufferParams);
                    context.cmd.SetGlobalFloat(OrthographicId, passData.currentOrthographic ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(PreviousOrthographicId, passData.previousOrthographic ? 1.0f : 0.0f);
                    context.cmd.SetGlobalVector(HistoryPrevTexelSizeId, passData.historyTexelSize);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.AoInputTexId, passData.current);
                    context.cmd.SetGlobalTexture(HoGTAOShaderConstants.HistoryPrevTexId, passData.previous);
                    context.cmd.SetGlobalTexture(HistoryPrevNormalTexId, passData.previousNormal);
                    context.cmd.SetGlobalTexture(GeometryInputId, passData.geometry);
                    context.cmd.SetGlobalTexture(GeometryDepthInputId, passData.geometryDepth);
                    context.cmd.SetGlobalTexture(HistoryDepthPrevId, passData.previousDepth);
                    context.cmd.SetGlobalFloat(UseMotionVectorsId, passData.useMotionVectors ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(UseCameraMotionId, passData.useCameraMotion ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(UseObjectMotionId, passData.motionMask.IsValid() ? 1.0f : 0.0f);
                    if (passData.motionVectors.IsValid())
                        context.cmd.SetGlobalTexture(MotionVectorTextureId, passData.motionVectors);
                    if (passData.motionMask.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoGTAOShaderConstants.MotionMaskId, passData.motionMask);
                        context.cmd.SetGlobalTexture(HoGTAOShaderConstants.MotionDeltaId, passData.motionDelta);
                    }
                    Blitter.BlitTexture(context.cmd, passData.current, new Vector4(1, 1, 0, 0), passData.material, 3);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<DepthHistoryData>("Ho-GTAO Depth History", out DepthHistoryData depthData, ProfilingSampler))
            {
                depthData.material = material;
                depthData.geometryDepth = geometry.depthTexture;
                depthData.geometryNormalDepth = geometry.normalDepthTexture;
                depthData.destination = nextDepth;
                builder.UseTexture(depthData.geometryDepth, AccessFlags.Read);
                builder.UseTexture(depthData.geometryNormalDepth, AccessFlags.Read);
                builder.SetRenderAttachment(depthData.destination, 0, AccessFlags.WriteAll);
                // Blitter binds its source through global _BlitTexture.
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DepthHistoryData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.geometryDepth, new Vector4(1, 1, 0, 0), data.material, 6);
                });
            }

            history.Swap();
            history.MarkValid();
            history.SetCameraState(currentView, currentDepthToViewParams, currentZBufferParams, currentOrthographic);

            if (debugTemporal)
            {
                // Temporal debug intentionally stops before spatial denoising and
                // final composition so the inspector shows the actual history
                // reprojection/rejection result.
                gtao.aoTexture = temporalDebug;
                return;
            }

            int spatialPassCount = settings.spatialFilter == HoGTAOSpatialFilter.Box
                ? Mathf.Clamp(settings.boxPassCount, 1, 3)
                : 1;
            TextureHandle spatialSource = nextNormal;
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
                    data.geometryDepth = geometry.depthTexture;
                    data.depth = nextDepth;
                    data.destination = spatial;
                    data.radius = settings.filterRadius;
                    data.adaptivity = settings.filterAdaptivity;
                    data.resolution = 1.0f;
                    data.filterType = (int)settings.spatialFilter;
                    data.step = settings.spatialFilter == HoGTAOSpatialFilter.Box
                        ? settings.boxPassCount >= 3
                            ? (spatialPass == 0 ? 4.0f : spatialPass == 1 ? 2.0f : 1.0f)
                            : settings.boxPassCount == 2
                                ? (spatialPass == 0 ? 2.0f : 1.0f)
                                : 1.0f
                        : 1.0f;
                float baselineSpread = 2.0f * Mathf.Tan(60.0f * Mathf.Deg2Rad * 0.5f) / 1080.0f;
                float actualSpread = 2.0f * Mathf.Tan(cameraData.camera.fieldOfView * Mathf.Deg2Rad * 0.5f)
                    / Mathf.Max(1.0f, cameraData.cameraTargetDescriptor.height);
                data.pixelSpreadMultiplier = actualSpread / Mathf.Max(baselineSpread, 1.0e-6f);
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseTexture(data.geometry, AccessFlags.Read);
                    builder.UseTexture(data.geometryDepth, AccessFlags.Read);
                    builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (SpatialData passData, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalTexture(GeometryInputId, passData.geometry);
                        context.cmd.SetGlobalTexture(GeometryDepthInputId, passData.geometryDepth);
                        context.cmd.SetGlobalTexture(SpatialDepthInputId, passData.depth);
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
                // Publish the visibility form as well, so lilToon materials
                // continue to consume AO while the feature-local debug view
                // is active.
                RecordBlit(renderGraph, frameData, spatial, resourceData.activeColorTexture, material, "Ho-GTAO Output");
                gtao.aoTexture = spatial;
                return;
            }

            gtao.aoTexture = RecordBlit(renderGraph, frameData, spatial, resourceData.activeColorTexture, material, "Ho-GTAO Output");
        }

        private void RecordObjectMotionPasses(
            RenderGraph renderGraph,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            TextureHandle depthTexture,
            out TextureHandle motionMask,
            out TextureHandle motionDelta)
        {
            motionMask = TextureHandle.nullHandle;
            motionDelta = TextureHandle.nullHandle;
            if (motionMaterial == null || !depthTexture.IsValid())
            {
                return;
            }

            TextureDesc motionDesc = renderGraph.GetTextureDesc(depthTexture);
            motionDesc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SFloat;
            motionDesc.depthBufferBits = 0;
            motionDesc.msaaSamples = MSAASamples.None;
            motionDesc.bindTextureMS = false;
            motionDesc.clearBuffer = true;
            motionDesc.clearColor = Color.clear;
            motionDesc.filterMode = FilterMode.Point;
            motionDesc.wrapMode = TextureWrapMode.Clamp;

            motionDesc.name = "_HoGTAOMotionMask";
            motionMask = renderGraph.CreateTexture(motionDesc);
            motionDesc.name = "_HoGTAOMotionDelta";
            motionDesc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            motionDelta = renderGraph.CreateTexture(motionDesc);

            RenderStateBlock motionState = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(false, CompareFunction.Equal)
            };
            UnityEngine.Rendering.RendererUtils.RendererListDesc rendererListDesc =
                new UnityEngine.Rendering.RendererUtils.RendererListDesc(
                    MotionVectorsShaderTagId,
                    renderingData.cullResults,
                    cameraData.camera)
                {
                    rendererConfiguration = PerObjectData.MotionVectors,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    stateBlock = motionState,
                    layerMask = cameraData.camera.cullingMask,
                    overrideMaterial = null
                };

            using (var builder = renderGraph.AddRasterRenderPass<ObjectMotionData>(
                "Ho-GTAO Object Motion Mask", out ObjectMotionData data, MotionMaskProfilingSampler))
            {
                // lilToon's own MotionVectors pass already emits the object
                // velocity target used as HTrace's motion mask.
                rendererListDesc.overrideMaterial = null;
                data.rendererList = renderGraph.CreateRendererList(rendererListDesc);
                data.destination = motionMask;
                builder.UseRendererList(data.rendererList);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(data.destination, HoGTAOShaderConstants.MotionMaskId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectMotionData passData, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                    context.cmd.DrawRendererList(passData.rendererList);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<ObjectMotionData>(
                "Ho-GTAO Object Motion Delta", out ObjectMotionData data, MotionDeltaProfilingSampler))
            {
                rendererListDesc.overrideMaterial = motionMaterial;
                rendererListDesc.overrideMaterialPassIndex = 1;
                data.rendererList = renderGraph.CreateRendererList(rendererListDesc);
                data.destination = motionDelta;
                builder.UseRendererList(data.rendererList);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(data.destination, HoGTAOShaderConstants.MotionDeltaId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectMotionData passData, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                    context.cmd.DrawRendererList(passData.rendererList);
                });
            }
        }

        private TextureHandle RecordCameraMotionPass(
            RenderGraph renderGraph,
            TextureHandle geometryDepth,
            Matrix4x4 currentView,
            Matrix4x4 inverseCurrentView,
            Matrix4x4 previousView,
            Vector4 currentDepthToViewParams,
            Vector4 previousDepthToViewParams,
            Vector4 currentZBufferParams,
            bool currentOrthographic,
            bool previousOrthographic)
        {
            TextureDesc descriptor = renderGraph.GetTextureDesc(geometryDepth);
            descriptor.name = "_HoGTAOCameraMotion";
            descriptor.format = GraphicsFormat.R16G16_SFloat;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.black;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<CameraMotionData>(
                "Ho-GTAO Camera Motion", out CameraMotionData data, CameraMotionProfilingSampler))
            {
                data.material = material;
                data.geometryDepth = geometryDepth;
                data.destination = destination;
                data.currentView = currentView;
                data.inverseCurrentView = inverseCurrentView;
                data.previousView = previousView;
                data.currentDepthToViewParams = currentDepthToViewParams;
                data.previousDepthToViewParams = previousDepthToViewParams;
                data.currentZBufferParams = currentZBufferParams;
                data.currentOrthographic = currentOrthographic;
                data.previousOrthographic = previousOrthographic;
                builder.UseTexture(data.geometryDepth, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CameraMotionData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(GeometryDepthInputId, passData.geometryDepth);
                    context.cmd.SetGlobalMatrix(ViewMatrixId, passData.currentView);
                    context.cmd.SetGlobalMatrix(InvViewMatrixId, passData.inverseCurrentView);
                    context.cmd.SetGlobalMatrix(PreviousViewMatrixId, passData.previousView);
                    context.cmd.SetGlobalVector(DepthToViewParamsId, passData.currentDepthToViewParams);
                    context.cmd.SetGlobalVector(PreviousDepthToViewParamsId, passData.previousDepthToViewParams);
                    context.cmd.SetGlobalVector(ZBufferParamsId, passData.currentZBufferParams);
                    context.cmd.SetGlobalFloat(OrthographicId, passData.currentOrthographic ? 1.0f : 0.0f);
                    context.cmd.SetGlobalFloat(PreviousOrthographicId, passData.previousOrthographic ? 1.0f : 0.0f);
                    Blitter.BlitTexture(context.cmd, passData.geometryDepth, new Vector4(1, 1, 0, 0), passData.material, 8);
                });
            }

            return destination;
        }

        private TextureHandle[] CreateDepthPyramid(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle geometryDepth,
            TextureHandle geometryNormalDepth,
            int width,
            int height)
        {
            TextureHandle[] mips = new TextureHandle[4];
            TextureHandle source = geometryDepth;
            TextureDesc geometryDesc = renderGraph.GetTextureDesc(geometryDepth);
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
                    data.coverage = i == 0 ? geometryNormalDepth : TextureHandle.nullHandle;
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
                    if (data.coverage.IsValid())
                    {
                        builder.UseTexture(data.coverage, AccessFlags.Read);
                    }
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
                builder.AllowGlobalStateModification(true);
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
