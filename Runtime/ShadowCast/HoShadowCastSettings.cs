#pragma warning disable CS0618

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    [System.Serializable]
    public sealed class HoShadowCastSettings
    {
        [InspectorName("启用")]
        public bool enabled = true;

        [InspectorName("渲染时机")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPrePasses;

        [InspectorName("Collect Visible Lights")]
        public bool collectVisibleLights = true;

        [InspectorName("Use Active Controller Override")]
        public bool useActiveControllerOverride = true;

        [InspectorName("Caster Layer Mask")]
        public LayerMask casterLayerMask = -1;

        [InspectorName("Light Layer Mask")]
        public LayerMask lightLayerMask = -1;

        [InspectorName("Shadow Strength")]
        [Range(0.0f, 1.0f)]
        public float shadowStrength = 1.0f;

        [InspectorName("Punctual Shadow Strength")]
        [Range(0.0f, 1.0f)]
        public float punctualShadowStrength = 0.5f;

        [InspectorName("Punctual Fade Speed")]
        [Range(0.1f, 4.0f)]
        public float punctualShadowFadeSpeed = 1.0f;

        [Header("PCSS")]
        [InspectorName("Enable PCSS")]
        public bool pcssEnabled = true;

        [InspectorName("PCSS Quality")]
        public HoShadowCastPcssQuality pcssQuality = HoShadowCastPcssQuality.High;

        [InspectorName("Punctual PCSS Softness")]
        [Range(0.0f, 4.0f)]
        public float punctualPcssSoftness = 0.6f;

        [InspectorName("Second Directional PCSS Softness")]
        [Range(0.0f, 4.0f)]
        public float secondDirectionalPcssSoftness = 4.0f;

        [InspectorName("Blocker Search Radius")]
        [Range(0.25f, 8.0f)]
        public float pcssBlockerSearchRadius = 2.8f;

        [InspectorName("Max Penumbra Radius")]
        [Range(1.0f, 32.0f)]
        public float pcssMaxPenumbraRadius = 7.4f;

        [InspectorName("PCSS Depth Bias")]
        [Range(0.0f, 0.01f)]
        public float pcssDepthBias = 0.0f;

        [Header("Second Directional")]
        [InspectorName("Second Directional Strength")]
        [Range(0.0f, 1.0f)]
        public float secondDirectionalShadowStrength = 0.3f;

        [InspectorName("Second Directional Atlas Size")]
        [Min(256)]
        public int secondDirectionalAtlasSize = 4096;

        [InspectorName("Second Directional Cascades")]
        [Range(1, HoShadowCastShaderConstants.MaxSecondDirectionalCascades)]
        public int secondDirectionalCascadeCount = 4;

        [InspectorName("Second Directional Max Distance")]
        [Min(0.01f)]
        public float secondDirectionalMaxDistance = 80.0f;

        [InspectorName("Second Directional Depth")]
        [Min(0.01f)]
        public float secondDirectionalShadowDepth = 80.0f;

        [InspectorName("Second Directional Cascade Splits")]
        public Vector3 secondDirectionalCascadeSplits = new Vector3(0.08f, 0.22f, 0.5f);

        [Header("Atlas")]
        [InspectorName("Atlas Size")]
        [Min(256)]
        public int atlasSize = 4096;

        [InspectorName("Directional Resolution")]
        [Min(64)]
        public int directionalResolution = 1024;

        [InspectorName("Spot Resolution")]
        [Min(64)]
        public int spotResolution = 512;

        [InspectorName("Point Face Resolution")]
        [Min(64)]
        public int pointFaceResolution = 512;

        [InspectorName("Directional Near Plane")]
        [Min(0.001f)]
        public float directionalNearPlane = 0.1f;

        [InspectorName("Directional Shadow Size")]
        [Min(0.01f)]
        public float directionalShadowSize = 20.0f;

        [InspectorName("Directional Shadow Depth")]
        [Min(0.01f)]
        public float directionalShadowDepth = 40.0f;

        [InspectorName("Debug Mode")]
        public HoShadowCastDebugMode debugMode = HoShadowCastDebugMode.Off;

        public void Validate()
        {
            shadowStrength = Mathf.Clamp01(shadowStrength);
            punctualShadowStrength = Mathf.Clamp01(punctualShadowStrength);
            punctualShadowFadeSpeed = punctualShadowFadeSpeed <= 0.0f ? 1.0f : Mathf.Clamp(punctualShadowFadeSpeed, 0.1f, 4.0f);
            pcssQuality = (HoShadowCastPcssQuality)Mathf.Clamp((int)pcssQuality, 0, 3);
            punctualPcssSoftness = Mathf.Clamp(punctualPcssSoftness, 0.0f, 4.0f);
            secondDirectionalPcssSoftness = Mathf.Clamp(secondDirectionalPcssSoftness, 0.0f, 4.0f);
            pcssBlockerSearchRadius = Mathf.Clamp(pcssBlockerSearchRadius, 0.25f, 8.0f);
            pcssMaxPenumbraRadius = Mathf.Clamp(pcssMaxPenumbraRadius, 1.0f, 32.0f);
            pcssDepthBias = Mathf.Clamp(pcssDepthBias, 0.0f, 0.01f);
            secondDirectionalShadowStrength = Mathf.Clamp01(secondDirectionalShadowStrength);
            secondDirectionalAtlasSize = Mathf.Max(256, secondDirectionalAtlasSize);
            secondDirectionalCascadeCount = Mathf.Clamp(secondDirectionalCascadeCount, 1, HoShadowCastShaderConstants.MaxSecondDirectionalCascades);
            secondDirectionalMaxDistance = Mathf.Max(0.01f, secondDirectionalMaxDistance);
            secondDirectionalShadowDepth = Mathf.Max(0.01f, secondDirectionalShadowDepth);
            secondDirectionalCascadeSplits = ClampCascadeSplits(secondDirectionalCascadeSplits);
            atlasSize = Mathf.Max(256, atlasSize);
            directionalResolution = Mathf.Max(64, directionalResolution);
            spotResolution = Mathf.Max(64, spotResolution);
            pointFaceResolution = Mathf.Max(64, pointFaceResolution);
            directionalNearPlane = Mathf.Max(0.001f, directionalNearPlane);
            directionalShadowSize = Mathf.Max(0.01f, directionalShadowSize);
            directionalShadowDepth = Mathf.Max(0.01f, directionalShadowDepth);
        }

        private static Vector3 ClampCascadeSplits(Vector3 splits)
        {
            float x = Mathf.Clamp(splits.x, 0.001f, 0.997f);
            float y = Mathf.Clamp(splits.y, x + 0.001f, 0.998f);
            float z = Mathf.Clamp(splits.z, y + 0.001f, 0.999f);
            return new Vector3(x, y, z);
        }
    }

    internal sealed class HoShadowCastFrameConfig
    {
        public bool collectVisibleLights;
        public bool usingControllerOverride;
        public HoShadowCastController controller;
        public Light[] directionalLights;
        public Light[] spotLights;
        public Light[] pointLights;
        public LayerMask casterLayerMask;
        public LayerMask lightLayerMask;
        public float shadowStrength;
        public float punctualShadowStrength;
        public float punctualShadowFadeSpeed;
        public bool pcssEnabled;
        public HoShadowCastPcssQuality pcssQuality;
        public float punctualPcssSoftness;
        public float secondDirectionalPcssSoftness;
        public float pcssBlockerSearchRadius;
        public float pcssMaxPenumbraRadius;
        public float pcssDepthBias;
        public float secondDirectionalShadowStrength;
        public int secondDirectionalAtlasSize;
        public int secondDirectionalCascadeCount;
        public float secondDirectionalMaxDistance;
        public float secondDirectionalShadowDepth;
        public Vector3 secondDirectionalCascadeSplits;
        public int atlasSize;
        public int directionalResolution;
        public int spotResolution;
        public int pointFaceResolution;
        public float directionalNearPlane;
        public float directionalShadowSize;
        public float directionalShadowDepth;
        public Vector3 directionalAnchorPosition;
        public HoShadowCastDebugMode debugMode;

        public static HoShadowCastFrameConfig Resolve(HoShadowCastSettings settings)
        {
            HoShadowCastController controller = settings != null && settings.useActiveControllerOverride
                ? HoShadowCastController.ActiveController
                : null;
            var config = new HoShadowCastFrameConfig();
            config.ApplySettings(settings);
            if (controller != null)
            {
                config.ApplyController(controller);
            }

            return config;
        }

        private void ApplySettings(HoShadowCastSettings settings)
        {
            if (settings == null)
            {
                settings = new HoShadowCastSettings();
            }

            settings.Validate();
            collectVisibleLights = settings.collectVisibleLights;
            usingControllerOverride = false;
            controller = null;
            directionalLights = null;
            spotLights = null;
            pointLights = null;
            casterLayerMask = settings.casterLayerMask;
            lightLayerMask = settings.lightLayerMask;
            shadowStrength = settings.shadowStrength;
            punctualShadowStrength = settings.punctualShadowStrength;
            punctualShadowFadeSpeed = settings.punctualShadowFadeSpeed;
            pcssEnabled = settings.pcssEnabled;
            pcssQuality = settings.pcssQuality;
            punctualPcssSoftness = settings.punctualPcssSoftness;
            secondDirectionalPcssSoftness = settings.secondDirectionalPcssSoftness;
            pcssBlockerSearchRadius = settings.pcssBlockerSearchRadius;
            pcssMaxPenumbraRadius = settings.pcssMaxPenumbraRadius;
            pcssDepthBias = settings.pcssDepthBias;
            secondDirectionalShadowStrength = settings.secondDirectionalShadowStrength;
            secondDirectionalAtlasSize = settings.secondDirectionalAtlasSize;
            secondDirectionalCascadeCount = settings.secondDirectionalCascadeCount;
            secondDirectionalMaxDistance = settings.secondDirectionalMaxDistance;
            secondDirectionalShadowDepth = settings.secondDirectionalShadowDepth;
            secondDirectionalCascadeSplits = settings.secondDirectionalCascadeSplits;
            atlasSize = settings.atlasSize;
            directionalResolution = settings.directionalResolution;
            spotResolution = settings.spotResolution;
            pointFaceResolution = settings.pointFaceResolution;
            directionalNearPlane = settings.directionalNearPlane;
            directionalShadowSize = settings.directionalShadowSize;
            directionalShadowDepth = settings.directionalShadowDepth;
            directionalAnchorPosition = Vector3.zero;
            debugMode = settings.debugMode;
        }

        private void ApplyController(HoShadowCastController source)
        {
            usingControllerOverride = true;
            controller = source;
            collectVisibleLights = false;
            directionalLights = source.directionalLights;
            spotLights = source.spotLights;
            pointLights = source.pointLights;
            casterLayerMask = source.casterLayerMask;
            shadowStrength = source.shadowStrength;
            punctualShadowStrength = source.punctualShadowStrength;
            punctualShadowFadeSpeed = source.punctualShadowFadeSpeed;
            pcssEnabled = source.pcssEnabled;
            pcssQuality = source.pcssQuality;
            punctualPcssSoftness = source.punctualPcssSoftness;
            secondDirectionalPcssSoftness = source.secondDirectionalPcssSoftness;
            pcssBlockerSearchRadius = source.pcssBlockerSearchRadius;
            pcssMaxPenumbraRadius = source.pcssMaxPenumbraRadius;
            pcssDepthBias = source.pcssDepthBias;
            secondDirectionalShadowStrength = source.secondDirectionalShadowStrength;
            secondDirectionalAtlasSize = source.secondDirectionalAtlasSize;
            secondDirectionalCascadeCount = source.secondDirectionalCascadeCount;
            secondDirectionalMaxDistance = source.secondDirectionalMaxDistance;
            secondDirectionalShadowDepth = source.secondDirectionalShadowDepth;
            secondDirectionalCascadeSplits = source.secondDirectionalCascadeSplits;
            atlasSize = source.atlasSize;
            directionalResolution = source.directionalResolution;
            spotResolution = source.spotResolution;
            pointFaceResolution = source.pointFaceResolution;
            directionalNearPlane = source.directionalNearPlane;
            directionalShadowSize = source.directionalShadowSize;
            directionalShadowDepth = source.directionalShadowDepth;
            directionalAnchorPosition = source.transform.position;
            debugMode = source.debugMode;
        }
    }
}
