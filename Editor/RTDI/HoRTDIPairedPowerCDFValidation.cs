using System;
using UnityEngine;
using lilToon.URP.Extensions.RTDI;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool pairedAuxiliaryPowerCDF;
        public static void RunPairedPowerCDFDomain(){pairedAuxiliaryPowerCDF=true;RunPairedLightDomain();}
        public static void RunPairedPowerCDFDomainCompatibility(){compatibility=true;RunPairedPowerCDFDomain();}
        private static float[] BuildAuxiliaryCDF(Light[] keys)
        {
            var weights=new double[keys.Length];double sum=0;
            for(int i=0;i<keys.Length;i++)
            {
                var source=keys[i];var color=QualitySettings.activeColorSpace==ColorSpace.Linear?source.color.linear:source.color;
                if(source.useColorTemperature)color*=Mathf.CorrelatedColorTemperatureToRGB(source.colorTemperature);
                double shaping=1;
                if(source.type==LightType.Spot){double inner=Math.Cos(source.innerSpotAngle*Math.PI/360),outer=Math.Cos(source.spotAngle*Math.PI/360);shaping=Math.Max(0,1-inner+Math.Max(0,inner-outer)/3)*.5;}
                // Positive RGB power proxy: preserve the balanced red/blue
                // auxiliary proposal while making weak emitters less likely.
                weights[i]=Math.Max(1e-20,Math.Max(color.r,Math.Max(color.g,color.b))*source.intensity*shaping);sum+=weights[i];
            }
            Require(double.IsFinite(sum)&&sum>0,"Auxiliary power sum invalid");
            var cdf=new float[keys.Length];double partial=0;float previous=0;
            for(int i=0;i<keys.Length;i++){partial+=weights[i];cdf[i]=i==keys.Length-1?1:(float)(partial/sum);Require(cdf[i]>previous,"Auxiliary CDF interval collapsed; unsupported dynamic range");previous=cdf[i];}
            return cdf;
        }
        private static int CheckAuxiliaryCDFSamples(HoRTDIContext c,float[] cdf)
        {
            var packed=new PairedPackedReservoir[1];int checks=0;
            foreach(int x in new[]{72,104,136,168})
            {
                const int y=128;int address=2*c.arrayPitch+(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;
                c.reservoirs.GetData(packed,0,address,1);uint index=packed[0].lightData&0x7fffffff;
                Require((packed[0].lightData&0x80000000)!=0&&index<cdf.Length,"Auxiliary CDF GPU sample invalid");
                float pdf=cdf[index]-(index>0?cdf[index-1]:0),expected=1/pdf;
                Require(float.IsFinite(packed[0].weight)&&Math.Abs(packed[0].weight-expected)<expected*3e-5,"Auxiliary CDF GPU inverse PDF mismatch");checks++;
            }
            return checks;
        }
    }
}
