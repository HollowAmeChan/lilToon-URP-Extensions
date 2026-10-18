using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceReuseNoise;
        public static void RunReuseNoise(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceReuseNoise=true;RunDenoise();}
        public static void RunReuseNoiseCompatibility(){compatibility=true;RunReuseNoise();}
        [Serializable] private sealed class ReuseNoiseReport{public bool qualityPass;public string path;public ReuseNoiseCase[] cases;}
        [Serializable] private sealed class ReuseNoiseCase
        {
            public string mode;public bool colocated,pass;public int initialSamples;
            public double rawRed,rawBlue,filteredRed,filteredBlue,rawMse,filteredMse,rawLuminanceMse,filteredLuminanceMse,rawSpatialCorrelation,filteredSpatialCorrelation,rawLagCorrelation,filteredLagCorrelation;
        }
        private static void ValidateReuseNoise()
        {
            const int frames=256,side=16,pixels=side*side;
            light.color=Color.red;var blue=Point(light.transform.position,Color.blue,light.intensity);
            if(forceF0Boundary){ValidateF0Boundary(blue);return;}
            if(forceZeroF0Transition){ValidateZeroF0Transition(blue);return;}
            if(forceReuseNoiseStages){ValidateReuseNoiseStages(blue);return;}
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var results=new List<ReuseNoiseCase>();bool qualityPass=true;
            foreach(int initialSamples in new[]{1,8})foreach(bool colocated in new[]{true,false})foreach(int reuse in new[]{0,1,2,3})
            {
                blue.transform.position=colocated?light.transform.position:new Vector3(-1.5f,0,-1);
                feature.Settings.initialSamples=initialSamples;feature.Settings.temporal=(reuse&1)!=0;feature.Settings.spatial=(reuse&2)!=0;
                RenderCamera();var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                for(int i=0;i<192;i++)RenderCamera();
                Require(c.nrd!=null&&c.nrd.LastDispatchCount>0,"reuse noise failed to dispatch RELAX");
                Require(c.lastTemporal==feature.Settings.temporal,"reuse noise temporal profile was not executed");
                var rawErrors=new double[frames*pixels];var filteredErrors=new double[frames*pixels];
                double rr=0,rb=0,fr=0,fb=0,referenceR=0,referenceB=0,rawMse=0,filteredMse=0,rawLuminanceMse=0,filteredLuminanceMse=0;
                for(int frame=0;frame<frames;frame++)
                {
                    RenderCamera();ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                    for(int y=0;y<side;y++)for(int x=0;x<side;x++)
                    {
                        double r=Expected(light,x+24,y+24),b=Expected(blue,x+24,y+24);var a=raw.GetPixel(x+24,y+24);var f=filtered.GetPixel(x+24,y+24);
                        Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.b),"reuse noise non-finite signal");
                        referenceR+=r;referenceB+=b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;
                        double ar=a.r-r,ab=a.b-b,er=f.r-r,eb=f.b-b;int index=frame*pixels+y*side+x;
                        rawErrors[index]=ar;filteredErrors[index]=er;rawMse+=ar*ar+ab*ab;filteredMse+=er*er+eb*eb;
                        double al=.2126*ar+.0722*ab,fl=.2126*er+.0722*eb;rawLuminanceMse+=al*al;filteredLuminanceMse+=fl*fl;
                    }
                }
                var result=new ReuseNoiseCase{mode=new[]{"none","temporal","spatial","temporalSpatial"}[reuse],initialSamples=initialSamples,colocated=colocated,rawRed=rr/referenceR,rawBlue=rb/referenceB,filteredRed=fr/referenceR,filteredBlue=fb/referenceB,rawMse=rawMse/(2*frames*pixels),filteredMse=filteredMse/(2*frames*pixels),rawLuminanceMse=rawLuminanceMse/(frames*pixels),filteredLuminanceMse=filteredLuminanceMse/(frames*pixels)};
                // Subtract each pixel's time mean: geometric shading and persistent bias
                // must not masquerade as stochastic correlation. Report red
                // covariance separately from the two-channel energy/MSE gates.
                CenterPixelErrors(rawErrors,frames,pixels);CenterPixelErrors(filteredErrors,frames,pixels);
                result.rawSpatialCorrelation=ErrorCorrelation(rawErrors,frames,side,false);result.filteredSpatialCorrelation=ErrorCorrelation(filteredErrors,frames,side,false);
                result.rawLagCorrelation=ErrorCorrelation(rawErrors,frames,side,true);result.filteredLagCorrelation=ErrorCorrelation(filteredErrors,frames,side,true);
                result.pass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.filteredMse<result.rawMse*.3;
                qualityPass&=result.pass;results.Add(result);
                Debug.Log($"[HoRTDIValidation] REUSE_NOISE initial={initialSamples} colocated={colocated} mode={result.mode} rawR={result.rawRed:F6} rawB={result.rawBlue:F6} filteredR={result.filteredRed:F6} filteredB={result.filteredBlue:F6} rawMSE={result.rawMse:F6} filteredMSE={result.filteredMse:F6} rawLumaMSE={result.rawLuminanceMse:F8} filteredLumaMSE={result.filteredLuminanceMse:F8} rawSpatial={result.rawSpatialCorrelation:F6} filteredSpatial={result.filteredSpatialCorrelation:F6} rawLag={result.rawLagCorrelation:F6} filteredLag={result.filteredLagCorrelation:F6} pass={result.pass}");
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+(referenceConvergenceBaselineRegression?"rtdi-convergence108-baseline-reuse-":"rtdi-reuse38-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new ReuseNoiseReport{qualityPass=qualityPass,path=compatibility?"compatibility":"RenderGraph",cases=results.ToArray()},true));
            Require(qualityPass,"reuse noise energy or variance gate failed");
        }
        private static void CenterPixelErrors(double[] errors,int frames,int pixels)
        {
            for(int p=0;p<pixels;p++){double mean=0;for(int f=0;f<frames;f++)mean+=errors[f*pixels+p];mean/=frames;for(int f=0;f<frames;f++)errors[f*pixels+p]-=mean;}
        }
        private static double ErrorCorrelation(double[] errors,int frames,int side,bool lag)
        {
            int pixels=side*side;double cross=0,left=0,right=0;
            for(int f=0;f<frames-(lag?1:0);f++)for(int y=0;y<side;y++)for(int x=0;x<side;x++)
            {
                int p=f*pixels+y*side+x;
                if(lag)Pair(errors[p],errors[p+pixels]);
                else{if(x+1<side)Pair(errors[p],errors[p+1]);if(y+1<side)Pair(errors[p],errors[p+side]);}
            }
            return left>0&&right>0?cross/Math.Sqrt(left*right):0;
            void Pair(double a,double b){cross+=a*b;left+=a*a;right+=b*b;}
        }
    }
}
