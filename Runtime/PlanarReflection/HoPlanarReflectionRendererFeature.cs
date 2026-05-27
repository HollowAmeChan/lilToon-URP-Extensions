using System;
using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace lilToon.URP.Extensions.PlanarReflection
{
    [DisallowMultipleRendererFeature("Ho-PlanarReflection")]
    [ExecuteAlways]
    public sealed class HoPlanarReflectionRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Skip all planar reflection rendering without removing the renderer feature.")]
            public bool enabled = true;

            [Tooltip("Render planar reflections for Game cameras.")]
            public bool renderGameView = true;

            [Tooltip("Render planar reflections for Scene View cameras.")]
            public bool renderSceneView = true;

            [Tooltip("Maximum surfaces rendered for one source camera. 0 means unlimited.")]
            [Min(0)]
            public int maxSurfacesPerCamera;

            [Tooltip("Composite planar reflection after transparents by reading MetadataBuffer and GeometryBuffer.")]
            public bool compositeEnabled = true;

            [Tooltip("Render pass event for the post composite. Run after water transparents and before post processing.")]
            public RenderPassEvent compositePassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Tooltip("Optional override shader for the composite pass.")]
            public Shader compositeShader;

            [Tooltip("Overall composite strength multiplied with per-material planar reflection strength.")]
            [Range(0.0f, 1.0f)]
            public float compositeStrength = 1.0f;

            [Tooltip("Screen-space reflection UV distortion driven by GeometryBuffer water normal.")]
            [Range(0.0f, 0.1f)]
            public float distortion = 0.018f;

            [Tooltip("Screen-space inset used as the source line for reflection edge pixel extension. 0 uses the texture border.")]
            [Range(0.0f, 0.25f)]
            [FormerlySerializedAs("edgeFadeDistance")]
            public float edgeExtendDistance = 0.035f;

            [Tooltip("Flip the reflection texture vertically during post composite.")]
            public bool compositeFlipY;

            [Tooltip("Minimum smoothness required before the composite reflection fades in.")]
            [Range(0.0f, 1.0f)]
            public float minSmoothness = 0.65f;

            [Tooltip("Enable GeometryBuffer depth rejection for distorted samples. Keep this off for large flat water surfaces.")]
            public bool enableDepthGate;

            [Tooltip("Reject distorted samples whose GeometryBuffer depth differs too much from the center water pixel. 0 disables the check.")]
            [Min(0.0f)]
            public float depthTolerance = 0.0f;

            [Tooltip("Exposure applied to the reflection texture before compositing, in EV stops. 0 keeps the captured reflection unchanged.")]
            [Range(-4.0f, 4.0f)]
            public float reflectionExposure;

            [Tooltip("Disk blur radius applied to the reflection texture before compositing, in reflection texture pixels. 0 disables the RDG preprocess pass unless exposure is non-zero.")]
            [Range(0.0f, 32.0f)]
            public float reflectionBlurRadiusPixels;

            [Tooltip("Tint multiplied onto the reflection texture before compositing.")]
            public Color tint = Color.white;

            [Header("Debug")]
            [Tooltip("Debug view rendered by the composite pass. Off renders the normal composite result.")]
            public HoPlanarReflectionDebugMode debugMode = HoPlanarReflectionDebugMode.Off;

            [Tooltip("Far depth used to normalize GeometryBuffer linear depth in debug views.")]
            [Min(0.0001f)]
            public float debugDepthFar = 100.0f;

            [Tooltip("Scale applied when visualizing screen-space distortion vectors.")]
            [Min(0.0001f)]
            public float debugDistortionScale = 32.0f;
        }

        private static readonly List<HoPlanarReflectionRendererFeature> ActiveFeatures =
            new List<HoPlanarReflectionRendererFeature>();

        private static bool registered;

        [SerializeField]
        private Settings settings = new Settings();

        private HoPlanarReflectionCompositePass compositePass;
        private Material compositeMaterial;
        private Shader compositeShader;
        private bool warnedMissingCompositeShader;

        public Settings FeatureSettings => settings;

        public override void Create()
        {
            RegisterFeature(this);
            compositePass = new HoPlanarReflectionCompositePass();
        }

        private void OnValidate()
        {
            ClampSettings(settings);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldCompositeRender(in renderingData))
            {
                compositePass?.ReleaseCompatibilityResources();
                return;
            }

            EnsureCompositeMaterial();
            if (compositeMaterial == null)
            {
                compositePass?.ReleaseCompatibilityResources();
                return;
            }

            compositePass?.Setup(settings, renderer.cameraColorTargetHandle, compositeMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Reflection rendering is driven from beginCameraRendering so the mirror camera renders before the source camera.
            if (!ShouldCompositeRender(in renderingData))
            {
                compositePass?.ReleaseCompatibilityResources();
                return;
            }

            EnsureCompositeMaterial();
            if (compositeMaterial == null)
            {
                compositePass?.ReleaseCompatibilityResources();
                return;
            }

            compositePass?.SetupRenderGraph(settings, compositeMaterial);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterFeature(this);
            compositePass?.ReleaseCompatibilityResources();
            compositePass = null;
            CoreUtils.Destroy(compositeMaterial);
            compositeMaterial = null;
            compositeShader = null;
        }

        private static void RegisterFeature(HoPlanarReflectionRendererFeature feature)
        {
            if (feature != null && !ActiveFeatures.Contains(feature))
            {
                ActiveFeatures.Add(feature);
            }

            if (registered)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += RenderPlanarReflections;
            registered = true;
        }

        private static void UnregisterFeature(HoPlanarReflectionRendererFeature feature)
        {
            ActiveFeatures.Remove(feature);
            if (!registered || ActiveFeatures.Count > 0)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= RenderPlanarReflections;
            registered = false;
        }

        private static void RenderPlanarReflections(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
            {
                HoPlanarReflectionSurface.ResetGlobalState();
                return;
            }

            HoPlanarReflectionRendererFeature feature = ResolveFeature(camera);
            if (feature == null)
            {
                HoPlanarReflectionRenderStats skippedStats = HoPlanarReflectionSurface.RenderAllSurfaces(
                    context,
                    camera,
                    new HoPlanarReflectionRenderSettings(false, false, false, 0, false));
                HoPlanarReflectionRuntimeDiagnostics.Publish(camera, skippedStats);
                return;
            }

            if (feature.settings != null && feature.settings.enabled && feature.settings.compositeEnabled)
            {
                feature.EnsureCompositeMaterial();
            }

            feature.PublishGlobalCompositeSettings();

            HoPlanarReflectionRenderStats stats = HoPlanarReflectionSurface.RenderAllSurfaces(
                context,
                camera,
                feature.CreateRenderSettings());
            HoPlanarReflectionRuntimeDiagnostics.Publish(camera, stats);
        }

        private static HoPlanarReflectionRendererFeature ResolveFeature(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            HoPlanarReflectionRendererFeature fallback = null;
            for (int i = 0; i < ActiveFeatures.Count; i++)
            {
                HoPlanarReflectionRendererFeature feature = ActiveFeatures[i];
                if (feature != null && feature.isActive && feature.settings != null)
                {
                    fallback ??= feature;
                    if (feature.settings.enabled)
                    {
                        return feature;
                    }
                }
            }

            return fallback;
        }

        private HoPlanarReflectionRenderSettings CreateRenderSettings()
        {
            Settings activeSettings = settings ?? new Settings();
            ClampSettings(activeSettings);
            bool compositeAvailable = activeSettings.compositeEnabled && compositeMaterial != null;
            return new HoPlanarReflectionRenderSettings(
                activeSettings.enabled,
                activeSettings.renderGameView,
                activeSettings.renderSceneView,
                activeSettings.maxSurfacesPerCamera,
                compositeAvailable);
        }

        private void PublishGlobalCompositeSettings()
        {
            Settings activeSettings = settings ?? new Settings();
            ClampSettings(activeSettings);
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeParamsId, HoPlanarReflectionShaderParams.CreateCompositeParams(activeSettings));
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeOptionsId, HoPlanarReflectionShaderParams.CreateCompositeOptions(activeSettings));
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeTintId, HoPlanarReflectionShaderParams.CreateTint(activeSettings));
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.PreprocessParamsId, HoPlanarReflectionShaderParams.CreatePreprocessParams(activeSettings));
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugParamsId, HoPlanarReflectionShaderParams.CreateDebugParams(activeSettings));
            Shader.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugInputStatusId, Vector4.one);
        }

        private bool ShouldCompositeRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || !settings.compositeEnabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return (cameraType == CameraType.Game && settings.renderGameView)
                || (cameraType == CameraType.SceneView && settings.renderSceneView);
        }

        private void EnsureCompositeMaterial()
        {
            Shader shader = settings != null && settings.compositeShader != null
                ? settings.compositeShader
                : Shader.Find(HoPlanarReflectionShaderConstants.CompositeShaderName);

            if (compositeMaterial != null && compositeShader == shader)
            {
                return;
            }

            CoreUtils.Destroy(compositeMaterial);
            compositeMaterial = null;
            compositeShader = shader;
            if (shader == null)
            {
                if (!warnedMissingCompositeShader)
                {
                    warnedMissingCompositeShader = true;
                    Debug.LogWarning($"Ho-PlanarReflection composite is unavailable because shader '{HoPlanarReflectionShaderConstants.CompositeShaderName}' could not be found.");
                }

                return;
            }

            compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        private static void ClampSettings(Settings activeSettings)
        {
            if (activeSettings == null)
            {
                return;
            }

            activeSettings.maxSurfacesPerCamera = Mathf.Max(0, activeSettings.maxSurfacesPerCamera);
            activeSettings.compositeStrength = Mathf.Clamp01(activeSettings.compositeStrength);
            activeSettings.distortion = Mathf.Clamp(activeSettings.distortion, 0.0f, 0.1f);
            activeSettings.edgeExtendDistance = Mathf.Clamp(activeSettings.edgeExtendDistance, 0.0f, 0.25f);
            activeSettings.minSmoothness = Mathf.Clamp01(activeSettings.minSmoothness);
            activeSettings.depthTolerance = Mathf.Max(0.0f, activeSettings.depthTolerance);
            activeSettings.reflectionExposure = Mathf.Clamp(activeSettings.reflectionExposure, -4.0f, 4.0f);
            activeSettings.reflectionBlurRadiusPixels = Mathf.Clamp(activeSettings.reflectionBlurRadiusPixels, 0.0f, 32.0f);
            activeSettings.debugDepthFar = Mathf.Max(0.0001f, activeSettings.debugDepthFar);
            activeSettings.debugDistortionScale = Mathf.Max(0.0001f, activeSettings.debugDistortionScale);
        }
    }

    public enum HoPlanarReflectionDebugMode
    {
        Off = 0,
        InputStatus = 1,
        SurfaceMask = 2,
        Smoothness = 3,
        Wetness = 4,
        NormalStrength = 5,
        ReflectionStrength = 6,
        WorldNormal = 7,
        LinearDepth = 8,
        Distortion = 9,
        DistortedUv = 10,
        ReflectionColor = 11,
        CompositeWeight = 12,
        DepthGate = 13,
        Custom0 = 14,
        EdgeExtend = 15
    }

    internal static class HoPlanarReflectionShaderConstants
    {
        public const string CompositeShaderName = "Hidden/lilToon/URP/PlanarReflection/Composite";
        public const string UsePlanarReflectionName = "_UsePlanarReflection";
        public const string ReflectionTextureName = "_LILPBRPlanarReflectionTexture";
        public const string ReflectionTextureMatrixName = "_LILPBRPlanarReflectionTextureMatrix";
        public const string ReflectionParamsName = "_LILPBRPlanarReflectionParams";
        public const string CompositeActiveName = "_HoPlanarReflectionCompositeActive";
        public const string CompositeParamsName = "_HoPlanarReflectionCompositeParams";
        public const string CompositeOptionsName = "_HoPlanarReflectionCompositeOptions";
        public const string CompositeTintName = "_HoPlanarReflectionCompositeTint";
        public const string ProcessedReflectionTextureName = "_HoPlanarReflectionProcessedTexture";
        public const string PreprocessParamsName = "_HoPlanarReflectionPreprocessParams";
        public const string DebugParamsName = "_HoPlanarReflectionDebugParams";
        public const string DebugInputStatusName = "_HoPlanarReflectionDebugInputStatus";

        public static readonly int UsePlanarReflectionId = Shader.PropertyToID(UsePlanarReflectionName);
        public static readonly int ReflectionTextureId = Shader.PropertyToID(ReflectionTextureName);
        public static readonly int ReflectionTextureMatrixId = Shader.PropertyToID(ReflectionTextureMatrixName);
        public static readonly int ReflectionParamsId = Shader.PropertyToID(ReflectionParamsName);
        public static readonly int CompositeActiveId = Shader.PropertyToID(CompositeActiveName);
        public static readonly int CompositeParamsId = Shader.PropertyToID(CompositeParamsName);
        public static readonly int CompositeOptionsId = Shader.PropertyToID(CompositeOptionsName);
        public static readonly int CompositeTintId = Shader.PropertyToID(CompositeTintName);
        public static readonly int ProcessedReflectionTextureId = Shader.PropertyToID(ProcessedReflectionTextureName);
        public static readonly int PreprocessParamsId = Shader.PropertyToID(PreprocessParamsName);
        public static readonly int DebugParamsId = Shader.PropertyToID(DebugParamsName);
        public static readonly int DebugInputStatusId = Shader.PropertyToID(DebugInputStatusName);
    }

    internal static class HoPlanarReflectionShaderParams
    {
        public static Vector4 CreateCompositeParams(HoPlanarReflectionRendererFeature.Settings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.compositeStrength),
                Mathf.Clamp(settings.distortion, 0.0f, 0.1f),
                Mathf.Clamp01(settings.minSmoothness),
                Mathf.Max(0.0f, settings.depthTolerance));
        }

        public static Vector4 CreateCompositeOptions(HoPlanarReflectionRendererFeature.Settings settings)
        {
            return new Vector4(
                settings.compositeFlipY ? 1.0f : 0.0f,
                settings.enableDepthGate ? 1.0f : 0.0f,
                Mathf.Clamp(settings.edgeExtendDistance, 0.0f, 0.25f),
                0.0f);
        }

        public static Vector4 CreateDebugParams(HoPlanarReflectionRendererFeature.Settings settings)
        {
            return new Vector4(
                (float)settings.debugMode,
                Mathf.Max(0.0001f, settings.debugDepthFar),
                Mathf.Max(0.0001f, settings.debugDistortionScale),
                0.0f);
        }

        public static Vector4 CreatePreprocessParams(HoPlanarReflectionRendererFeature.Settings settings)
        {
            return new Vector4(
                Mathf.Pow(2.0f, Mathf.Clamp(settings.reflectionExposure, -4.0f, 4.0f)),
                Mathf.Clamp(settings.reflectionBlurRadiusPixels, 0.0f, 32.0f),
                0.0f,
                0.0f);
        }

        public static Vector4 CreateTint(HoPlanarReflectionRendererFeature.Settings settings)
        {
            Color tint = settings.tint;
            return new Vector4(tint.r, tint.g, tint.b, tint.a);
        }

        public static void ApplyMaterial(
            Material material,
            Vector4 compositeParams,
            Vector4 compositeOptions,
            Vector4 tint,
            Vector4 preprocessParams,
            Vector4 debugParams,
            Vector4 debugInputStatus)
        {
            material.SetVector(HoPlanarReflectionShaderConstants.CompositeParamsId, compositeParams);
            material.SetVector(HoPlanarReflectionShaderConstants.CompositeOptionsId, compositeOptions);
            material.SetVector(HoPlanarReflectionShaderConstants.CompositeTintId, tint);
            material.SetVector(HoPlanarReflectionShaderConstants.PreprocessParamsId, preprocessParams);
            material.SetVector(HoPlanarReflectionShaderConstants.DebugParamsId, debugParams);
            material.SetVector(HoPlanarReflectionShaderConstants.DebugInputStatusId, debugInputStatus);
        }

        public static void ApplyGlobals(
            CommandBuffer cmd,
            Vector4 compositeParams,
            Vector4 compositeOptions,
            Vector4 tint,
            Vector4 preprocessParams,
            Vector4 debugParams,
            Vector4 debugInputStatus)
        {
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeParamsId, compositeParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeOptionsId, compositeOptions);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeTintId, tint);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.PreprocessParamsId, preprocessParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugParamsId, debugParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugInputStatusId, debugInputStatus);
        }

        public static void ApplyGlobals(
            RasterCommandBuffer cmd,
            Vector4 compositeParams,
            Vector4 compositeOptions,
            Vector4 tint,
            Vector4 preprocessParams,
            Vector4 debugParams,
            Vector4 debugInputStatus)
        {
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeParamsId, compositeParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeOptionsId, compositeOptions);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.CompositeTintId, tint);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.PreprocessParamsId, preprocessParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugParamsId, debugParams);
            cmd.SetGlobalVector(HoPlanarReflectionShaderConstants.DebugInputStatusId, debugInputStatus);
        }

        public static void ApplyMaterial(Material material, HoPlanarReflectionRendererFeature.Settings settings, Vector4 debugInputStatus)
        {
            ApplyMaterial(
                material,
                CreateCompositeParams(settings),
                CreateCompositeOptions(settings),
                CreateTint(settings),
                CreatePreprocessParams(settings),
                CreateDebugParams(settings),
                debugInputStatus);
        }
    }

    internal sealed class HoPlanarReflectionCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-PlanarReflection Composite");
        private static readonly ProfilingSampler PreprocessProfilingSampler = new ProfilingSampler("Ho-PlanarReflection Preprocess");

        private HoPlanarReflectionRendererFeature.Settings settings;
        private RTHandle cameraColorTarget;
        private RTHandle compositeSourceTexture;
        private RTHandle reflectionProcessedTexture;
        private Material compositeMaterial;

        private sealed class PreprocessPassData
        {
            public TextureHandle source;
            public Material material;
            public Vector4 preprocessParams;
        }

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle reflectionTexture;
            public TextureHandle maskIdTexture;
            public TextureHandle custom0Texture;
            public TextureHandle normalDepthTexture;
            public Material material;
            public Vector4 compositeParams;
            public Vector4 compositeOptions;
            public Vector4 tint;
            public Vector4 preprocessParams;
            public Vector4 debugParams;
            public Vector4 debugInputStatus;
        }

        public void Setup(
            HoPlanarReflectionRendererFeature.Settings settings,
            RTHandle cameraColorTarget,
            Material compositeMaterial)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.compositeMaterial = compositeMaterial;
            ConfigurePass();
        }

        public void SetupRenderGraph(
            HoPlanarReflectionRendererFeature.Settings settings,
            Material compositeMaterial)
        {
            this.settings = settings;
            this.cameraColorTarget = null;
            this.compositeMaterial = compositeMaterial;
            ConfigurePass();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (cameraColorTarget != null)
            {
                ConfigureTarget(cameraColorTarget);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || compositeMaterial == null || cameraColorTarget == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
                ApplyMaterialProperties(compositeMaterial, settings);
                Vector4 preprocessParams = HoPlanarReflectionShaderParams.CreatePreprocessParams(settings);
                HoPlanarReflectionShaderParams.ApplyGlobals(
                    cmd,
                    HoPlanarReflectionShaderParams.CreateCompositeParams(settings),
                    HoPlanarReflectionShaderParams.CreateCompositeOptions(settings),
                    HoPlanarReflectionShaderParams.CreateTint(settings),
                    preprocessParams,
                    HoPlanarReflectionShaderParams.CreateDebugParams(settings),
                    Vector4.one);
                RTHandle reflectionSource = HoPlanarReflectionSurface.CurrentReflectionTextureHandle;
                if (reflectionSource != null && RequiresReflectionPreprocess(settings))
                {
                    ReAllocateReflectionProcessed(HoPlanarReflectionSurface.CurrentReflectionTexture);
                    if (reflectionProcessedTexture != null)
                    {
                        Blitter.BlitCameraTexture(cmd, reflectionSource, reflectionProcessedTexture, compositeMaterial, 1);
                        cmd.SetGlobalTexture(HoPlanarReflectionShaderConstants.ProcessedReflectionTextureId, reflectionProcessedTexture.nameID);
                    }
                    else
                    {
                        cmd.SetGlobalTexture(HoPlanarReflectionShaderConstants.ProcessedReflectionTextureId, reflectionSource.nameID);
                    }
                }
                else if (reflectionSource != null)
                {
                    cmd.SetGlobalTexture(HoPlanarReflectionShaderConstants.ProcessedReflectionTextureId, reflectionSource.nameID);
                }
                else
                {
                    cmd.SetGlobalTexture(HoPlanarReflectionShaderConstants.ProcessedReflectionTextureId, Texture2D.blackTexture);
                }

                Blitter.BlitCameraTexture(cmd, cameraColorTarget, compositeSourceTexture, 0, true);
                Blitter.BlitCameraTexture(
                    cmd,
                    compositeSourceTexture,
                    cameraColorTarget,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    compositeMaterial,
                    0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null || compositeMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle maskIdTexture = metadataResources.maskIdTexture;
            TextureHandle custom0Texture = metadataResources.custom0Texture;
            TextureHandle normalDepthTexture = geometryResources.normalDepthTexture;
            RTHandle reflectionRtHandle = HoPlanarReflectionSurface.CurrentReflectionTextureHandle;
            RenderTexture reflectionTextureResource = HoPlanarReflectionSurface.CurrentReflectionTexture;
            bool hasSource = source.IsValid();
            bool hasReflectionTexture = reflectionRtHandle != null && reflectionTextureResource != null;
            bool hasMaskId = maskIdTexture.IsValid();
            bool hasCustom0 = custom0Texture.IsValid();
            bool hasNormalDepth = normalDepthTexture.IsValid();
            bool inputStatusDebug = settings.debugMode == HoPlanarReflectionDebugMode.InputStatus;
            if (!hasSource || (!inputStatusDebug && (!hasReflectionTexture || !hasMaskId || !hasCustom0 || !hasNormalDepth)))
            {
                return;
            }

            TextureHandle reflectionTexture = hasReflectionTexture
                ? renderGraph.ImportTexture(reflectionRtHandle)
                : TextureHandle.nullHandle;
            TextureHandle compositeReflectionTexture = reflectionTexture;
            Vector4 preprocessParams = HoPlanarReflectionShaderParams.CreatePreprocessParams(settings);
            if (hasReflectionTexture && RequiresReflectionPreprocess(settings))
            {
                TextureDesc preprocessDesc = CreateReflectionTextureDesc(reflectionTextureResource, "_HoPlanarReflectionProcessedTexture");
                TextureHandle processedReflectionTexture = renderGraph.CreateTexture(preprocessDesc);
                AddReflectionPreprocessPass(
                    renderGraph,
                    "Ho-PlanarReflection Preprocess",
                    compositeMaterial,
                    reflectionTexture,
                    processedReflectionTexture,
                    preprocessParams);
                compositeReflectionTexture = processedReflectionTexture;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_HoPlanarReflectionCompositeColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-PlanarReflection Composite", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.reflectionTexture = compositeReflectionTexture;
                passData.maskIdTexture = maskIdTexture;
                passData.custom0Texture = custom0Texture;
                passData.normalDepthTexture = normalDepthTexture;
                passData.material = compositeMaterial;
                passData.compositeParams = HoPlanarReflectionShaderParams.CreateCompositeParams(settings);
                passData.compositeOptions = HoPlanarReflectionShaderParams.CreateCompositeOptions(settings);
                passData.tint = HoPlanarReflectionShaderParams.CreateTint(settings);
                passData.preprocessParams = preprocessParams;
                passData.debugParams = HoPlanarReflectionShaderParams.CreateDebugParams(settings);
                passData.debugInputStatus = new Vector4(hasReflectionTexture ? 1.0f : 0.0f, hasMaskId ? 1.0f : 0.0f, hasNormalDepth ? 1.0f : 0.0f, hasCustom0 ? 1.0f : 0.0f);

                builder.UseTexture(source, AccessFlags.Read);
                if (compositeReflectionTexture.IsValid())
                {
                    builder.UseTexture(compositeReflectionTexture, AccessFlags.Read);
                }

                if (hasMaskId)
                {
                    builder.UseTexture(maskIdTexture, AccessFlags.Read);
                }

                if (hasCustom0)
                {
                    builder.UseTexture(custom0Texture, AccessFlags.Read);
                }

                if (hasNormalDepth)
                {
                    builder.UseTexture(normalDepthTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    HoPlanarReflectionShaderParams.ApplyMaterial(
                        data.material,
                        data.compositeParams,
                        data.compositeOptions,
                        data.tint,
                        data.preprocessParams,
                        data.debugParams,
                        data.debugInputStatus);
                    HoPlanarReflectionShaderParams.ApplyGlobals(
                        context.cmd,
                        data.compositeParams,
                        data.compositeOptions,
                        data.tint,
                        data.preprocessParams,
                        data.debugParams,
                        data.debugInputStatus);
                    if (data.reflectionTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoPlanarReflectionShaderConstants.ProcessedReflectionTextureId, data.reflectionTexture);
                    }

                    if (data.maskIdTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    }

                    if (data.custom0Texture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, data.custom0Texture);
                    }

                    if (data.normalDepthTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        public void ReleaseCompatibilityResources()
        {
            compositeSourceTexture?.Release();
            compositeSourceTexture = null;
            reflectionProcessedTexture?.Release();
            reflectionProcessedTexture = null;
        }

        private void ConfigurePass()
        {
            renderPassEvent = settings != null ? settings.compositePassEvent : RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private void ReAllocateCompositeSource(RenderTextureDescriptor cameraTargetDescriptor)
        {
            cameraTargetDescriptor.depthBufferBits = 0;
            cameraTargetDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraTargetDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(
                ref compositeSourceTexture,
                cameraTargetDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_HoPlanarReflectionCompositeSource");
        }

        private void ReAllocateReflectionProcessed(RenderTexture reflectionTexture)
        {
            if (reflectionTexture == null)
            {
                reflectionProcessedTexture?.Release();
                reflectionProcessedTexture = null;
                return;
            }

            RenderTextureDescriptor descriptor = reflectionTexture.descriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            if (descriptor.graphicsFormat == GraphicsFormat.None)
            {
                descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            }

            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            RenderingUtils.ReAllocateIfNeeded(
                ref reflectionProcessedTexture,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_HoPlanarReflectionProcessedTexture");
        }

        private static bool RequiresReflectionPreprocess(HoPlanarReflectionRendererFeature.Settings settings)
        {
            if (settings == null)
            {
                return false;
            }

            return Mathf.Abs(settings.reflectionExposure) > 0.0001f
                || settings.reflectionBlurRadiusPixels > 0.0001f;
        }

        private static void AddReflectionPreprocessPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle source,
            TextureHandle destination,
            Vector4 preprocessParams)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PreprocessPassData>(passName, out PreprocessPassData passData, PreprocessProfilingSampler))
            {
                passData.source = source;
                passData.material = material;
                passData.preprocessParams = preprocessParams;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PreprocessPassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoPlanarReflectionShaderConstants.PreprocessParamsId, data.preprocessParams);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 1);
                });
            }
        }

        private static TextureDesc CreateReflectionTextureDesc(RenderTexture reflectionTexture, string name)
        {
            RenderTextureDescriptor descriptor = reflectionTexture.descriptor;
            GraphicsFormat colorFormat = GetReflectionColorFormat(reflectionTexture, descriptor);
            TextureDesc textureDesc = new TextureDesc(
                Mathf.Max(1, descriptor.width),
                Mathf.Max(1, descriptor.height));
            textureDesc.name = name;
            textureDesc.format = colorFormat;
            textureDesc.dimension = descriptor.dimension;
            textureDesc.slices = Mathf.Max(1, descriptor.volumeDepth);
            textureDesc.depthBufferBits = 0;
            textureDesc.msaaSamples = MSAASamples.None;
            textureDesc.clearBuffer = false;
            textureDesc.filterMode = FilterMode.Bilinear;
            textureDesc.wrapMode = TextureWrapMode.Clamp;
            textureDesc.bindTextureMS = false;
            textureDesc.useDynamicScale = descriptor.useDynamicScale;
            textureDesc.useDynamicScaleExplicit = descriptor.useDynamicScaleExplicit;
            textureDesc.vrUsage = descriptor.vrUsage;
            return textureDesc;
        }

        private static GraphicsFormat GetReflectionColorFormat(RenderTexture reflectionTexture, RenderTextureDescriptor descriptor)
        {
            GraphicsFormat colorFormat = reflectionTexture.graphicsFormat != GraphicsFormat.None
                ? reflectionTexture.graphicsFormat
                : descriptor.graphicsFormat;
            return colorFormat != GraphicsFormat.None
                ? colorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static void ApplyMaterialProperties(Material material, HoPlanarReflectionRendererFeature.Settings settings)
        {
            HoPlanarReflectionShaderParams.ApplyMaterial(material, settings, Vector4.one);
        }
    }
}
