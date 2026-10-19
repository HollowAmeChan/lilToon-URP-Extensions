using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RTDI
{
    // Resolve once for this camera, without writing Volume values back into the
    // shared renderer asset. Enable is opt-in; other unchecked parameters retain
    // serialized legacy defaults so existing explicitly enabled scenes keep tuning.
    internal readonly struct HoRTDIViewSettings
    {
        internal readonly bool enabled,composite,denoise,useEnvironment;
        internal readonly HoRTDICompositeMode compositeMode;
        internal readonly float compositeAmount,environmentIntensity;
        internal readonly bool temporal,spatial;
        internal readonly HoRTDILocalSampling localSampling;
        internal readonly HoRTDIFinalSampling finalSampling;
        internal readonly int initialSamples,maxHistory,spatialSamples,environmentSamples,environmentBrdfSamples;
        internal readonly float spatialRadius;
        internal HoRTDIViewSettings(HoRTDISettings source,HoRTDIVolume volume)
        {
            enabled=source.enabled&&volume!=null&&volume.IsActive();
            composite=Select(volume?.composite,source.composite);
            compositeMode=Select(volume?.compositeMode,source.compositeMode);
            denoise=Select(volume?.denoise,source.denoise);
            useEnvironment=Select(volume?.useEnvironment,source.useEnvironment);
            float amount=compositeMode==HoRTDICompositeMode.Additive
                ?Select(volume?.additiveIntensity,source.additiveIntensity):Select(volume?.compositeWeight,source.compositeWeight);
            compositeAmount=SafeGain(amount);
            if(compositeMode!=HoRTDICompositeMode.Additive)compositeAmount=Mathf.Min(1,compositeAmount);
            environmentIntensity=SafeGain(Select(volume?.environmentIntensity,source.environmentIntensity));
            localSampling=Select(volume?.localSampling,source.localSampling);
            finalSampling=Select(volume?.finalSampling,source.finalSampling);
            initialSamples=Mathf.Clamp(Select(volume?.initialSamples,source.initialSamples),1,32);
            temporal=Select(volume?.temporal,source.temporal);
            maxHistory=Mathf.Clamp(Select(volume?.maxHistory,source.maxHistory),1,32);
            spatial=Select(volume?.spatial,source.spatial);
            spatialSamples=Mathf.Clamp(Select(volume?.spatialSamples,source.spatialSamples),1,16);
            float radius=Select(volume?.spatialRadius,source.spatialRadius);
            spatialRadius=float.IsFinite(radius)?Mathf.Clamp(radius,1,32):16;
            environmentSamples=Mathf.Clamp(Select(volume?.environmentSamples,source.environmentSamples),1,32);
            environmentBrdfSamples=Mathf.Clamp(Select(volume?.environmentBrdfSamples,source.environmentBrdfSamples),0,32);
        }
        private static T Select<T>(VolumeParameter<T> parameter,T fallback)=>parameter!=null&&parameter.overrideState?parameter.value:fallback;
        private static float SafeGain(float value)=>float.IsFinite(value)?Mathf.Max(0,value):0;
    }
}
