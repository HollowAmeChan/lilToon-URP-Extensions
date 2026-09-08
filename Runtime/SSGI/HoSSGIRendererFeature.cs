#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.MetadataBuffer;

namespace lilToon.URP.Extensions.SSGI
{
    [DisallowMultipleRendererFeature("Ho-SSGI")]
    public sealed class HoSSGIRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoSSGISettings settings = new HoSSGISettings();
        private HoSSGIPass pass;
        private HoSSGIDebugPass debugPass;
        private Material material;
        private Material debugMaterial;
        private Shader shader;
        private Shader debugShader;

        public HoSSGISettings Settings => settings;

        public override void Create()
        {
            pass = new HoSSGIPass();
            debugPass = new HoSSGIDebugPass();
            RenderPipelineManager.beginCameraRendering += ResetGlobalState;
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

            pass.Setup(settings, material);
            renderer.EnqueuePass(pass);
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
            debugPass = null;
        }

        private static void ResetGlobalState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(HoSSGIShaderConstants.GITextureId, Texture2D.blackTexture);
        }
    }

    internal sealed class HoSSGIPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle geometry;
            public TextureHandle surfaceColor;
            public TextureHandle output;
            public int rayCount;
            public int stepCount;
            public float rayLength;
            public float thickness;
            public float intensity;
            public float sourceSaturation;
        }

        private HoSSGISettings settings;
        private Material material;

        public void Setup(HoSSGISettings settings, Material material)
        {
            this.settings = settings;
            this.material = material;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || material == null) return;
            HoGeometryBufferRenderGraphResources geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoMetadataBufferRenderGraphResources metadata = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            TextureHandle source = metadata.surfaceColorTexture;
            if (!source.IsValid() || !geometry.normalDepthTexture.IsValid() || !metadata.surfaceColorTexture.IsValid()) return;

            TextureDesc outputDesc = renderGraph.GetTextureDesc(source);
            outputDesc.name = HoSSGIShaderConstants.GITextureName;
            outputDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
            outputDesc.depthBufferBits = 0;
            outputDesc.clearBuffer = true;
            outputDesc.clearColor = Color.clear;
            TextureHandle output = renderGraph.CreateTexture(outputDesc);
            HoSSGIRenderGraphResources resources = frameData.GetOrCreate<HoSSGIRenderGraphResources>();
            resources.giTexture = output;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SSGI Raw Trace", out PassData data, new ProfilingSampler("Ho-SSGI Raw Trace")))
            {
                data.material = material;
                data.source = source;
                data.geometry = geometry.normalDepthTexture;
                data.surfaceColor = metadata.surfaceColorTexture;
                data.output = output;
                data.rayCount = Mathf.Clamp(settings.rayCount, 1, 32);
                data.stepCount = Mathf.Clamp(settings.stepCount, 4, 64);
                data.rayLength = Mathf.Max(settings.rayLength, 0.01f);
                data.thickness = Mathf.Clamp01(settings.thickness);
                data.intensity = Mathf.Max(settings.intensity, 0.0f);
                data.sourceSaturation = Mathf.Clamp01(settings.sourceSaturation);
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.surfaceColor, AccessFlags.Read);
                builder.SetRenderAttachment(data.output, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(data.output, HoSSGIShaderConstants.GITextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetInt(HoSSGIShaderConstants.RayCountId, passData.rayCount);
                    passData.material.SetInt(HoSSGIShaderConstants.StepCountId, passData.stepCount);
                    passData.material.SetFloat(HoSSGIShaderConstants.RayLengthId, passData.rayLength);
                    passData.material.SetFloat(HoSSGIShaderConstants.ThicknessId, passData.thickness);
                    passData.material.SetFloat(HoSSGIShaderConstants.IntensityId, passData.intensity);
                    passData.material.SetFloat(HoSSGIShaderConstants.SourceSaturationId, passData.sourceSaturation);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceId, passData.source);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SurfaceColorId, passData.surfaceColor);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }
        }
    }

    internal sealed class HoSSGIDebugPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle geometry;
            public TextureHandle surfaceColor;
            public TextureHandle gi;
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
            HoMetadataBufferRenderGraphResources metadata = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || !ssgi.giTexture.IsValid() || !geometry.normalDepthTexture.IsValid() || !metadata.surfaceColorTexture.IsValid()) return;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_HoSSGIDebugColor";
            desc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(desc);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SSGI Debug", out PassData data, new ProfilingSampler("Ho-SSGI Debug")))
            {
                data.material = material;
                data.source = source;
                data.geometry = geometry.normalDepthTexture;
                data.surfaceColor = metadata.surfaceColorTexture;
                data.gi = ssgi.giTexture;
                data.destination = destination;
                data.debugMode = (int)settings.debugMode;
                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.geometry, AccessFlags.Read);
                builder.UseTexture(data.surfaceColor, AccessFlags.Read);
                builder.UseTexture(data.gi, AccessFlags.Read);
                builder.SetRenderAttachment(data.destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.material.SetInt(HoSSGIShaderConstants.DebugModeId, passData.debugMode);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SourceId, passData.source);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GeometryId, passData.geometry);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.SurfaceColorId, passData.surfaceColor);
                    context.cmd.SetGlobalTexture(HoSSGIShaderConstants.GITextureId, passData.gi);
                    Blitter.BlitTexture(context.cmd, passData.source, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
            }
            resourceData.cameraColor = destination;
        }
    }
}
