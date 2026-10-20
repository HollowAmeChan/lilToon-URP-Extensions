using System;
using UnityEngine;

namespace lilToon.URP.Extensions.RTGI
{
    [Serializable]
    public sealed class HoRTGISettings
    {
        public bool enabled = true;
        public bool inSceneView = true;
        [Tooltip("在透明物前向有效接收面叠加GI；Volume可覆盖开关和强度。")]
        public bool composite;
        [Min(0)] public float intensity=.25f;
        public bool denoise;
        [Range(1,32)] public int secondaryLightSamples = 8;
        public bool temporal = true;
        [Range(1,32)] public int maxHistory = 20;
        public bool spatial = true;
        [Range(1,16)] public int spatialSamples = 4;
        [Range(1,32)] public float spatialRadius = 16;
        [Min(.00001f)] public float rayBias = .001f;
        [Min(1)] public float maxDistance = 1000;
        public bool useEnvironment = true;
        // With RTDI, direct sky belongs to DI. Enable explicitly for GI-only sky.
        public bool includeDirectEnvironment;
        [Range(0,32)] public int secondaryEnvironmentSamples = 4;
        public LayerMask geometryLayers = -1;
        [Tooltip("使用这些材质的子网格不参与GI世界求交，可排除合并模型内的天空壳；光线穿过它们，不改变原画面绘制。")]
        public Material[] excludedRayMaterials=Array.Empty<Material>();
        public LayerMask lightLayers = -1;
    }
}
