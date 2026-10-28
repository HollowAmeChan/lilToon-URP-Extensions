using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RT;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGIValidation
    {
        private static bool forceHostContract;
        private static int hostStep;
        private static SkinnedMeshRenderer sharedSkin;
        private static Transform sharedBone;
        private static readonly List<string> hostChecks=new();
        private static HoRTView oldGIView;
        private static long oldLifetime;
        private static uint bakeBaseline;
        public static void RunHost(){forceHostContract=true;Run();}
        public static void RunHostCompatibility(){compatibility=true;RunHost();}
        private static void HostCheck(bool condition,string label){Require(condition,label);hostChecks.Add(label);}
        private static void SetupHostContract()
        {
            direct.SetActive(true);feature.Settings.temporal=true;feature.Settings.spatial=false;
            direct.Settings.temporal=true;direct.Settings.spatial=false;
            feature.Settings.denoise=true;volume.denoise.Override(true);direct.Settings.denoise=true;
            foreach(float intensity in new[]{4f,0f})
            {
                var lamp=Keep(new GameObject("Host contract analytic light")).AddComponent<Light>();
                lamp.type=LightType.Point;lamp.intensity=intensity;lamp.range=10;lamp.transform.position=new Vector3(1.5f,0,-1);
            }
            var source=Keep(new Mesh{name="Shared DI GI pose source"});
            source.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
            source.triangles=new[]{0,2,1,2,3,1};source.RecalculateNormals();source.RecalculateBounds();source.bindposes=new[]{Matrix4x4.identity};
            var weights=new BoneWeight[4];for(int i=0;i<4;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};source.boneWeights=weights;
            var holder=Keep(new GameObject("Shared DI GI offscreen skin"));holder.transform.position=Vector3.right*100;
            sharedBone=Keep(new GameObject("Shared DI GI bone")).transform;sharedBone.SetParent(holder.transform,false);
            sharedSkin=holder.AddComponent<SkinnedMeshRenderer>();sharedSkin.sharedMesh=source;sharedSkin.bones=new[]{sharedBone};sharedSkin.rootBone=sharedBone;
            sharedSkin.sharedMaterial=receiverMaterial;
        }
        private static Mesh Pose(HoRTView view)
        {
            for(uint id=1;id<128;id++)if(view.Scene.TryResolve(id,out var instance)&&instance.renderer==sharedSkin)return instance.mesh;
            throw new Exception("Shared skin absent from the requested production view");
        }
        private static void TickHostContract()
        {
            switch(hostStep++)
            {
                case 0:
                    Render();
                    var gi=feature.SceneViewForCamera(camera);var di=direct.SceneViewForCamera(camera);
                    HostCheck(gi.IsValid&&di.IsValid&&rtHost.ViewCount==2,"Production DI and GI acquire separate host views");
                    HostCheck(!ReferenceEquals(gi.Scene,di.Scene)&&Pose(gi)==Pose(di),"Distinct native material profiles share one pose Mesh");
                    bakeBaseline=rtHost.BakeCount;HostCheck(bakeBaseline==1,"Production two-feature initial pose Bake occurs once");
                    HostCheck(feature.LastContext.lastTemporal&&direct.LastContext.lastTemporal,"Both effects execute their own temporal history");
                    HostCheck(feature.LastContext.nrd!=null&&direct.LastContext.nrd!=null&&!ReferenceEquals(feature.LastContext.nrd.core,direct.LastContext.nrd),"Both production effects retain independent NRD contexts");
                    HostCheck(feature.LastContext.nrd.core.LastDispatchCount>0&&direct.LastContext.nrd.LastDispatchCount>0,"Both NRD paths actually dispatch in the shared-host render");
                    oldGIView=gi;sharedBone.localPosition=Vector3.forward*.25f;
                    break;
                case 1:
                    Render();
                    HostCheck(rtHost.BakeCount==bakeBaseline+1,"One bone edit adds one Bake across production DI and GI");
                    HostCheck(Pose(feature.SceneViewForCamera(camera))==Pose(direct.SceneViewForCamera(camera)),"Both effects retain the same updated pose Mesh");
                    HostCheck(!oldGIView.IsValid,"Old GI view epoch is invalid after another render");
                    HostCheck(!feature.LastContext.lastTemporal,"GI rejects changed scene visibility history");
                    feature.SetActive(false);break;
                case 2:
                    RenderCameraOnly();
                    HostCheck(rtHost.ViewCount==1&&Shader.GetGlobalFloat("_HoRTDIActive")>.5f,"DI still produces when GI stops requesting");
                    HostCheck(direct.LastContext.historyValid,"GI disposal does not invalidate DI independent history");
                    feature.SetActive(true);break;
                case 3:
                    Render();HostCheck(rtHost.ViewCount==2&&!feature.LastContext.lastTemporal,"Returning GI gets a fresh scene lifetime history");
                    direct.SetActive(false);break;
                case 4:
                    Render();HostCheck(rtHost.ViewCount==1&&feature.LastContext.historyValid,"GI keeps producing after DI stops requesting");
                    oldGIView=feature.SceneViewForCamera(camera);oldLifetime=oldGIView.Scene.Lifetime;
                    rtHost.SetActive(false);break;
                case 5:
                    RenderCameraOnly();
                    HostCheck(!oldGIView.IsValid&&!feature.LastContext.historyValid&&feature.Status.Contains("Ho-RT"),"Missing host invalidates GI view and history explicitly");
                    HostCheck(Shader.GetGlobalFloat("_HoRTGIActive")==0,"Missing host never republishes stale GI activation");
                    rtHost.SetActive(true);break;
                case 6:
                    Render();HostCheck(!feature.LastContext.lastTemporal,"GI re-enable begins without the invalid history");
                    rtHost.Create();break;
                case 7:
                    Render();HostCheck(feature.SceneViewForCamera(camera).Scene.Lifetime!=oldLifetime&&!feature.LastContext.lastTemporal,"Host recreation resets GI history even when revision numbers repeat");
                    var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/HoRTGIHostValidation.cs");
                    var folder=Path.Combine(package.resolvedPath,"Tests~/RT/R3/GI");Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder,"host-report-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new HostReport{pass=true,checks=hostChecks,bakeCount=rtHost.BakeCount},true));
                    Debug.Log("[HoRTGIHostValidation] PASS production DI/GI shared pose, separate histories, lifetime and explicit dependencies; compatibility="+compatibility);
                    Finish(null);break;
            }
        }
        private static void RenderCameraOnly(){UnityEngine.Rendering.VolumeManager.instance.Update(camera.transform,-1);UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});}
        [Serializable] private class HostReport {public bool pass;public uint bakeCount;public List<string> checks;}
    }
}
