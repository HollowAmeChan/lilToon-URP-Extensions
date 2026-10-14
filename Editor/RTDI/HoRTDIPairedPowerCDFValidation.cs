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
        private static int CheckAuxiliaryCDFSamples(HoRTDIContext c,float[] cdf,GraphicsBuffer second,Texture2D visibility,out int agreements)
        {
            var packed=new PairedPackedReservoir[1];int checks=0;agreements=0;
            foreach(int x in new[]{72,104,136,168})
            {
                const int y=128;int address=2*c.arrayPitch+(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;
                c.reservoirs.GetData(packed,0,address,1);uint index=packed[0].lightData&0x7fffffff;
                Require((packed[0].lightData&0x80000000)!=0&&index<cdf.Length,"Auxiliary CDF GPU sample invalid");
                float pdf=cdf[index]-(index>0?cdf[index-1]:0),expected=1/pdf;
                if(HoRTDISurfaceAuxiliaryValidation.Enabled)Require(float.IsFinite(packed[0].weight)&&packed[0].weight>=0&&(packed[0].mVisibility>>18)==4,"Fresh RIS sample normalization metadata invalid");
                else Require(float.IsFinite(packed[0].weight)&&Math.Abs(packed[0].weight-expected)<expected*3e-5,"Auxiliary CDF GPU inverse PDF mismatch");checks++;
                if(second!=null)
                {
                    ReadSecondAuxiliary(second,packed,0,y*c.width+x,1);uint secondIndex=packed[0].lightData&0x7fffffff;bool secondCachedVisibility=(packed[0].mVisibility&63)!=0;
                    Require((packed[0].lightData&0x80000000)!=0&&secondIndex<cdf.Length,"Second auxiliary GPU sample invalid");
                    float secondPdf=cdf[secondIndex]-(secondIndex>0?cdf[secondIndex-1]:0),secondExpected=1/secondPdf;
                    if(stratifiedAuxiliaryAudit)
                    {
                        float firstLo=index>0?cdf[index-1]:0,secondLo=secondIndex>0?cdf[secondIndex-1]:0;
                        Require((firstLo<.5f&&cdf[secondIndex]>.5f)||(secondLo<.5f&&cdf[index]>.5f),"Auxiliary pair cannot cover both CDF strata");
                        if(cdf.Length==2&&cdf[0]==.5f)Require(index!=secondIndex,"Balanced two-light stratification repeated one light");
                    }
                    if(HoRTDISurfaceAuxiliaryValidation.Enabled)Require(float.IsFinite(packed[0].weight)&&packed[0].weight>=0,"Fresh RIS compact finalized weight invalid");
                    else Require(float.IsFinite(packed[0].weight)&&Math.Abs(packed[0].weight-secondExpected)<secondExpected*3e-5,"Second auxiliary GPU inverse PDF mismatch");
                    uint bits=0;
                    if(pairedOpaqueAudit)
                    {
                        Require(((packed[0].mVisibility&63)!=0)==!CutoutPlaneShadow(c.previousLightKeys[secondIndex],x,y,c.width),"Opaque second visibility/cache mismatch");
                        c.reservoirs.GetData(packed,0,address,1);Require(((packed[0].mVisibility&63)!=0)==!CutoutPlaneShadow(c.previousLightKeys[index],x,y,c.width),"Opaque first visibility/cache mismatch");
                    }
                    else
                    {
                        bits=(uint)Mathf.RoundToInt(visibility.GetPixel(x,y).r*7);
                        if(compactAuxiliaryAudit)Require(secondCachedVisibility==((bits&4)!=0),"Compact auxiliary cache differs from native final visibility");
                        Require(((bits>>1)&1)==(CutoutPlaneShadow(c.previousLightKeys[index],x,y,c.width)?0u:1u)&&((bits>>2)&1)==(CutoutPlaneShadow(c.previousLightKeys[secondIndex],x,y,c.width)?0u:1u),"Two auxiliary native alpha visibility mismatch");
                    }
                    c.reservoirs.GetData(packed,0,address-c.arrayPitch,1);
                    if((packed[0].lightData&0x80000000)!=0)
                    {
                        uint primaryIndex=packed[0].lightData&0x7fffffff;bool blocked=CutoutPlaneShadow(c.previousLightKeys[primaryIndex],x,y,c.width);
                        Require((pairedOpaqueAudit||(bits&1)==(blocked?0u:1u))&&((packed[0].mVisibility&63)!=0)==!blocked,"Three-sample primary visibility/cache mismatch");
                    }
                    if(secondIndex==index)agreements++;checks++;
                }
            }
            return checks;
        }
    }
}
