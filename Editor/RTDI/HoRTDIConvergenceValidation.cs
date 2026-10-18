using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool referenceConvergenceAudit,referenceConvergenceLongHistory,referenceConvergenceOriginalNrd,referenceConvergenceFastOne;
        private static bool referenceConvergenceMotionControl;
        private static bool referenceConvergenceMatchedPoint;
        private static bool referenceConvergenceAdaptiveAudit;
        private static bool referenceConvergenceShortMotion;
        private static bool referenceConvergenceBaselineRegression;
        public static void RunConvergence(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Convergence benchmark requires an isolated project");referenceConvergenceAudit=forceDenoise=true;Run();}
        public static void RunConvergenceReferenceHistory(){referenceConvergenceLongHistory=true;RunConvergence();}
        public static void RunConvergenceLongMainOnly(){referenceConvergenceFastOne=true;RunConvergenceReferenceHistory();}
        public static void RunConvergenceReferenceRELAX(){referenceConvergenceOriginalNrd=true;RunConvergence();}
        public static void RunConvergenceReferenceHistoryMotion(){referenceConvergenceMotionControl=true;RunCutoutMotionProductionQuality();}
        public static void RunConvergenceLongMainMotion(){referenceConvergenceFastOne=true;RunConvergenceReferenceHistoryMotion();}
        public static void RunMatchedConvergence(){referenceConvergenceMatchedPoint=true;RunConvergence();}
        public static void RunMatchedConvergenceReferenceHistory(){referenceConvergenceMatchedPoint=true;RunConvergenceReferenceHistory();}
        public static void RunMatchedConvergenceReferenceRELAX(){referenceConvergenceMatchedPoint=true;RunConvergenceReferenceRELAX();}
        public static void RunMatchedConvergenceAdaptive(){referenceConvergenceAdaptiveAudit=true;RunMatchedConvergence();}
        public static void RunConvergenceAdaptiveMotion(){referenceConvergenceAdaptiveAudit=true;RunCutoutMotionProductionQuality();}
        public static void RunConvergenceShortHistoryMotion(){referenceConvergenceShortMotion=referenceConvergenceAdaptiveAudit=referenceConvergenceMotionControl=referenceConvergenceFastOne=true;RunCutoutMotionProductionQuality();}
        public static void RunMatchedConvergenceAdaptiveCompatibility(){compatibility=true;RunMatchedConvergenceAdaptive();}
        public static void RunConvergenceAdaptiveMotionCompatibility(){compatibility=true;RunConvergenceAdaptiveMotion();}
        public static void RunConvergenceAdaptiveSpecularMotion(){referenceConvergenceAdaptiveAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunConvergenceAdaptiveDynamic(){referenceConvergenceAdaptiveAudit=true;RunRuntimeMultiDynamic();}
        public static void RunConvergenceAdaptiveDynamicCompatibility(){compatibility=true;RunConvergenceAdaptiveDynamic();}
        public static void RunConvergenceBaselinePrimary(){referenceConvergenceBaselineRegression=true;RunPrimaryMotionQuality();}
        public static void RunConvergenceBaselineReuse(){referenceConvergenceBaselineRegression=true;RunReuseNoise();}
        public static void RunConvergenceBaselineEnvironment(){referenceConvergenceBaselineRegression=true;RunRuntimeMultiEnvironment();}
        private static void ValidateConvergence()
        {
            const int size=128,components=32*32*3;
            target=Keep(new RenderTexture(size,size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            surfaceMaterial.SetFloat("_Reflectance",.5f);surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Smoothness",.5f);
            light.type=LightType.Directional;light.color=Color.red;light.intensity=1;light.transform.rotation=Quaternion.Euler(0,25,0);
            var blue=Point(Vector3.zero,Color.blue,1);blue.type=LightType.Directional;blue.transform.rotation=Quaternion.Euler(0,-25,0);
            if(referenceConvergenceMatchedPoint)
            {
                camera.orthographic=false;camera.fieldOfView=40;surfaceMaterial.SetFloat("_Reflectance",.04f);
                light.type=blue.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);blue.transform.position=new Vector3(-1.5f,0,-1);light.intensity=blue.intensity=4;
            }
            feature.Settings.temporal=feature.Settings.spatial=true;feature.Settings.useEnvironment=false;
            feature.Settings.finalSampling=HoRTDIFinalSampling.Single;feature.Settings.initialSamples=8;
            RenderCamera();var c=feature.LastContext;Require(c?.nrd!=null,"Convergence NRD context missing");
            ValidateConvergenceStabilityContract(c);
            if(!referenceConvergenceAdaptiveAudit)c.nrd.ValidationSettings=s=>{s.diffFrames=3;s.diffFast=1;return s;};
            if(referenceConvergenceLongHistory)c.nrd.ValidationSettings=s=>{s.diffFrames=30;s.diffFast=referenceConvergenceFastOne?1u:6u;return s;};
            using var originalNrd=referenceConvergenceOriginalNrd?new OriginalNrdConvergenceOverride(c):null;
            c.historyValid=c.nrdHistoryValid=false;c.frame=0;
            var image=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            string[] names={"rawDiffuse","filteredDiffuse","rawSpecular","filteredSpecular","rawCombined","filteredCombined"};
            var stats=new HoRTDIConvergenceStatistics[names.Length];for(int i=0;i<stats.Length;i++)stats[i]=new HoRTDIConvergenceStatistics(components);
            var combined=new[]{new double[components],new double[components]};
            var values=new double[components];var results=new List<HoRTDIConvergenceStatistics.Result>();int first=1,resets=0,stableFrames=0;
            for(int frame=1;frame<=512;frame++)
            {
                c.nrd.ValidationTimeSeconds=frame/60.0;RenderCamera();if(c.lastNrdReset)resets++;
                if(c.nrd.StableDiffuseHistory)stableFrames++;
                Require(c.nrd.LastDispatchCount>0&&c.auxiliaryCDF==null,"Convergence production path not active");
                var textures=new[]{c.diffuse.rt,c.filteredDiffuse.rt,c.specular.rt,c.filteredSpecular.rt};
                for(int signal=0;signal<4;signal++)
                {
                    ReadImage(textures[signal],image);int p=0;
                    for(int y=48;y<80;y++)for(int x=48;x<80;x++){var color=image.GetPixel(x,y);Require(color.a>.5f,"Convergence receiver invalid");values[p++]=color.r;values[p++]=color.g;values[p++]=color.b;}
                    stats[signal].Add(values);
                    for(int i=0;i<components;i++){if(signal<2)combined[signal][i]=values[i];else combined[signal-2][i]+=values[i];}
                }
                stats[4].Add(combined[0]);stats[5].Add(combined[1]);
                if(frame==8||frame==32||frame==128||frame==256||frame==512)
                {
                    for(int signal=0;signal<stats.Length;signal++){var result=stats[signal].Summarize(names[signal],first,frame);results.Add(result);Debug.Log("[HoRTDIConvergence] "+JsonUtility.ToJson(result));stats[signal]=new HoRTDIConvergenceStatistics(components);}
                    first=frame+1;
                }
            }
            Require(resets==1,"Static convergence history reset unexpectedly: "+resets);
            if(referenceConvergenceAdaptiveAudit)Require(stableFrames>400&&c.nrd.AppliedSettings.diffFrames==30,"Static history did not promote to the stable budget");
            string variant=referenceConvergenceOriginalNrd?"original-relax-same-input":referenceConvergenceFastOne?"long-main-only":referenceConvergenceLongHistory?"reference-history":"production";
            if(referenceConvergenceAdaptiveAudit)variant="adaptive-production";
            if(referenceConvergenceMatchedPoint)variant="matched-point-"+variant;
            if(compatibility)variant+="-compat";
            var report=new ConvergenceReport{variant=variant,historyResets=resets,stableFrames=stableFrames,results=results.ToArray()};
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-convergence108-"+variant+".json"),JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDIConvergence] CAPTURE_COMPLETE "+variant);
        }
        private static void ValidateConvergenceStabilityContract(HoRTDIContext context)
        {
            double? previousOverride=context.nrd.ValidationTimeSeconds;
            context.nrd.ValidationTimeSeconds=null;
            try
            {
            foreach(int fps in new[]{30,60,120})
            {
                double start=100+fps;
                context.nrd.SetRenderTime(start,out _);context.nrd.ObserveLightingStability(true);
                for(int i=1;i<fps/2;i++){context.nrd.SetRenderTime(start+i/(double)fps,out _);context.nrd.ObserveLightingStability(false);Require(!context.nrd.StableDiffuseHistory,"Stable budget promoted before half a second");}
                context.nrd.SetRenderTime(start+.5,out _);context.nrd.ObserveLightingStability(false);Require(context.nrd.StableDiffuseHistory,"Stable budget depends on frame count instead of elapsed time");
                context.nrd.SetRenderTime(start+.5+1.0/fps,out _);Require(context.nrd.ObserveLightingStability(true)&&!context.nrd.StableDiffuseHistory,"Motion did not restart settled NRD history");
            }
            context.nrd.SetRenderTime(1,out bool rewind);context.nrd.ObserveLightingStability(rewind);Require(!context.nrd.StableDiffuseHistory,"Clock rewind retained stable history");
            for(int i=0;i<100;i++){context.nrd.SetRenderTime(1,out _);context.nrd.ObserveLightingStability(false);Require(!context.nrd.StableDiffuseHistory,"Repeated same-time requests fabricated stable elapsed time");}
            context.nrd.SetRenderTime(double.NaN,out bool invalid);context.nrd.ObserveLightingStability(invalid);Require(!context.nrd.StableDiffuseHistory,"Invalid clock retained stable history");
            Debug.Log("[HoRTDIConvergence] STABILITY_CONTRACT_PASS 30/60/120 Hz, motion transition, rewind, same-time, invalid time");
            }
            finally{context.nrd.ValidationTimeSeconds=previousOverride;}
        }
        private static IDisposable ApplyConvergenceMotionHistory(HoRTDIContext context)=>referenceConvergenceMotionControl?new ConvergenceMotionHistory(context):null;
        private sealed class ConvergenceMotionHistory:IDisposable
        {
            readonly HoRTDIContext context;
            readonly bool previousStabilityOverride;
            readonly Func<lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.RelaxSettings,lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.RelaxSettings> previous;
            internal ConvergenceMotionHistory(HoRTDIContext context)
            {
                this.context=context;previous=context.nrd.ValidationSettings;
                previousStabilityOverride=context.nrd.ValidationDisableStableDiffuseHistory;context.nrd.ValidationDisableStableDiffuseHistory=true;
                context.nrd.ValidationSettings=s=>{if(previous!=null)s=previous(s);s.diffFrames=referenceConvergenceShortMotion?3u:30u;s.diffFast=referenceConvergenceFastOne?1u:6u;return s;};
            }
            public void Dispose(){context.nrd.ValidationSettings=previous;context.nrd.ValidationDisableStableDiffuseHistory=previousStabilityOverride;}
        }
        private sealed class OriginalNrdConvergenceOverride:IDisposable
        {
            readonly HoRTDIContext context;
            readonly ComputeShader[] shaders,previous;
            readonly int[] indices={4,6,9,10};
            internal OriginalNrdConvergenceOverride(HoRTDIContext context)
            {
                this.context=context;
                shaders=(ComputeShader[])context.nrd.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.nrd);
                previous=(ComputeShader[])shaders.Clone();
                string[] bodies={"RELAX_TemporalAccumulation.cs.hlsl","RELAX_HistoryClamping.cs.hlsl","RELAX_AtrousSmem.cs.hlsl","RELAX_Atrous.cs.hlsl"};
                const string package="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/";
                const string folder="Assets/HoRTDIOriginalNrd108";
                Directory.CreateDirectory(Path.Combine(Application.dataPath,"HoRTDIOriginalNrd108"));
                for(int i=0;i<indices.Length;i++)
                {
                    string asset=folder+"/Original"+indices[i]+".compute";
                    string source="#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n#define NRD_SIGNAL BOTH\n#define NRD_MODE RADIANCE\n#include \""+package+"Shaders/NRD/HoRTDINrdBindings.hlsl\"\n#include \""+package+"ThirdParty/NRD/"+bodies[i]+"\"\n";
                    string path=Path.Combine(Application.dataPath,"../"+asset);
                    if(!File.Exists(path)||File.ReadAllText(path)!=source)File.WriteAllText(path,source);
                    AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                    var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset);Require(shader!=null,"Original NRD wrapper missing");
                    foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                    shaders[indices[i]]=shader;
                }
                // Exact RelaxSettings defaults in the user's local reference project.
                context.nrd.ValidationSettings=s=>{s.acceleration=.3f;s.reset=.5f;s.diffFrames=s.specFrames=30;s.diffFast=s.specFast=6;s.historyFixFrames=3;s.diffPrepass=30;s.specPrepass=50;s.varianceHistory=3;s.minDiffMaterial=s.minSpecMaterial=4;return s;};
            }
            public void Dispose(){foreach(int i in indices)shaders[i]=previous[i];context.nrd.ValidationSettings=null;}
        }
        [Serializable] private sealed class ConvergenceReport
        {
            public bool diagnosticOnly=true;
            public string variant;
            public int historyResets;
            public int stableFrames;
            public HoRTDIConvergenceStatistics.Result[] results;
        }
    }
}
