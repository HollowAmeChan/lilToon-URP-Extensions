using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
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

        public bool IsActive => enabled && intensity > 0.0f;
    }
}
