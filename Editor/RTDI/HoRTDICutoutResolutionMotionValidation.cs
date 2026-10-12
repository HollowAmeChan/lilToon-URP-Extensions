using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutResolutionMotion;
        public static void RunCutoutResolutionMotion(){forceCutoutResolutionMotion=true;RunCutoutQuality();}
        public static void RunCutoutResolutionMotionCompatibility(){compatibility=true;RunCutoutResolutionMotion();}
        [Serializable] private sealed class ResolutionMotionReport{public bool qualityPass;public string path;public ResolutionMotionCase[] cases;}
        [Serializable] private sealed class ResolutionMotionCase{public int size,points,tracedFrames;public string mode;public bool pass;public double rawRed,rawBlue,filteredRed,filteredBlue,frameL1,changedL1;}
        private static void ValidateCutoutResolutionMotion()
        {
            light.color=Color.red;var blue=Point(new Vector3(-1.5f,0,-1),Color.blue,4);feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=feature.Settings.denoise=true;
            var cases=new List<ResolutionMotionCase>();bool allPass=true;int clock=0;
            foreach(int size in new[]{256,512})
            {
                target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
                var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var points=new List<CutoutResolutionPoint>();int stride=Math.Max(1,size/128);
                for(int y=size/4;y<3*size/4;y+=stride)for(int x=size/4;x<11*size/16;x+=stride)
                    points.Add(new CutoutResolutionPoint{x=x,y=y,unshadowed=F0BoundaryLamp(light,x,y,size),b=F0BoundaryLamp(blue,x,y,size)});
                foreach(string mode in new[]{"slow","rapid","step"})
                {
                    skinBlockerBone.localPosition=Vector3.zero;RenderCamera();var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)Draw();
                    double rr=0,rb=0,fr=0,fb=0,referenceR=0,referenceB=0,error=0,changedError=0,changedReference=0;int traced=0;var previous=new double[points.Count];var changed=new bool[points.Count];
                    for(int frame=0;frame<256;frame++)
                    {
                        float offset=mode=="slow"?.12f*Mathf.Sin(frame*.0175f):mode=="rapid"?.25f+.25f*Mathf.Sin(frame*.27f):(frame/16&1)==0?0:.5f;
                        skinBlockerBone.localPosition=Vector3.right*offset;Draw();Require(c.nrd.LastDispatchCount>0,"resolution motion bypassed RELAX");traced+=c.lastVisibilityGradients?1:0;
                        ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                        for(int i=0;i<points.Count;i++)
                        {
                            var p=points[i];Require(!CutoutPlaneShadow(blue,p.x,p.y,size),"resolution motion Far must be unoccluded");double r=CutoutPlaneShadow(light,p.x,p.y,size)?0:p.unshadowed;var a=raw.GetPixel(p.x,p.y);var f=filtered.GetPixel(p.x,p.y);
                            Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.b),"resolution motion non-finite signal");
                            if(mode=="step"&&frame%16==0)changed[i]=frame>0&&Math.Abs(r-previous[i])>1e-6;
                            if(mode=="step"&&frame%16<3&&changed[i]){changedError+=Math.Abs(f.r-r);changedReference+=p.unshadowed;}
                            previous[i]=r;referenceR+=r;referenceB+=p.b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;error+=Math.Abs(f.r-r)+Math.Abs(f.b-p.b);
                        }
                    }
                    var result=new ResolutionMotionCase{size=size,mode=mode,points=points.Count,tracedFrames=traced,rawRed=rr/referenceR,rawBlue=rb/referenceB,filteredRed=fr/referenceR,filteredBlue=fb/referenceB,frameL1=error/(referenceR+referenceB),changedL1=changedReference>0?changedError/changedReference:0};
                    result.pass=traced>0&&Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.frameL1<.35&&(mode!="step"||changedReference>0&&result.changedL1<.35);cases.Add(result);allPass&=result.pass;
                    Debug.Log($"[HoRTDIValidation] CUTOUT_RESOLUTION_MOTION size={size} mode={mode} rawR={result.rawRed:F6} rawB={result.rawBlue:F6} filteredR={result.filteredRed:F6} filteredB={result.filteredBlue:F6} frameL1={result.frameL1:F6} changedL1={result.changedL1:F6} traced={traced} pass={result.pass}");
                    c.nrd.ValidationTimeSeconds=null;
                    void Draw(){c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();}
                }
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-resolutionmotion45-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ResolutionMotionReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",cases=cases.ToArray()},true));
            Require(allPass,"large resolution motion energy/noise/changed-pixel gates failed");
        }
    }
}
