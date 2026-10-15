using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool componentNoiseAudit;
        private static readonly List<ComponentNoiseCase> componentNoiseCases=new();
        public static void RunComponentNoise(){componentNoiseAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunComponentNoiseCompatibility(){compatibility=true;RunComponentNoise();}
        [Serializable] private sealed class ComponentNoiseReport{public bool diagnosticOnly=true;public string path;public string[] channels=new[]{"red","green","blue","luminance"};public ComponentNoiseCase[] cases;}
        [Serializable] private sealed class ComponentNoiseCase
        {
            public bool mixed,reuse;public int frames,points=4;public double maxDecompositionError;
            public double[] primaryVariance=new double[4],firstVariance=new double[4],secondVariance=new double[4],auxiliaryVariance=new double[4],rawVariance=new double[4],auxiliaryCovariance=new double[4],primaryAuxiliaryCovariance=new double[4],reconstructedVariance=new double[4];
        }
        private sealed class ComponentNoiseCapture
        {
            private readonly HoRTDIContext context;private readonly Texture2D visibility;
            private readonly PairedPackedReservoir[] packed=new PairedPackedReservoir[1];private readonly GuideAuxiliary[] second=new GuideAuxiliary[1];
            private readonly double[,] mean=new double[4,16],m2=new double[4,16],auxCov=new double[4,4],primaryCov=new double[4,4];
            private readonly ComponentNoiseCase result;
            internal ComponentNoiseCapture(HoRTDIContext context,bool mixed,bool reuse)
            {
                this.context=context;result=new ComponentNoiseCase{mixed=mixed,reuse=reuse};Require(context.width==128&&context.finalVisibility!=null&&camera.orthographic,"Component audit requires native orthographic fixture");
                visibility=Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true));
                foreach(var source in context.previousLightKeys)Require(source.type==LightType.Point&&(source.color==Color.red||source.color==Color.blue),"Component audit requires red/blue delta lights");
            }
            internal void Observe(Texture2D raw)
            {
                ReadImage(context.finalVisibility.rt,visibility);int n=++result.frames,point=0;
                foreach(int x in new[]{52,60,68,76})
                {
                    const int y=64;uint bits=(uint)Mathf.RoundToInt(visibility.GetPixel(x,y).r*7);
                    int address=context.arrayPitch+(y/16)*context.rowPitch+(x/16)*256+(y%16)*16+x%16;
                    Color total=raw.GetPixel(x,y),primary=Color.clear,first=Color.clear,secondary=Color.clear;
                    context.reservoirs.GetData(packed,0,address,1);uint index=packed[0].lightData&0x7fffffff;
                    if((packed[0].lightData&0x80000000)!=0)
                    {
                        if(index==0x7ffffffe)primary=new Color(0,total.g,0,0); // Only sky carries green in this fixture.
                        else if((bits&1)!=0)primary=Estimate(index,packed[0].weight)*(result.mixed?.5f:1);
                    }
                    if(result.mixed)
                    {
                        context.reservoirs.GetData(packed,0,address+context.arrayPitch,1);
                        if((bits&2)!=0&&(packed[0].lightData&0x80000000)!=0)first=Estimate(packed[0].lightData&0x7fffffff,packed[0].weight)*.25f;
                        context.auxiliarySecondary.GetData(second,0,y*128+x,1);uint auxiliaryIndex=second[0].index&0x7fffffff;
                        if((bits&4)!=0&&auxiliaryIndex!=0x7fffffff)secondary=Estimate(auxiliaryIndex,second[0].weight)*.25f;
                    }
                    Color predicted=primary+first+secondary;
                    double error=(Math.Abs(total.r-predicted.r)+Math.Abs(total.g-predicted.g)+Math.Abs(total.b-predicted.b))/(total.r+total.g+total.b+.001);
                    Require(double.IsFinite(error)&&error<.003,"Specular packed component decomposition mismatch");result.maxDecompositionError=Math.Max(result.maxDecompositionError,error);
                    Color[] components={primary,first,secondary,total};var delta=new double[16];var values=new double[16];
                    for(int component=0;component<4;component++)for(int channel=0;channel<4;channel++)
                    {
                        Color c=components[component];double value=channel<3?c[channel]:.2126*c.r+.7152*c.g+.0722*c.b;int entry=component*4+channel;
                        values[entry]=value;delta[entry]=value-mean[point,entry];mean[point,entry]+=delta[entry]/n;m2[point,entry]+=delta[entry]*(value-mean[point,entry]);
                    }
                    for(int channel=0;channel<4;channel++)
                    {
                        auxCov[point,channel]+=delta[4+channel]*(values[8+channel]-mean[point,8+channel]);
                        primaryCov[point,channel]+=delta[channel]*(values[4+channel]+values[8+channel]-mean[point,4+channel]-mean[point,8+channel]);
                    }
                    point++;
                    Color Estimate(uint lightIndex,float weight){Require(lightIndex<context.previousLightKeys.Length&&float.IsFinite(weight)&&weight>=0,"Component sample invalid");var source=context.previousLightKeys[lightIndex];return LightColor(source)*(float)(ResolutionMotionLamp(source,x,y,128)*weight);}
                }
            }
            internal void Finish()
            {
                Require(result.frames==512,"Component audit frame count mismatch");double denominator=(result.frames-1)*4;
                for(int point=0;point<4;point++)for(int channel=0;channel<4;channel++)
                {
                    result.primaryVariance[channel]+=m2[point,channel]/denominator;result.firstVariance[channel]+=m2[point,4+channel]/denominator;result.secondVariance[channel]+=m2[point,8+channel]/denominator;result.rawVariance[channel]+=m2[point,12+channel]/denominator;
                    result.auxiliaryCovariance[channel]+=auxCov[point,channel]/denominator;result.primaryAuxiliaryCovariance[channel]+=primaryCov[point,channel]/denominator;
                }
                for(int channel=0;channel<4;channel++){result.auxiliaryVariance[channel]=result.firstVariance[channel]+result.secondVariance[channel]+2*result.auxiliaryCovariance[channel];result.reconstructedVariance[channel]=result.primaryVariance[channel]+result.auxiliaryVariance[channel]+2*result.primaryAuxiliaryCovariance[channel];}
                componentNoiseCases.Add(result);
            }
        }
        private static void WriteComponentNoise()=>File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-componentnoise101-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ComponentNoiseReport{path=compatibility?"compatibility":"RenderGraph",cases=componentNoiseCases.ToArray()},true));
    }
}
