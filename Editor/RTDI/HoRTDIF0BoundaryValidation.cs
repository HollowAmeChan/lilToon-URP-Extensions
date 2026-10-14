using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceF0Boundary;
        private static bool f0BoundaryLarge;
        private static int f0BoundaryUniform=-1;
        public static void RunF0Boundary(){forceF0Boundary=true;RunReuseNoise();}
        public static void RunF0BoundaryCompatibility(){compatibility=true;RunF0Boundary();}
        public static void RunF0BoundaryLarge(){f0BoundaryLarge=true;RunF0Boundary();}
        public static void RunF0BoundaryLargeCompatibility(){compatibility=true;RunF0BoundaryLarge();}
        public static void RunF0BoundaryUniformZero(){f0BoundaryUniform=0;RunF0Boundary();}
        public static void RunF0BoundaryUniformOne(){f0BoundaryUniform=1;RunF0Boundary();}
        [Serializable] private sealed class F0BoundaryReport{public bool qualityPass;public string path;public int uniformF0;public F0BoundaryCase[] cases;}
        [Serializable] private sealed class F0BoundaryCase{public int size;public bool reuse,swapped,pass;public string region;public double rawRed,rawBlue,filteredRed,filteredBlue,frameL1,greenLeak;}
        private sealed class F0BoundarySums{internal double r,b,ar,ab,fr,fb,error,green;}
        private static void ValidateF0Boundary(Light blue)
        {
            using var temperedOverride=temperedTargetAudit?HoRTDITemperedTargetValidation.Apply(feature):null;
            blue.transform.position=new Vector3(-1.5f,0,-1);primaryReceiver.SetActive(false);
            var mats=new[]{Keep(new Material(surfaceMaterial)),Keep(new Material(surfaceMaterial))};
            for(int side=0;side<2;side++)
            {
                var quad=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));quad.transform.position=new Vector3(side==0?-.5f:.5f,0,0);quad.transform.localScale=new Vector3(1,2,1);quad.GetComponent<Renderer>().sharedMaterial=mats[side];
            }
            feature.Settings.initialSamples=8;camera.aspect=1;
            var cases=new List<F0BoundaryCase>();bool allPass=true;int clock=0;
            foreach(int size in f0BoundaryUniform<0?(f0BoundaryLarge?new[]{256,512}:new[]{64,128}):new[]{64})
            {
                target.Release();target.width=target.height=size;target.Create();
                var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
                foreach(bool swapped in f0BoundaryUniform<0?new[]{false,true}:new[]{false})foreach(bool reuse in f0BoundaryUniform<0?new[]{false,true}:new[]{true})
                {
                    mats[0].SetFloat("_Reflectance",swapped?1f/255:0);mats[1].SetFloat("_Reflectance",swapped?0:1f/255);feature.Settings.temporal=feature.Settings.spatial=reuse;
                    if(f0BoundaryUniform>=0)foreach(var mat in mats)mat.SetFloat("_Reflectance",f0BoundaryUniform/255f);
                    RenderCamera();var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                    for(int i=0;i<192;i++)Draw();Require(c.nrd.LastDispatchCount>0&&c.lastTemporal==reuse,"F0 boundary profile not executed");
                    var sums=new Dictionary<string,F0BoundarySums>();foreach(string region in new[]{"left","right","leftEdge","rightEdge"})sums[region]=new F0BoundarySums();
                    for(int frame=0;frame<512;frame++)
                    {
                        Draw();ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                        int stride=Math.Max(1,size/128);
                        for(int y=3*size/8;y<5*size/8;y+=stride)for(int x=3*size/8;x<5*size/8;x++)
                        {
                            bool grid=(x-3*size/8)%stride==0,edge=x>=size/2-2&&x<size/2+2;
                            if(!grid&&!edge)continue;
                            double r=F0BoundaryLamp(light,x,y,size),b=F0BoundaryLamp(blue,x,y,size);var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                            Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.g)&&float.IsFinite(f.b),"F0 boundary non-finite signal");
                            Require(Math.Abs(a.g)<1e-6,"F0 boundary raw green absent-channel truth");
                            if(grid)Accumulate(sums[x<size/2?"left":"right"]);if(x>=size/2-2&&x<size/2)Accumulate(sums["leftEdge"]);if(x>=size/2&&x<size/2+2)Accumulate(sums["rightEdge"]);
                            void Accumulate(F0BoundarySums s){s.r+=r;s.b+=b;s.ar+=a.r;s.ab+=a.b;s.fr+=f.r;s.fb+=f.b;s.error+=Math.Abs(f.r-r)+Math.Abs(f.b-b);s.green+=Math.Abs(f.g);}
                        }
                    }
                    foreach(var pair in sums)
                    {
                        var s=pair.Value;var result=new F0BoundaryCase{size=size,reuse=reuse,swapped=swapped,region=pair.Key,rawRed=s.ar/s.r,rawBlue=s.ab/s.b,filteredRed=s.fr/s.r,filteredBlue=s.fb/s.b,frameL1=s.error/(s.r+s.b),greenLeak=s.green/(s.r+s.b)};
                        result.pass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.frameL1<.35&&result.greenLeak<.02;allPass&=result.pass;cases.Add(result);
                        Debug.Log($"[HoRTDIValidation] F0_BOUNDARY size={size} swapped={swapped} reuse={reuse} region={pair.Key} rawR={result.rawRed:F6} rawB={result.rawBlue:F6} filteredR={result.filteredRed:F6} filteredB={result.filteredBlue:F6} frameL1={result.frameL1:F6} green={result.greenLeak:F6} pass={result.pass}");
                    }
                    c.nrd.ValidationTimeSeconds=null;
                    void Draw(){c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();}
                }
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+(temperedTargetAudit?"rtdi-tempered92-f0boundary-":f0BoundaryLarge?"rtdi-f0boundary44-":"rtdi-f0boundary42-")+(f0BoundaryUniform>=0?"uniform"+f0BoundaryUniform+"-":"")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new F0BoundaryReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",uniformF0=f0BoundaryUniform,cases=cases.ToArray()},true));
            Require(allPass,"F0 spatial boundary/resolution quality gates failed");
        }
        private static float F0BoundaryLamp(Light source,int x,int y,int size)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/size,(y+.5f)/size,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=source.transform.position-p;float d2=delta.sqrMagnitude;
            float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return source.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
        }
    }
}
