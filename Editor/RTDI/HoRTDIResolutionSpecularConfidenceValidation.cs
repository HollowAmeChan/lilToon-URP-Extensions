using System;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool cutoutResolutionConfidenceAudit;
        public static void RunCutoutResolutionSpecularConfidence(){cutoutResolutionConfidenceAudit=true;RunCutoutResolutionSpecularStages();}
        public static void RunCutoutResolutionSpecularConfidenceCompatibility(){compatibility=true;RunCutoutResolutionSpecularConfidence();}
        [Serializable] private sealed class ResolutionSpecularConfidenceCase
        {
            public string mode;public bool skipVisibilityConfidence;public int tracedFrames;public uint configuredSpecFrames;
            public long samples,changedSamples;
            public double meanConfidence,lowConfidenceFraction,meanReprojection,meanCommonHistoryFrames;
            public double changedMeanConfidence,changedLowFraction,changedMeanHistoryFrames,unchangedMeanConfidence,changedL1;
        }
        private sealed class ResolutionSpecularConfidenceSums
        {
            private long samples,low,changed,changedLow;
            private double confidenceSum,reprojectionSum,historySum,changedConfidenceSum,changedHistorySum;
            internal double changedError,changedReference;
            internal void Observe(float confidence,float reprojection,float history,bool changedNow)
            {
                samples++;confidenceSum+=confidence;reprojectionSum+=reprojection;historySum+=history;if(confidence<.25f)low++;
                if(changedNow){changed++;changedConfidenceSum+=confidence;changedHistorySum+=history;if(confidence<.25f)changedLow++;}
            }
            internal ResolutionSpecularConfidenceCase Result(string mode,bool skip,int traced,uint specFrames)
                =>new ResolutionSpecularConfidenceCase{mode=mode,skipVisibilityConfidence=skip,tracedFrames=traced,configuredSpecFrames=specFrames,samples=samples,changedSamples=changed,
                    meanConfidence=confidenceSum/samples,lowConfidenceFraction=(double)low/samples,meanReprojection=reprojectionSum/samples,meanCommonHistoryFrames=historySum/samples,
                    changedMeanConfidence=changed>0?changedConfidenceSum/changed:0,changedLowFraction=changed>0?(double)changedLow/changed:0,changedMeanHistoryFrames=changed>0?changedHistorySum/changed:0,
                    unchangedMeanConfidence=samples>changed?(confidenceSum-changedConfidenceSum)/(samples-changed):0,changedL1=changedReference>0?changedError/changedReference:0};
        }
    }
}
