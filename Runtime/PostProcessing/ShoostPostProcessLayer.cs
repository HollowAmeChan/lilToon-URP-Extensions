using System;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    [Serializable]
    public sealed class ShoostPostProcessLayer
    {
        [Tooltip("显示在 Volume 图层列表里的名称。")]
        public string name = "Post Process Layer";

        [Tooltip("不从列表移除，但跳过这个图层。")]
        public bool enabled = true;

        [Tooltip("这个图层对应的 Shoost 效果。Custom Material 会直接使用材质或 Shader 覆盖。")]
        public ShoostPostProcessEffect effect = ShoostPostProcessEffect.CustomMaterial;

        [Tooltip("关闭后，Scene 视图相机会跳过这个图层。")]
        public bool showInSceneView = true;

        [Tooltip("可选材质覆盖。移植新 Shader 时最安全。")]
        public Material materialOverride;

        [Tooltip("可选 Shader 覆盖。运行时会为它创建并缓存材质。")]
        public Shader shaderOverride;

        [Tooltip("这个图层使用的 Shader Pass 索引。")]
        [Min(0)]
        public int passIndex;

        [Tooltip("图层强度。移植的 Shader 应该读取 _Intensity。")]
        [Range(0.0f, 1.0f)]
        public float intensity = 1.0f;

        [Tooltip("混合模式。会以 _LayerBlendMode 暴露给 Shader。")]
        public ShoostPostProcessBlendMode blendMode = ShoostPostProcessBlendMode.Normal;

        [Tooltip("插入位置。Effect Default 会尽量沿用 Shoost / PPS v2 的默认顺序。")]
        public ShoostPostProcessInjectionPoint injectionPoint = ShoostPostProcessInjectionPoint.EffectDefault;

        [Tooltip("颜色参数。会以 _LayerColor 暴露给 Shader。")]
        public Color color = Color.white;

        [Tooltip("可选纹理。会以 _LayerTexture 暴露给 Shader。")]
        public Texture texture;

        [Tooltip("通用参数向量 0。会以 _LayerParams0 暴露给 Shader。")]
        public Vector4 parameters0;

        [Tooltip("通用参数向量 1。会以 _LayerParams1 暴露给 Shader。")]
        public Vector4 parameters1;

        [Tooltip("通用参数向量 2。会以 _LayerParams2 暴露给 Shader。")]
        public Vector4 parameters2;

        [Tooltip("通用参数向量 3。会以 _LayerParams3 暴露给 Shader。")]
        public Vector4 parameters3;

        [Tooltip("通用参数向量 4。会以 _LayerParams4 暴露给 Shader。")]
        public Vector4 parameters4;

        [Tooltip("通用参数向量 5。会以 _LayerParams5 暴露给 Shader。")]
        public Vector4 parameters5;

        [Tooltip("通用参数向量 6。会以 _LayerParams6 暴露给 Shader。")]
        public Vector4 parameters6;

        [Tooltip("通用参数向量 7。会以 _LayerParams7 暴露给 Shader。")]
        public Vector4 parameters7;

        [Tooltip("通用参数向量 8。会以 _LayerParams8 暴露给 Shader。")]
        public Vector4 parameters8;

        [Tooltip("通用参数向量 9。会以 _LayerParams9 暴露给 Shader。")]
        public Vector4 parameters9;

        [Tooltip("通用参数向量 10。会以 _LayerParams10 暴露给 Shader。")]
        public Vector4 parameters10;

        [Tooltip("通用参数向量 11。会以 _LayerParams11 暴露给 Shader。")]
        public Vector4 parameters11;

        [Tooltip("通用参数向量 12。会以 _LayerParams12 暴露给 Shader。")]
        public Vector4 parameters12;

        public bool IsActive => enabled && intensity > 0.0f;
    }
}
