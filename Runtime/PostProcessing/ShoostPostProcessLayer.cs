using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ShoostPostProcessLayer
    {
        [Tooltip("Name shown in the Volume layer list.")]
        public string name = "Post Process Layer";

        [Tooltip("Skip this layer without removing it from the stack.")]
        public bool enabled = true;

        [Tooltip("Shoost effect slot this layer represents. Custom Material uses the material or shader override directly.")]
        public ShoostPostProcessEffect effect = ShoostPostProcessEffect.CustomMaterial;

        [Tooltip("If disabled, Scene view cameras skip this layer.")]
        public bool showInSceneView = true;

        [Tooltip("Optional material override. This is the safest way to test a newly ported shader.")]
        public Material materialOverride;

        [Tooltip("Optional shader override. A runtime material is created and cached for this shader.")]
        public Shader shaderOverride;

        [Tooltip("Shader pass index used by this layer.")]
        [Min(0)]
        public int passIndex;

        [Tooltip("Common layer strength. Ported shaders should read _Intensity.")]
        [Range(0.0f, 1.0f)]
        public float intensity = 1.0f;

        [Tooltip("Common blend mode value exposed to shaders as _LayerBlendMode.")]
        public ShoostPostProcessBlendMode blendMode = ShoostPostProcessBlendMode.Normal;

        [Tooltip("Where this layer is injected. Effect Default follows the original Shoost/PPS v2 BeforeStack or AfterStack category when known.")]
        public ShoostPostProcessInjectionPoint injectionPoint = ShoostPostProcessInjectionPoint.EffectDefault;

        [Tooltip("Common color exposed to shaders as _LayerColor.")]
        public Color color = Color.white;

        [Tooltip("Optional texture exposed to shaders as _LayerTexture.")]
        public Texture texture;

        [Tooltip("Generic parameter vector exposed as _LayerParams0.")]
        public Vector4 parameters0;

        [Tooltip("Generic parameter vector exposed as _LayerParams1.")]
        public Vector4 parameters1;

        [Tooltip("Generic parameter vector exposed as _LayerParams2.")]
        public Vector4 parameters2;

        [Tooltip("Generic parameter vector exposed as _LayerParams3.")]
        public Vector4 parameters3;

        public bool IsActive => enabled && intensity > 0.0f;
    }
}
