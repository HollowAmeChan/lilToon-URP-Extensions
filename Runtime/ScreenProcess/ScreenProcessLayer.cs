using System;
using UnityEngine;
using lilToon.URP.Extensions.AttributeComposite;

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

        [Tooltip("选择本层使用的 AC 覆盖率来源。")]
        public HoACQueryKind maskSource = HoACQueryKind.TotalCoverage;
        [Tooltip("HoSemanticSchema 中的稳定语义名，例如 Face、FrontHair、Eye。")]
        public string maskSemanticName = "CharacterFull";
        [Tooltip("组查询使用 1..255；完整身份为 (组 ID << 8) | 部件槽位。")]
        public int maskId = 1;
        [Tooltip("限定选择和反选的范围；全屏反选可用于排除角色。")]
        public HoACMaskDomain maskDomain = HoACMaskDomain.Screen;

        [Tooltip("在所选范围内排除命中的覆盖率。输入缺失时返回零。")]
        [UnityEngine.Serialization.FormerlySerializedAs("invert" + "Rule" + "Mask")]
        public bool invertMask;

        [Tooltip("Replace this layer output with the resolved ScreenProcess mask for debugging.")]
                [UnityEngine.Serialization.FormerlySerializedAs("debug" + "Rule" + "Mask")]
        public bool debugMask;

        public bool IsActive => enabled && intensity > 0.0001f;

        [NonSerialized] private bool queryCached;
        [NonSerialized] private HoACQueryKind cachedSource;
        [NonSerialized] private HoACMaskDomain cachedDomain;
        [NonSerialized] private string cachedName;
        [NonSerialized] private int cachedId;
        [NonSerialized] private HoACQueryDescriptor cachedQuery;

        internal HoACQueryDescriptor ResolveMaskQuery()
        {
            if (!queryCached || cachedSource != maskSource || cachedDomain != maskDomain ||
                cachedName != maskSemanticName || cachedId != maskId)
            {
                cachedQuery = HoACQueryDescriptor.Resolve(maskSource, maskSemanticName, maskId, maskDomain);
                cachedSource = maskSource; cachedDomain = maskDomain; cachedName = maskSemanticName; cachedId = maskId;
                queryCached = true;
            }
            return cachedQuery;
        }
    }
}
