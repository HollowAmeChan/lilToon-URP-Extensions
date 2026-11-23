using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Atmosphere
{
    public enum HoAtmosphereDebugMode
    {
        Off, Radiance, Directions, Availability,
        [InspectorName("介质密度切片")] MediumDensity,
        [InspectorName("介质激活切片")] MediumActivation,
        All = 255
    }

    [Serializable]
    public sealed class HoAtmosphereDebugModeParameter : VolumeParameter<HoAtmosphereDebugMode>
    {
        public HoAtmosphereDebugModeParameter(HoAtmosphereDebugMode value, bool state = false) : base(value, state) { }
        public override void Interp(HoAtmosphereDebugMode from, HoAtmosphereDebugMode to, float t) => value = t > 0 ? to : from;
    }

    [Serializable, VolumeComponentMenu(HoVolumeNames.Atmosphere)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.Atmosphere)]
#endif
    public sealed class HoAtmosphereVolume : VolumeComponent, IPostProcessComponent
    {
        public HoAtmosphereVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.Atmosphere;
#endif
        }

        [InspectorName("环境输入启用"), Tooltip("仅覆盖环境辐射输入，不关闭基础大气区域介质。\n\n未覆盖的运行参数沿用 Ho Atmosphere Feature 兜底值。")]
        public BoolParameter enable = new BoolParameter(true);
        public MinFloatParameter intensity = new MinFloatParameter(1, 0);
        public ColorParameter tint = new ColorParameter(Color.white, true, false, false);
        [Tooltip("世界空间水平旋转（度）；未覆盖时使用 feature 默认值。")]
        public FloatParameter rotationY = new FloatParameter(0);
        public HoAtmosphereDebugModeParameter debugMode = new HoAtmosphereDebugModeParameter(HoAtmosphereDebugMode.Off);
        public BoolParameter debugInSceneView = new BoolParameter(true);
        public BoolParameter debugInGameView = new BoolParameter(true);
        public bool IsActive() => enable.value;
        public bool IsTileCompatible() => false;
    }
}
