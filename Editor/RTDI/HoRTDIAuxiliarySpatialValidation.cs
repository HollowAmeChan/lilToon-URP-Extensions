using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool auxiliarySpatialAudit;
        private static readonly List<AuxiliarySpatialCase> auxiliarySpatialCases=new();
        public static void RunPerspectiveAuxiliarySpatialBaseline(){auxiliarySpatialAudit=true;RunPerspectiveAuxiliaryEnvironmentBaseline();}
        public static void RunPerspectiveAuxiliarySpatial(){auxiliarySpatialAudit=true;RunPerspectiveAuxiliaryEnvironment();}
        public static void RunPerspectiveAuxiliarySpatialCompatibility(){compatibility=true;RunPerspectiveAuxiliarySpatial();}
        [Serializable] private sealed class AuxiliarySpatialReport{public bool diagnosticOnly=true;public string mode,path;public AuxiliarySpatialCase[] cases;}
        [Serializable] private sealed class AuxiliarySpatialCase{public string region;public bool reuse;public int points,frames;public double rawRed,rawGreen,rawBlue,filteredRed,filteredGreen,filteredBlue,redExcess,blueExcess,redReference,blueReference;}
        private sealed class SpatialPoint{internal int x,y;internal double r,g,b;internal bool[] regions;}
        private static void ValidateAuxiliarySpatial(HoRTDIContext c,bool reuse,Light blue)
        {
            const int size=128,frames=512;var points=new List<SpatialPoint>();var cache=new Dictionary<int,double>();var names=new[]{"all","inner64","inner32","inner16","outer","annulus"};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var p=PlanePosition(x,y,size);if(Math.Abs(p.x)>=.995f||Math.Abs(p.y)>=.995f)continue;
                var view=(camera.transform.position-p).normalized;int key=(int)Math.Round(Math.Max(0,Vector3.Dot(Vector3.back,view))*1000000);
                if(!cache.TryGetValue(key,out double sky)){sky=EnvironmentObliqueReference(102.0/255,key/1000000.0);cache[key]=sky;}
                var red=ReferenceLocalLight(light,x,y,size,102f/255).specular;var far=ReferenceLocalLight(blue,x,y,size,102f/255).specular;
                bool inner64=x>=32&&x<96&&y>=32&&y<96,inner32=x>=48&&x<80&&y>=48&&y<80,inner16=x>=56&&x<72&&y>=56&&y<72;
                points.Add(new SpatialPoint{x=x,y=y,r=red.r,g=sky,b=far.b,regions=new[]{true,inner64,inner32,inner16,!inner64,inner64&&!inner32}});
            }
            var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var sums=new double[names.Length,9];var counts=new int[names.Length];foreach(var p in points)for(int region=0;region<names.Length;region++)if(p.regions[region])counts[region]++;
            c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)RenderCamera();
            for(int frame=0;frame<frames;frame++)
            {
                RenderCamera();Require(c.nrd.LastDispatchCount>0&&c.environmentSampling,"Spatial environment audit did not execute");ReadImage(c.specular.rt,raw);ReadImage(c.filteredSpecular.rt,filtered);
                foreach(var p in points)
                {
                    var a=raw.GetPixel(p.x,p.y);var f=filtered.GetPixel(p.x,p.y);Require(a.a>.5f&&f.a>.5f,"Expected plane pixel has invalid primary association");
                    Require(float.IsFinite(a.r)&&float.IsFinite(a.g)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.g)&&float.IsFinite(f.b),"Spatial audit non-finite signal");
                    for(int region=0;region<names.Length;region++)if(p.regions[region]){sums[region,0]+=a.r;sums[region,1]+=a.g;sums[region,2]+=a.b;sums[region,3]+=f.r;sums[region,4]+=f.g;sums[region,5]+=f.b;sums[region,6]+=p.r;sums[region,7]+=p.g;sums[region,8]+=p.b;}
                }
            }
            for(int region=0;region<names.Length;region++)
            {
                var result=new AuxiliarySpatialCase{region=names[region],reuse=reuse,points=counts[region],frames=frames,rawRed=sums[region,0]/sums[region,6],rawGreen=sums[region,1]/sums[region,7],rawBlue=sums[region,2]/sums[region,8],filteredRed=sums[region,3]/sums[region,6],filteredGreen=sums[region,4]/sums[region,7],filteredBlue=sums[region,5]/sums[region,8],redExcess=(sums[region,3]-sums[region,6])/frames,blueExcess=(sums[region,5]-sums[region,8])/frames,redReference=sums[region,6]/frames,blueReference=sums[region,8]/frames};
                auxiliarySpatialCases.Add(result);Debug.Log($"[HoRTDIValidation] AUX_SPATIAL region={result.region} reuse={reuse} points={result.points} raw={result.rawRed:F6}/{result.rawGreen:F6}/{result.rawBlue:F6} filtered={result.filteredRed:F6}/{result.filteredGreen:F6}/{result.filteredBlue:F6} excess={result.redExcess:E6}/{result.blueExcess:E6}");
            }
            Require(counts[0]==counts[1]+counts[4]&&counts[1]==counts[2]+counts[5],"Spatial ROI decomposition mismatch");
        }
        private static void WriteAuxiliarySpatial(string mode)
            =>File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-auxspatial63-"+mode+"-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new AuxiliarySpatialReport{mode=mode,path=compatibility?"compatibility":"RenderGraph",cases=auxiliarySpatialCases.ToArray()},true));
    }
}
