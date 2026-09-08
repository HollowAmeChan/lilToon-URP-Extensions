#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.GeometryBuffer;

namespace lilToon.URP.Extensions.SSGI
{
    [DisallowMultipleRendererFeature("Ho-SSGI")]
    public sealed class HoSSGIRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoSSGISettings settings = new HoSSGISettings();
        private HoSSGIPass pass;
        private HoSSGICompositePass compositePass;
        private HoSSGIDebugPass debugPass;
        private HoSSGIHistory history;
        private Material material;
        private Material debugMaterial;
        private Shader shader;
        private Shader debugShader;

        public HoSSGISettings Settings => settings;

        public override void Create()
        {
            pass = new HoSSGIPass();
            compositePass = new HoSSGICompositePass();
            debugPass = new HoSSGIDebugPass();
            history = new HoSSGIHistory();
            RenderPipelineManager.beginCameraRendering += ResetGlobalState;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null)
            {
                return;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

            ResolveVolume();
            if (!settings.enabled)
            {
                history?.Invalidate();
                return;
            }

            Shader currentShader = settings.shader != null ? settings.shader : Shader.Find(HoSSGIShaderConstants.ShaderName);
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

            Shader currentDebugShader = settings.debugShader != null ? settings.debugShader : Shader.Find(HoSSGIShaderConstants.DebugShaderName);
            if (debugMaterial == null || debugShader != currentDebugShader)
            {
                CoreUtils.Destroy(debugMaterial);
                debugShader = currentDebugShader;
                debugMaterial = debugShader != null ? CoreUtils.CreateEngineMaterial(debugShader) : null;
            }

            pass.Setup(settings, material, history);
            renderer.EnqueuePass(pass);
            if (settings.debugMode == HoSSGIDebugMode.Off)
            {
                compositePass.Setup(settings, material);
                renderer.EnqueuePass(compositePass);
            }
            bool debugEnabled = settings.debugMode != HoSSGIDebugMode.Off
                && debugMaterial != null
                && ((cameraType == CameraType.Game && settings.debugInGameView)
                    || (cameraType == CameraType.SceneView && settings.debugInSceneView));
            if (debugEnabled)
            {
                debugPass.Setup(settings, debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            RenderPipelineManager.beginCameraRendering -= ResetGlobalState;
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(debugMaterial);
            material = null;
            debugMaterial = null;
            pass = null;
            compositePass = null;
            debugPass = null;
            history?.Dispose();
            history = null;
        }

        private static void ResetGlobalState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(HoSSGIShaderConstants.GITextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoSSGIShaderConstants.RawGIId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoSSGIShaderConstants.SourceId, Texture2D.blackTexture);
        }

        private void ResolveVolume()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            HoSSGIVolume volume = stack != null ? stack.GetComponent<HoSSGIVolume>() : null;
            if (volume == null || settings == null)
            {
                return;
            }

            settings.enabled = volume.enable.value;
            settings.rayCount = volume.rayCount.value;
            settings.stepCount = volume.stepCount.value;
            settings.rayLength = volume.rayLength.value;
            settings.thickness = volume.thickness.value;
            settings.temporalBlend = volume.temporalBlend.value;
            settings.spatialRadius = volume.spatialRadius.value;
            settings.temporalReservoirReuse = volume.temporalReservoirReuse.value;
            settings.spatialReservoirReuse = volume.spatialReservoirReuse.value;
            settings.temporalReservoirValidation = volume.temporalReservoirValidation.value;
            settings.spatialReservoirValidation = volume.spatialReservoirValidation.value;
            settings.fireflySuppression = volume.fireflySuppression.value;
            settings.intensity = volume.intensity.value;
            settings.sourceSaturation = volume.sourceSaturation.value;
            settings.debugMode = volume.debugMode.value;
        }
    }

    internal sealed class HoSSGIHistory : System.IDisposable
    {
        private RTHandle previous;
        private RTHandle next;
        private RTHandle previousSource;
        private RTHandle nextSource;
        private RTHandle previousDenoised;
        private RTHandle nextDenoised;
        private RTHandle previousDepth;
        private RTHandle nextDepth;
        private RTHandle previousReservoirColor;
        private RTHandle nextReservoirColor;
        private RTHandle previousReservoirAux;
        private RTHandle nextReservoirAux;
        private RTHandle previousReservoirRay;
        private RTHandle nextReservoirRay;
        private int width;
        private int height;
        private int cameraId;
        private bool valid;

        public RTHandle Previous => previous;
        public RTHandle Next => next;
        public RTHandle PreviousSource => previousSource;
        public RTHandle NextSource => nextSource;
        public RTHandle PreviousDenoised => previousDenoised;
        public RTHandle NextDenoised => nextDenoised;
        public RTHandle PreviousDepth => previousDepth;
        public RTHandle NextDepth => nextDepth;
        public RTHandle PreviousReservoirColor => previousReservoirColor;
        public RTHandle NextReservoirColor => nextReservoirColor;
        public RTHandle PreviousReservoirAux => previousReservoirAux;
        public RTHandle NextReservoirAux => nextReservoirAux;
        public RTHandle PreviousReservoirRay => previousReservoirRay;
        public RTHandle NextReservoirRay => nextReservoirRay;
        public bool Valid => valid;

        public void Ensure(int requestedWidth, int requestedHeight, int requestedCameraId)
        {
            bool changed = width != requestedWidth || height != requestedHeight || cameraId != requestedCameraId;
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(requestedWidth, requestedHeight)
            {
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = 0,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };
            RenderingUtils.ReAllocateIfNeeded(ref previous, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevTex");
            RenderingUtils.ReAllocateIfNeeded(ref next, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextTex");
            RenderingUtils.ReAllocateIfNeeded(ref previousSource, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGISourceHistoryPrevTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextSource, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGISourceHistoryNextTex");
            RenderingUtils.ReAllocateIfNeeded(ref previousDenoised, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevDenoisedTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextDenoised, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextDenoisedTex");
            RenderTextureDescriptor depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref previousDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevDepthTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextDepth, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextDepthTex");
            RenderingUtils.ReAllocateIfNeeded(ref previousReservoirColor, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevReservoirColorTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextReservoirColor, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextReservoirColorTex");
            RenderingUtils.ReAllocateIfNeeded(ref previousReservoirAux, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevReservoirAuxTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextReservoirAux, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextReservoirAuxTex");
            RenderingUtils.ReAllocateIfNeeded(ref previousReservoirRay, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryPrevReservoirRayTex");
            RenderingUtils.ReAllocateIfNeeded(ref nextReservoirRay, descriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HoSSGIHistoryNextReservoirRayTex");
            if (changed)
            {
                width = requestedWidth;
                height = requestedHeight;
                cameraId = requestedCameraId;
                valid = false;
            }
        }

        public void Swap()
        {
            RTHandle texture = previous;
            previous = next;
            next = texture;
            texture = previousSource;
            previousSource = nextSource;
            nextSource = texture;
            texture = previousDepth;
            previousDepth = nextDepth;
            nextDepth = texture;
            texture = previousReservoirColor;
            previousReservoirColor = nextReservoirColor;
            nextReservoirColor = texture;
            texture = previousReservoirAux;
            previousReservoirAux = nextReservoirAux;
            nextReservoirAux = texture;
            texture = previousReservoirRay;
            previousReservoirRay = nextReservoirRay;
            nextReservoirRay = texture;
        }

        public void SwapDenoised()
        {
            RTHandle texture = previousDenoised;
            previousDenoised = nextDenoised;
            nextDenoised = texture;
        }

        public void MarkValid() => valid = true;

        public void Invalidate() => valid = false;

        public void Dispose()
        {
            previous?.Release();
            next?.Release();
            previousSource?.Release();
            nextSource?.Release();
            previousDenoised?.Release();
            nextDenoised?.Release();
            previousDepth?.Release();
            nextDepth?.Release();
            previousReservoirColor?.Release();
            nextReservoirColor?.Release();
            previousReservoirAux?.Release();
            nextReservoirAux?.Release();
            previousReservoirRay?.Release();
            nextReservoirRay?.Release();
            previous = null;
            next = null;
            previousSource = null;
            nextSource = null;
            previousDenoised = null;
            nextDenoised = null;
            previousDepth = null;
            nextDepth = null;
            previousReservoirColor = null;
            nextReservoirColor = null;
            previousReservoirAux = null;
            nextReservoirAux = null;
            previousReservoirRay = null;
            nextReservoirRay = null;
            valid = false;
        }
    }

    internal sealed class HoSSGIPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle geometry;
            public TextureHandle source;
            public TextureHandle sky;
            public TextureHandle output;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle reservoirRay;
            public int rayCount;
            public int stepCount;
            public float rayLength;
            public float thickness;
            public float sourceSaturation;
            public int frameIndex;
        }

        private sealed class SourceReprojectionPassData
        {
            public Material material;
            public TextureHandle currentSource;
            public TextureHandle previousSource;
            public TextureHandle previousDepth;
            public TextureHandle geometry;
            public TextureHandle motion;
            public TextureHandle output;
            public bool historyValid;
        }

        private sealed class SourceHistoryPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle output;
        }

        private sealed class TemporalPassData
        {
            public Material material;
            public TextureHandle current;
            public TextureHandle previous;
            public TextureHandle previousDepth;
            public TextureHandle currentReservoirColor;
            public TextureHandle currentReservoirAux;
            public TextureHandle currentReservoirRay;
            public TextureHandle previousReservoirColor;
            public TextureHandle previousReservoirAux;
            public TextureHandle previousReservoirRay;
            public TextureHandle geometry;
            public TextureHandle motion;
            public TextureHandle output;
            public TextureHandle reservoirOutputColor;
            public TextureHandle reservoirOutputAux;
            public TextureHandle reservoirOutputRay;
            public float blend;
            public bool historyValid;
            public bool reservoirReuse;
            public bool reservoirValidation;
        }

        private sealed class DepthHistoryPassData
        {
            public Material material;
            public TextureHandle geometry;
            public TextureHandle output;
        }

        private sealed class SpatialPassData
        {
            public Material material;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle reservoirRay;
            public TextureHandle temporal;
            public TextureHandle geometry;
            public TextureHandle output;
            public float radius;
            public bool reservoirReuse;
            public bool reservoirValidation;
        }

        private sealed class SpatialResamplingPassData
        {
            public Material material;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle reservoirRay;
            public TextureHandle temporal;
            public TextureHandle geometry;
            public TextureHandle outputColor;
            public TextureHandle outputAux;
            public TextureHandle outputRay;
            public float radius;
            public bool reservoirReuse;
        }

        private sealed class SpatialValidationPassData
        {
            public Material material;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle reservoirRay;
            public TextureHandle temporal;
            public TextureHandle geometry;
            public TextureHandle output;
            public bool reservoirValidation;
        }

        private sealed class FireflyPassData
        {
            public Material material;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle reservoirRay;
            public TextureHandle geometry;
            public TextureHandle outputColor;
            public TextureHandle outputAux;
            public TextureHandle outputRay;
            public bool enabled;
        }

        private sealed class BilateralPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle geometry;
            public TextureHandle output;
            public float radius;
        }

        private sealed class DenoisedTemporalPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle history;
            public TextureHandle geometry;
            public TextureHandle motion;
            public TextureHandle output;
            public bool historyValid;
        }

        private sealed class DenoisedHistoryPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle output;
        }

        private HoSSGISettings settings;
        private Material material;
        private HoSSGIHistory history;

        public void Setup(HoSSGISettings settings, Material material, HoSSGIHistory history)
        {
            this.settings = settings;
            this.material = material;
            this.history = history;
            int configuredEvent = settings != null ? (int)settings.passEvent : (int)RenderPassEvent.AfterRenderingOpaques;
            renderPassEvent = (RenderPassEvent)Mathf.Clamp(
                configuredEvent,
                (int)RenderPassEvent.AfterRenderingOpaques,
                (int)RenderPassEvent.BeforeRenderingPostProcessing);
            ConfigureInput(ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null || history == null) return;
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || !geometry.normalDepthTexture.IsValid())
            {
                history.Invalidate();
                return;
            }

            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            int width = Mathf.Max(1, sourceDesc.width);
            int height = Mathf.Max(1, sourceDesc.height);
            int cameraId = cameraData.camera != null ? cameraData.camera.GetInstanceID() : 0;
            history.Ensure(width, height, cameraId);

            TextureHandle previousSource = renderGraph.ImportTexture(history.PreviousSource);
            TextureHandle nextSource = renderGraph.ImportTexture(history.NextSource);
            TextureHandle previousDepthForSource = renderGraph.ImportTexture(history.PreviousDepth);
            TextureHandle sourceReprojected = renderGraph.CreateTexture(sourceDesc);
            using (var builder = renderGraph.AddRasterRenderPass<SourceReprojectionPassData>("Ho-SSGI Source Reprojection", out SourceReprojectionPassData data, new ProfilingSampler("Ho-SSGI Source Reprojection")))
            {
                data.material = material;
                data.currentSource = source;
                data.previousSource = previousSource;
                data.previousDepth = previousDepthForSource;
                data.geometry = geometry.normalDepthTexture;
                data.motion = resourceData.motionVectorColor;
                data.output = sourceReprojected;
                data.historyValid = history.Valid;
                builder.UseTexture(data.currentSource, AccessFlags.Read);
                builder.UseTexture(data.previousSource, AccessFlags.Read);
                builder.UseTexture(data.previousDepth, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                if (data.motion.IsValid()) builder.UseTexture(data.motion, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SourceReprojectionPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.HistoryValidId, passData.historyValid ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.MotionValidId, passData.motion.IsValid() ? 1.0f : 0.0f);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceId, passData.currentSource);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceHistoryId, passData.previousSource);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.HistoryDepthId, passData.previousDepth);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    if (passData.motion.IsValid()) context.cmd.SetGlobalTexture(HoSSGIShaderConstants.MotionVectorId, passData.motion);
                    Blitter.BlitTexture(context.cmd, passData.currentSource, new Vector4(1, 1, 0, 0), passData.material, 7);
                });
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = HoSSGIShaderConstants.RawGITextureName;
            outputDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
            outputDesc.depthBufferBits = 0;
            outputDesc.msaaSamples = MSAASamples.None;
            outputDesc.clearBuffer = true;
            outputDesc.clearColor = Color.clear;
            TextureHandle raw = renderGraph.CreateTexture(outputDesc);
            TextureDesc reservoirDesc = outputDesc;
            reservoirDesc.name = HoSSGIShaderConstants.ReservoirColorName;
            reservoirDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
            TextureHandle rawReservoirColor = renderGraph.CreateTexture(reservoirDesc);
            reservoirDesc.name = HoSSGIShaderConstants.ReservoirAuxName;
            TextureHandle rawReservoirAux = renderGraph.CreateTexture(reservoirDesc);
            reservoirDesc.name = HoSSGIShaderConstants.ReservoirRayName;
            TextureHandle rawReservoirRay = renderGraph.CreateTexture(reservoirDesc);
            HoSSGIRenderGraphResources resources = frameData.GetOrCreate<HoSSGIRenderGraphResources>();
            resources.rawGiTexture = raw;
            resources.sourceTexture = sourceReprojected;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SSGI Raw Trace", out PassData data, new ProfilingSampler("Ho-SSGI Raw Trace")))
            {
                data.material = material;
                data.geometry = geometry.normalDepthTexture;
                data.source = sourceReprojected;
                data.sky = geometry.skyTexture;
                data.output = raw;
                data.reservoirColor = rawReservoirColor;
                data.reservoirAux = rawReservoirAux;
                data.reservoirRay = rawReservoirRay;
                data.rayCount = Mathf.Clamp(settings.rayCount, 1, 128);
                data.stepCount = Mathf.Clamp(settings.stepCount, 4, 256);
                data.rayLength = Mathf.Clamp(settings.rayLength, 0.01f, 32.0f);
                data.thickness = Mathf.Clamp(settings.thickness, 0.0f, 4.0f);
                data.sourceSaturation = Mathf.Clamp01(settings.sourceSaturation);
                data.frameIndex = Time.frameCount;
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.source, AccessFlags.Read);
                if (data.sky.IsValid()) builder.UseTexture(data.sky, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirColor, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirAux, 2, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirRay, 3, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(data.output, HoSSGIShaderConstants.RawGIId);
                builder.SetGlobalTextureAfterPass(data.source, HoSSGIShaderConstants.SourceId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetInt(HoSSGIShaderConstants.RayCountId, passData.rayCount);
                    passData.material.SetInt(HoSSGIShaderConstants.StepCountId, passData.stepCount);
                    passData.material.SetFloat(HoSSGIShaderConstants.RayLengthId, passData.rayLength);
                    passData.material.SetFloat(HoSSGIShaderConstants.ThicknessId, passData.thickness);
                    passData.material.SetFloat(HoSSGIShaderConstants.SourceSaturationId, passData.sourceSaturation);
                    passData.material.SetInt(HoSSGIShaderConstants.FrameIndexId, passData.frameIndex);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceId, passData.source);
                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, passData.sky.IsValid() ? 1.0f : 0.0f);
                    if (passData.sky.IsValid()) context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, passData.sky);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }

            TextureHandle previous = renderGraph.ImportTexture(history.Previous);
            TextureHandle next = renderGraph.ImportTexture(history.Next);
            TextureHandle previousDenoised = renderGraph.ImportTexture(history.PreviousDenoised);
            TextureHandle nextDenoised = renderGraph.ImportTexture(history.NextDenoised);
            TextureHandle previousDepth = renderGraph.ImportTexture(history.PreviousDepth);
            TextureHandle nextDepth = renderGraph.ImportTexture(history.NextDepth);
            TextureHandle previousReservoirColor = renderGraph.ImportTexture(history.PreviousReservoirColor);
            TextureHandle nextReservoirColor = renderGraph.ImportTexture(history.NextReservoirColor);
            TextureHandle previousReservoirAux = renderGraph.ImportTexture(history.PreviousReservoirAux);
            TextureHandle nextReservoirAux = renderGraph.ImportTexture(history.NextReservoirAux);
            TextureHandle previousReservoirRay = renderGraph.ImportTexture(history.PreviousReservoirRay);
            TextureHandle nextReservoirRay = renderGraph.ImportTexture(history.NextReservoirRay);
            TextureHandle motion = resourceData.motionVectorColor;
            TextureHandle temporal = renderGraph.CreateTexture(outputDesc);

            using (var builder = renderGraph.AddRasterRenderPass<TemporalPassData>("Ho-SSGI Temporal", out TemporalPassData data, new ProfilingSampler("Ho-SSGI Temporal")))
            {
                data.material = material;
                data.current = raw;
                data.previous = previous;
                data.previousDepth = previousDepth;
                data.currentReservoirColor = rawReservoirColor;
                data.currentReservoirAux = rawReservoirAux;
                data.currentReservoirRay = rawReservoirRay;
                data.previousReservoirColor = previousReservoirColor;
                data.previousReservoirAux = previousReservoirAux;
                data.previousReservoirRay = previousReservoirRay;
                data.geometry = geometry.normalDepthTexture;
                data.motion = motion;
                data.output = temporal;
                data.reservoirOutputColor = nextReservoirColor;
                data.reservoirOutputAux = nextReservoirAux;
                data.reservoirOutputRay = nextReservoirRay;
                data.blend = Mathf.Clamp01(settings.temporalBlend);
                data.historyValid = history.Valid;
                data.reservoirReuse = settings.temporalReservoirReuse;
                data.reservoirValidation = settings.temporalReservoirValidation;
                builder.UseTexture(data.current, AccessFlags.Read);
                builder.UseTexture(data.previous, AccessFlags.Read);
                builder.UseTexture(data.previousDepth, AccessFlags.Read);
                builder.UseTexture(data.currentReservoirColor, AccessFlags.Read);
                builder.UseTexture(data.currentReservoirAux, AccessFlags.Read);
                builder.UseTexture(data.currentReservoirRay, AccessFlags.Read);
                builder.UseTexture(data.previousReservoirColor, AccessFlags.Read);
                builder.UseTexture(data.previousReservoirAux, AccessFlags.Read);
                builder.UseTexture(data.previousReservoirRay, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                if (data.motion.IsValid()) builder.UseTexture(data.motion, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirOutputColor, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirOutputAux, 2, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.reservoirOutputRay, 3, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (TemporalPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.TemporalBlendId, passData.blend);
                    passData.material.SetFloat(HoSSGIShaderConstants.HistoryValidId, passData.historyValid ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.MotionValidId, passData.motion.IsValid() ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirReuseId, passData.reservoirReuse ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirValidationId, passData.reservoirValidation ? 1.0f : 0.0f);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIInputId, passData.current);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.HistoryTextureId, passData.previous);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.HistoryDepthId, passData.previousDepth);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirColorId, passData.currentReservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirAuxId, passData.currentReservoirAux);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirRayId, passData.currentReservoirRay);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirHistoryColorId, passData.previousReservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirHistoryAuxId, passData.previousReservoirAux);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirHistoryRayId, passData.previousReservoirRay);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    if (passData.motion.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoSSGIShaderConstants.MotionVectorId, passData.motion);
                    }
                    Blitter.BlitTexture(context.cmd, passData.current, new Vector4(1, 1, 0, 0), passData.material, 2);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<DepthHistoryPassData>("Ho-SSGI Depth History", out DepthHistoryPassData data, new ProfilingSampler("Ho-SSGI Depth History")))
            {
                data.material = material;
                data.geometry = geometry.normalDepthTexture;
                data.output = nextDepth;
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DepthHistoryPassData passData, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, passData.geometry, new Vector4(1, 1, 0, 0), passData.material, 3);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<SourceHistoryPassData>("Ho-SSGI Source History", out SourceHistoryPassData data, new ProfilingSampler("Ho-SSGI Source History")))
            {
                data.material = material;
                data.source = source;
                data.output = nextSource;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SourceHistoryPassData passData, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 8);
                });
            }

            history.Swap();
            history.MarkValid();

            TextureDesc fireflyDesc = reservoirDesc;
            fireflyDesc.name = HoSSGIShaderConstants.ReservoirOutputColorName;
            TextureHandle fireflyReservoirColor = renderGraph.CreateTexture(fireflyDesc);
            fireflyDesc.name = HoSSGIShaderConstants.ReservoirOutputAuxName;
            TextureHandle fireflyReservoirAux = renderGraph.CreateTexture(fireflyDesc);
            fireflyDesc.name = HoSSGIShaderConstants.ReservoirOutputRayName;
            TextureHandle fireflyReservoirRay = renderGraph.CreateTexture(fireflyDesc);
            using (var builder = renderGraph.AddRasterRenderPass<FireflyPassData>("Ho-SSGI Firefly", out FireflyPassData data, new ProfilingSampler("Ho-SSGI Firefly")))
            {
                data.material = material;
                data.reservoirColor = nextReservoirColor;
                data.reservoirAux = nextReservoirAux;
                data.reservoirRay = nextReservoirRay;
                data.geometry = geometry.normalDepthTexture;
                data.outputColor = fireflyReservoirColor;
                data.outputAux = fireflyReservoirAux;
                data.outputRay = fireflyReservoirRay;
                data.enabled = settings.fireflySuppression;
                builder.UseTexture(data.reservoirColor, AccessFlags.Read);
                builder.UseTexture(data.reservoirAux, AccessFlags.Read);
                builder.UseTexture(data.reservoirRay, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.outputColor, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.outputAux, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.outputRay, 2, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (FireflyPassData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirColorId, passData.reservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirAuxId, passData.reservoirAux);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirRayId, passData.reservoirRay);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    passData.material.SetFloat(HoSSGIShaderConstants.FireflyEnabledId, passData.enabled ? 1.0f : 0.0f);
                    Blitter.BlitTexture(context.cmd, passData.reservoirColor, new Vector4(1, 1, 0, 0), passData.material, 5);
                });
            }

            TextureHandle spatialReservoirColor = renderGraph.CreateTexture(reservoirDesc);
            TextureHandle spatialReservoirAux = renderGraph.CreateTexture(reservoirDesc);
            TextureHandle spatialReservoirRay = renderGraph.CreateTexture(reservoirDesc);
            using (var builder = renderGraph.AddRasterRenderPass<SpatialResamplingPassData>("Ho-SSGI Spatial Resampling", out SpatialResamplingPassData data, new ProfilingSampler("Ho-SSGI Spatial Resampling")))
            {
                data.material = material;
                data.reservoirColor = fireflyReservoirColor;
                data.reservoirAux = fireflyReservoirAux;
                data.reservoirRay = fireflyReservoirRay;
                data.temporal = temporal;
                data.geometry = geometry.normalDepthTexture;
                data.outputColor = spatialReservoirColor;
                data.outputAux = spatialReservoirAux;
                data.outputRay = spatialReservoirRay;
                data.radius = Mathf.Clamp(settings.spatialRadius, 0.5f, 8.0f);
                data.reservoirReuse = settings.spatialReservoirReuse;
                builder.UseTexture(data.reservoirColor, AccessFlags.Read);
                builder.UseTexture(data.reservoirAux, AccessFlags.Read);
                builder.UseTexture(data.reservoirRay, AccessFlags.Read);
                builder.UseTexture(data.temporal, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.outputColor, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.outputAux, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(data.outputRay, 2, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SpatialResamplingPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.SpatialRadiusId, passData.radius);
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirReuseId, passData.reservoirReuse ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirValidationId, 0.0f);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirColorId, passData.reservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirAuxId, passData.reservoirAux);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirRayId, passData.reservoirRay);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIInputId, passData.temporal);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    Blitter.BlitTexture(context.cmd, passData.reservoirColor, new Vector4(1, 1, 0, 0), passData.material, 4);
                });
            }

            TextureHandle filtered = renderGraph.CreateTexture(outputDesc);
            using (var builder = renderGraph.AddRasterRenderPass<SpatialValidationPassData>("Ho-SSGI Spatial Validation", out SpatialValidationPassData data, new ProfilingSampler("Ho-SSGI Spatial Validation")))
            {
                data.material = material;
                data.reservoirColor = spatialReservoirColor;
                data.reservoirAux = spatialReservoirAux;
                data.reservoirRay = spatialReservoirRay;
                data.temporal = temporal;
                data.geometry = geometry.normalDepthTexture;
                data.output = filtered;
                data.reservoirValidation = settings.spatialReservoirValidation;
                builder.UseTexture(data.reservoirColor, AccessFlags.Read);
                builder.UseTexture(data.reservoirAux, AccessFlags.Read);
                builder.UseTexture(data.reservoirRay, AccessFlags.Read);
                builder.UseTexture(data.temporal, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SpatialValidationPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirReuseId, 1.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.ReservoirValidationId, passData.reservoirValidation ? 1.0f : 0.0f);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirColorId, passData.reservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirAuxId, passData.reservoirAux);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirRayId, passData.reservoirRay);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIInputId, passData.temporal);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    Blitter.BlitTexture(context.cmd, passData.reservoirColor, new Vector4(1, 1, 0, 0), passData.material, 11);
                });
            }
            TextureHandle temporallyDenoised = renderGraph.CreateTexture(outputDesc);
            using (var builder = renderGraph.AddRasterRenderPass<DenoisedTemporalPassData>("Ho-SSGI Temporal Accumulation", out DenoisedTemporalPassData data, new ProfilingSampler("Ho-SSGI Temporal Accumulation")))
            {
                data.material = material;
                data.source = filtered;
                data.history = previousDenoised;
                data.geometry = geometry.normalDepthTexture;
                data.motion = motion;
                data.output = temporallyDenoised;
                data.historyValid = history.Valid;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.history, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                if (data.motion.IsValid()) builder.UseTexture(data.motion, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DenoisedTemporalPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.HistoryValidId, passData.historyValid ? 1.0f : 0.0f);
                    passData.material.SetFloat(HoSSGIShaderConstants.MotionValidId, passData.motion.IsValid() ? 1.0f : 0.0f);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIInputId, passData.source);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.DenoisedHistoryId, passData.history);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    if (passData.motion.IsValid()) context.cmd.SetGlobalTexture(HoSSGIShaderConstants.MotionVectorId, passData.motion);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 9);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<DenoisedHistoryPassData>("Ho-SSGI Denoised History", out DenoisedHistoryPassData data, new ProfilingSampler("Ho-SSGI Denoised History")))
            {
                data.material = material;
                data.source = temporallyDenoised;
                data.output = nextDenoised;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DenoisedHistoryPassData passData, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 10);
                });
            }
            history.SwapDenoised();

            TextureDesc bilateralDesc = outputDesc;
            bilateralDesc.name = "_HoSSGIBilateralDenoise";
            TextureHandle denoised = renderGraph.CreateTexture(bilateralDesc);
            using (var builder = renderGraph.AddRasterRenderPass<BilateralPassData>("Ho-SSGI Bilateral Denoise", out BilateralPassData data, new ProfilingSampler("Ho-SSGI Bilateral Denoise")))
            {
                data.material = material;
                data.source = temporallyDenoised;
                data.geometry = geometry.normalDepthTexture;
                data.output = denoised;
                data.radius = Mathf.Clamp(settings.spatialRadius, 0.5f, 8.0f);
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(data.output, HoSSGIShaderConstants.GITextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BilateralPassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.SpatialRadiusId, passData.radius);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIInputId, passData.source);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 6);
                });
            }
            resources.giTexture = denoised;
            resources.reservoirColorTexture = fireflyReservoirColor;
            resources.reservoirAuxTexture = fireflyReservoirAux;
        }
    }

    internal sealed class HoSSGICompositePass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle gi;
            public TextureHandle geometry;
            public TextureHandle destination;
            public float strength;
        }

        private HoSSGISettings settings;
        private Material material;

        public void Setup(HoSSGISettings settings, Material material)
        {
            this.settings = settings;
            this.material = material;
            int configuredEvent = settings != null ? (int)settings.compositePassEvent : (int)RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent = (RenderPassEvent)Mathf.Clamp(
                configuredEvent,
                (int)RenderPassEvent.AfterRenderingOpaques,
                (int)RenderPassEvent.BeforeRenderingPostProcessing);
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoSSGIRenderGraphResources ssgi = frameData.GetOrCreate<HoSSGIRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || !ssgi.giTexture.IsValid() || !geometry.normalDepthTexture.IsValid()) return;

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_HoSSGICompositeColor";
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SSGI Composite", out PassData data, new ProfilingSampler("Ho-SSGI Composite")))
            {
                data.material = material;
                data.source = source;
                data.gi = ssgi.giTexture;
                data.geometry = geometry.normalDepthTexture;
                data.destination = destination;
                data.strength = Mathf.Max(settings.intensity, 0.0f);
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.gi, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetFloat(HoSSGIShaderConstants.IntensityId, passData.strength);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GITextureId, passData.gi);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 1);
                });
            }
            resourceData.cameraColor = destination;
        }
    }

    internal sealed class HoSSGIDebugPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle cameraColor;
            public TextureHandle source;
            public TextureHandle rawGi;
            public TextureHandle geometry;
            public TextureHandle gi;
            public TextureHandle reservoirColor;
            public TextureHandle reservoirAux;
            public TextureHandle destination;
            public int debugMode;
        }

        private HoSSGISettings settings;
        private Material material;

        public void Setup(HoSSGISettings settings, Material material)
        {
            this.settings = settings;
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoSSGIRenderGraphResources ssgi = frameData.GetOrCreate<HoSSGIRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            TextureHandle source = ssgi.sourceTexture;
            TextureHandle rawGi = ssgi.rawGiTexture;
            if (!cameraColor.IsValid() || !source.IsValid() || !rawGi.IsValid() || !ssgi.giTexture.IsValid()
                || !ssgi.reservoirColorTexture.IsValid() || !ssgi.reservoirAuxTexture.IsValid()
                || !geometry.normalDepthTexture.IsValid()) return;
            TextureDesc desc = renderGraph.GetTextureDesc(cameraColor);
            desc.name = "_HoSSGIDebugColor";
            desc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(desc);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SSGI Debug", out PassData data, new ProfilingSampler("Ho-SSGI Debug")))
            {
                data.material = material;
                data.cameraColor = cameraColor;
                data.source = source;
                data.rawGi = rawGi;
                data.geometry = geometry.normalDepthTexture;
                data.gi = ssgi.giTexture;
                data.reservoirColor = ssgi.reservoirColorTexture;
                data.reservoirAux = ssgi.reservoirAuxTexture;
                data.destination = destination;
                data.debugMode = (int)settings.debugMode;
                builder.UseTexture(data.cameraColor, AccessFlags.Read);
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.rawGi, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.gi, AccessFlags.Read);
                builder.UseTexture(data.reservoirColor, AccessFlags.Read);
                builder.UseTexture(data.reservoirAux, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetInt(HoSSGIShaderConstants.DebugModeId, passData.debugMode);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceId, passData.source);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.RawGIId, passData.rawGi);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GITextureId, passData.gi);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirColorId, passData.reservoirColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.ReservoirAuxId, passData.reservoirAux);
                    Blitter.BlitTexture(context.cmd, passData.cameraColor, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }
            resourceData.cameraColor = destination;
        }
    }
}
