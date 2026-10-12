using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutResolution;
        public static void RunCutoutResolution(){forceCutoutResolution=true;RunCutoutQuality();}
        public static void RunCutoutResolutionCompatibility(){compatibility=true;RunCutoutResolution();}
        [Serializable] private sealed class CutoutResolutionReport{public bool qualityPass;public string path;public CutoutResolutionCase[] cases;}
        [Serializable] private sealed class CutoutResolutionCase{public int size,points,corePoints;public bool reuse,pass;public double rawRed,rawBlue,filteredRed,filteredBlue,frameL1,coreLeak;}
        private sealed class CutoutResolutionPoint{internal int x,y;internal bool core;internal double r,b,unshadowed;}
        private static void ValidateCutoutResolution()
        {
            light.color=Color.red;var blue=Point(new Vector3(-1.5f,0,-1),Color.blue,4);feature.Settings.initialSamples=8;feature.Settings.denoise=true;
            var results=new List<CutoutResolutionCase>();bool allPass=true;int clock=0;
            foreach(int size in new[]{256,512})
            {
                target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
                var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
                var points=new List<CutoutResolutionPoint>();int stride=Math.Max(1,size/128);
                for(int y=size/4;y<3*size/4;y+=stride)for(int x=size/4;x<11*size/16;x+=stride)
                {
                    double unshadowed=F0BoundaryLamp(light,x,y,size),b=F0BoundaryLamp(blue,x,y,size);bool shadow=CutoutPlaneShadow(light,x,y,size),core=shadow;
                    Require(!CutoutPlaneShadow(blue,x,y,size),"resolution fixture Far must be unoccluded");
                    for(int dy=-2;dy<=2&&core;dy++)for(int dx=-2;dx<=2;dx++)if(!CutoutPlaneShadow(light,x+dx,y+dy,size)){core=false;break;}
                    points.Add(new CutoutResolutionPoint{x=x,y=y,r=shadow?0:unshadowed,b=b,unshadowed=unshadowed,core=core});
                }
                foreach(bool reuse in new[]{false,true})
                {
                    feature.Settings.temporal=feature.Settings.spatial=reuse;RenderCamera();var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                    for(int i=0;i<192;i++)Draw();Require(c.nrd.LastDispatchCount>0&&c.lastTemporal==reuse,"cutout resolution profile not executed");
                    double rr=0,rb=0,fr=0,fb=0,referenceR=0,referenceB=0,error=0;var coreSums=new double[points.Count];
                    for(int frame=0;frame<512;frame++)
                    {
                        Draw();ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                        for(int i=0;i<points.Count;i++)
                        {
                            var p=points[i];var a=raw.GetPixel(p.x,p.y);var f=filtered.GetPixel(p.x,p.y);Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.b),"cutout resolution non-finite signal");
                            referenceR+=p.r;referenceB+=p.b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;error+=Math.Abs(f.r-p.r)+Math.Abs(f.b-p.b);
                            if(p.core){Require(Math.Abs(a.r)<1e-6,"cutout resolution core raw visibility mismatch");coreSums[i]+=f.r/p.unshadowed;}
                        }
                    }
                    var result=new CutoutResolutionCase{size=size,reuse=reuse,points=points.Count,rawRed=rr/referenceR,rawBlue=rb/referenceB,filteredRed=fr/referenceR,filteredBlue=fb/referenceB,frameL1=error/(referenceR+referenceB)};
                    for(int i=0;i<points.Count;i++)if(points[i].core){result.corePoints++;result.coreLeak=Math.Max(result.coreLeak,coreSums[i]/512);}
                    result.pass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.frameL1<.35&&result.corePoints>0&&result.coreLeak<.02;results.Add(result);allPass&=result.pass;
                    Debug.Log($"[HoRTDIValidation] CUTOUT_RESOLUTION size={size} reuse={reuse} points={result.points} core={result.corePoints} rawR={result.rawRed:F6} rawB={result.rawBlue:F6} filteredR={result.filteredRed:F6} filteredB={result.filteredBlue:F6} frameL1={result.frameL1:F6} coreLeak={result.coreLeak:F6} pass={result.pass}");
                    c.nrd.ValidationTimeSeconds=null;
                    void Draw(){c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();}
                }
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-cutoutresolution44-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new CutoutResolutionReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",cases=results.ToArray()},true));
            Require(allPass,"cutout resolution energy/shape/core quality gates failed");
        }
    }
}
