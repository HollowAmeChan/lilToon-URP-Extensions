using System;
using lilToon.URP.Extensions.NRD;
namespace lilToon.URP.Extensions.RTDI.NRD
{
    internal static class HoRTDINrdProfile
    {
            // Keep the established responsive diffuse profile during changes.
            // A settled scene can retain the reference-length diffuse history;
            // fast history, specular history and spatial settings stay unchanged.
            internal const float StableDiffusePeriod=.5f;
            internal static HoNrdNative.RelaxSettings DenseDI(float seconds=1f/60,bool stableDiffuse=false)
            {
                var result=new HoNrdNative.RelaxSettings
                {
                acceleration=.6f,spatialSigma=4.5f,temporalSigma=.5f,reset=1,
                historyFixFrames=0,baseStride=14,alternateStride=14,historyNormalPower=8,fastSigma=2,diffPrepass=0,specPrepass=0,
                minHitWeight=.1f,varianceHistory=0,diffPhi=2,specPhi=1,lobeFraction=.5f,roughFraction=.15f,specSlack=.15f,atrous=5,
                depthThreshold=.003f,lumRelax=.5f,normalRelax=.3f,roughRelax=1,minDiffMaterial=0,minSpecMaterial=0,roughEdge=1
                };
                if(!float.IsFinite(seconds)||seconds<=0)seconds=1f/60;
                // Pinned NRD GetMaxAccumulatedFrameNum: round(period * FPS).
                // Two temporal observations are the minimum for a variance
                // estimate. Dense DI avoids the SDK cold spatial color bootstrap,
                // which averages through depth and visibility discontinuities.
                result.diffFrames=Math.Max(1u,Frames(stableDiffuse?StableDiffusePeriod:.05f,seconds,255));result.specFrames=Frames(.5f,seconds,255);
                result.diffFast=Math.Min(Frames(1f/60,seconds,255),result.diffFrames>0?result.diffFrames-1:0);
                result.specFast=Math.Min(Frames(.1f,seconds,255),result.specFrames>0?result.specFrames-1:0);
                // Dense DI supplies both lobes every pixel. The sparse-history
                // reconstruction averages colors before luminance edge stopping.
                result.historyFixFrames=0;return result;
            }
            private static uint Frames(float period,float seconds,uint maximum)=>(uint)Math.Min(maximum,Math.Max(0,Math.Floor(period/seconds+.5)));
    }
}
