using System;
using UnityEngine;
namespace lilToon.URP.Extensions.RTDI
{
    [Serializable]
    public sealed class HoRTDISettings
    {
        public bool enabled = true;
        public bool inSceneView = true;
        [Tooltip("局部灯初始候选数；存在方向灯时，另从方向灯分区抽取一个候选。")]
        [Range(1,32)] public int initialSamples = 8;
        public bool temporal = true;
        [Range(1,32)] public int maxHistory = 20;
        public bool spatial = true;
        [Range(1,16)] public int spatialSamples = 4;
        [Range(1,32)] public float spatialRadius = 16;
        [Min(.00001f)] public float rayBias = .001f;
        public LayerMask geometryLayers = -1;
        public LayerMask lightLayers = -1;
    }
}
