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
        private static readonly List<AuxiliaryEnvironmentStageCase> auxiliaryEnvironmentStageCases=new();
        public static void RunAuxiliaryEnvironmentStagesBaseline(){auxiliaryEnvironmentStages=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunAuxiliaryEnvironmentStages(){auxiliaryEnvironmentStages=true;RunAuxiliaryEnvironment();}
        public static void RunAuxiliaryEnvironmentStagesCompatibility(){compatibility=true;RunAuxiliaryEnvironmentStages();}
        [Serializable] private sealed class AuxiliaryEnvironmentStageReport{public bool diagnosticOnly=true;public string path,mode;public AuxiliaryEnvironmentStageCase[] cases;}
        [Serializable] private sealed class AuxiliaryEnvironmentStageCase{public bool reuse,specular;public int pipeline,captures;public double red,green,blue,minSignal,maxNegative;public long negativeValues;}
        private sealed class AuxiliaryStageSums{internal double r,g,b,min;internal long negatives;}
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
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>Copy(cmd,stage,texture);c.nrd.ValidationSpecularStage=(cmd,stage,texture)=>Copy(cmd,100+stage,texture);
            var points=new List<(int x,int y,double dr,double db,double sr,double sb)>();
            for(int y=48;y<80;y++)for(int x=48;x<80;x++)points.Add((x,y,F0BoundaryLamp(light,x,y,size),F0BoundaryLamp(blue,x,y,size),ResolutionMotionLamp(light,x,y,size),ResolutionMotionLamp(blue,x,y,size)));
            try
            {
                c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)RenderCamera();foreach(int key in targets.Keys)captures[key]=0;
                var sums=new Dictionary<int,AuxiliaryStageSums>();foreach(int key in images.Keys)sums[key]=new AuxiliaryStageSums();
                double refDR=0,refDB=0,refSR=0,refSB=0;var scale=new uint[c.nrdScale.count];
                for(int frame=0;frame<frames;frame++)
                {
                    var before=new Dictionary<int,int>(captures);RenderCamera();Require(c.nrd.LastDispatchCount>0,"Environment stage bypassed RELAX");
                    foreach(var pair in targets){Require(captures[pair.Key]>before[pair.Key],"Environment stage missing current capture");ReadImage(pair.Value,images[pair.Key]);}
                    ReadImage(c.diffuse.rt,images[-1]);ReadImage(c.filteredDiffuse.rt,images[-2]);ReadImage(c.specular.rt,images[99]);ReadImage(c.filteredSpecular.rt,images[98]);ReadImage(c.nrdSpecular.rt,input);
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
                        foreach(var pair in images)
                        {
                            int key=pair.Key;bool spec=key>=98;double factor=key==99||key==98||key<0?1:spec?specFactor:inverse;var v=pair.Value.GetPixel(p.x,p.y);double r=v.r*factor,g=v.g*factor,b=v.b*factor;
                            Require(double.IsFinite(r)&&double.IsFinite(g)&&double.IsFinite(b),"Environment stage non-finite signal");var s=sums[key];s.r+=r;s.g+=g;s.b+=b;s.min=Math.Min(s.min,Math.Min(r,Math.Min(g,b)));s.negatives+=(r<0?1:0)+(g<0?1:0)+(b<0?1:0);
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
                    auxiliaryEnvironmentStageCases.Add(result);Debug.Log($"[HoRTDIValidation] AUX_ENV_STAGE reuse={reuse} specular={spec} pipeline={pipeline} ratio={result.red:F6}/{result.green:F6}/{result.blue:F6} min={result.minSignal:E6} negativeValues={result.negativeValues} captures={result.captures}");
                }
            }
            finally{c.nrd.ValidationDiffuseStage=null;c.nrd.ValidationSpecularStage=null;c.nrd.ValidationTimeSeconds=null;}
            void Copy(CommandBuffer cmd,int key,RenderTargetIdentifier texture){if(targets.TryGetValue(key,out var rt)){cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));captures[key]++;}}
        }
        private static void WriteAuxiliaryEnvironmentStages(string mode)
            =>File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-auxenvstages55-"+mode+"-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new AuxiliaryEnvironmentStageReport{path=compatibility?"compatibility":"RenderGraph",mode=mode,cases=auxiliaryEnvironmentStageCases.ToArray()},true));
    }
}
