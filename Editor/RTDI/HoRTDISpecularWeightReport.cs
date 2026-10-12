using System;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        [Serializable] private sealed class SpecularWeightReport{public bool diagnosticOnly=true;public string mode,path;public int size,frames,weightBins=64,confidenceBins=8;public uint[] histogram;public SpecularWeightPass[] passes;}
        [Serializable] private sealed class SpecularWeightPass{public int step;public long samples;public double meanWeightBin,meanConfidenceBin,weightBelowQuarterFraction,weightBelowHalfFraction,confidenceBelowQuarterFraction;}
        private static void WriteLateSpecWeights(LateSpecOverride scope,string mode,int size,int frames)
        {
            var histogram=new uint[2048];scope.histogram.GetData(histogram);var passes=new SpecularWeightPass[4];
            for(int pass=0;pass<4;pass++)
            {
                var result=new SpecularWeightPass{step=2<<pass};double weight=0,confidence=0,quarter=0,half=0,lowConfidence=0;
                for(int cb=0;cb<8;cb++)for(int wb=0;wb<64;wb++)
                {
                    uint n=histogram[pass*512+cb*64+wb];result.samples+=n;weight+=n*(wb+.5)/64;confidence+=n*(cb+.5)/8;if(wb<16)quarter+=n;if(wb<32)half+=n;if(cb<2)lowConfidence+=n;
                }
                Require(result.samples==frames*(size/2)*(7*size/16),"Specular histogram ROI/pass count mismatch");
                result.meanWeightBin=weight/result.samples;result.meanConfidenceBin=confidence/result.samples;result.weightBelowQuarterFraction=quarter/result.samples;result.weightBelowHalfFraction=half/result.samples;result.confidenceBelowQuarterFraction=lowConfidence/result.samples;passes[pass]=result;
                Debug.Log($"[HoRTDIValidation] SPEC_WEIGHT mode={mode} step={result.step} samples={result.samples} meanW={result.meanWeightBin:F6} belowHalf={result.weightBelowHalfFraction:F6} lowConfidence={result.confidenceBelowQuarterFraction:F6}");
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-specweights59-"+mode+"-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new SpecularWeightReport{mode=mode,path=compatibility?"compatibility":"RenderGraph",size=size,frames=frames,histogram=histogram,passes=passes},true));
        }
    }
}
