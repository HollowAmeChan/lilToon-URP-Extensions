#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool volumeControlsAudit;
        public static void RunVolumeControls(){volumeControlsAudit=true;RunEnvironment();}
        public static void RunVolumeControlsCompatibility(){compatibility=true;RunVolumeControls();}
        [Serializable] private sealed class VolumeControlsReport
        {
            public bool pass,compatibility,legacyFallback,featureUntouched;
            public float localWeight;
            public Color cameraA,cameraB,expectedA,expectedB;
            public List<string> checks=new();
        }
        private static void ValidateVolumeControls()
        {
            var report=new VolumeControlsReport{compatibility=compatibility,localWeight=.5f};
            void Check(bool ok,string label){Require(ok,label);report.checks.Add(label);}
            void RGB(Color actual,Color expected,string label,float tolerance=.006f){NearRGB(actual,expected,label,tolerance);report.checks.Add(label);}
            void ClearAppearance(HoRTDIVolume v)
            {v.composite.overrideState=v.compositeMode.overrideState=v.compositeWeight.overrideState=v.additiveIntensity.overrideState=v.useEnvironment.overrideState=v.environmentIntensity.overrideState=v.denoise.overrideState=false;}
            var legacy=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/RTDIPlayerProbe/Volume.asset");
            Check(legacy!=null&&legacy.TryGet<HoRTDIVolume>(out _),"Legacy serialized Volume fixture available");
            legacy.TryGet<HoRTDIVolume>(out var oldVolume);
            Check(!oldVolume.composite.overrideState&&!oldVolume.compositeMode.overrideState&&!oldVolume.compositeWeight.overrideState
                &&!oldVolume.additiveIntensity.overrideState&&!oldVolume.useEnvironment.overrideState&&!oldVolume.environmentIntensity.overrideState&&!oldVolume.denoise.overrideState,"Old profile leaves new appearance overrides unchecked");
            feature.Settings.composite=false;feature.Settings.compositeMode=HoRTDICompositeMode.Replace;feature.Settings.additiveIntensity=1.5f;feature.Settings.compositeWeight=1;
            feature.Settings.denoise=true;feature.Settings.useEnvironment=true;feature.Settings.environmentIntensity=.75f;
            light.type=LightType.Directional;light.transform.rotation=Quaternion.identity;light.intensity=2;light.shadows=LightShadows.None;blocker.SetActive(false);
            string source=JsonUtility.ToJson(feature.Settings);
            var inherited=new HoRTDIViewSettings(feature.Settings,oldVolume);
            Check(!inherited.composite&&inherited.denoise&&inherited.useEnvironment&&inherited.environmentIntensity==.75f,"Legacy profile retains existing Feature appearance");
            RenderCamera();var original=Read(target);var c=feature.LastContext;
            Check(c.nrd!=null&&c.environmentSampling,"Unchecked overrides use actual legacy NRD/sky settings");report.legacyFallback=true;

            // Exercise the actual blended camera stack, not just IsActive on a
            // profile component. A disabled Volume must not dispatch stale DI.
            var holder=Array.Find(UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None),v=>v.sharedProfile!=null&&v.sharedProfile.components.Contains(volume));
            Check(holder!=null,"Global Volume fixture found");
            var fresh=Keep(ScriptableObject.CreateInstance<HoRTDIVolume>());
            Check(!fresh.enable.value&&!fresh.IsActive()&&!new HoRTDIViewSettings(feature.Settings,null).enabled,"Missing/new Volume defaults to disabled");
            void Disabled(string label)
            {
                int frame=c.frame;RenderCamera();
                Check(c.frame==frame&&!c.historyValid&&!c.nrdHistoryValid&&Shader.GetGlobalFloat("_HoRTDIActive")==0,label);
                Check(Shader.GetGlobalTexture("_HoRTDIDiffuseTexture")==Texture2D.blackTexture,label+" clears published output");
                RGB(Read(target),original,label+" preserves camera");
            }
            volume.enable.overrideState=false;Disabled("Unchecked enable override disables production");volume.enable.Override(true);
            volume.enable.Override(false);Disabled("Explicit Volume off disables production");volume.enable.Override(true);
            volume.active=false;Disabled("Inactive Volume component disables production");volume.active=true;
            holder.weight=0;Disabled("Zero Volume weight disables production");holder.weight=1;
            holder.enabled=false;Disabled("Feature without participating Volume stays disabled");holder.enabled=true;
            var profile=holder.sharedProfile;holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            Disabled("Profile without RTDI component stays disabled");holder.sharedProfile=profile;
            RenderCamera();Check(c.lastNrdReset&&c.historyValid,"Reenabled Volume restarts valid history");

            volume.composite.Override(true);volume.compositeMode.Override(HoRTDICompositeMode.Additive);volume.additiveIntensity.Override(.25f);
            volume.compositeWeight.Override(0);volume.useEnvironment.Override(false);volume.environmentIntensity.Override(.25f);volume.denoise.Override(false);
            RenderCamera();RGB(Read(target),AddExpected(original,Direct(),.25f),"Volume additive gain reaches final camera");
            Check(c.nrd==null&&!c.environmentSampling&&c.environmentPdf==null,"Volume disables actual NRD/sky resources");
            volume.compositeMode.Override(HoRTDICompositeMode.Replace);volume.compositeWeight.Override(.5f);RenderCamera();
            RGB(Read(target),Color.Lerp(original,CompositePublished(),.5f),"Volume replacement mode/weight reaches final camera");
            volume.enable.Override(false);RenderCamera();RGB(Read(target),original,"Volume enable disables composition");volume.enable.Override(true);
            volume.compositeMode.Override(HoRTDICompositeMode.Additive);volume.useEnvironment.Override(true);volume.environmentIntensity.Override(0);RenderCamera();
            var analytic=ReadMean(c.diffuse.rt);Check(!c.environmentSampling,"Volume sky intensity0 disables sampling");
            volume.environmentIntensity.Override(.25f);var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var skyMean=EnvironmentMean(image,8,128,false);RGB(skyMean,analytic+new Color(.25f,.125f,.0625f,0),"Volume sky gain preserves independent analytic light",.025f);

            // A real local Volume blends over the global .25 baseline. Two
            // cameras share a renderer but use independent Volume triggers.
            volume.useEnvironment.Override(false);
            var localObject=Keep(new GameObject("RTDI local override"));localObject.transform.position=camera.transform.position;
            var box=localObject.AddComponent<BoxCollider>();box.size=Vector3.one*4;box.isTrigger=true;
            var local=localObject.AddComponent<Volume>();local.isGlobal=false;local.blendDistance=0;local.weight=.5f;local.priority=10;
            local.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());var localSettings=local.sharedProfile.Add<HoRTDIVolume>();
            localSettings.additiveIntensity.Override(1);localSettings.denoise.Override(true);
            volume.localSampling.Override(HoRTDILocalSampling.Uniform);volume.finalSampling.Override(HoRTDIFinalSampling.Single);
            volume.initialSamples.Override(8);volume.temporal.Override(false);volume.maxHistory.Override(4);volume.spatial.Override(false);
            volume.spatialSamples.Override(4);volume.spatialRadius.Override(8);volume.environmentSamples.Override(4);volume.environmentBrdfSamples.Override(4);
            localSettings.localSampling.Override(HoRTDILocalSampling.Power);localSettings.finalSampling.Override(HoRTDIFinalSampling.TripleAnalytic);
            localSettings.initialSamples.Override(24);localSettings.temporal.Override(true);localSettings.maxHistory.Override(10);localSettings.spatial.Override(true);
            localSettings.spatialSamples.Override(8);localSettings.spatialRadius.Override(16);localSettings.environmentSamples.Override(8);localSettings.environmentBrdfSamples.Override(0);
            void Quality(bool inside)
            {
                const BindingFlags inspect=BindingFlags.NonPublic|BindingFlags.Instance;
                var stack=VolumeManager.instance.stack.GetComponent<HoRTDIVolume>();var resolved=new HoRTDIViewSettings(feature.Settings,stack);
                Check(resolved.localSampling==(inside?HoRTDILocalSampling.Power:HoRTDILocalSampling.Uniform)&&resolved.finalSampling==(inside?HoRTDIFinalSampling.TripleAnalytic:HoRTDIFinalSampling.Single),"Per-camera sampling enums "+(inside?"A":"B"));
                Check(resolved.initialSamples==(inside?16:8)&&resolved.temporal==inside&&resolved.maxHistory==(inside?7:4)&&resolved.spatial==inside
                    &&resolved.spatialSamples==(inside?6:4)&&resolved.spatialRadius==(inside?12:8)&&resolved.environmentSamples==(inside?6:4)&&resolved.environmentBrdfSamples==(inside?2:4),"Weighted sampling sliders and toggles "+(inside?"A":"B"));
                var pass=feature.GetType().GetField("pass",inspect).GetValue(feature);
                var passSettings=pass.GetType().GetField("settings",inspect).GetValue(pass);
                foreach(string name in new[]{"initialSamples","temporal","maxHistory","spatial","spatialSamples","spatialRadius","environmentSamples","environmentBrdfSamples"})
                    Check(Equals(passSettings.GetType().GetField(name,inspect).GetValue(passSettings),typeof(HoRTDIViewSettings).GetField(name,inspect).GetValue(resolved)),"GPU pass captures Volume "+name+" "+(inside?"A":"B"));
                Check((int)pass.GetType().GetField("requestedFinalSamples",inspect).GetValue(pass)==(inside?3:1),"GPU final sample budget follows Volume "+(inside?"A":"B"));
            }
            var outside=Keep(new GameObject("Outside Volume trigger"));outside.transform.position=camera.transform.position+Vector3.right*20;
            var second=Keep(new GameObject("Volume-isolated camera B")).AddComponent<Camera>();second.CopyFrom(camera);second.enabled=false;
            second.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
            var secondData=second.GetUniversalAdditionalCameraData();secondData.volumeLayerMask=-1;secondData.volumeTrigger=outside.transform;
            var secondTarget=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));secondTarget.Create();
            RenderCamera();report.cameraA=Read(target);report.expectedA=AddExpected(original,Direct(),.625f);RGB(report.cameraA,report.expectedA,"Local Volume weight .5 produces .625 additive gain");
            Check(c.nrd!=null,"Local Volume enables camera A denoiser");
            Quality(true);
            VolumeManager.instance.Update(outside.transform,-1);RenderPipeline.SubmitRenderRequest(second,new UniversalRenderPipeline.SingleCameraRequest{destination=secondTarget});
            var b=feature.ContextForCamera(second);report.cameraB=Read(secondTarget);
            var bDirect=Read(b.PublishedDiffuse.rt)+Read(b.PublishedSpecular.rt);report.expectedB=AddExpected(original,bDirect,.25f);
            RGB(report.cameraB,report.expectedB,"Camera B outside local Volume retains .25 gain");Check(b.nrd==null&&c.nrd!=null,"Volume NRD overrides remain per camera");
            Quality(false);
            RenderCamera();RGB(Read(target),AddExpected(original,Direct(),.625f),"Camera A retains local gain after rendering B");
            Quality(true);
            Check(JsonUtility.ToJson(feature.Settings)==source,"Camera Volume overrides did not mutate shared Feature settings");

            local.weight=0;volume.denoise.Override(true);volume.useEnvironment.Override(true);volume.environmentIntensity.Override(.25f);
            for(int i=0;i<64;i++)RenderCamera();Check(c.nrd!=null&&c.nrd.LastDispatchCount>0,"Volume NRD executes actual RELAX");
            volume.environmentIntensity.Override(.5f);RenderCamera();Check(c.lastNrdReset&&!c.lastTemporal,"Volume sky change resets DI/NRD history");
            volume.additiveIntensity.Override(.5f);RenderCamera();Check(!c.lastNrdReset,"Volume display gain preserves NRD history");
            volume.denoise.Override(false);RenderCamera();Check(c.nrd==null,"Volume denoiser disable releases resources");
            volume.additiveIntensity.value=99;Check(volume.additiveIntensity.value==8,"Additive Volume slider upper bound");
            volume.environmentIntensity.value=-1;Check(volume.environmentIntensity.value==0,"Sky Volume slider lower bound");
            volume.compositeWeight.value=2;Check(volume.compositeWeight.value==1,"Replacement Volume slider bound");
            volume.initialSamples.value=99;volume.maxHistory.value=0;volume.spatialSamples.value=99;volume.spatialRadius.value=0;
            volume.environmentSamples.value=0;volume.environmentBrdfSamples.value=-1;
            Check(volume.initialSamples.value==32&&volume.maxHistory.value==1&&volume.spatialSamples.value==16&&volume.spatialRadius.value==1
                &&volume.environmentSamples.value==1&&volume.environmentBrdfSamples.value==0,"Quality slider bounds");
            foreach(var parameter in new VolumeParameter[]{volume.localSampling,volume.finalSampling,volume.initialSamples,volume.temporal,volume.maxHistory,volume.spatial,volume.spatialSamples,volume.spatialRadius,volume.environmentSamples,volume.environmentBrdfSamples})parameter.overrideState=false;
            feature.Settings.denoise=false;volume.denoise.Override(true);camera.cameraType=CameraType.SceneView;RenderCamera();
            Check(c.nrd!=null&&c.nrd.SceneViewPreview,"Volume-only denoising enters Scene View preview despite legacy Feature being off");
            camera.cameraType=CameraType.Game;feature.Settings.denoise=true;
            ClearAppearance(volume);RenderCamera();RGB(Read(target),original,"Clearing Volume overrides restores legacy original camera");
            Check(c.nrd!=null&&c.environmentSampling,"Clearing overrides restores legacy NRD/sky");
            feature.Settings.enabled=false;RenderCamera();RGB(Read(target),original,"Feature production master switch remains authoritative");feature.Settings.enabled=true;
            var fallback=new HoRTDIViewSettings(feature.Settings,VolumeManager.instance.stack.GetComponent<HoRTDIVolume>());
            Check(fallback.initialSamples==feature.Settings.initialSamples&&fallback.localSampling==feature.Settings.localSampling&&fallback.finalSampling==feature.Settings.finalSampling
                &&fallback.temporal==feature.Settings.temporal&&fallback.spatial==feature.Settings.spatial&&fallback.maxHistory==feature.Settings.maxHistory
                &&fallback.spatialSamples==feature.Settings.spatialSamples&&fallback.spatialRadius==feature.Settings.spatialRadius
                &&fallback.environmentSamples==feature.Settings.environmentSamples&&fallback.environmentBrdfSamples==feature.Settings.environmentBrdfSamples,"Cleared quality overrides preserve serialized tuning");
            // A non-degenerate local-light + sky domain must allocate the real
            // Power and triple-final GPU resources selected only by Volume.
            light.type=LightType.Point;light.transform.position=new Vector3(1.5f,0,-1);
            volume.localSampling.Override(HoRTDILocalSampling.Power);volume.finalSampling.Override(HoRTDIFinalSampling.TripleAnalytic);
            volume.temporal.Override(true);volume.spatial.Override(true);volume.initialSamples.Override(6);volume.maxHistory.Override(7);
            volume.spatialSamples.Override(3);volume.spatialRadius.Override(9);volume.environmentSamples.Override(8);volume.environmentBrdfSamples.Override(2);
            RenderCamera();Check(c.powerSampling&&c.powerPdf!=null&&c.auxiliaryCDF!=null,"Volume Power/triple execute actual GPU resource paths");
            RenderCamera();Check(c.lastTemporal&&!c.lastNrdReset,"Volume temporal sampling reuses stable history");
            Check(JsonUtility.ToJson(feature.Settings)==source,"All Volume edits leave shared Feature appearance unchanged");
            report.featureUntouched=true;report.pass=true;
            File.WriteAllText("rtdi-volume-controls-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDIVolumeControls] PASS "+report.checks.Count+" checks, explicit opt-in, weighted sampling quality, independent cameras, legacy fallback and actual GPU; compatibility="+compatibility);
        }
    }
}
