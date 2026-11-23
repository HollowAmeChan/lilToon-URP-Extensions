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
    [Serializable]
    public sealed class HoRTDILocalSamplingParameter : VolumeParameter<HoRTDILocalSampling>
    {
        public HoRTDILocalSamplingParameter(HoRTDILocalSampling value) : base(value) { }
        public override void Interp(HoRTDILocalSampling from,HoRTDILocalSampling to,float t)=>value=t>0?to:from;
    }
    [Serializable]
    public sealed class HoRTDIFinalSamplingParameter : VolumeParameter<HoRTDIFinalSampling>
    {
        public HoRTDIFinalSamplingParameter(HoRTDIFinalSampling value) : base(value) { }
        public override void Interp(HoRTDIFinalSampling from,HoRTDIFinalSampling to,float t)=>value=t>0?to:from;
    }
    [Serializable,VolumeComponentMenu(HoVolumeNames.RTDI)]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = HoVolumeNames.RTDI)]
#endif
    public sealed class HoRTDIVolume : VolumeComponent,IPostProcessComponent
    {
        public HoRTDIVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = HoVolumeNames.RTDI;
#endif
        }

        [Tooltip("必须勾选此项覆盖并开启，当前相机才运行 RTDI；仅添加 Renderer Feature 不会自动启用。\n\n勾选左侧覆盖并启用才运行 RTDI。这里控制生产参数；画面合成由 Ho ScreenProcess 的 RTDI 图层控制。")]
        [InspectorName("启用")] public BoolParameter enable=new BoolParameter(false);
        // Legacy serialized fields retained for old Volume assets. Camera-color
        // ownership now belongs to ScreenProcess RTDI layers.
        [HideInInspector] public BoolParameter composite=new BoolParameter(false);
        [HideInInspector] public HoRTDICompositeModeParameter compositeMode=new HoRTDICompositeModeParameter(HoRTDICompositeMode.Replace);
        [HideInInspector] public ClampedFloatParameter compositeWeight=new ClampedFloatParameter(1,0,1);
        [HideInInspector] public ClampedFloatParameter additiveIntensity=new ClampedFloatParameter(.25f,0,8);
        [InspectorName("天空环境光")] public BoolParameter useEnvironment=new BoolParameter(true);
        [Tooltip("仅调整 RTDI 天空照明；0关闭天空采样，不改变可见天空或解析灯。")]
        public ClampedFloatParameter environmentIntensity=new ClampedFloatParameter(1,0,8);
        [InspectorName("NRD RELAX 降噪")] public BoolParameter denoise=new BoolParameter(false);
        [Tooltip("均匀模式开销较低；功率模式适合强弱跨度较大的局部灯。")]
        public HoRTDILocalSamplingParameter localSampling=new HoRTDILocalSamplingParameter(HoRTDILocalSampling.Uniform);
        [Tooltip("局部灯初始候选数；增加可降低方差，同时增加采样开销。方向灯另有一个候选。")]
        public ClampedIntParameter initialSamples=new ClampedIntParameter(8,1,32);
        public BoolParameter temporal=new BoolParameter(true);
        [Tooltip("ReSTIR 历史样本预算，不是 NRD 历史帧数；建议从4开始。\n\nReSTIR 历史上限，不是 NRD 历史帧数，通常从 4 开始；增加候选和重采样预算会增加开销。")]
        public ClampedIntParameter maxHistory=new ClampedIntParameter(4,1,32);
        public BoolParameter spatial=new BoolParameter(true);
        public ClampedIntParameter spatialSamples=new ClampedIntParameter(4,1,16);
        [Tooltip("ReSTIR 邻域采样半径，单位为像素。")]
        public ClampedFloatParameter spatialRadius=new ClampedFloatParameter(16,1,32);
        [Tooltip("三样本增加两次解析灯最终可见性查询，可降低动态多灯噪声，但增加开销。")]
        public HoRTDIFinalSamplingParameter finalSampling=new HoRTDIFinalSamplingParameter(HoRTDIFinalSampling.Single);
        public ClampedIntParameter environmentSamples=new ClampedIntParameter(4,1,32);
        [Tooltip("与天空 PDF 使用 MIS 的 BRDF 候选；0关闭该提案。")]
        public ClampedIntParameter environmentBrdfSamples=new ClampedIntParameter(4,0,32);
        [Tooltip("查看独立参考 BRDF 直接光；关闭调试不影响 RTDI 产品生产。")]
        [InspectorName("调试模式")] public HoRTDIDebugModeParameter debugMode=new HoRTDIDebugModeParameter(HoRTDIDebugMode.Off);
        public BoolParameter debugInSceneView=new BoolParameter(true);
        public BoolParameter debugInGameView=new BoolParameter(true);
        public bool IsActive()=>active&&enable.overrideState&&enable.value;
        public bool IsTileCompatible()=>false;
    }
}
