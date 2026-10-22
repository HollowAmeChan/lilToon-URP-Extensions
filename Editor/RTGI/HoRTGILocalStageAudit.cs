using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Observation only: never overrides settings, pool formats, or GPU outputs.
    internal sealed class HoRTGILocalStageAudit:IDisposable
    {
        internal static readonly int[] CaptureIds={3,4,5,6,9,10};
        internal static readonly int[] StageIds={-2,-1,3,4,5,6,9,10,100};
        internal static readonly int[] PropagationIds={-2,-1,3,4,5,6,9,101,102,103,104,100};
        [Serializable]internal sealed class Stage{public int pipeline;public string meaning;public Vector3 rgb;public long negativeComponents;}
        [Serializable]internal sealed class Point{public int x,y;public Stage[] stages;}
        [Serializable]internal sealed class Capture{public int pipeline,writes,width,height;public string format;public bool uav;}
        [Serializable]internal sealed class Binding{public int pipeline,dispatch,resourceIndex,poolIndex;public uint descriptorType;public string binding,resourceType;}
        [Serializable]internal sealed class Segment
        {
            public int seed,warmup,measured,firstShaderFrame,lastShaderFrame,firstApplicationFrame,lastApplicationFrame;
            public string stream,streamSha256;public bool actualTemporal;public float maxPrimaryError;
            public int lightCount,instances,cutouts,lightingSignature;public uint sceneRevision,skyRevision;
            public HoRTGISpatialAudit.Report spatial;public Point[] points;public Capture[] captures;public Binding[] bindings;
            public string weightStream,weightStreamSha256,publicationMap,publicationMapSha256;public int weightFrames;
        }
        [Serializable]internal sealed class Report
        {
            public bool complete;public string referenceInput,referenceArchiveSha256,referenceRawSha256;
            public int[] seeds,stageIds;public int width=256,height=256,warmup,measured;
            public HoRTGIPoolPrecisionAudit.Report precision;public Segment[] segments;
            public HoRTGIAtrousTrace.Report trace;public HoRTGIDenseReferenceAudit.Point[] referencePoints;public string legacyReferenceInput,legacyReferenceSha256;
        }
        [Serializable]sealed class ReferenceFile{public int width,height;public HoRTGIDenseReferenceAudit.Report denseReference;}
        readonly HoRTGIContext context;readonly HoRTGIDenseReferenceFeature feature;
        readonly HoRTGIPoolPrecisionAudit precision;readonly Func<RenderTexture,Color[]> read;
        readonly Dictionary<int,RenderTexture> captured=new();readonly Dictionary<int,int> writes=new(),totals=new();
        readonly List<Segment> segments=new();HoRTGIDenseReferenceAudit.Point[] reference;
        readonly Report report;readonly bool propagation;readonly int[] stageIds;readonly bool[] guideInitialized;readonly HoRTGIAtrousTrace trace;BinaryWriter stream,weightStream;Segment current;HoRTGISpatialAudit spatial;
        double[,,] sums;long[,] negative;int observed;float maxPrimaryError;bool disposed;
        internal HoRTGILocalStageAudit(HoRTGIContext context,HoRTGIDenseReferenceFeature feature,Func<RenderTexture,Color[]> read,int[] seeds,int warmup,int measured,bool propagation=false,bool traceControl=false)
        {
            this.context=context;this.feature=feature;this.read=read;this.propagation=propagation;stageIds=propagation?PropagationIds:StageIds;
            if(context.width!=256||context.height!=256||context.nrd==null||context.nrd.core.ValidationDiffuseStage!=null)throw new Exception("Local stage scope/domain is not available");
            string relative="Tests~/RTGI/LocalReference/results/reference.json.gz";
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));byte[] packed=File.ReadAllBytes(Path.Combine(root,relative));byte[] raw;
            using(var input=new GZipStream(new MemoryStream(packed),CompressionMode.Decompress))using(var output=new MemoryStream()){input.CopyTo(output);raw=output.ToArray();}
            var file=JsonUtility.FromJson<ReferenceFile>(System.Text.Encoding.UTF8.GetString(raw).TrimStart('\ufeff'));
            reference=file.denseReference.points;
            if(file.width!=256||file.height!=256||!file.denseReference.allPointsConverged||reference.Length!=18||!reference.Select(p=>new Vector2Int(p.x,p.y)).SequenceEqual(HoRTGIDenseReferenceAudit.LocalPixels))throw new Exception("Expected all18 qualified local reference receivers");
            string legacyPath=null,legacyHash=null;
            if(propagation)
            {
                legacyPath="Tests~/RTGI/DenseReference/results/fixed-reference.json";byte[] legacyBytes=File.ReadAllBytes(Path.Combine(root,legacyPath));legacyHash=Hash(legacyBytes);
                var legacy=JsonUtility.FromJson<ReferenceFile>(System.Text.Encoding.UTF8.GetString(legacyBytes).TrimStart('\ufeff')).denseReference.points.First(p=>p.x==141&&p.y==157);
                if(!legacy.converged)throw new Exception("Counterexample reference must be qualified");
                reference=reference.Concat(new[]{legacy}).Concat(HoRTGIAtrousTrace.Pixels.Skip(19).Select(p=>new HoRTGIDenseReferenceAudit.Point{x=p.x,y=p.y,group="unreferenced neighbor",samples=0,converged=false})).ToArray();
            }
            guideInitialized=reference.Select(p=>p.samples>0).ToArray();
            report=new Report{legacyReferenceInput=legacyPath,legacyReferenceSha256=legacyHash,referencePoints=reference,referenceInput=relative,referenceArchiveSha256=Hash(packed),referenceRawSha256=Hash(raw),seeds=seeds,stageIds=stageIds,warmup=warmup,measured=measured};
            precision=new HoRTGIPoolPrecisionAudit(context.nrd,"production");
            if(propagation){trace=new HoRTGIAtrousTrace(context.nrd.core,!traceControl);report.trace=trace.report;}
            context.nrd.core.ValidationDiffuseStage=CaptureStage;
        }
        void CaptureStage(CommandBuffer cmd,int pipeline,RenderTargetIdentifier source)
        {
            if(!CaptureIds.Contains(pipeline))throw new Exception("Unexpected diffuse stage writer: "+pipeline);
            int ordinal=writes.TryGetValue(pipeline,out int previousWrites)?previousWrites+1:1;
            int key=propagation&&pipeline==10?100+ordinal:pipeline;
            if(!captured.TryGetValue(key,out var destination))
            {
                destination=new RenderTexture(256,256,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){enableRandomWrite=true};
                if(!destination.Create())throw new Exception("Local stage allocation failed");captured.Add(key,destination);
            }
            precision.Copy(cmd,source,destination);writes.TryGetValue(pipeline,out int count);writes[pipeline]=count+1;
            if(trace!=null&&pipeline==9)trace.Bind(cmd);
        }
        internal void BeginFrame(bool collectWeights=false){writes.Clear();if(trace!=null)trace.enabled=collectWeights&&trace.replacementActive;}
        internal void Begin(int seed,string output)
        {
            observed=0;maxPrimaryError=0;sums=new double[stageIds.Length,reference.Length,3];negative=new long[stageIds.Length,reference.Length];totals.Clear();
            current=new Segment{seed=seed,warmup=report.warmup,measured=report.measured,firstShaderFrame=seed+report.warmup,lastShaderFrame=seed+report.warmup+report.measured-1,stream="stages-"+seed+".bin"};
            spatial=new HoRTGISpatialAudit(256,256);stream=new BinaryWriter(File.Create(Path.Combine(output,current.stream)));
            stream.Write(new byte[]{(byte)'H',(byte)'G',(byte)(propagation?'P':'S'),(byte)'1'});
            foreach(int value in new[]{256,256,reference.Length,stageIds.Length,report.measured,seed})stream.Write(value);
            foreach(int id in stageIds)stream.Write(id);foreach(var point in reference){stream.Write(point.x);stream.Write(point.y);}
            if(propagation){current.weightStream="weights-"+seed+".bin";weightStream=new BinaryWriter(File.Create(Path.Combine(output,current.weightStream)));weightStream.Write(new byte[]{(byte)'H',(byte)'G',(byte)'W',(byte)'1'});foreach(int value in new[]{reference.Length,4,HoRTGIAtrousTrace.VectorsPerPoint,seed})weightStream.Write(value);}
        }
        internal void ValidateProbe(bool first)
        {
            var settings=feature.lastSettings;
            if(feature.lastLightCount!=18||feature.lastInstances!=15||feature.lastCutouts!=12||!feature.lastEnvironment||!context.alphaVisibility||!settings.temporal||!settings.spatial||settings.secondaryLightSamples!=8||settings.secondaryEnvironmentSamples!=4||settings.spatialSamples!=4||settings.spatialRadius!=16||settings.maxHistory!=20||settings.maxDistance!=1000||settings.rayBias!=.001f||settings.includeDirectEnvironment)throw new Exception("Local stage primary/support/sampling domain changed");
            var probes=new HoRTGIDenseReferenceFeature.PrimaryProbe[reference.Length];feature.probe.GetData(probes);var rays=new Vector4[reference.Length*4];feature.rayProbe.GetData(rays);
            for(int i=0;i<reference.Length;i++)
            {
                var p=probes[i];var expected=reference[i];
                if(!guideInitialized[i]){expected.primaryWorld=p.rabWorld;expected.normal=p.normalDepth;expected.viewDepth=p.normalDepth.w;expected.albedo=p.albedo;guideInitialized[i]=true;}
                float error=Mathf.Max(Vector3.Distance(p.rabWorld,expected.primaryWorld),Vector3.Distance(p.normalDepth,expected.normal),Vector3.Distance(p.albedo,expected.albedo),Mathf.Abs(p.normalDepth.w-expected.viewDepth));
                if(p.originalWorld.w<.5f||p.rabWorld.w<.5f)throw new Exception("Invalid local stage primary");
                var values=new[]{p.originalWorld,p.rabWorld,p.normalDepth,p.albedo};
                for(int c=0;c<4;c++)
                {
                    if(!float.IsFinite(p.errors0[c])||!float.IsFinite(p.errors1[c]))throw new Exception("Nonfinite local primary comparison");
                    error=Mathf.Max(error,p.errors0[c],p.errors1[c]);
                    for(int v=0;v<4;v++)error=Mathf.Max(error,Mathf.Abs(values[v][c]-rays[i*4+v][c]));
                }
                if(!float.IsFinite(error)||error>.0005f)throw new Exception("Local full/RAB/history/ray/reference primary mismatch");maxPrimaryError=Mathf.Max(maxPrimaryError,error);
            }
            if(first)
            {
                current.lightCount=feature.lastLightCount;current.instances=feature.lastInstances;current.cutouts=feature.lastCutouts;
                current.lightingSignature=feature.lastLightingSignature;current.sceneRevision=feature.lastSceneRevision;current.skyRevision=feature.lastSkyRevision;
            }
            else if(current.lightingSignature!=feature.lastLightingSignature||current.sceneRevision!=feature.lastSceneRevision||current.skyRevision!=feature.lastSkyRevision)throw new Exception("Local stage light/scene/sky changed");
        }
        internal void AddFrame()
        {
            if(context.frame!=current.firstShaderFrame+observed+1||!context.lastTemporal)throw new Exception("Local stage shader/temporal sequence changed");
            foreach(int id in CaptureIds)
            {
                if(!writes.TryGetValue(id,out int count)||count<=0||!captured[propagation&&id==10?104:id].enableRandomWrite||captured[propagation&&id==10?104:id].graphicsFormat!=GraphicsFormat.R32G32B32A32_SFloat)throw new Exception("Local stage was not actually captured: "+id);
                totals.TryGetValue(id,out int previous);totals[id]=previous+count;
            }
            var raw=read(context.diffuse.rt);var published=read(context.PublishedDiffuse.rt);spatial.AddPublication(raw,published,observed);
            var guides=new HoRTGICastleValidation.Primary[256*256];context.history.GetData(guides);
            stream.Write(context.frame-1);stream.Write(Time.frameCount);foreach(int id in CaptureIds)stream.Write(writes[id]);
            for(int stage=0;stage<stageIds.Length;stage++)
            {
                int id=stageIds[stage];Color[] values=id==-2?raw:id==100?published:id==-1?read(context.nrd.signal.rt):read(captured[id]);
                for(int p=0;p<reference.Length;p++)
                {
                    int at=reference[p].y*256+reference[p].x;uint albedo=guides[at].diffuse;
                    for(int c=0;c<3;c++)
                    {
                        float factor=((albedo>>(8*c))&255)/255f;
                        if(Mathf.Abs(factor-reference[p].albedo[c])>.00001f)throw new Exception("Local stage actual albedo changed");
                        float value=values[at][c];if(id!=-2&&id!=100)value*=context.nrd.AppliedSignalScale*factor;
                        if(!float.IsFinite(value))throw new Exception("Nonfinite local stage RGB");
                        stream.Write(value);sums[stage,p,c]+=value;if(value<0)negative[stage,p]++;
                    }
                }
            }
            if(trace!=null&&trace.enabled)
            {
                var values=trace.Read();weightStream.Write(observed);weightStream.Write(context.frame-1);weightStream.Write(Time.frameCount);
                foreach(var v in values){weightStream.Write(v.x);weightStream.Write(v.y);weightStream.Write(v.z);weightStream.Write(v.w);}current.weightFrames++;
            }
            if(observed==0)current.firstApplicationFrame=Time.frameCount;current.lastApplicationFrame=Time.frameCount;observed++;
        }
        internal void End(string output)
        {
            if(observed!=report.measured)throw new Exception("Incomplete local stage window");stream.Dispose();stream=null;
            if(propagation){weightStream.Dispose();weightStream=null;current.weightStreamSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.weightStream)));current.publicationMap="publication-"+current.seed+".bin";spatial.WritePublicationMeans(Path.Combine(output,current.publicationMap));current.publicationMapSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.publicationMap)));}
            current.actualTemporal=context.lastTemporal;current.maxPrimaryError=maxPrimaryError;current.spatial=spatial.Finish();current.streamSha256=Hash(File.ReadAllBytes(Path.Combine(output,current.stream)));
            current.points=reference.Select((point,p)=>new Point{x=point.x,y=point.y,stages=stageIds.Select((id,s)=>new Stage{pipeline=id,meaning=Meaning(id),rgb=new Vector3((float)(sums[s,p,0]/observed),(float)(sums[s,p,1]/observed),(float)(sums[s,p,2]/observed)),negativeComponents=negative[s,p]}).ToArray()}).ToArray();
            current.captures=CaptureIds.Select(id=>new Capture{pipeline=id,writes=totals[id],width=256,height=256,format=captured[propagation&&id==10?104:id].graphicsFormat.ToString(),uav=captured[propagation&&id==10?104:id].enableRandomWrite}).ToArray();
            current.bindings=Bindings();segments.Add(current);current=null;spatial=null;
        }
        Binding[] Bindings()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var core=context.nrd.core;var passes=(HoRTDINrdNative.DispatchDesc[])typeof(HoRTDINrdContext).GetField("passes",flags).GetValue(core);
            var resources=(HoRTDINrdNative.ResourceDesc[,])typeof(HoRTDINrdContext).GetField("resources",flags).GetValue(core);var result=new List<Binding>();
            for(int i=0;i<core.LastDispatchCount;i++)for(int j=0;j<passes[i].resourceCount;j++)
            {
                int id=(int)passes[i].pipeline;var r=resources[i,j];string name=HoRTGINrdManifest.Resources[id][j];
                if(r.descriptorType==1&&(name=="gOut_Diff"||name=="gOut_Diff_Variance"))result.Add(new Binding{pipeline=id,dispatch=i,resourceIndex=j,poolIndex=(int)r.poolIndex,descriptorType=r.descriptorType,binding=name,resourceType=HoRTDINrdNative.ResourceName(r.type)});
            }
            return result.ToArray();
        }
        internal Report Snapshot(bool terminal)
        {
            report.segments=segments.ToArray();report.complete=terminal&&segments.Count==report.seeds.Length;
            if(terminal&&!report.complete)throw new Exception("Local stage seed contract incomplete");report.precision=precision.Snapshot();return report;
        }
        internal static string Meaning(int id)=>id switch{-2=>"Physical raw",-1=>"NRD input remodulated",3=>"PrePass RGB",4=>"Temporal slow RGB",5=>"Conditional HistoryFix / fast RGB",6=>"Clamping slow RGB",9=>"First Atrous RGB",10=>"Last Atrous RGB",100=>"Physical publication",101=>"Atrous ordinal1 / step2",102=>"Atrous ordinal2 / step4",103=>"Atrous ordinal3 / step8",104=>"Atrous ordinal4 / step16",_=>throw new Exception("Unknown local stage")};
        static string Hash(byte[] value){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(value)).Replace("-","");}
        public void Dispose()
        {
            if(disposed)return;disposed=true;stream?.Dispose();weightStream?.Dispose();trace?.Dispose();context.nrd.core.ValidationDiffuseStage=null;precision.Dispose();
            foreach(var texture in captured.Values){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}captured.Clear();
        }
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunLocalStageQualification()=>Run(false,256,denseScheduled:true,denseLocal:true,localStages:true,seedWarmup:16,seedMeasured:16,validationTag:"local-stage-qualification");
        public static void RunLocalStages()=>Run(false,256,denseScheduled:true,denseLocal:true,localStages:true,validationTag:"local-stages");
        public static void RunPropagationQualification()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,seedWarmup:16,seedMeasured:16,validationTag:"propagation-qualification");
        public static void RunPropagationControlQualification()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,seedWarmup:16,seedMeasured:16,validationTag:"propagation-control-qualification");
        public static void RunPropagationControl()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,validationTag:"propagation-control");
        public static void RunPropagation()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,validationTag:"propagation");
        static async Task<HoRTGILocalStageAudit.Report> AuditLocalStages(HoRTGIRendererFeature gi,HoRTGIContext context,HoRTGIDenseReferenceFeature feature,Action render,Func<RenderTexture,Color[]> read,int warmup,int measured,string output,bool propagation=false,bool traceControl=false)
        {
            int[] seeds=measured==16?new[]{0,104729}:new[]{0,104729,5347609,6396185};int oldFrame=context.frame;bool temporal=gi.Settings.temporal,spatial=gi.Settings.spatial;
            using var audit=new HoRTGILocalStageAudit(context,feature,read,seeds,warmup,measured,propagation,traceControl);
            try
            {
                gi.Settings.temporal=gi.Settings.spatial=true;
                foreach(int seed in seeds)
                {
                    context.historyValid=false;context.nrd.historyValid=false;context.frame=seed;feature.collect=false;
                    for(int i=0;i<warmup;i++){await NextEditorTick();audit.BeginFrame();render();}
                    audit.Begin(seed,output);
                    for(int i=0;i<measured;i++)
                    {
                        await NextEditorTick();feature.collect=i==0||i==measured-1;feature.batch=Array.IndexOf(seeds,seed);audit.BeginFrame(propagation&&(i==0||i==measured-1||(measured>16&&i%32==0)));render();
                        if(feature.collect)audit.ValidateProbe(i==0);audit.AddFrame();
                    }
                    feature.collect=false;audit.End(output);File.WriteAllText(Path.Combine(output,"local-stage-progress.json"),JsonUtility.ToJson(audit.Snapshot(false),false));
                    Debug.Log("[HoGILocalStages] seed="+seed+" complete");
                }
                var report=audit.Snapshot(true);Debug.Log("[HoGILocalStages] COMPLETE segments="+report.segments.Length);return report;
            }
            finally{feature.collect=false;gi.Settings.temporal=temporal;gi.Settings.spatial=spatial;context.historyValid=false;context.nrd.historyValid=false;context.frame=oldFrame;}
        }
    }
}
