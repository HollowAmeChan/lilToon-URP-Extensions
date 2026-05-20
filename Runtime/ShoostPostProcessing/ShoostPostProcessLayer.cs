using System;
using System.Collections.Generic;
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

        [Tooltip("颜色参数。会以 _LayerColor 暴露给 Shader。")]
        public Color color = Color.white;

        [Tooltip("可选纹理。会以 _LayerTexture 暴露给 Shader。")]
        public Texture texture;

        [Tooltip("Logo overlay input texture 0.")]
        public Texture logoTexture0;

        [Tooltip("Logo overlay input texture 1.")]
        public Texture logoTexture1;

        [Tooltip("Logo overlay input texture 2.")]
        public Texture logoTexture2;

        [Tooltip("Logo overlay input texture 3.")]
        public Texture logoTexture3;

        [Tooltip("Logo overlay input texture 4.")]
        public Texture logoTexture4;

        [Tooltip("Logo overlay input texture 5.")]
        public Texture logoTexture5;

        [Tooltip("Logo overlay input texture 6.")]
        public Texture logoTexture6;

        [Tooltip("Logo overlay input texture 7.")]
        public Texture logoTexture7;

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

        [Tooltip("使用 HoAOV 数据限制当前图层作用范围。")]
        public bool useAovMask;

        [Tooltip("当前图层遮罩读取的 HoAOV 通道。")]
        public HoPostAovSource aovSource = HoPostAovSource.Mask;

        [Tooltip("把 HoAOV 通道转换成遮罩的方式。")]
        public HoPostAovMaskMode aovMaskMode = HoPostAovMaskMode.Direct;

        [Tooltip("HoAOV 遮罩使用的阈值、容差或匹配宽度。")]
        [Min(0.0f)]
        public float aovThreshold = 0.5f;

        [Tooltip("匹配数值模式使用的目标值。ID 类通道会先编码再比较。")]
        public float aovMatchValue;

        [Tooltip("匹配颜色模式使用的目标颜色。")]
        public Color aovMatchColor = Color.white;

        [Tooltip("在 HoAOV 覆盖范围内反转解析后的遮罩。")]
        public bool invertAovMask;

        [Tooltip("调试时直接输出解析后的 HoAOV 遮罩。")]
        public bool debugAovMask;

        [Tooltip("精细化 HoAOV 遮罩规则列表。运行时最多解析四条规则；为空时会兼容旧的单条 AOV 遮罩字段。")]
        public List<HoPostAovMaskRule> aovRules = new List<HoPostAovMaskRule>();

        public bool IsActive => enabled && intensity > 0.0f;
    }
}
