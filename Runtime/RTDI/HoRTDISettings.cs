using System;
using UnityEngine;
namespace lilToon.URP.Extensions.RTDI
{
    public enum HoRTDILocalSampling { [InspectorName("均匀")] Uniform, [InspectorName("功率")] Power }
    [Serializable]
    public sealed class HoRTDISettings
    {
        public bool enabled = true;
        public bool inSceneView = true;
        [Tooltip("NRD RELAX diffuse/specular；当前仅支持静态几何、Mono 与同分辨率 GB/SB。保留原始光照输出以便对照。")]
        public bool denoise;
        [Tooltip("均匀模式用于低开销与对照；功率模式使用 SDK PDF mip / RIS 预采样，降低强弱灯功率跨度造成的方差。")]
        public HoRTDILocalSampling localSampling = HoRTDILocalSampling.Uniform;
        [Tooltip("局部灯初始候选数；存在方向灯时，另从方向灯分区抽取一个候选。")]
        [Range(1,32)] public int initialSamples = 8;
        public bool temporal = true;
        [Tooltip("ReSTIR 历史样本预算相对当前候选数的上限，不是 NRD 的历史帧数。推荐从 4 开始；长历史可能放大局部高光的方差，可按场景调整。")]
        [Range(1,32)] public int maxHistory = 4;
        public bool spatial = true;
        [Range(1,16)] public int spatialSamples = 4;
        [Range(1,32)] public float spatialRadius = 16;
        [Min(.00001f)] public float rayBias = .001f;
        public LayerMask geometryLayers = -1;
        public LayerMask lightLayers = -1;
    }
}
