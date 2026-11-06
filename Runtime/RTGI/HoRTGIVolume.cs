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
    [Serializable,VolumeComponentMenu("Post-processing/Ho-RTGI/间接光")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoRTGIVolume : VolumeComponent,IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable=new BoolParameter(true);
        // Legacy serialized fields retained for old Volume assets. Camera-color
        // ownership now belongs to ScreenProcess RTGI layers.
        [HideInInspector] public BoolParameter composite=new BoolParameter(false);
        [HideInInspector] public MinFloatParameter intensity=new MinFloatParameter(.25f,0);
        [InspectorName("降噪")] public BoolParameter denoise=new BoolParameter(false);
        [InspectorName("排除 Ray 材质")]
        [Tooltip("通常填入本场景天空球材质，避免天空壳参与 RTGI 求交。")]
        public HoRTGIMaterialArrayParameter excludedRayMaterials = new HoRTGIMaterialArrayParameter(Array.Empty<Material>());
        [InspectorName("调试模式")] public HoRTGIDebugModeParameter debugMode=new HoRTGIDebugModeParameter(HoRTGIDebugMode.Off);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>enable.value;
        public bool IsTileCompatible()=>false;
    }
}
