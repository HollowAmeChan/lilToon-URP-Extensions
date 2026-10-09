using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI
{
    public enum HoRTDIDebugMode { Off, Diffuse, Specular, Combined, Receiver, RawDiffuse, RawSpecular, All=255 }
    [Serializable]
    public sealed class HoRTDIDebugModeParameter : VolumeParameter<HoRTDIDebugMode>
    {
        public HoRTDIDebugModeParameter(HoRTDIDebugMode value) : base(value) { }
        public override void Interp(HoRTDIDebugMode from,HoRTDIDebugMode to,float t) => value=t>0?to:from;
    }
    [Serializable,VolumeComponentMenu("Post-processing/Ho-RTDI/直接光")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoRTDIVolume : VolumeComponent,IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable=new BoolParameter(true);
        [InspectorName("调试模式")] public HoRTDIDebugModeParameter debugMode=new HoRTDIDebugModeParameter(HoRTDIDebugMode.Off);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>enable.value;
        public bool IsTileCompatible()=>false;
    }
}
