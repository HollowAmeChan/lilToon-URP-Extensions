using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Atmosphere
{
    public enum HoAtmosphereDebugMode { Off, Radiance, Directions, Availability, All = 255 }

    [Serializable]
    public sealed class HoAtmosphereDebugModeParameter : VolumeParameter<HoAtmosphereDebugMode>
    {
        public HoAtmosphereDebugModeParameter(HoAtmosphereDebugMode value, bool state = false) : base(value, state) { }
        public override void Interp(HoAtmosphereDebugMode from, HoAtmosphereDebugMode to, float t) => value = t > 0 ? to : from;
    }

    [Serializable, VolumeComponentMenu("Post-processing/HoAtmosphere/环境输入")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoAtmosphereVolume : VolumeComponent, IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable = new BoolParameter(true);
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
