using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceReuseNoiseStages;
        private static bool forceZeroF0Transition;
        public static void RunReuseNoiseStages(){forceReuseNoiseStages=true;RunReuseNoise();}
        public static void RunReuseNoiseStagesCompatibility(){compatibility=true;RunReuseNoiseStages();}
        public static void RunZeroF0Transition(){forceZeroF0Transition=true;RunReuseNoise();}
        public static void RunZeroF0TransitionCompatibility(){compatibility=true;RunZeroF0Transition();}
        [Serializable] private sealed class NoiseStageReport{public string path;public NoiseStageCase[] cases;}
        [Serializable] private sealed class NoiseStageCase{public bool colocated,reuse;public int pipeline;public string alphaStatistic;public double rgbMse,rgbVariance,luminanceVariance,alphaSpread,redRatio,blueRatio;}
        private sealed class NoiseStageSums
        {
            internal readonly double[] rgb=new double[768],rgb2=new double[768],lum=new double[256],lum2=new double[256];
            internal double error,alpha;
        }
        private static void ValidateReuseNoiseStages(Light blue)
        {
            const int frames=512,pixels=256;
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();var captures=new Dictionary<int,int>();
            foreach(int stage in new[]{4,6,9,10})
            {
                var rt=Keep(new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[stage]=rt;
                images[stage]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));captures[stage]=0;
            }
            foreach(int stage in new[]{-1,-2})images[stage]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            feature.Settings.initialSamples=8;RenderCamera();var c=feature.LastContext;
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>{if(targets.TryGetValue(stage,out var rt)){cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));captures[stage]++;}};
            var cases=new List<NoiseStageCase>();
            try
            {
                foreach(bool colocated in new[]{true,false})foreach(bool reuse in new[]{false,true})
                {
                    blue.transform.position=colocated?light.transform.position:new Vector3(-1.5f,0,-1);feature.Settings.temporal=feature.Settings.spatial=reuse;
                    c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)RenderCamera();foreach(int stage in targets.Keys)captures[stage]=0;
                    Require(c.nrd.LastDispatchCount>0&&c.lastTemporal==reuse,"noise stage profile was not executed");
                    var sums=new Dictionary<int,NoiseStageSums>();foreach(int stage in images.Keys)sums[stage]=new NoiseStageSums();
                    var scale=new uint[c.nrdScale.count];double referenceRed=0,referenceBlue=0;
                    for(int frame=0;frame<frames;frame++)
                    {
                        RenderCamera();ReadImage(c.diffuse.rt,images[-1]);ReadImage(c.filteredDiffuse.rt,images[-2]);foreach(var pair in targets)ReadImage(pair.Value,images[pair.Key]);
                        c.nrdScale.GetData(scale);double inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);Require(double.IsFinite(inverse)&&inverse>0,"noise stage scale invalid");
                        for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                        {
                            int p=(y-24)*16+x-24;double r=Expected(light,x,y),b=Expected(blue,x,y);referenceRed+=r;referenceBlue+=b;
                            foreach(var pair in images)
                            {
                                var v=pair.Value.GetPixel(x,y);double factor=pair.Key>=4?inverse:1,vr=v.r*factor,vg=v.g*factor,vb=v.b*factor,l=.2126*vr+.7152*vg+.0722*vb;
                                Require(double.IsFinite(vr)&&double.IsFinite(vg)&&double.IsFinite(vb),"noise stage non-finite color");var s=sums[pair.Key];
                                s.rgb[3*p]+=vr;s.rgb[3*p+1]+=vg;s.rgb[3*p+2]+=vb;s.rgb2[3*p]+=vr*vr;s.rgb2[3*p+1]+=vg*vg;s.rgb2[3*p+2]+=vb*vb;s.lum[p]+=l;s.lum2[p]+=l*l;
                                s.error+=(vr-r)*(vr-r)+vg*vg+(vb-b)*(vb-b);
                                // Temporal/Clamping alpha is the raw temporal second moment;
                                // first Atrous alpha is propagated scalar variance. Last-pass
                                // alpha is history length, so never reinterpret it as variance.
                                if(pair.Key==4||pair.Key==6)s.alpha+=Math.Max(0,v.a*inverse*inverse-l*l);
                                if(pair.Key==9)s.alpha+=Math.Max(0,v.a*inverse*inverse);
                                if(pair.Key==10)Require(Math.Abs(vr-images[-2].GetPixel(x,y).r)<(r+b)*.003,"noise stage final scale/capture mismatch");
                            }
                        }
                    }
                    foreach(var pair in captures)Require(pair.Value>=frames,"noise stage did not capture each frame: "+pair.Key);
                    foreach(var pair in sums)
                    {
                        var s=pair.Value;var result=new NoiseStageCase{colocated=colocated,reuse=reuse,pipeline=pair.Key,alphaStatistic=pair.Key==4||pair.Key==6?"temporalMomentSpread":pair.Key==9?"spatialVariance":"notVariance",rgbMse=s.error/(3*frames*pixels),alphaSpread=s.alpha/(frames*pixels)};
                        for(int p=0;p<pixels;p++)
                        {
                            result.redRatio+=s.rgb[3*p];result.blueRatio+=s.rgb[3*p+2];result.luminanceVariance+=Math.Max(0,s.lum2[p]/frames-Math.Pow(s.lum[p]/frames,2));
                            for(int ch=0;ch<3;ch++)result.rgbVariance+=Math.Max(0,s.rgb2[3*p+ch]/frames-Math.Pow(s.rgb[3*p+ch]/frames,2));
                        }
                        result.redRatio/=referenceRed;result.blueRatio/=referenceBlue;result.rgbVariance/=3*pixels;result.luminanceVariance/=pixels;cases.Add(result);
                        Debug.Log($"[HoRTDIValidation] NOISE_STAGE colocated={colocated} reuse={reuse} pipeline={pair.Key} rgbMSE={result.rgbMse:F8} rgbVar={result.rgbVariance:F8} lumaVar={result.luminanceVariance:F10} alphaSpread={result.alphaSpread:F10} alpha={result.alphaStatistic}");
                    }
                }
                File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-noise41-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new NoiseStageReport{path=compatibility?"compatibility":"RenderGraph",cases=cases.ToArray()},true));
            }
            finally{c.nrd.ValidationDiffuseStage=null;}
        }
        private static void ValidateZeroF0Transition(Light blue)
        {
            blue.transform.position=new Vector3(-1.5f,0,-1);feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=true;
            surfaceMaterial.SetFloat("_Reflectance",0);for(int i=0;i<192;i++)RenderCamera();
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            double rr=0,rb=0,fr=0,fb=0,referenceR=0,referenceB=0,error=0;
            for(int frame=0;frame<256;frame++)
            {
                surfaceMaterial.SetFloat("_Reflectance",(frame/32&1)==0?0:1f/255);RenderCamera();var c=feature.LastContext;ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                Require(c.nrd.LastDispatchCount>0,"F0 transition bypassed RELAX");
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                {
                    double r=Expected(light,x,y),b=Expected(blue,x,y);var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                    Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.b),"F0 transition non-finite signal");
                    referenceR+=r;referenceB+=b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;error+=Math.Abs(f.r-r)+Math.Abs(f.b-b);
                }
            }
            Debug.Log($"[HoRTDIValidation] F0_TRANSITION rawR={rr/referenceR:F6} rawB={rb/referenceB:F6} filteredR={fr/referenceR:F6} filteredB={fb/referenceB:F6} frameL1={error/(referenceR+referenceB):F6}");
            Require(Math.Abs(rr/referenceR-1)<.03&&Math.Abs(rb/referenceB-1)<.03,"F0 proposal transition raw energy mismatch");
            Require(Math.Abs(fr/referenceR-1)<.05&&Math.Abs(fb/referenceB-1)<.05&&error/(referenceR+referenceB)<.35,"F0 proposal transition filtered energy/noise mismatch");
        }
    }
}
