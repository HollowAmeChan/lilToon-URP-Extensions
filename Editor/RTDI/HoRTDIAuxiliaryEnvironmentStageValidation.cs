using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool auxiliaryEnvironmentStages;
        private static bool auxiliaryEnvironmentMomentAudit;
        private static bool auxiliaryClampingAudit;
        private static readonly List<AuxiliaryEnvironmentStageCase> auxiliaryEnvironmentStageCases=new();
        public static void RunAuxiliaryEnvironmentStagesBaseline(){auxiliaryEnvironmentStages=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunAuxiliaryEnvironmentStages(){auxiliaryEnvironmentStages=true;RunAuxiliaryEnvironment();}
        public static void RunAuxiliaryEnvironmentStagesCompatibility(){compatibility=true;RunAuxiliaryEnvironmentStages();}
        public static void RunAuxiliaryEnvironmentMomentsBaseline(){auxiliaryEnvironmentMomentAudit=true;RunAuxiliaryEnvironmentStagesBaseline();}
        public static void RunAuxiliaryEnvironmentMoments(){auxiliaryEnvironmentMomentAudit=true;RunAuxiliaryEnvironmentStages();}
        public static void RunAuxiliaryEnvironmentMomentsCompatibility(){compatibility=true;RunAuxiliaryEnvironmentMoments();}
        public static void RunAuxiliaryClampingBaseline(){auxiliaryClampingAudit=true;RunAuxiliaryEnvironmentStagesBaseline();}
        public static void RunAuxiliaryClamping(){auxiliaryClampingAudit=true;RunAuxiliaryEnvironmentStages();}
        public static void RunAuxiliaryClampingCompatibility(){compatibility=true;RunAuxiliaryClamping();}
        [Serializable] private sealed class AuxiliaryEnvironmentStageReport{public bool diagnosticOnly=true;public string path,mode;public AuxiliaryEnvironmentStageCase[] cases;}
        [Serializable] private sealed class AuxiliaryEnvironmentStageCase{public bool reuse,specular;public int pipeline,captures;public double red,green,blue,minSignal,maxNegative,rgbVariance,luminanceVariance,alphaSpread;public string alphaStatistic;public long negativeValues;}
        private sealed class AuxiliaryStageSums
        {
            internal double r,g,b,min,alpha;internal long negatives;
            internal readonly double[] rgb=new double[3072],rgb2=new double[3072],lum=new double[1024],lum2=new double[1024];
        }
        private static void ValidateAuxiliaryEnvironmentStages(HoRTDIContext c,bool mixed,bool reuse,Light blue,double skySpec)
        {
            const int size=128,frames=512;Require(mixed,"Environment stage audit requires the failing mixed case");
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();var captures=new Dictionary<int,int>();
            foreach(bool spec in new[]{false,true})foreach(int stage in new[]{4,6,9,10})
            {
                int key=(spec?100:0)+stage;var rt=Keep(new RenderTexture(size,size,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[key]=rt;
                images[key]=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));captures[key]=0;
            }
            foreach(int key in new[]{-1,-2,99,98})images[key]=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var input=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            RenderTexture fastSource=null,fastSource6=null,fast4=null,fast6=null;Texture2D fast4Image=null,fast6Image=null;
            if(auxiliaryClampingAudit)
            {
                fastSource=FindNrdTemporalPool(c,"gOut_SpecFast");fastSource6=FindNrdTemporalPool(c,"gOut_SpecFast",false,6);var desc=fastSource.descriptor;desc.enableRandomWrite=false;
                fast4=Keep(new RenderTexture(desc));fast4.Create();fast6=Keep(new RenderTexture(desc));fast6.Create();
                fast4Image=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));fast6Image=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            }
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>Copy(cmd,stage,texture);
            c.nrd.ValidationSpecularStage=(cmd,stage,texture)=>{Copy(cmd,100+stage,texture);if(auxiliaryClampingAudit&&(stage==4||stage==6))cmd.CopyTexture(stage==4?fastSource:fastSource6,new RenderTargetIdentifier(stage==4?fast4:fast6));};
            var points=new List<(int x,int y,double dr,double db,double sr,double sb)>();
            for(int y=48;y<80;y++)for(int x=48;x<80;x++)points.Add((x,y,F0BoundaryLamp(light,x,y,size),F0BoundaryLamp(blue,x,y,size),ResolutionMotionLamp(light,x,y,size),ResolutionMotionLamp(blue,x,y,size)));
            try
            {
                c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)RenderCamera();foreach(int key in targets.Keys)captures[key]=0;
                var sums=new Dictionary<int,AuxiliaryStageSums>();foreach(int key in images.Keys)sums[key]=new AuxiliaryStageSums();
                double refDR=0,refDB=0,refSR=0,refSB=0;var scale=new uint[c.nrdScale.count];
                long clampSamples=0,changed=0,fastChanged=0;double totalChange=0,totalFastChange=0,distanceBefore=0,distanceAfter=0,maxChange=0;
                for(int frame=0;frame<frames;frame++)
                {
                    var before=new Dictionary<int,int>(captures);RenderCamera();Require(c.nrd.LastDispatchCount>0,"Environment stage bypassed RELAX");
                    foreach(var pair in targets){Require(captures[pair.Key]>before[pair.Key],"Environment stage missing current capture");ReadImage(pair.Value,images[pair.Key]);}
                    ReadImage(c.diffuse.rt,images[-1]);ReadImage(c.filteredDiffuse.rt,images[-2]);ReadImage(c.specular.rt,images[99]);ReadImage(c.filteredSpecular.rt,images[98]);ReadImage(c.nrdSpecular.rt,input);
                    if(auxiliaryClampingAudit){ReadImage(fast4,fast4Image);ReadImage(fast6,fast6Image);}
                    c.nrdScale.GetData(scale);double inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]),specFactor=0;Require(double.IsFinite(inverse)&&inverse>0,"Environment stage scale invalid");
                    foreach(var p in points)
                    {
                        var a=input.GetPixel(p.x,p.y);var raw=images[99].GetPixel(p.x,p.y);double denominator=a.r+a.g+a.b;
                        if(denominator>0){specFactor=(raw.r+raw.g+raw.b)/denominator;break;}
                    }
                    Require(double.IsFinite(specFactor)&&specFactor>0,"Environment stage material factor unavailable");
                    foreach(var p in points)
                    {
                        refDR+=p.dr;refDB+=p.db;refSR+=p.sr;refSB+=p.sb;
                        if(auxiliaryClampingAudit)
                        {
                            var a=images[104].GetPixel(p.x,p.y);var b=images[106].GetPixel(p.x,p.y);var f=fast4Image.GetPixel(p.x,p.y);var nextFast=fast6Image.GetPixel(p.x,p.y);
                            double reference=p.sr+skySpec+p.sb,change=(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))*specFactor/reference,fastChange=(Math.Abs(f.r-nextFast.r)+Math.Abs(f.g-nextFast.g)+Math.Abs(f.b-nextFast.b))*specFactor/reference;
                            changed+=change>.0003?1:0;fastChanged+=fastChange>.0003?1:0;clampSamples++;totalChange+=change;totalFastChange+=fastChange;maxChange=Math.Max(maxChange,change);
                            distanceBefore+=(Math.Abs(a.r-f.r)+Math.Abs(a.g-f.g)+Math.Abs(a.b-f.b))*specFactor/reference;distanceAfter+=(Math.Abs(b.r-nextFast.r)+Math.Abs(b.g-nextFast.g)+Math.Abs(b.b-nextFast.b))*specFactor/reference;
                        }
                        foreach(var pair in images)
                        {
                            int key=pair.Key;bool spec=key>=98;double factor=key==99||key==98||key<0?1:spec?specFactor:inverse;var v=pair.Value.GetPixel(p.x,p.y);double r=v.r*factor,g=v.g*factor,b=v.b*factor;
                            Require(double.IsFinite(r)&&double.IsFinite(g)&&double.IsFinite(b),"Environment stage non-finite signal");var s=sums[key];s.r+=r;s.g+=g;s.b+=b;s.min=Math.Min(s.min,Math.Min(r,Math.Min(g,b)));s.negatives+=(r<0?1:0)+(g<0?1:0)+(b<0?1:0);
                            if(auxiliaryEnvironmentMomentAudit)
                            {
                                int index=(p.y-48)*32+p.x-48;double l=.2126*r+.7152*g+.0722*b;
                                s.rgb[3*index]+=r;s.rgb[3*index+1]+=g;s.rgb[3*index+2]+=b;s.rgb2[3*index]+=r*r;s.rgb2[3*index+1]+=g*g;s.rgb2[3*index+2]+=b*b;s.lum[index]+=l;s.lum2[index]+=l*l;
                                int pipeline=spec?key-100:key;
                                if(pipeline==4||pipeline==6)s.alpha+=Math.Max(0,v.a*factor*factor-l*l);
                                else if(pipeline==9)s.alpha+=Math.Max(0,v.a*factor*factor);
                                // Last Atrous alpha is history length, never variance.
                            }
                            if(key==10||key==110)
                            {
                                var final=images[spec?98:-2].GetPixel(p.x,p.y);double reference=spec?p.sr+skySpec+p.sb:p.dr+1+p.db;
                                Require(Math.Abs(r-final.r)+Math.Abs(g-final.g)+Math.Abs(b-final.b)<reference*.003,"Environment final stage/material scale mismatch");
                            }
                        }
                    }
                }
                foreach(var pair in sums)
                {
                    bool spec=pair.Key>=98;int pipeline=spec?pair.Key-100:pair.Key;var s=pair.Value;
                    var result=new AuxiliaryEnvironmentStageCase{reuse=reuse,specular=spec,pipeline=pipeline,captures=pipeline<0?frames:captures[pair.Key],red=s.r/(spec?refSR:refDR),green=s.g/(frames*points.Count*(spec?skySpec:1)),blue=s.b/(spec?refSB:refDB),minSignal=s.min,maxNegative=-s.min,negativeValues=s.negatives};
                    if(auxiliaryEnvironmentMomentAudit)
                    {
                        for(int i=0;i<points.Count;i++)
                        {
                            for(int channel=0;channel<3;channel++){int index=i*3+channel;result.rgbVariance+=Math.Max(0,s.rgb2[index]/frames-Math.Pow(s.rgb[index]/frames,2));}
                            result.luminanceVariance+=Math.Max(0,s.lum2[i]/frames-Math.Pow(s.lum[i]/frames,2));
                        }
                        result.rgbVariance/=3*points.Count;result.luminanceVariance/=points.Count;result.alphaSpread=s.alpha/(frames*points.Count);result.alphaStatistic=pipeline==4||pipeline==6?"temporalMomentSpread":pipeline==9?"propagatedScalarVariance":"notVariance";
                        Debug.Log($"[HoRTDIValidation] AUX_ENV_MOMENT reuse={reuse} specular={spec} pipeline={pipeline} rgbVariance={result.rgbVariance:E6} luminanceVariance={result.luminanceVariance:E6} alphaSpread={result.alphaSpread:E6} alpha={result.alphaStatistic}");
                    }
                    auxiliaryEnvironmentStageCases.Add(result);Debug.Log($"[HoRTDIValidation] AUX_ENV_STAGE reuse={reuse} specular={spec} pipeline={pipeline} ratio={result.red:F6}/{result.green:F6}/{result.blue:F6} min={result.minSignal:E6} negativeValues={result.negativeValues} captures={result.captures}");
                }
                if(auxiliaryClampingAudit)WriteClampingAudit(reuse,clampSamples,changed,fastChanged,totalChange,totalFastChange,distanceBefore,distanceAfter,maxChange,c.nrd.AppliedSettings.specFrames,c.nrd.AppliedSettings.specFast);
            }
            finally{c.nrd.ValidationDiffuseStage=null;c.nrd.ValidationSpecularStage=null;c.nrd.ValidationTimeSeconds=null;}
            void Copy(CommandBuffer cmd,int key,RenderTargetIdentifier texture){if(targets.TryGetValue(key,out var rt)){cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));captures[key]++;}}
        }
        private static void WriteAuxiliaryEnvironmentStages(string mode)
            =>File.WriteAllText(Path.Combine(Application.dataPath,"../"+(auxiliaryClampingAudit?"rtdi-auxclamp67-stages-":auxiliaryEnvironmentMomentAudit?"rtdi-auxenvmoments65-":"rtdi-auxenvstages55-")+mode+"-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new AuxiliaryEnvironmentStageReport{path=compatibility?"compatibility":"RenderGraph",mode=mode,cases=auxiliaryEnvironmentStageCases.ToArray()},true));
    }
}
