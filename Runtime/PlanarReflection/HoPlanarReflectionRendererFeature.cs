using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        }

        private static readonly List<HoPlanarReflectionRendererFeature> ActiveFeatures =
            new List<HoPlanarReflectionRendererFeature>();

        private static bool registered;

        [SerializeField]
        private Settings settings = new Settings();

        public Settings FeatureSettings => settings;

        public override void Create()
        {
            RegisterFeature(this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Reflection rendering is driven from beginCameraRendering so the mirror camera renders before the source camera.
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterFeature(this);
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
            if (camera == null || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
            {
                return;
            }

            HoPlanarReflectionRendererFeature feature = ResolveFeature(camera);
            if (feature == null)
            {
                HoPlanarReflectionRenderStats skippedStats = HoPlanarReflectionSurface.RenderAllSurfaces(
                    context,
                    camera,
                    new HoPlanarReflectionRenderSettings(false, false, false, 0));
                HoPlanarReflectionRuntimeDiagnostics.Publish(camera, skippedStats);
                return;
            }

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
            return new HoPlanarReflectionRenderSettings(
                activeSettings.enabled,
                activeSettings.renderGameView,
                activeSettings.renderSceneView,
                activeSettings.maxSurfacesPerCamera);
        }
    }
}
