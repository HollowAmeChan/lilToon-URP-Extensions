using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutShadowStages;
        public static void RunCutoutShadowStages(){forceCutoutShadowStages=true;RunCutoutMultiQuality();}
        public static void RunCutoutShadowStagesCompatibility(){compatibility=true;RunCutoutShadowStages();}
        [Serializable] private sealed class ShadowStageReport{public string path;public ShadowStageCase[] cases;}
        [Serializable] private sealed class ShadowStageCase{public string mode;public int pipeline,pixels;public double maxLeak,meanLeak;}
        private static void ValidateCutoutShadowStages()
        {
            const int frames=512;
            var core=new List<Vector2Int>();
            for(int y=18;y<46;y++)for(int x=18;x<42;x++)
            {
                bool inside=true;for(int dy=-2;dy<=2&&inside;dy++)for(int dx=-2;dx<=2;dx++)if(!CutoutPlaneShadow(x+dx,y+dy)){inside=false;break;}
                if(inside)core.Add(new Vector2Int(x,y));
            }
            Require(core.Count>0,"shadow stage CPU core missing");
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();var captures=new Dictionary<int,int>();
            foreach(int stage in new[]{4,6,9,10})
            {
                var rt=Keep(new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[stage]=rt;
                images[stage]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));captures[stage]=0;
            }
            // Negative IDs distinguish direct raw/published reads from native pipelines.
            images[-1]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));images[-2]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            RenderCamera();var c=feature.LastContext;
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>{if(targets.TryGetValue(stage,out var rt)){cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));captures[stage]++;}};
            var cases=new List<ShadowStageCase>();
            try
            {
                foreach(int reuse in new[]{0,1,2,3})
                {
                    feature.Settings.temporal=(reuse&1)!=0;feature.Settings.spatial=(reuse&2)!=0;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                    for(int i=0;i<192;i++)RenderCamera();foreach(int stage in targets.Keys)captures[stage]=0;
                    Require(c.nrd.LastDispatchCount>0&&ReadMean(c.diffuse.rt).b>0,"shadow stage did not render unoccluded Far lighting/RELAX");
                    Require(c.lastTemporal==feature.Settings.temporal,"shadow stage temporal profile was not executed");
                    var sums=new Dictionary<int,double[]>();foreach(int stage in images.Keys)sums[stage]=new double[core.Count];
                    var scale=new uint[c.nrdScale.count];
                    for(int frame=0;frame<frames;frame++)
                    {
                        RenderCamera();ReadImage(c.diffuse.rt,images[-1]);ReadImage(c.filteredDiffuse.rt,images[-2]);foreach(var pair in targets)ReadImage(pair.Value,images[pair.Key]);
                        c.nrdScale.GetData(scale);float inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);Require(float.IsFinite(inverse)&&inverse>0,"shadow stage scale invalid");
                        for(int i=0;i<core.Count;i++)
                        {
                            var p=core[i];float reference=CutoutLampReference(light,p.x,p.y);Require(reference>0,"shadow stage CPU reference invalid");
                            foreach(var pair in images)
                            {
                                float value=pair.Value.GetPixel(p.x,p.y).r*(pair.Key>=4&&pair.Key<=10?inverse:1);
                                Require(float.IsFinite(value),"shadow stage non-finite signal");sums[pair.Key][i]+=value/reference;
                            }
                            float final=images[-2].GetPixel(p.x,p.y).r,stageFinal=images[10].GetPixel(p.x,p.y).r*inverse;
                            Require(Math.Abs(final-stageFinal)<reference*.003f,"shadow stage final scale/capture mismatch");
                        }
                    }
                    foreach(var pair in captures)Require(pair.Value>=frames,"shadow stage did not capture each frame: "+pair.Key);
                    foreach(var pair in sums)
                    {
                        var result=new ShadowStageCase{mode=new[]{"static","staticT","staticS","staticTS"}[reuse],pipeline=pair.Key,pixels=core.Count};
                        foreach(double sum in pair.Value){double v=sum/frames;result.maxLeak=Math.Max(result.maxLeak,v);result.meanLeak+=v;}
                        result.meanLeak/=core.Count;cases.Add(result);
                        Debug.Log($"[HoRTDIValidation] SHADOW_STAGE mode={result.mode} pipeline={pair.Key} pixels={core.Count} max={result.maxLeak:F6} mean={result.meanLeak:F6}");
                    }
                }
                File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-shadow40-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ShadowStageReport{path=compatibility?"compatibility":"RenderGraph",cases=cases.ToArray()},true));
            }
            finally{c.nrd.ValidationDiffuseStage=null;}
        }
    }
}
