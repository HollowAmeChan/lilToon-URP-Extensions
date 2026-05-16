using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    public enum HoPostAovSource
    {
        Mask = 0,
        GroupId = 1,
        ObjectId = 2,
        Flags = 3,
        Thickness = 4,
        Curvature = 5,
        Material = 6,
        Utility = 7,
        Custom0 = 8,
        Custom1 = 9,
        Custom2 = 10,
        Custom3 = 11
    }

    public enum HoPostAovMaskMode
    {
        Direct = 0,
        Threshold = 1,
        MatchValue = 2,
        MatchColor = 3
    }

    [Serializable]
    public sealed class HoPostProcessLayer
    {
        [Tooltip("Display name in the HoPost stack.")]
        public string name = "HoPost Layer";

        [Tooltip("Keep the layer in the stack, but skip it at runtime.")]
        public bool enabled = true;

        [Tooltip("The HoPost effect slot represented by this layer.")]
        public HoPostProcessEffect effect = HoPostProcessEffect.EdgeLight;

        [Tooltip("Optional material override. Used for experiments or custom passes.")]
        public Material materialOverride;

        [Tooltip("Optional shader override. Runtime creates and caches a material for it.")]
        public Shader shaderOverride;

        [Tooltip("Shader pass index used by this layer.")]
        [Min(0)]
        public int passIndex;

        [Tooltip("Layer intensity. The shader reads _Intensity.")]
        [Range(0.0f, 1.0f)]
        public float intensity = 1.0f;

        [Tooltip("Blend mode hint for HoPost effects. The shader reads _LayerBlendMode.")]
        public HoPostProcessBlendMode blendMode = HoPostProcessBlendMode.Add;

        [Tooltip("Primary color. EdgeLight and other HDR subject effects should treat this as HDR.")]
        public Color color = Color.white;

        [Tooltip("Optional layer texture. The shader reads _LayerTexture and _LayerTextureEnabled.")]
        public Texture texture;

        public Vector4 parameters0;
        public Vector4 parameters1;
        public Vector4 parameters2;
        public Vector4 parameters3;
        public Vector4 parameters4;
        public Vector4 parameters5;

        [Tooltip("Use HoAOV data as a per-layer mask.")]
        public bool useAovMask;

        [Tooltip("HoAOV channel sampled by this layer mask.")]
        public HoPostAovSource aovSource = HoPostAovSource.Mask;

        [Tooltip("How the sampled HoAOV data is converted into a mask.")]
        public HoPostAovMaskMode aovMaskMode = HoPostAovMaskMode.Direct;

        [Tooltip("Threshold, tolerance, or match width used by the HoAOV mask.")]
        [Min(0.0f)]
        public float aovThreshold = 0.5f;

        [Tooltip("Soft transition width for threshold and match modes.")]
        [Min(0.0001f)]
        public float aovSoftness = 0.02f;

        [Tooltip("Numeric value used by Match Value mode. ID sources encode this value before comparison.")]
        public float aovMatchValue;

        [Tooltip("Color used by Match Color mode.")]
        public Color aovMatchColor = Color.white;

        [Tooltip("Invert the resolved HoAOV mask within covered HoAOV pixels.")]
        public bool invertAovMask;

        [Tooltip("Replace this layer output with the resolved HoAOV mask for debugging.")]
        public bool debugAovMask;

        public bool IsActive => enabled && intensity > 0.0001f;
    }
}
