using System;
using System.Collections.Generic;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool multiCameraMeanAudit;
        public static void RunMultiCameraDIMean(){multiCameraMeanAudit=true;RunMultiCameraDI();}
        public static void RunMultiCameraDIMeanCompatibility(){compatibility=true;RunMultiCameraDIMean();}
        public static void RunMultiCameraOpaqueDIMean(){pairedOpaqueAudit=true;RunMultiCameraDIMean();}
        public static void RunMultiCameraOpaqueDIMeanCompatibility(){compatibility=true;RunMultiCameraOpaqueDIMean();}
        public static void RunMultiCameraDINoReuseMean(){multiCameraMeanAudit=true;RunMultiCameraDINoReuse();}
        public static void RunMultiCameraDITemporalMean(){multiCameraMeanAudit=true;RunMultiCameraDITemporal();}
        public static void RunMultiCameraDISpatialMean(){multiCameraMeanAudit=true;RunMultiCameraDISpatial();}

        [Serializable] private sealed class MultiCameraMeanPoint
        {
            public string view;public int x,y,samples,blocks;public bool pass;
            public double rawRatio,filteredRatio,rawVariance,filteredVariance,rawBlockStandardError,filteredBlockStandardError;
        }
        private sealed class CameraMeanAccumulator
        {
            internal readonly MultiCameraMeanPoint result;
            private double rawSum,filteredSum,rawSquare,filteredSquare,rawBlock,filteredBlock;
            private readonly List<double> rawBlocks=new(),filteredBlocks=new();
            internal CameraMeanAccumulator(string view,int x,int y){result=new MultiCameraMeanPoint{view=view,x=x,y=y};}
            internal void Add(double raw,double filtered)
            {
                result.samples++;rawSum+=raw;filteredSum+=filtered;rawSquare+=raw*raw;filteredSquare+=filtered*filtered;rawBlock+=raw;filteredBlock+=filtered;
                if(result.samples%64==0){rawBlocks.Add(rawBlock/64);filteredBlocks.Add(filteredBlock/64);rawBlock=filteredBlock=0;}
            }
            internal MultiCameraMeanPoint Finish()
            {
                Require(result.samples==1024&&rawBlocks.Count==16,"Each camera mean point requires exactly 1024 samples and 16 complete blocks");
                result.blocks=rawBlocks.Count;result.rawRatio=rawSum/result.samples;result.filteredRatio=filteredSum/result.samples;
                result.rawVariance=Math.Max(0,rawSquare/result.samples-result.rawRatio*result.rawRatio);result.filteredVariance=Math.Max(0,filteredSquare/result.samples-result.filteredRatio*result.filteredRatio);
                result.rawBlockStandardError=StandardError(rawBlocks,result.rawRatio);result.filteredBlockStandardError=StandardError(filteredBlocks,result.filteredRatio);
                result.pass=Math.Abs(result.rawRatio-1)<.03&&Math.Abs(result.filteredRatio-1)<.05;return result;
            }
            private static double StandardError(List<double> blocks,double mean)
            {double sum=0;foreach(double value in blocks)sum+=(value-mean)*(value-mean);return Math.Sqrt(sum/(blocks.Count*(blocks.Count-1)));}
        }
    }
}
