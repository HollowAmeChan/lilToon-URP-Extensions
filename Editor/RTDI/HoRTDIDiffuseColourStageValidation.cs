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
        private static bool diffuseColourStageAudit;
        public static void RunDiffuseColourStages(){diffuseColourStageAudit=true;RunRuntimeMultiOpaqueDynamic();}
        public static void RunDiffuseColourStagesCompatibility(){compatibility=true;RunDiffuseColourStages();}
        [Serializable] private sealed class ColourStagePoint{public string phase;public int stage,frames,firstPositiveFrame=-1;public double peak,mean,initialPeak;}
        private sealed class ColourStageCapture:IDisposable
        {
            private readonly HoRTDIContext context;
            private readonly Dictionary<int,RenderTexture> targets=new();
            private readonly Dictionary<int,Texture2D> images=new();
            private readonly Dictionary<int,int> captured=new();
            private readonly Dictionary<(string,int),ColourStagePoint> stats=new();
            private readonly uint[] scale=new uint[4];
            internal ColourStageCapture(HoRTDIContext context)
            {
                this.context=context;
                foreach(int stage in new[]{3,4,6,9,10}){var texture=Keep(new RenderTexture(128,128,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));texture.Create();targets.Add(stage,texture);images.Add(stage,Keep(new Texture2D(128,128,TextureFormat.RGBAFloat,false,true)));captured.Add(stage,0);}
                context.nrd.ValidationDiffuseStage=(cmd,stage,source)=>{if(targets.TryGetValue(stage,out var target)){cmd.CopyTexture(source,new RenderTargetIdentifier(target));captured[stage]++;}};
            }
            internal void Observe(string phase,int frame,Texture2D raw,Texture2D filtered)
            {
                int channel=phase=="disableRed"?0:phase=="disableBlue"?2:-1;if(channel<0)return;
                context.nrdScale.GetData(scale);double inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);
                foreach(int stage in targets.Keys){Require(captured[stage]>0,"Colour stage capture missing");ReadImage(targets[stage],images[stage]);}
                Add(-1,raw,1);Add(-2,filtered,1);foreach(int stage in targets.Keys)Add(stage,images[stage],inverse);
                void Add(int stage,Texture2D image,double factor)
                {
                    var key=(phase,stage);if(!stats.TryGetValue(key,out var value)){value=new ColourStagePoint{phase=phase,stage=stage};stats.Add(key,value);}
                    double sum=0,peak=0;for(int y=48;y<80;y+=2)for(int x=48;x<80;x+=2){double sample=image.GetPixel(x,y)[channel]*factor;Require(double.IsFinite(sample),"Non-finite colour stage");sum+=sample;peak=Math.Max(peak,sample);}
                    if(peak>1e-6&&value.firstPositiveFrame<0)value.firstPositiveFrame=frame;
                    if(frame==0)value.initialPeak=peak;
                    if(frame>=16){value.frames++;value.mean+=sum/256;value.peak=Math.Max(value.peak,peak);}
                }
            }
            public void Dispose()
            {
                context.nrd.ValidationDiffuseStage=null;var result=new List<ColourStagePoint>();foreach(var value in stats.Values){value.mean/=Math.Max(1,value.frames);result.Add(value);Debug.Log($"[HoRTDIValidation] COLOUR_STAGE phase={value.phase} stage={value.stage} peak={value.peak:G9} mean={value.mean:G9} initial={value.initialPeak:G9} firstPositive={value.firstPositiveFrame} frames={value.frames}");}
                File.WriteAllText(Path.Combine(Application.dataPath,"../"+(diffuseSegmentAudit?"rtdi-diffusesegment83-stages-":"rtdi-colourstage82-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ColourStageReport{diagnosticOnly=true,cases=result.ToArray()},true));
            }
        }
        [Serializable] private sealed class ColourStageReport{public bool diagnosticOnly;public ColourStagePoint[] cases;}
    }
}
