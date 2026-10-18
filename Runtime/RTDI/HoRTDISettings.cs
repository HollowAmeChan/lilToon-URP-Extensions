using System;
using UnityEngine;
namespace lilToon.URP.Extensions.RTDI
{
    public enum HoRTDILocalSampling { [InspectorName("均匀")] Uniform, [InspectorName("功率")] Power }
    public enum HoRTDIFinalSampling { [InspectorName("单样本")] Single, [InspectorName("三样本（增加可见性开销）")] TripleAnalytic }
    [Serializable]
    public sealed class HoRTDISettings
    {
        public bool enabled = true;
        public bool inSceneView = true;
        [Tooltip("在透明物绘制前，用参考 PBR 的直接光与 SB 自发光替换有效不透明接收面的旧照明。天空、无效接收面与之后的透明物保留；不使用原 NPR ramp、间接光或反射探针。")]
        public bool composite;
        [Tooltip("原画面与参考 PBR 合成结果的过渡权重；1 完全替换旧照明，0 保留原画面。不是额外光照强度。")]
        [Range(0,1)] public float compositeWeight=1;
        [Tooltip("NRD RELAX diffuse/specular；Mono 与同分辨率 GB/SB。几何与光源变化通过历史失效和可见性 confidence 处理，保留原始光照输出以便对照。")]
        public bool denoise;
        [Tooltip("从前置 Ho-Atmosphere 借用天空 HDR，作为 RTXDI 环境光候选。")]
        public bool useEnvironment = true;
        [Tooltip("仅缩放 RTDI 的天空照明，不改变可见天空、Ho-Atmosphere 或解析灯。1 保持原效果，0 关闭天空采样；调整会重置相关历史。")]
        [Min(0)] public float environmentIntensity = 1;
        internal float EffectiveEnvironmentIntensity=>float.IsFinite(environmentIntensity)?Mathf.Max(0,environmentIntensity):0;
        [Range(1,32)] public int environmentSamples = 4;
        [Tooltip("环境 BRDF 提案；余弦 diffuse / GGX VNDF 与天空 PDF 使用 SDK MIS。0 可用于对照。")]
        [Range(0,32)] public int environmentBrdfSamples = 4;
        [Tooltip("均匀模式用于低开销与对照；功率模式使用 SDK PDF mip / RIS 预采样，降低强弱灯功率跨度造成的方差。")]
        public HoRTDILocalSampling localSampling = HoRTDILocalSampling.Uniform;
        [Tooltip("局部灯初始候选数；存在方向灯时，另从方向灯分区抽取一个候选。")]
        [Range(1,32)] public int initialSamples = 8;
        [Tooltip("三样本增加两次解析灯最终可见性查询，降低动态阴影与多灯噪声；默认单样本。")]
        public HoRTDIFinalSampling finalSampling=HoRTDIFinalSampling.Single;
        public bool temporal = true;
        [Tooltip("ReSTIR 历史样本预算相对当前候选数的上限，不是 NRD 的历史帧数。推荐从 4 开始；长历史可能放大局部高光的方差，可按场景调整。")]
        [Range(1,32)] public int maxHistory = 4;
        public bool spatial = true;
        [Range(1,16)] public int spatialSamples = 4;
        [Range(1,32)] public float spatialRadius = 16;
        [Min(.00001f)] public float rayBias = .001f;
        public LayerMask geometryLayers = -1;
        [Tooltip("使用这些材质的子网格不参与 RTDI 世界求交，可用于合并模型中的天空壳。灯光会穿过被排除的子网格，不改变主画面绘制。")]
        public Material[] excludedRayMaterials=Array.Empty<Material>();
        public LayerMask lightLayers = -1;
    }
}
