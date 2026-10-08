using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTGI
{
    public enum HoRTGIDebugMode { Off, Diffuse, Receiver, All=255 }
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
        [InspectorName("调试模式")] public HoRTGIDebugModeParameter debugMode=new HoRTGIDebugModeParameter(HoRTGIDebugMode.Off);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>enable.value;
        public bool IsTileCompatible()=>false;
    }
}
