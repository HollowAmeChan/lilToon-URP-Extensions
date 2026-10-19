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
    [Serializable]
    public sealed class HoRTDICompositeModeParameter : VolumeParameter<HoRTDICompositeMode>
    {
        public HoRTDICompositeModeParameter(HoRTDICompositeMode value) : base(value) { }
        public override void Interp(HoRTDICompositeMode from,HoRTDICompositeMode to,float t)=>value=t>0?to:from;
    }
    [Serializable,VolumeComponentMenu("Post-processing/Ho-RTDI/直接光")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class HoRTDIVolume : VolumeComponent,IPostProcessComponent
    {
        [InspectorName("启用")] public BoolParameter enable=new BoolParameter(true);
        [InspectorName("合成到画面")] public BoolParameter composite=new BoolParameter(false);
        [InspectorName("混合模式")] public HoRTDICompositeModeParameter compositeMode=new HoRTDICompositeModeParameter(HoRTDICompositeMode.Replace);
        [Tooltip("参考 PBR 替换模式：原画面与 RTDI 照明的过渡权重。")]
        public ClampedFloatParameter compositeWeight=new ClampedFloatParameter(1,0,1);
        [Tooltip("加法模式：新增直接光的倍率，不重复添加自发光。")]
        public ClampedFloatParameter additiveIntensity=new ClampedFloatParameter(.25f,0,8);
        [InspectorName("天空环境光")] public BoolParameter useEnvironment=new BoolParameter(true);
        [Tooltip("仅调整 RTDI 天空照明；0关闭天空采样，不改变可见天空或解析灯。")]
        public ClampedFloatParameter environmentIntensity=new ClampedFloatParameter(1,0,8);
        [InspectorName("NRD RELAX 降噪")] public BoolParameter denoise=new BoolParameter(false);
        [InspectorName("调试模式")] public HoRTDIDebugModeParameter debugMode=new HoRTDIDebugModeParameter(HoRTDIDebugMode.Off);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>enable.value;
        public bool IsTileCompatible()=>false;
    }
}
