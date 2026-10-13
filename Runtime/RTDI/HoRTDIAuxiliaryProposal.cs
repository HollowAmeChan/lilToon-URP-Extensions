using System;

namespace lilToon.URP.Extensions.RTDI
{
    internal static class HoRTDIAuxiliaryProposal
    {
        // Input is the immutable light snapshot in actual shader index order.
        internal static float[] Build(HoRTDIRendererFeature.AnalyticLight[] lights,int count)
        {
            if(count==0)return new[]{1f};var weights=new double[count];double sum=0;
            for(int i=0;i<count;i++)
            {
                var light=lights[i];double power=Math.Max(0,Math.Max(light.radianceType.x,Math.Max(light.radianceType.y,light.radianceType.z))),shape=1;
                if(light.radianceType.w==1)shape=Math.Max(0,1-light.innerShadow.x+Math.Max(0,light.innerShadow.x-light.directionOuter.w)/3)*.5;
                weights[i]=Math.Max(1e-20,power*shape);sum+=weights[i];
            }
            var cdf=new float[count];double partial=0;float previous=0;bool valid=double.IsFinite(sum)&&sum>0;
            // A distant high-power emitter can dominate this global proxy
            // while a weak nearby emitter dominates the actual BRDF. Reserve
            // 1/16 proposal probability for uniform support; use the exact
            // resulting FP32 interval PDF in shading, not the power-only PDF.
            for(int i=0;valid&&i<count;i++){partial+=weights[i];cdf[i]=i==count-1?1:(float)((15.0/16)*(partial/sum)+(1.0/16)*(i+1.0)/count);valid=float.IsFinite(cdf[i])&&cdf[i]>previous;previous=cdf[i];}
            // Never silently remove support when FP32 cannot represent a tiny
            // power interval. Uniform proposal remains correct, with more noise.
            if(!valid)for(int i=0;i<count;i++)cdf[i]=(i+1f)/count;
            return cdf;
        }
    }
}
