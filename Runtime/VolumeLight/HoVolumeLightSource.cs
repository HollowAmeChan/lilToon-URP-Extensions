using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.VolumeLight
{
    public enum HoVolumeLightQuality { SD, HD }

    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("Rendering/Ho-VolumeLight 局部体积光")]
    public sealed class HoVolumeLightSource : MonoBehaviour
    {
        internal static readonly List<HoVolumeLightSource> Active = new();
        [InspectorName("质量"), Tooltip("SD 仅范围与相机深度；HD 每相机捕获独立光源深度，支持材质 ShadowCaster/Cutout。")]
        public HoVolumeLightQuality quality = HoVolumeLightQuality.HD;
        [Min(0), InspectorName("体积亮度")] public float intensity = 8;
        [ColorUsage(false, true), InspectorName("体积染色")] public Color tint = Color.white;
        [Range(16, 256), InspectorName("采样步数")] public int stepCount = 64;
        [Range(128, 2048), InspectorName("HD 每面分辨率")] public int shadowResolution = 512;
        [Range(0, 1), InspectorName("阴影强度")] public float shadowStrength = 1;
        [Min(.01f), InspectorName("光源柔化半径"), Tooltip("限制接近光源时的反平方亮斑，不改变 Light.range。")]
        public float sourceRadius = .25f;
        [Range(0, 1), InspectorName("范围软边")] public float edgeSoftness = .15f;
        [InspectorName("采样抖动"), Tooltip("蓝噪声打散规律条纹；此局部版本不使用时域历史。")]
        public bool jitter = true;
        public string Status { get; internal set; } = "等待 Ho-VolumeLight Feature";
        internal Light Source => GetComponent<Light>();
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); Status = "已关闭"; }
        private void OnDestroy() => Active.Remove(this);
    }
}
