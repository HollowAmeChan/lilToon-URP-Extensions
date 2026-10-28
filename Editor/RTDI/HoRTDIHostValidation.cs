using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RT;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceHostContract;
        private static int hostStep;
        private static HoRTView previousHostView;
        private static long previousHostLifetime;
        private static Light hostZeroLight;
        private static readonly List<string> hostChecks=new();
        public static void RunHost(){forceHostContract=true;Run();}
        public static void RunHostCompatibility(){compatibility=true;RunHost();}
        private static void HostCheck(bool condition,string message)
        {Require(condition,message);hostChecks.Add(message);}

        private static void TickHostContract()
        {
            switch(hostStep++)
            {
                case 0:
                    Near(Render().r,Expected(light),"Host DI initial analytic energy",.025f);
                    previousHostView=feature.SceneViewForCamera(camera);
                    HostCheck(previousHostView.IsValid&&rtHost.ViewCount==1,"DI acquires exactly one host-owned view");
                    previousHostLifetime=previousHostView.Scene.Lifetime;
                    hostZeroLight=Point(new Vector3(-1,0,-1),Color.white,0);
                    feature.Settings.temporal=true; feature.Settings.spatial=false;
                    break;
                case 1:
                    Render();HostCheck(feature.LastContext.historyValid,"DI keeps independent history with host input");
                    break;
                case 2:
                    Render();HostCheck(feature.LastContext.lastTemporal,"Two-light DI actually uses its temporal history");
                    previousHostView=feature.SceneViewForCamera(camera);rtHost.SetActive(false);
                    break;
                case 3:
                    RenderCamera();
                    HostCheck(!feature.SceneViewForCamera(camera).IsValid&&!previousHostView.IsValid,"Missing/disabled host invalidates DI borrowed view");
                    HostCheck(feature.Status.Contains("Ho-RT")&&!feature.LastContext.historyValid,"Missing host reports explicit state and invalidates history");
                    HostCheck(Shader.GetGlobalFloat("_HoRTDIActive")==0,"Missing host publishes no stale DI activation");
                    rtHost.SetActive(true);
                    break;
                case 4:
                    Render();HostCheck(feature.SceneViewForCamera(camera).Scene.Lifetime==previousHostLifetime,"Re-enabling host reuses the valid scene");
                    HostCheck(!feature.LastContext.lastTemporal,"Re-enable begins from invalidated DI history");
                    rtHost.Create();
                    break;
                case 5:
                    Render();
                    HostCheck(feature.SceneViewForCamera(camera).Scene.Lifetime!=previousHostLifetime,"Host recreation produces a new scene lifetime");
                    HostCheck(!feature.LastContext.lastTemporal,"New lifetime rejects same-numbered old scene revision history");
                    previousHostLifetime=feature.SceneViewForCamera(camera).Scene.Lifetime;
                    feature.Create();
                    HostCheck(rtHost.ViewCount==1,"DI recreation does not directly release host-owned resources");
                    break;
                case 6:
                    Render();
                    HostCheck(feature.SceneViewForCamera(camera).Scene.Lifetime!=previousHostLifetime,"DI material profile identity replacement is owned by host");
                    HostCheck(!feature.LastContext.lastTemporal,"Recreated DI starts independent history");
                    feature.SetActive(false);
                    break;
                case 7:
                    RenderCamera();HostCheck(rtHost.ViewCount==0,"Host retires a client that stops requesting");
                    feature.SetActive(true);
                    break;
                case 8:
                    Render();HostCheck(feature.SceneViewForCamera(camera).IsValid,"DI resumes through the host without a private fallback scene");
                    HostCheck(!feature.LastContext.lastTemporal,"Re-enabled client does not consume the removed scene history");
                    hostZeroLight.enabled=false;feature.Settings.temporal=false;
                    Near(Render().r,Expected(light),"Host DI restored analytic energy",.025f);
                    var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTDI/HoRTDIHostValidation.cs");
                    var folder=Path.Combine(package.resolvedPath,"Tests~/RT/R3/DI");Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder,"host-report-"+(compatibility?"compat":"rg")+".json"),UnityEngine.JsonUtility.ToJson(new HostReport{pass=true,checks=hostChecks,sceneLifetime=feature.SceneViewForCamera(camera).Scene.Lifetime},true));
                    Debug.Log("[HoRTDIHostValidation] PASS explicit dependency, lifetime reset, client/profile ownership and restored energy; compatibility="+compatibility);
                    Finish(null);break;
            }
        }
        [Serializable] private class HostReport {public bool pass;public long sceneLifetime;public List<string> checks;}
    }
}
