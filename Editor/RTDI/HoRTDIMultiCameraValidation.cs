using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool multiCameraDIAudit;
        private static ScriptableRendererFeature multiCameraCapture;
        private static void SetupMultiCameraCapture(UniversalRendererData renderer)
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Multi-camera capture requires isolated project");
            var type=Type.GetType("HoRTDIPlayerCaptureFeature, Assembly-CSharp");Require(type!=null,"Prepare the existing isolated Player capture templates first");
            multiCameraCapture=Keep((ScriptableRendererFeature)ScriptableObject.CreateInstance(type));
            var shader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/RTDIPlayerProbe/HoRTDIPlayerCapture.shader");Require(shader!=null,"Isolated capture shader missing");
            type.GetField("captureShader").SetValue(multiCameraCapture,shader);renderer.rendererFeatures.Add(multiCameraCapture);
        }
        public static void RunMultiCameraDI(){multiCameraDIAudit=true;RunCutoutResolutionSpecular();}
        public static void RunMultiCameraDICompatibility(){compatibility=true;RunMultiCameraDI();}
        public static void RunMultiCameraOpaqueDI(){pairedOpaqueAudit=true;RunMultiCameraDI();}
        public static void RunMultiCameraOpaqueDICompatibility(){compatibility=true;RunMultiCameraOpaqueDI();}
        [Serializable] private sealed class MultiCameraReport
        {
            public bool pass,isolationPass;public string path,visibility;public int alternatingRenders,isolationChecks,resetChecks,skipChecks,disposalChecks,qualityFailures;
            public double maxRawRelativeError,maxFilteredRelativeError,maxAbsentColour;
        }
        private static void ValidateMultiCameraDI(Light blue)
        {
            surfaceMaterial.SetFloat("_Reflectance",0);blocker.transform.position=Vector3.right*4;
            if(pairedOpaqueAudit)blocker.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;
            feature.Settings.useEnvironment=false;feature.Settings.localSampling=HoRTDILocalSampling.Power;
            var cameraA=camera;var targetA=target;
            var cameraB=Keep(new GameObject("Independent DI camera B")).AddComponent<Camera>();cameraB.CopyFrom(cameraA);cameraB.enabled=false;
            cameraB.transform.SetPositionAndRotation(cameraA.transform.position,cameraA.transform.rotation);cameraB.GetUniversalAdditionalCameraData().volumeLayerMask=-1;cameraB.aspect=1.2f;
            var targetB=Keep(new RenderTexture(96,80,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));targetB.Create();
            blue.type=LightType.Directional;blue.transform.rotation=Quaternion.identity;blue.intensity=.8f;blue.color=Color.blue;light.color=Color.red;
            var redCompanion=Point(light.transform.position,Color.red,light.intensity);var blueCompanion=Point(blue.transform.position,Color.blue,blue.intensity);blueCompanion.type=LightType.Directional;blueCompanion.transform.rotation=blue.transform.rotation;
            var contexts=(Dictionary<Camera,HoRTDIContext>)typeof(HoRTDIRendererFeature).GetField("contexts",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(feature);
            HoRTDIContext a=null,b=null;int tickA=0,tickB=0;
            var report=new MultiCameraReport{path=compatibility?"compatibility":"RenderGraph",visibility=pairedOpaqueAudit?"opaque":"native-cutout"};
            try
            {
                for(int frame=0;frame<96;frame++){RenderView(cameraA,targetA,light,false,false);a=contexts[cameraA];RenderView(cameraB,targetB,blue,false,false);b=contexts[cameraB];}
                Require(!ReferenceEquals(a,b)&&!ReferenceEquals(a.reservoirs,b.reservoirs)&&!ReferenceEquals(a.history,b.history)&&!ReferenceEquals(a.nrd,b.nrd)&&!ReferenceEquals(a.filteredDiffuse,b.filteredDiffuse),"DI camera resources share ownership");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var poolField=a.nrd.GetType().GetField("permanent",flags);
                var poolA=(RTHandle[])poolField.GetValue(a.nrd);var poolB=(RTHandle[])poolField.GetValue(b.nrd);
                for(int i=0;i<poolA.Length;i++)Require(!ReferenceEquals(poolA[i],poolB[i]),"Camera NRD permanent history alias");
                for(int frame=0;frame<48;frame++)
                {
                    if(frame==16){light.type=LightType.Spot;light.spotAngle=70;light.innerSpotAngle=30;light.transform.rotation=Quaternion.LookRotation(-light.transform.position,Vector3.up);}
                    if(frame==24){targetA.Release();targetA.width=80;targetA.height=64;targetA.Create();cameraA.aspect=1.25f;}
                    if(frame==40)
                    {
                        UnityEngine.Object.DestroyImmediate(cameraB.gameObject);
                        RenderView(cameraA,targetA,light,false,true);
                        Require(contexts.Count==1&&b.reservoirs==null&&b.nrd==null,"Destroyed camera DI context was not released");report.disposalChecks++;continue;
                    }
                    if(frame>40){RenderView(cameraA,targetA,light,false,true);continue;}
                    int oldBFrame=b.frame;uint oldBHash=ReservoirFingerprint(b);var oldBBuffer=b.reservoirs;
                    RenderView(cameraA,targetA,light,frame==32,true,frame==16||frame==24||frame==33);
                    Require(b.frame==oldBFrame&&ReferenceEquals(b.reservoirs,oldBBuffer)&&ReservoirFingerprint(b)==oldBHash,"Camera A render changed camera B reservoir/history");report.isolationChecks++;
                    int oldAFrame=a.frame;uint oldAHash=ReservoirFingerprint(a);var oldABuffer=a.reservoirs;
                    RenderView(cameraB,targetB,blue,false,true);
                    Require(a.frame==oldAFrame&&ReferenceEquals(a.reservoirs,oldABuffer)&&ReservoirFingerprint(a)==oldAHash,"Camera B render changed camera A reservoir/history");report.isolationChecks++;
                }
                report.isolationPass=true;report.pass=report.qualityFailures==0;File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-multicamera71-"+(pairedOpaqueAudit?"opaque-":"cutout-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(report,true));
                Debug.Log($"[HoRTDIValidation] MULTI_CAMERA pass={report.pass} isolationPass=true renders={report.alternatingRenders} isolation={report.isolationChecks} resets={report.resetChecks} skip={report.skipChecks} dispose={report.disposalChecks} raw={report.maxRawRelativeError:G6} filtered={report.maxFilteredRelativeError:G6} absent={report.maxAbsentColour:G6} qualityFailures={report.qualityFailures}");
                Require(report.pass,"Multi-camera physical reference gates failed; isolation findings saved separately");
            }
            finally{volume.enable.value=true;camera=cameraA;target=targetA;if(a?.nrd!=null)a.nrd.ValidationTimeSeconds=null;if(b?.nrd!=null)b.nrd.ValidationTimeSeconds=null;}

            void RenderView(Camera view,RenderTexture destination,Light source,bool skip,bool check,bool expectedReset=false)
            {
                camera=view;target=destination;light.enabled=redCompanion.enabled=ReferenceEquals(source,light);blue.enabled=blueCompanion.enabled=ReferenceEquals(source,blue);volume.enable.value=!skip;
                redCompanion.type=light.type;redCompanion.transform.rotation=light.transform.rotation;redCompanion.spotAngle=light.spotAngle;redCompanion.innerSpotAngle=light.innerSpotAngle;
                contexts.TryGetValue(view,out var before);int frameBefore=before?.frame??0;var oldKeys=before==null?Array.Empty<Light>():(Light[])before.previousLightKeys.Clone();
                bool resized=before!=null&&(before.width!=destination.width||before.height!=destination.height);
                int tick=ReferenceEquals(view,cameraA)?++tickA:++tickB;if(before?.nrd!=null)before.nrd.ValidationTimeSeconds=tick/60.0;
                VolumeManager.instance.Update(view.transform,-1);RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});
                report.alternatingRenders++;
                if(skip)
                {
                    Require(before.frame==frameBefore&&!before.historyValid&&!before.nrdHistoryValid,"Skipped camera retained DI history or advanced frame");
                    Require(Shader.GetGlobalFloat("_HoRTDIActive")==0&&ReferenceEquals(Shader.GetGlobalTexture("_HoRTDIDiffuseTexture"),Texture2D.blackTexture),"Skipped camera published another camera's DI");report.skipChecks++;return;
                }
                var c=contexts[view];Require(ReferenceEquals(feature.LastContext,c)&&c.frame==(resized?1:frameBefore+1)&&c.width==destination.width&&c.height==destination.height&&(!check||c.nrd.LastDispatchCount>0),$"Camera DI ownership/dispatch/dimensions invalid: view={view.name} last={ReferenceEquals(feature.LastContext,c)} frame={frameBefore}->{c.frame} size={c.width}x{c.height} expected={destination.width}x{destination.height} dispatch={c.nrd.LastDispatchCount} status={c.nrdStatus}");
                var companion=ReferenceEquals(source,light)?redCompanion:blueCompanion;
                Require(c.previousLightKeys.Length==2&&Array.Exists(c.previousLightKeys,p=>ReferenceEquals(p,source))&&Array.Exists(c.previousLightKeys,p=>ReferenceEquals(p,companion))&&!c.environmentSampling&&(c.finalVisibility==null)==pairedOpaqueAudit,"Camera light/visibility domain invalid");
                if(check)
                {
                    Require(c.lastNrdReset==expectedReset,"Camera NRD reset leaked across views or missed local change");report.resetChecks++;CheckDynamicLightMaps(c,oldKeys,c.previousLightKeys);
                    Require(Shader.GetGlobalFloat("_HoRTDIActive")==1,"Camera DI active publication missing");
                    // RenderGraph releases frame-scoped global texture bindings
                    // after the request. Read the downstream GPU capture made
                    // inside that camera graph, not Shader.GetGlobalTexture here.
                    var captured=(RenderTexture)multiCameraCapture.GetType().GetProperty("Diffuse").GetValue(multiCameraCapture);var pixel=Read(captured);
                    int colour=ReferenceEquals(source,light)?0:2;
                    Require(pixel[colour]>.01f&&pixel[1]<.001f&&pixel[2-colour]<.001f,"Downstream pass consumed another camera's DI colour");
                    var raw=Keep(new Texture2D(c.width,c.height,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(c.width,c.height,TextureFormat.RGBAFloat,false,true));ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                    for(int y=c.height/2-4;y<c.height/2+4;y++)for(int x=c.width/2-4;x<c.width/2+4;x++)
                    {
                        // PlanePosition uses normalized viewport coordinates for
                        // each view; this fixture includes a rectangular target.
                        var ray=view.ViewportPointToRay(new Vector3((x+.5f)/c.width,(y+.5f)/c.height,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);
                        Color expected=source.type==LightType.Directional?Color.blue*((source.intensity+companion.intensity)/Mathf.PI):DynamicDiffuseAtPosition(source,p)+DynamicDiffuseAtPosition(companion,p);
                        var r=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                        for(int channel=0;channel<3;channel++)
                        {
                            Require(float.IsFinite(r[channel])&&float.IsFinite(f[channel])&&r[channel]>=0&&f[channel]>=0,"Multi-camera non-finite/negative radiance");
                            if(expected[channel]>0){double er=Math.Abs(r[channel]/expected[channel]-1),ef=Math.Abs(f[channel]/expected[channel]-1);report.maxRawRelativeError=Math.Max(report.maxRawRelativeError,er);report.maxFilteredRelativeError=Math.Max(report.maxFilteredRelativeError,ef);if(er>=.01||ef>=.05)report.qualityFailures++;}
                            else {report.maxAbsentColour=Math.Max(report.maxAbsentColour,Math.Max(r[channel],f[channel]));Require(r[channel]<.001&&f[channel]<.001,"Another camera's light colour entered DI history");}
                        }
                    }
                }
            }
        }

        private static uint ReservoirFingerprint(HoRTDIContext c)
        {
            var values=new PairedPackedReservoir[c.reservoirs.count];c.reservoirs.GetData(values);uint hash=2166136261;
            foreach(var v in values){hash=(hash^v.lightData)*16777619;hash=(hash^v.uvData)*16777619;hash=(hash^v.mVisibility)*16777619;hash=(hash^v.distanceAge)*16777619;hash=(hash^(uint)BitConverter.SingleToInt32Bits(v.targetPdf))*16777619;hash=(hash^(uint)BitConverter.SingleToInt32Bits(v.weight))*16777619;}return hash;
        }

        private static Color DynamicDiffuseAtPosition(Light source,Vector3 position)
        {
            var delta=source.transform.position-position;double d2=delta.sqrMagnitude,fade=Math.Max(0,1-Math.Pow(d2/(source.range*source.range),2)),cone=1;
            if(source.type==LightType.Spot){double inner=Math.Cos(source.innerSpotAngle*Math.PI/360),outer=Math.Cos(source.spotAngle*Math.PI/360);cone=Math.Clamp((Vector3.Dot(-delta.normalized,source.transform.forward)-outer)/(inner-outer),0,1);}
            return source.color.linear*(float)(source.intensity*fade*fade*cone*cone*Math.Max(0,-delta.normalized.z)/(d2*Math.PI));
        }
    }
}
