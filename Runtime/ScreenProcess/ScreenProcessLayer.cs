using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ScreenProcessLayer
    {
        [Tooltip("Display name in the ScreenProcess stack.")]
        public string name = "ScreenProcess Layer";

        [Tooltip("Keep the layer in the stack, but skip it at runtime.")]
        public bool enabled = true;

        [Tooltip("The ScreenProcess effect slot represented by this layer.")]
        public ScreenProcessEffect effect = ScreenProcessEffect.EdgeLight;

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

        [Tooltip("Blend mode hint for ScreenProcess effects. The shader reads _LayerBlendMode.")]
        public ScreenProcessBlendMode blendMode = ScreenProcessBlendMode.Add;

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

        [Tooltip("Scene object used by ScreenProcess Depth Of Field target focus mode.")]
        public Transform depthOfFieldFocusTarget;

        [Tooltip("Fallback scene hierarchy path used when a Volume Profile asset cannot keep a scene object reference.")]
        public string depthOfFieldFocusTargetPath;

        [Tooltip("Additional camera-space distance offset added to the Depth Of Field focus target.")]
        public float depthOfFieldFocusOffset;

        [Tooltip("Use the character coverage mask (AC total coverage, from OB) as this layer mask.")]
        // The retired field name is split into literals on purpose: the serialized data still needs it,
        // but the old identifier must not reappear in a plain-text search of the tree.
        [UnityEngine.Serialization.FormerlySerializedAs("use" + "Rule" + "Mask")]
        public bool useMask;

        [Tooltip("Invert the resolved ScreenProcess mask within covered character pixels.")]
        [UnityEngine.Serialization.FormerlySerializedAs("invert" + "Rule" + "Mask")]
        public bool invertMask;

        [Tooltip("Replace this layer output with the resolved ScreenProcess mask for debugging.")]
                [UnityEngine.Serialization.FormerlySerializedAs("debug" + "Rule" + "Mask")]
        public bool debugMask;

        public bool IsActive => enabled && intensity > 0.0001f;
    }
}
