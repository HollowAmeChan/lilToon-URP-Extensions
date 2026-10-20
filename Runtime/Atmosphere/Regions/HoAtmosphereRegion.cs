using UnityEngine;

namespace lilToon.URP.Extensions.Atmosphere
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/Ho-Atmosphere 大气区域")]
    public sealed class HoAtmosphereRegion : MonoBehaviour
    {
        [InspectorName("区域大小"), Tooltip("局部盒形区域尺寸；世界范围跟随 Transform，允许旋转与缩放。")]
        public Vector3 size = new Vector3(20, 10, 20);
        [Min(0), InspectorName("边缘淡出"), Tooltip("区域内部向边界淡出的世界距离（米）；仅控制显示权重，不改变光束形状或阴影。")]
        public float blendDistance = 1;
        [Range(0, 1), InspectorName("体积光参与"), Tooltip("区域对体积光的显示权重；光源照亮范围与遮挡仍由光束维护。")]
        public float volumeLightWeight = 1;
        [Min(0), InspectorName("介质密度"), Tooltip("每米介质强度；启用路径消光时作为消光系数，散射再乘反照率。")]
        public float density = .02f;
        [ColorUsage(false, true), InspectorName("介质颜色"), Tooltip("介质对照明贡献的线性 HDR 染色。")]
        public Color tint = Color.white;
        [ColorUsage(false), InspectorName("散射反照率"), Tooltip("RGB 各通道被散射的比例（0–1）；剩余部分为吸收。白色保持原有散射。")]
        public Color scatteringAlbedo = Color.white;
        [Range(-.9f, .9f), InspectorName("散射方向性"), Tooltip("正值更偏前向散射，负值更偏后向；由雾与尘粒共同读取。")]
        public float anisotropy = .3f;
        [InspectorName("路径消光"), Tooltip("沿观察路径衰减体积贡献；关闭时保持稀薄加亮近似，不改变背景表面颜色。")]
        public bool pathExtinction;
        [InspectorName("可见灰尘")]
        public HoAtmosphereDustSettings dust = new();
        [System.NonSerialized] private HoAtmosphereDustField dustField;
        internal HoAtmosphereDustField GetDustField() => dustField ??= new HoAtmosphereDustField();
        internal bool HasDustBuffer => dustField != null && dustField.HasBuffer;
        public int PreparedDustCount => dustField != null ? dustField.Count : 0;
        public bool DustCapacityLimited => dustField != null && dustField.CapacityLimited;
        [InspectorName("空间密度纹理"), Tooltip("可选 Texture3D，R 通道调制密度；为空时使用均匀密度，不自动创建资产。")]
        public Texture3D densityTexture;
        [Range(0, 1), InspectorName("纹理扰动强度")]
        public float noiseStrength = 1;
        [Min(.0001f), InspectorName("纹理世界尺度"), Tooltip("世界空间每次纹理重复的长度（米）；移动灯光或区域不会拖动纹理。")]
        public float noiseWorldSize = 5;
        [InspectorName("风速"), Tooltip("密度纹理沿世界方向移动的速度（米/秒）。")]
        public Vector3 wind = new Vector3(.1f, 0, 0);

        private void OnEnable() => HoAtmosphereRegionRegistry.Register(this);
        private void Update() { if (dustField != null && (dust == null || !dust.enabled)) ReleaseDust(); }
        private void OnDisable() { HoAtmosphereRegionRegistry.Unregister(this); ReleaseDust(); }
        private void OnDestroy() { HoAtmosphereRegionRegistry.Unregister(this); ReleaseDust(); }
        internal void ReleaseDust() { dustField?.Dispose(); dustField = null; }
        private void OnValidate()
        {
            size = new Vector3(Positive(size.x, 1), Positive(size.y, 1), Positive(size.z, 1));
            blendDistance = Mathf.Max(0, Finite(blendDistance));
            volumeLightWeight = Mathf.Clamp01(Finite(volumeLightWeight));
            density = Mathf.Max(0, Finite(density));
            scatteringAlbedo = new Color(Mathf.Clamp01(Finite(scatteringAlbedo.r, 1)), Mathf.Clamp01(Finite(scatteringAlbedo.g, 1)), Mathf.Clamp01(Finite(scatteringAlbedo.b, 1)), 1);
            anisotropy = Mathf.Clamp(Finite(anisotropy, .3f), -.9f, .9f);
            dust ??= new HoAtmosphereDustSettings();
            dust.Constrain();
            // Release GPU resources from render/disable lifecycle, not the loading-thread OnValidate callback.
            noiseStrength = Mathf.Clamp01(Finite(noiseStrength));
            noiseWorldSize = Positive(noiseWorldSize, 5);
        }
        internal static float Finite(float value, float fallback = 0) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        private static float Positive(float value, float fallback) => Mathf.Max(.0001f, Mathf.Abs(Finite(value, fallback)));

        public Bounds WorldBounds
        {
            get
            {
                Vector3 half = size * .5f;
                var result = new Bounds(transform.position, Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            result.Encapsulate(transform.TransformPoint(Vector3.Scale(half, new Vector3(x, y, z))));
                return result;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.42f, .72f, .58f, 1);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = previous; Gizmos.color = previousColor;
        }
    }
}
