// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.AOV;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    [DisallowMultipleRendererFeature("lilToon-HoSubsurfaceScattering")]
    public sealed class HoSubsurfaceScatteringRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoSubsurfaceScatteringSettings settings = new HoSubsurfaceScatteringSettings();

        private readonly HoSubsurfaceScatteringRenderTargets renderTargets = new HoSubsurfaceScatteringRenderTargets();
        private HoSubsurfaceScatteringSourcePass sourcePass;
        private HoSubsurfaceScatteringBlurPass horizontalBlurPass;
        private HoSubsurfaceScatteringBlurPass verticalBlurPass;
        private HoSubsurfaceScatteringCompositePass compositePass;
        private Material material;
        private Shader materialShader;
        private bool warnedMissingShader;

        public HoSubsurfaceScatteringSettings Settings => settings;

        public override void Create()
        {
            settings?.ClampPassEvents();
            sourcePass = new HoSubsurfaceScatteringSourcePass();
            horizontalBlurPass = new HoSubsurfaceScatteringBlurPass("lilToon-HoSSS Horizontal Diffusion", new Vector2(1.0f, 0.0f));
            verticalBlurPass = new HoSubsurfaceScatteringBlurPass("lilToon-HoSSS Vertical Diffusion", new Vector2(0.0f, 1.0f));
            compositePass = new HoSubsurfaceScatteringCompositePass();
        }

        private void OnValidate()
        {
            settings?.ClampPassEvents();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            settings?.ClampPassEvents();
            EnsureMaterial();
            if (material == null)
            {
                return;
            }

            sourcePass?.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, material);
            horizontalBlurPass?.Setup(settings, renderTargets, material, false);
            verticalBlurPass?.Setup(settings, renderTargets, material, true);
            compositePass?.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            settings?.ClampPassEvents();
            EnsureMaterial();
            if (material == null)
            {
                return;
            }

            sourcePass?.SetupRenderGraph(settings, renderTargets, material);
            horizontalBlurPass?.SetupRenderGraph(settings, renderTargets, material, false);
            verticalBlurPass?.SetupRenderGraph(settings, renderTargets, material, true);
            compositePass?.SetupRenderGraph(settings, renderTargets, material);

            renderer.EnqueuePass(sourcePass);
            renderer.EnqueuePass(horizontalBlurPass);
            renderer.EnqueuePass(verticalBlurPass);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            renderTargets.Release();
            CoreUtils.Destroy(material);
            sourcePass = null;
            horizontalBlurPass = null;
            verticalBlurPass = null;
            compositePass = null;
            material = null;
            materialShader = null;
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.SceneView)
            {
                return settings.renderInSceneView;
            }

            return cameraType == CameraType.Game;
        }

        private void EnsureMaterial()
        {
            Shader shader = settings != null && settings.shader != null
                ? settings.shader
                : Shader.Find(HoSubsurfaceScatteringShaderConstants.ShaderName);

            if (material != null && materialShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(material);
            material = null;
            materialShader = shader;
            if (shader == null)
            {
                if (!warnedMissingShader)
                {
                    warnedMissingShader = true;
                    Debug.LogWarning($"HoSubsurfaceScattering is unavailable because shader '{HoSubsurfaceScatteringShaderConstants.ShaderName}' could not be found.");
                }

                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed class HoSubsurfaceScatteringSourcePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoSSS Source");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetSourceRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetSourceRenderPassEvent();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings, renderTargets.SourceTexture);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.SourceTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 0);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid() || !aovResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle source = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                HoSubsurfaceScatteringShaderConstants.SourceTextureName));
            sssResources.sourceTexture = source;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoSSS Source", out PassData passData, ProfilingSampler))
            {
                passData.source = cameraColor;
                passData.maskIdTexture = aovResources.maskIdTexture;
                passData.normalDepthTexture = aovResources.normalDepthTexture;
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings, cameraData.cameraTargetDescriptor, source.GetDescriptor(renderGraph));
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(source, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(source, HoSubsurfaceScatteringShaderConstants.SourceTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    SetMaterialProperties(data.material, data.sssParams, data.gateParams, data.color);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings, RTHandle sourceTarget)
        {
            SetMaterialProperties(material, CreateSssParams(settings, sourceTarget), CreateGateParams(settings), settings.color);
        }

        private static void SetMaterialProperties(Material material, Vector4 sssParams, Vector4 gateParams, Vector4 color)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, sssParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, gateParams);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, color);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, RTHandle sourceTarget)
        {
            RenderTexture rt = sourceTarget != null ? sourceTarget.rt : null;
            float scaleCompensation = rt != null ? RTHandles.rtHandleProperties.currentViewportSize.x / Mathf.Max(1.0f, rt.width) : 1.0f;
            return CreateSssParams(settings, scaleCompensation);
        }

        private static Vector4 CreateSssParams(
            HoSubsurfaceScatteringSettings settings,
            RenderTextureDescriptor cameraTextureDescriptor,
            TextureDesc sourceDesc)
        {
            float scaleCompensation = sourceDesc.width > 0
                ? cameraTextureDescriptor.width / (float)sourceDesc.width
                : 1.0f;
            return CreateSssParams(settings, scaleCompensation);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, float scaleCompensation)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.strength),
                Mathf.Max(0.0f, settings.radius),
                Mathf.Clamp01(settings.sourcePreserve),
                Mathf.Max(1.0f, scaleCompensation));
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0001f, settings.depthTolerance),
                Mathf.Max(0.01f, settings.normalTolerance),
                0.0f,
                0.0f);
        }

    }

    internal sealed class HoSubsurfaceScatteringBlurPass : ScriptableRenderPass
    {
        private const int BlurPassIndex = 1;
        private readonly ProfilingSampler blurProfilingSampler;
        private readonly string blurPassName;
        private readonly Vector2 direction;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;
        private bool vertical;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 direction;
        }

        public HoSubsurfaceScatteringBlurPass(string passName, Vector2 direction)
        {
            blurPassName = passName;
            this.direction = direction;
            blurProfilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            this.vertical = vertical;
            renderPassEvent = vertical
                ? settings.GetVerticalBlurRenderPassEvent()
                : settings.GetHorizontalBlurRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material,
            bool vertical)
        {
            Setup(settings, renderTargets, material, vertical);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            RTHandle source = vertical ? renderTargets.DiffusionTexture : renderTargets.SourceTexture;
            RTHandle destination = vertical ? renderTargets.SourceTexture : renderTargets.DiffusionTexture;
            if (source == null || destination == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, blurProfilingSampler))
            {
                SetMaterialProperties(material, settings, destination);
                SetDirection(material);
                Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, BlurPassIndex);
                cmd.SetGlobalTexture(vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureId : HoSubsurfaceScatteringShaderConstants.DiffusionTextureId, destination.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle source = vertical ? sssResources.diffusionTexture : sssResources.sourceTexture;
            if (!source.IsValid() || !aovResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = renderGraph.CreateTexture(HoSubsurfaceScatteringRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureName : HoSubsurfaceScatteringShaderConstants.DiffusionTextureName));

            if (vertical)
            {
                sssResources.sourceTexture = destination;
            }
            else
            {
                sssResources.diffusionTexture = destination;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(blurPassName, out PassData passData, blurProfilingSampler))
            {
                passData.source = source;
                passData.destination = destination;
                passData.maskIdTexture = aovResources.maskIdTexture;
                passData.normalDepthTexture = aovResources.normalDepthTexture;
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings, cameraData.cameraTargetDescriptor, destination.GetDescriptor(renderGraph));
                passData.gateParams = CreateGateParams(settings);
                passData.direction = new Vector4(direction.x, direction.y, 0.0f, 0.0f);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(destination, vertical ? HoSubsurfaceScatteringShaderConstants.SourceTextureId : HoSubsurfaceScatteringShaderConstants.DiffusionTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, data.direction);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, BlurPassIndex);
                });
            }
        }

        private void SetDirection(Material material)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.DirectionId, new Vector4(direction.x, direction.y, 0.0f, 0.0f));
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings, RTHandle target)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings, target));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings, RTHandle target)
        {
            RenderTexture rt = target != null ? target.rt : null;
            float scaleCompensation = rt != null ? RTHandles.rtHandleProperties.currentViewportSize.x / Mathf.Max(1.0f, rt.width) : 1.0f;
            return new Vector4(Mathf.Max(0.0f, settings.strength), Mathf.Max(0.0f, settings.radius), Mathf.Clamp01(settings.sourcePreserve), Mathf.Max(1.0f, scaleCompensation));
        }

        private static Vector4 CreateSssParams(
            HoSubsurfaceScatteringSettings settings,
            RenderTextureDescriptor cameraTextureDescriptor,
            TextureDesc targetDesc)
        {
            float scaleCompensation = targetDesc.width > 0
                ? cameraTextureDescriptor.width / (float)targetDesc.width
                : 1.0f;
            return new Vector4(Mathf.Max(0.0f, settings.strength), Mathf.Max(0.0f, settings.radius), Mathf.Clamp01(settings.sourcePreserve), Mathf.Max(1.0f, scaleCompensation));
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), 0.0f, 0.0f);
        }
    }

    internal sealed class HoSubsurfaceScatteringCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoSSS Composite");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle cameraColor;
            public TextureHandle sssTexture;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetCompositeRenderPassEvent();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.SourceTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.CompositeSourceTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, renderTargets.CompositeSourceTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 2);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            TextureHandle sssTexture = sssResources.sourceTexture;
            if (!cameraColor.IsValid() || !sssTexture.IsValid() || !aovResources.HasRequiredTextures)
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            destinationDesc.name = "_lilHoSSSCompositeColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoSSS Composite", out PassData passData, ProfilingSampler))
            {
                passData.cameraColor = cameraColor;
                passData.sssTexture = sssTexture;
                passData.maskIdTexture = aovResources.maskIdTexture;
                passData.normalDepthTexture = aovResources.normalDepthTexture;
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings);
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(sssTexture, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, data.color);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.sssTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.cameraColor, new Vector4(1, 1, 0, 0), data.material, 2);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, settings.color);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0f, settings.strength), Mathf.Max(0.0f, settings.radius), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), 0.0f, 0.0f);
        }
    }

    internal sealed class HoSubsurfaceScatteringRenderGraphResources : ContextItem
    {
        public TextureHandle sourceTexture = TextureHandle.nullHandle;
        public TextureHandle diffusionTexture = TextureHandle.nullHandle;

        public override void Reset()
        {
            sourceTexture = TextureHandle.nullHandle;
            diffusionTexture = TextureHandle.nullHandle;
        }

        public static TextureDesc CreateDescriptor(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoSubsurfaceScatteringSettings settings,
            string name)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = HoSubsurfaceScatteringRenderTargets.GetColorGraphicsFormat(cameraTextureDescriptor);
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }
    }

    internal sealed class HoSubsurfaceScatteringRenderTargets
    {
        private RTHandle sourceTexture;
        private RTHandle diffusionTexture;
        private RTHandle compositeSourceTexture;

        public RTHandle SourceTexture => sourceTexture;
        public RTHandle DiffusionTexture => diffusionTexture;
        public RTHandle CompositeSourceTexture => compositeSourceTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, HoSubsurfaceScatteringSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.graphicsFormat = GetColorGraphicsFormat(cameraTextureDescriptor);
            descriptor.msaaSamples = 1;
            descriptor.width = Mathf.Max(1, descriptor.width / divisor);
            descriptor.height = Mathf.Max(1, descriptor.height / divisor);

            RenderingUtils.ReAllocateIfNeeded(ref sourceTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.SourceTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref diffusionTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.DiffusionTextureName);
        }

        public void ReAllocateCompositeSource(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
            cameraTextureDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraTextureDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref compositeSourceTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoSubsurfaceScatteringShaderConstants.CompositeSourceTextureName);
        }

        public void Release()
        {
            sourceTexture?.Release();
            diffusionTexture?.Release();
            compositeSourceTexture?.Release();
            sourceTexture = null;
            diffusionTexture = null;
            compositeSourceTexture = null;
        }

        internal static GraphicsFormat GetColorGraphicsFormat(RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (cameraTextureDescriptor.graphicsFormat != GraphicsFormat.None &&
                SystemInfo.IsFormatSupported(cameraTextureDescriptor.graphicsFormat, FormatUsage.Render))
            {
                return cameraTextureDescriptor.graphicsFormat;
            }

            if (SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render))
            {
                return GraphicsFormat.R16G16B16A16_SFloat;
            }

            return SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
        }
    }
}
