using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool cutoutResolutionSpecularStages;
        public static void RunCutoutResolutionSpecularStages(){cutoutResolutionSpecularStages=true;RunCutoutResolutionSpecular();}
        public static void RunCutoutResolutionSpecularStagesCompatibility(){compatibility=true;RunCutoutResolutionSpecularStages();}
        [Serializable] private sealed class ResolutionSpecularStageReport{public bool diagnosticOnly=true;public string path;public ResolutionSpecularStageCase[] cases;}
        [Serializable] private sealed class ResolutionSpecularStageCase
        {
            public int size,points,pipeline,captures,tracedFrames;
            public string mode;public bool reuse,qualityPass;
            public double redRatio,blueRatio,frameL1,rgbMse;
        }
        private sealed class ResolutionSpecularStageSums{internal double red,blue,l1,mse;}
        private static void ValidateCutoutResolutionSpecularStages(Light blue)
        {
            const int size=256,frames=256;
            target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
            var points=new List<CutoutResolutionPoint>();
            for(int y=size/4;y<3*size/4;y+=2)for(int x=size/4;x<11*size/16;x+=2)
                points.Add(new CutoutResolutionPoint{x=x,y=y,unshadowed=ResolutionMotionLamp(light,x,y,size),b=ResolutionMotionLamp(blue,x,y,size)});
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();var captures=new Dictionary<int,int>();
            foreach(int stage in new[]{4,6,9,10})
            {
                var rt=Keep(new RenderTexture(size,size,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[stage]=rt;
                images[stage]=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));captures[stage]=0;
            }
            foreach(int stage in new[]{-1,-2})images[stage]=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var stagedInput=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            RenderCamera();var c=feature.LastContext;int clock=0;
            c.nrd.ValidationSpecularStage=(cmd,stage,texture)=>{if(targets.TryGetValue(stage,out var rt)){cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));captures[stage]++;}};
            var results=new List<ResolutionSpecularStageCase>();
            try
            {
                foreach(string profile in new[]{"slowTS","rapidTS","rapidNone"})
                {
                    bool reuse=profile!="rapidNone",rapid=profile!="slowTS";
                    feature.Settings.temporal=feature.Settings.spatial=reuse;
                    skinBlockerBone.localPosition=Vector3.zero;RenderCamera();c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                    for(int i=0;i<192;i++)Draw();foreach(int stage in targets.Keys)captures[stage]=0;
                    Require(c.lastTemporal==reuse,"specular stages reuse profile not executed");
                    var sums=new Dictionary<int,ResolutionSpecularStageSums>();foreach(int stage in images.Keys)sums[stage]=new ResolutionSpecularStageSums();
                    var scale=new uint[c.nrdScale.count];double referenceR=0,referenceB=0;int traced=0;
                    for(int frame=0;frame<frames;frame++)
                    {
                        float offset=rapid?.25f+.25f*Mathf.Sin(frame*.27f):.12f*Mathf.Sin(frame*.0175f);
                        skinBlockerBone.localPosition=Vector3.right*offset;
                        var before=new Dictionary<int,int>(captures);Draw();Require(c.nrd.LastDispatchCount>0,"specular stage bypassed RELAX");traced+=c.lastVisibilityGradients?1:0;
                        foreach(int stage in targets.Keys){Require(captures[stage]>before[stage],"specular stage missing current frame: "+stage);ReadImage(targets[stage],images[stage]);}
                        ReadImage(c.specular.rt,images[-1]);ReadImage(c.filteredSpecular.rt,images[-2]);ReadImage(c.nrdSpecular.rt,stagedInput);
                        c.nrdScale.GetData(scale);double inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);Require(double.IsFinite(inverse)&&inverse>0,"specular stage scale invalid");
                        // This fixture has uniform grey F0, roughness and an
                        // orthographic view. The raw / normalized-input ratio
                        // restores its common material factor AND HDR scale.
                        // Keep the CPU GGX reference independent of this ratio.
                        double physicalMultiplier=0;
                        foreach(var p in points)
                        {
                            var input=stagedInput.GetPixel(p.x,p.y);var raw=images[-1].GetPixel(p.x,p.y);
                            if(input.r+input.b>0){physicalMultiplier=(raw.r+raw.b)/(input.r+input.b);break;}
                        }
                        Require(double.IsFinite(physicalMultiplier)&&physicalMultiplier>0,"specular stage cannot recover uniform material factor");
                        foreach(var p in points)
                        {
                            Require(!CutoutPlaneShadow(blue,p.x,p.y,size),"specular stage Far must be unoccluded");
                            double r=CutoutPlaneShadow(light,p.x,p.y,size)?0:p.unshadowed,b=p.b;referenceR+=r;referenceB+=b;
                            foreach(var pair in images)
                            {
                                var v=pair.Value.GetPixel(p.x,p.y);double factor=pair.Key>=0?physicalMultiplier:1,vr=v.r*factor,vg=v.g*factor,vb=v.b*factor;
                                Require(double.IsFinite(vr)&&double.IsFinite(vg)&&double.IsFinite(vb),"specular stage non-finite signal");
                                var s=sums[pair.Key];s.red+=vr;s.blue+=vb;s.l1+=Math.Abs(vr-r)+Math.Abs(vb-b);s.mse+=(vr-r)*(vr-r)+vg*vg+(vb-b)*(vb-b);
                                if(pair.Key==10)
                                {
                                    var final=images[-2].GetPixel(p.x,p.y);
                                    Require(Math.Abs(vr-final.r)+Math.Abs(vg-final.g)+Math.Abs(vb-final.b)<(p.unshadowed+b)*.003,$"specular final scale/capture mismatch at {p.x},{p.y}: restored={vr}/{vb}, published={final.r}/{final.b}, multiplier={physicalMultiplier}");
                                }
                            }
                        }
                    }
                    Require(referenceR>0&&referenceB>0&&traced>0,"specular stage missing lighting/visibility reference");
                    foreach(var pair in sums)
                    {
                        var s=pair.Value;var result=new ResolutionSpecularStageCase{size=size,points=points.Count,pipeline=pair.Key,captures=pair.Key>=0?captures[pair.Key]:frames,tracedFrames=traced,mode=profile,reuse=reuse,redRatio=s.red/referenceR,blueRatio=s.blue/referenceB,frameL1=s.l1/(referenceR+referenceB),rgbMse=s.mse/(3*frames*points.Count)};
                        double energyGate=pair.Key==-1?.03:.05;
                        result.qualityPass=Math.Abs(result.redRatio-1)<energyGate&&Math.Abs(result.blueRatio-1)<energyGate&&result.frameL1<.35;
                        results.Add(result);
                        Debug.Log($"[HoRTDIValidation] RESOLUTION_SPECULAR_STAGE mode={profile} pipeline={pair.Key} red={result.redRatio:F6} blue={result.blueRatio:F6} frameL1={result.frameL1:F6} mse={result.rgbMse:E6} captures={result.captures} qualityPass={result.qualityPass}");
                    }
                    Require(Math.Abs(sums[-1].red/referenceR-1)<.03&&Math.Abs(sums[-1].blue/referenceB-1)<.03,"specular stage raw energy reference failed");
                }
                File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-specularstages47-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ResolutionSpecularStageReport{path=compatibility?"compatibility":"RenderGraph",cases=results.ToArray()},true));
            }
            finally{c.nrd.ValidationSpecularStage=null;c.nrd.ValidationTimeSeconds=null;}
            void Draw(){c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();}
        }
    }
}
