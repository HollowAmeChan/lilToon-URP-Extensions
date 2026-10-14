using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool guideDistanceAudit;
        private static readonly List<GuideDistanceCase> guideDistanceCases=new();
        public static void RunRuntimeGuideDistances(){guideDistanceAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunRuntimeGuideDistancesCompatibility(){compatibility=true;RunRuntimeGuideDistances();}
        [Serializable] private sealed class GuideDistanceReport{public bool diagnosticOnly=true;public string path;public GuideDistanceCase[] cases;}
        [Serializable] private sealed class GuideDistanceCase
        {
            public bool mixed,reuse;public int probes,primaryMatches,equalLobeDistances,primaryEnvironmentWithVisibleLocalSpecular;
            public double minSpecDistance=double.MaxValue,maxSpecDistance,maxGuideToLocalDistance;
        }
        [StructLayout(LayoutKind.Sequential)] private struct GuideAuxiliary{internal uint index;internal float weight;}
        private sealed class GuideDistanceCapture
        {
            private readonly HoRTDIContext context;private readonly Texture2D diffuse,specular,visibility;
            private readonly PairedPackedReservoir[] packed=new PairedPackedReservoir[1];
            private readonly GuideAuxiliary[] secondary=new GuideAuxiliary[1];private readonly GuideDistanceCase result;
            internal GuideDistanceCapture(HoRTDIContext context,bool mixed,bool reuse)
            {
                this.context=context;result=new GuideDistanceCase{mixed=mixed,reuse=reuse};
                Require(context.width==128&&context.height==128&&context.finalVisibility!=null,"Guide audit requires native 128-square fixture");
                diffuse=Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true));specular=Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true));visibility=Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true));
            }
            internal void Observe(int frame)
            {
                if(frame%16!=0)return;
                ReadImage(context.nrdDiffuse.rt,diffuse);ReadImage(context.nrdSpecular.rt,specular);ReadImage(context.finalVisibility.rt,visibility);
                foreach(int x in new[]{52,60,68,76})
                {
                    const int y=64;int address=context.arrayPitch+(y/16)*context.rowPitch+(x/16)*256+(y%16)*16+x%16;
                    context.reservoirs.GetData(packed,0,address,1);uint primary=packed[0].lightData&0x7fffffff;
                    Require((packed[0].lightData&0x80000000)!=0,"Guide audit primary sample invalid");
                    float d=diffuse.GetPixel(x,y).a,s=specular.GetPixel(x,y).a;
                    Require(float.IsFinite(d)&&float.IsFinite(s)&&d>=0&&s>=0,"NRD input distance invalid");
                    double expected=primary==0x7ffffffe?65504:Distance(primary,x,y);
                    result.probes++;if(Math.Abs(s-expected)<Math.Max(.005,expected*.002))result.primaryMatches++;if(d==s)result.equalLobeDistances++;
                    result.minSpecDistance=Math.Min(result.minSpecDistance,s);result.maxSpecDistance=Math.Max(result.maxSpecDistance,s);
                    if(!result.mixed)continue;
                    uint bits=(uint)Mathf.RoundToInt(visibility.GetPixel(x,y).r*7);double nearest=double.MaxValue;
                    context.reservoirs.GetData(packed,0,address+context.arrayPitch,1);
                    if((bits&2)!=0&&(packed[0].lightData&0x80000000)!=0&&packed[0].weight>0)Add(packed[0].lightData&0x7fffffff);
                    context.auxiliarySecondary.GetData(secondary,0,y*128+x,1);uint second=secondary[0].index&0x7fffffff;
                    if((bits&4)!=0&&second!=0x7fffffff&&secondary[0].weight>0)Add(second);
                    if(nearest<double.MaxValue){result.maxGuideToLocalDistance=Math.Max(result.maxGuideToLocalDistance,s/nearest);if(primary==0x7ffffffe)result.primaryEnvironmentWithVisibleLocalSpecular++;}
                    void Add(uint index){Require(index<context.previousLightKeys.Length,"Guide auxiliary identity invalid");if(ResolutionMotionLamp(context.previousLightKeys[index],x,y,128)>0)nearest=Math.Min(nearest,Distance(index,x,y));}
                }
            }
            private double Distance(uint index,int x,int y)
            {
                Require(index<context.previousLightKeys.Length,"Guide primary identity invalid");var source=context.previousLightKeys[index];
                return source.type==LightType.Directional?65504:Math.Min(65504,(source.transform.position-PlanePosition(x,y,128)).magnitude);
            }
            internal void Finish(){Require(result.probes==128,"Guide audit observation count mismatch");guideDistanceCases.Add(result);}
        }
        private static void WriteGuideDistances()=>File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-guidedistance95-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new GuideDistanceReport{path=compatibility?"compatibility":"RenderGraph",cases=guideDistanceCases.ToArray()},true));
    }
}
