using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGIDenseReferenceAudit
    {
        internal static readonly Vector2Int[] Pixels={new(87,165),new(158,146),new(201,156),new(147,237),new(196,245),new(58,88),new(209,185),new(185,180),new(122,45),new(196,141),new(134,150),new(141,157),new(16,16),new(128,128),new(240,32),new(32,240)};
        internal static readonly Vector2Int[] ControlPixels={new(16,16),new(240,32)};
        internal static readonly Vector2Int[] LocalPixels=new[]{204,206,226,227,252,255}.SelectMany(y=>new[]{85,86,87}.Select(x=>new Vector2Int(x,y))).ToArray();
        [Serializable]internal sealed class Batch{public int index,seed,samplesPerPoint,applicationFrame,renderedFrame;public double editorTimeSeconds;public Vector3[] means;public int[] positiveSamples;public float[] maxima;}
        [Serializable]internal sealed class Point
        {
            public int x,y;public string group;public long samples,positiveSamples;public Vector3 referenceMean,batchMeanStandardError,firstHalfMean,secondHalfMean,splitRelativeDifference,mainRaw,mainFiltered;
            public bool converged;public Vector3 primaryWorld,normal,albedo;public float viewDepth,maxSample,maxPrimaryError,maxRayPrimaryError;
        }
        [Serializable]internal sealed class Report
        {
            public string protocol="Selected full-resolution receivers, independent batch seeds, original one-bounce estimator with temporal/spatial/NRD off. Shared material/light implementation, not independent absolute shading truth or full-image quality acceptance.";
            public bool functionalPass,allPointsConverged,environment,alphaVisibility;public int samplesPerPoint,batchCount,mainFrames,lightCount,instances,cutouts,secondaryLightSamples,secondaryEnvironmentSamples;
            public bool stableDomain,scheduledEditorTicks;public int lightingSignature;public uint sceneRevision,skyRevision;
            public float maxDistance,rayBias;public Point[] points;public Batch[] batches;
            public GuideSnapshot firstGuides,lastGuides;
        }
        [Serializable]internal sealed class Guide
        {
            public int x,y;public Vector4 packedNormalRoughness,scaledSignal;
            public Vector3 decodedNormal,remodulatedSignal,rawPhysical,filteredPhysical;
            public float viewZ,normalPackError,decodedNormalError,signalRestorationError;
        }
        [Serializable]internal sealed class GuideSnapshot
        {
            public int batch,applicationFrame,width,height;public float signalScale;
            public string normalFormat,viewZFormat,signalFormat,rawFormat,filteredFormat;public Guide[] points;
        }
        readonly HoRTGIDenseReferenceFeature feature;readonly List<Batch> batches=new();
        readonly Vector2Int[] receivers;readonly double[,] raw,filtered;
        HoRTGIDenseReferenceFeature.PrimaryProbe[] primary;int mainFrames;
        int lightingSignature;uint sceneRevision,skyRevision;
        readonly float[] rayErrors;
        GuideSnapshot firstGuides,lastGuides;
        internal HoRTGIDenseReferenceAudit(HoRTGIDenseReferenceFeature feature,Vector2Int[] selected=null){this.feature=feature;receivers=(selected??Pixels).ToArray();raw=new double[receivers.Length,3];filtered=new double[receivers.Length,3];rayErrors=new float[receivers.Length];}
        internal void AddMain(Color[] a,Color[] b,int width)
        {
            for(int i=0;i<receivers.Length;i++)for(int c=0;c<3;c++){int at=receivers[i].y*width+receivers[i].x;raw[i,c]+=a[at][c];filtered[i,c]+=b[at][c];}mainFrames++;
        }
        internal void AddBatch(int batch,bool captureGuides=false)
        {
            if(batch!=batches.Count)throw new Exception("Dense reference batches out of order");
            var probes=new HoRTGIDenseReferenceFeature.PrimaryProbe[receivers.Length];feature.probe.GetData(probes);
            var rayProbes=new Vector4[receivers.Length*4];feature.rayProbe.GetData(rayProbes);
            if(feature.lastLightCount!=18||feature.lastInstances!=15||feature.lastCutouts!=12||!feature.lastEnvironment||!feature.context.alphaVisibility)throw new Exception("Reference support/light/sky/Cutout domain differs from castle");
            if(batch==0){lightingSignature=feature.lastLightingSignature;sceneRevision=feature.lastSceneRevision;skyRevision=feature.lastSkyRevision;}
            else if(lightingSignature!=feature.lastLightingSignature||sceneRevision!=feature.lastSceneRevision||skyRevision!=feature.lastSkyRevision)throw new Exception("Reference scene/light/sky domain changed during batches");
            for(int i=0;i<probes.Length;i++)
            {
                var p=probes[i];if(p.originalWorld.w<.5f||p.rabWorld.w<.5f)throw new Exception("Invalid mapped reference primary");
                for(int c=0;c<4;c++)if(!float.IsFinite(p.errors0[c])||!float.IsFinite(p.errors1[c])||p.errors0[c]>.0005f||p.errors1[c]>.0005f)throw new Exception("Mapped/full/history primary mismatch at "+receivers[i]+" errors="+p.errors0+"/"+p.errors1);
                if(primary!=null&&Vector3.Distance(p.rabWorld,primary[i].rabWorld)>.0001f)throw new Exception("Reference receiver moved during batch collection");
                if(primary!=null&&(Vector3.Distance(p.normalDepth,primary[i].normalDepth)>.0001f||Vector3.Distance(p.albedo,primary[i].albedo)>.0001f))throw new Exception("Reference normal/albedo changed during batch collection");
                var expected=new[]{p.originalWorld,p.rabWorld,p.normalDepth,p.albedo};
                for(int j=0;j<4;j++)for(int c=0;c<4;c++)
                {float error=Mathf.Abs(rayProbes[i*4+j][c]-expected[j][c]);if(!float.IsFinite(error)||error>.0005f)throw new Exception("Raygen/compute primary mismatch at "+receivers[i]);rayErrors[i]=Mathf.Max(rayErrors[i],error);}
            }
            primary=probes;
            if(captureGuides){lastGuides=CaptureGuides(batch);firstGuides??=lastGuides;}
            var texture=feature.context.diffuse.rt;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);Color[] values;var old=RenderTexture.active;
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();values=image.GetPixels();}
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            var result=new Batch{index=batch,seed=104729+batch*8191,samplesPerPoint=feature.samplesPerPoint,applicationFrame=Time.frameCount,renderedFrame=Time.renderedFrameCount,editorTimeSeconds=EditorApplication.timeSinceStartup,means=new Vector3[receivers.Length],positiveSamples=new int[receivers.Length],maxima=new float[receivers.Length]};
            for(int i=0;i<receivers.Length;i++)
            {
                var sums=new double[3];
                for(int sample=0;sample<feature.samplesPerPoint;sample++)
                {
                    var value=values[i*feature.samplesPerPoint+sample];if(value.a<.5f)throw new Exception("Dense sample lost receiver validity");bool positive=false;
                    for(int c=0;c<3;c++){if(!float.IsFinite(value[c])||value[c]<0)throw new Exception("Invalid dense radiance");sums[c]+=value[c];result.maxima[i]=Math.Max(result.maxima[i],value[c]);positive|=value[c]>0;}if(positive)result.positiveSamples[i]++;
                }
                result.means[i]=new Vector3((float)(sums[0]/feature.samplesPerPoint),(float)(sums[1]/feature.samplesPerPoint),(float)(sums[2]/feature.samplesPerPoint));
            }
            batches.Add(result);
            if((batch+1)%16==0)Debug.Log("[HoGIDense] completed batches="+(batch+1)+" samplesPerPoint="+((long)(batch+1)*feature.samplesPerPoint));
        }
        static string Group(Vector2Int pixel){int i=Array.IndexOf(Pixels,pixel);return i<0?"other":i<8?"high loss":i<12?"high gain":"control";}
        GuideSnapshot CaptureGuides(int batch)
        {
            var main=feature.mainContext;var nrd=main?.nrd;if(nrd==null)throw new Exception("Local reference main NRD guides missing");
            var normal=Read(nrd.normal.rt);var z=Read(nrd.viewZ.rt);var signal=Read(nrd.signal.rt);
            var physical=Read(main.diffuse.rt);var publication=Read(main.PublishedDiffuse.rt);
            var result=new GuideSnapshot{batch=batch,applicationFrame=Time.frameCount,width=main.width,height=main.height,signalScale=nrd.AppliedSignalScale,
                normalFormat=nrd.normal.rt.graphicsFormat.ToString(),viewZFormat=nrd.viewZ.rt.graphicsFormat.ToString(),signalFormat=nrd.signal.rt.graphicsFormat.ToString(),
                rawFormat=main.diffuse.rt.graphicsFormat.ToString(),filteredFormat=main.PublishedDiffuse.rt.graphicsFormat.ToString(),points=new Guide[receivers.Length]};
            if(result.width!=256||result.height!=256||result.signalScale!=64)throw new Exception("Local guide domain or units changed");
            for(int i=0;i<receivers.Length;i++)
            {
                int at=receivers[i].y*main.width+receivers[i].x;Vector4 p=normal[at];Vector3 n=primary[i].normalDepth;
                if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)||!float.IsFinite(p.w)||!float.IsFinite(z[at].r))throw new Exception("Nonfinite local normal/depth guide");
                var l1=n/(Mathf.Abs(n.x)+Mathf.Abs(n.y)+Mathf.Abs(n.z));
                var expected=new Vector4(l1.x*.5f+l1.y*.5f+.5f,l1.y*.5f+.5f-l1.x*.5f,l1.z<0?0:1,0);
                var decoded=new Vector3(p.x-p.y,p.x+p.y-1,(p.z*2-1<0?-1:1)*(1-Mathf.Abs(p.x-p.y)-Mathf.Abs(p.x+p.y-1))).normalized;
                var guide=new Guide{x=receivers[i].x,y=receivers[i].y,packedNormalRoughness=p,scaledSignal=signal[at],decodedNormal=decoded,viewZ=z[at].r,
                    rawPhysical=Rgb(physical[at]),filteredPhysical=Rgb(publication[at]),decodedNormalError=Vector3.Distance(decoded,n)};
                for(int c=0;c<4;c++)guide.normalPackError=Mathf.Max(guide.normalPackError,Mathf.Abs(p[c]-expected[c]));
                var albedo=(Vector3)primary[i].albedo;guide.remodulatedSignal=Vector3.Scale(Rgb(signal[at])*result.signalScale,albedo);
                for(int c=0;c<3;c++)
                {
                    if(!float.IsFinite(guide.rawPhysical[c])||!float.IsFinite(guide.filteredPhysical[c])||!float.IsFinite(guide.remodulatedSignal[c]))throw new Exception("Nonfinite local guide publication");
                    float error=Mathf.Abs(guide.rawPhysical[c]-guide.remodulatedSignal[c]);guide.signalRestorationError=Mathf.Max(guide.signalRestorationError,error);
                    if(error>Mathf.Max(.000005f,Mathf.Abs(guide.rawPhysical[c])*.002f))throw new Exception("Local half-signal remodulation differs beyond input precision");
                }
                if(guide.normalPackError>.0005f||guide.decodedNormalError>.004f||Mathf.Abs(guide.viewZ-primary[i].normalDepth.w)>.0005f)throw new Exception("Local NRD guide differs from actual primary");
                result.points[i]=guide;
            }
            return result;
            static Vector3 Rgb(Color v)=>new(v.r,v.g,v.b);
            static Color[] Read(RenderTexture texture)
            {
                var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
                try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();return image.GetPixels();}
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            }
        }
        internal Report Finish()
        {
            if(batches.Count==0||primary==null)throw new Exception("No actual dense batches");var points=new Point[receivers.Length];
            for(int i=0;i<receivers.Length;i++)
            {
                var p=new Point{x=receivers[i].x,y=receivers[i].y,group=Group(receivers[i]),samples=(long)batches.Count*feature.samplesPerPoint,positiveSamples=batches.Sum(x=>(long)x.positiveSamples[i]),primaryWorld=primary[i].rabWorld,normal=primary[i].normalDepth,viewDepth=primary[i].normalDepth.w,albedo=primary[i].albedo,maxSample=batches.Max(x=>x.maxima[i]),converged=batches.Count>=16};
                for(int c=0;c<3;c++)
                {
                    double mean=batches.Average(x=>x.means[i][c]);double variance=batches.Count>1?batches.Sum(x=>Math.Pow(x.means[i][c]-mean,2))/(batches.Count-1):0;
                    int half=batches.Count/2;double first=half>0?batches.Take(half).Average(x=>x.means[i][c]):mean,second=half>0?batches.Skip(half).Average(x=>x.means[i][c]):mean;
                    double se=Math.Sqrt(variance/batches.Count),split=Math.Abs(first-second)/Math.Max(Math.Abs(mean),1e-8);
                    p.referenceMean[c]=(float)mean;p.batchMeanStandardError[c]=(float)se;p.firstHalfMean[c]=(float)first;p.secondHalfMean[c]=(float)second;p.splitRelativeDifference[c]=(float)split;
                    if(mainFrames>0){p.mainRaw[c]=(float)(raw[i,c]/mainFrames);p.mainFiltered[c]=(float)(filtered[i,c]/mainFrames);}
                    if(mean>1e-6)p.converged&=se/mean<=.02&&split<=.05;else p.converged&=se<=1e-7&&Math.Abs(first-second)<=1e-7;
                }
                // All-zero finite samples alone do not prove a black receiver's truth.
                if(p.positiveSamples<64&&((Vector3)primary[i].albedo).sqrMagnitude>0)p.converged=false;
                p.maxPrimaryError=Mathf.Max(primary[i].errors0.x,primary[i].errors0.y,primary[i].errors0.z,primary[i].errors0.w,primary[i].errors1.x,primary[i].errors1.y,primary[i].errors1.z,primary[i].errors1.w);points[i]=p;
                p.maxRayPrimaryError=rayErrors[i];
            }
            var settings=feature.lastSettings;
            return new Report{functionalPass=true,allPointsConverged=points.All(p=>p.converged),stableDomain=true,scheduledEditorTicks=feature.scheduled,lightingSignature=lightingSignature,sceneRevision=sceneRevision,skyRevision=skyRevision,environment=feature.lastEnvironment,alphaVisibility=feature.context.alphaVisibility,samplesPerPoint=feature.samplesPerPoint,batchCount=batches.Count,mainFrames=mainFrames,lightCount=feature.lastLightCount,instances=feature.lastInstances,cutouts=feature.lastCutouts,secondaryLightSamples=settings.secondaryLightSamples,secondaryEnvironmentSamples=settings.secondaryEnvironmentSamples,maxDistance=settings.maxDistance,rayBias=settings.rayBias,points=points,batches=batches.ToArray(),firstGuides=firstGuides,lastGuides=lastGuides};
        }
    }
}
