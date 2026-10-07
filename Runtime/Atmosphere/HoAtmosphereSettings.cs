using System;
using UnityEngine;

namespace lilToon.URP.Extensions.Atmosphere
{
    public enum HoAtmosphereSource
    {
        [InspectorName("场景天空盒")] SceneSkybox,
        [InspectorName("指定天空盒材质")] SkyboxMaterial,
        [InspectorName("原始 HDR Cubemap")] RadianceCubemap,
        [InspectorName("常量环境")] Constant
    }

    public enum HoAtmosphereUpdate
    {
        [InspectorName("来源变化时")] OnChange,
        [InspectorName("每帧")] EveryFrame,
        [InspectorName("手动刷新")] Manual
    }

    [Serializable]
    public sealed class HoAtmosphereSettings
    {
        [Tooltip("向本相机的后续 pass 提供环境输入；不改变主相机天空背景。")]
        public bool enabled = true;
        public HoAtmosphereSource source = HoAtmosphereSource.SceneSkybox;
        public Material skyboxMaterial;
        [Tooltip("原始环境辐射；RGBM 编码请通过天空盒材质输入，由原 shader 解码。")]
        public Cubemap radianceCubemap;
        [ColorUsage(false, true)] public Color constantColor = Color.black;
        [Min(0)] public float intensity = 1;
        [ColorUsage(false, true)] public Color tint = Color.white;
        [Tooltip("世界空间水平旋转（度）；正 90 度将原 +Z 内容移到 +X。")]
        public float rotationY;
        [Tooltip("材质捕获的每面分辨率，取二次幂；直接 Cubemap 与常量来源忽略此项。")]
        [Range(16, 1024)] public int resolution = 256;
        [Tooltip("时间或任意 shader global 驱动的动态天空使用每帧更新，或显式刷新。")]
        public HoAtmosphereUpdate update = HoAtmosphereUpdate.OnChange;
        public bool inSceneView = true;
    }
}
