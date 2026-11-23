using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTGI
{
    [Serializable]
    public sealed class HoRTGIMaterialArrayParameter : VolumeParameter<Material[]>
    {
        public HoRTGIMaterialArrayParameter(Material[] value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(Material[] from, Material[] to, float t) => value = t > 0.0f ? to : from;
    }

    public enum HoRTGIDebugMode { Off, Diffuse, Receiver, RawDiffuse, All=255 }
    [Serializable]
    public sealed class HoRTGIDebugModeParameter : VolumeParameter<HoRTGIDebugMode>
    {
        public HoRTGIDebugModeParameter(HoRTGIDebugMode value) : base(value) { }
        public override void Interp(HoRTGIDebugMode from,HoRTGIDebugMode to,float t) => value=t>0?to:from;
    }
    [Serializable,VolumeComponentMenu(HoVolumeNames.RTGI)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.RTGI)]
#endif
    public sealed class HoRTGIVolume : VolumeComponent,IPostProcessComponent
    {
        public HoRTGIVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.RTGI;
#endif
        }

        [Tooltip("未覆盖时沿用 Feature 默认值；画面合成由 Ho ScreenProcess 的 RTGI 图层控制。")]
        [InspectorName("启用")] public BoolParameter enable=new BoolParameter(true);
        // Legacy serialized fields retained for old Volume assets. Camera-color
        // ownership now belongs to ScreenProcess RTGI layers.
        [HideInInspector] public BoolParameter composite=new BoolParameter(false);
        [HideInInspector] public MinFloatParameter intensity=new MinFloatParameter(.25f,0);
        [InspectorName("降噪")] public BoolParameter denoise=new BoolParameter(false);
        [InspectorName("排除 Ray 材质")]
        [Tooltip("通常填入本场景天空球材质，避免天空壳参与 RTGI 求交。")]
        public HoRTGIMaterialArrayParameter excludedRayMaterials = new HoRTGIMaterialArrayParameter(Array.Empty<Material>());
        [Tooltip("查看独立间接光产品和接收域；调试不改变 RTGI 产品生产。")]
        [InspectorName("调试模式")] public HoRTGIDebugModeParameter debugMode=new HoRTGIDebugModeParameter(HoRTGIDebugMode.Off);
        [InspectorName("调试曝光（EV）")] public ClampedFloatParameter debugExposure = new ClampedFloatParameter(0, -24, 24);
        [InspectorName("调试 Reinhard 映射")] public BoolParameter debugTonemap = new BoolParameter(false);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>enable.value;
        public bool IsTileCompatible()=>false;
    }
}
