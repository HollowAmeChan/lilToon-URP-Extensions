using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RTDI
{
    // Resolve once for this camera, without writing Volume values back into the
    // shared renderer asset. Unchecked/new parameters retain legacy settings.
    internal readonly struct HoRTDIViewSettings
    {
        internal readonly bool enabled,composite,denoise,useEnvironment;
        internal readonly HoRTDICompositeMode compositeMode;
        internal readonly float compositeAmount,environmentIntensity;
        internal HoRTDIViewSettings(HoRTDISettings source,HoRTDIVolume volume)
        {
            enabled=source.enabled&&Select(volume?.enable,true);
            composite=Select(volume?.composite,source.composite);
            compositeMode=Select(volume?.compositeMode,source.compositeMode);
            denoise=Select(volume?.denoise,source.denoise);
            useEnvironment=Select(volume?.useEnvironment,source.useEnvironment);
            float amount=compositeMode==HoRTDICompositeMode.Additive
                ?Select(volume?.additiveIntensity,source.additiveIntensity):Select(volume?.compositeWeight,source.compositeWeight);
            compositeAmount=SafeGain(amount);
            if(compositeMode!=HoRTDICompositeMode.Additive)compositeAmount=Mathf.Min(1,compositeAmount);
            environmentIntensity=SafeGain(Select(volume?.environmentIntensity,source.environmentIntensity));
        }
        private static T Select<T>(VolumeParameter<T> parameter,T fallback)=>parameter!=null&&parameter.overrideState?parameter.value:fallback;
        private static float SafeGain(float value)=>float.IsFinite(value)?Mathf.Max(0,value):0;
    }
}
