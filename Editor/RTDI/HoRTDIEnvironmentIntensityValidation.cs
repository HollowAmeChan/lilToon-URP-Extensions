using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEditor;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceEnvironmentIntensity;
        public static void RunEnvironmentIntensity(){forceEnvironmentIntensity=true;RunEnvironment();}
        public static void RunEnvironmentIntensityCompatibility(){compatibility=true;RunEnvironmentIntensity();}
        public static void RunEnvironmentIntensityLegacy()
        {
            int exit=0;
            try
            {
                Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");
                const string path="Assets/RTDIPlayerProbe/LegacyPartialRenderer.asset";
                Require(File.Exists(path)&&!File.ReadAllText(path).Contains("environmentIntensity:"),"Legacy fixture is absent or already migrated; use the Player Prepare fixture");
                HoRTDIRendererFeature legacy=null;foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))if(asset is HoRTDIRendererFeature f)legacy=f;
                Require(legacy!=null&&legacy.Settings.environmentIntensity==1,"Old serialized renderer did not retain sky intensity1 by default");
                File.WriteAllText("rtdi-environment-intensity-legacy.json","{\"pass\":true,\"serializedFieldAbsent\":true,\"loadedIntensity\":1}");
                Debug.Log("[HoRTDIValidation] PASS old serialized renderer missing environmentIntensity loads default1; fixture not saved");
            }
            catch(Exception e){Debug.LogException(e);exit=1;}
            EditorApplication.Exit(exit);
        }
        [Serializable] private sealed class EnvironmentIntensityCase {public float intensity;public Color diffuse,specular;public bool sampling;public int risCount;}
        [Serializable] private sealed class EnvironmentIntensityReport
        {public bool pass,compatibility;public int checks;public List<EnvironmentIntensityCase> cases=new();}
        private static void ValidateEnvironmentIntensity(Texture2D image)
        {
            var report=new EnvironmentIntensityReport{compatibility=compatibility};
            void Check(bool condition,string label){Require(condition,label);report.checks++;}
            void RGB(Color actual,Color expected,string label,float tolerance=.02f){NearRGB(actual,expected,label,tolerance);report.checks++;}
            Check(new HoRTDISettings().environmentIntensity==1,"Environment intensity default changed");
            surfaceMaterial.SetFloat("_Reflectance",.04f);RenderCamera();var c=feature.LastContext;
            Color diffuseAtOne=default,specularAtOne=default;
            foreach(float intensity in new[]{1f,.25f,2f,0f})
            {
                feature.Settings.environmentIntensity=intensity;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
                var diffuse=EnvironmentMean(image,8,128,false);c.historyValid=false;c.frame=0;
                var specular=EnvironmentMean(image,8,128,false,true);
                if(intensity==1){diffuseAtOne=diffuse;specularAtOne=specular;}
                RGB(diffuse,new Color(1,.5f,.25f,1)*intensity,"Independent constant-sky Lambert intensity "+intensity,.03f);
                RGB(diffuse,diffuseAtOne*intensity,"Diffuse scales with sky intensity "+intensity,.006f);
                RGB(specular,specularAtOne*intensity,"Specular scales with sky intensity "+intensity,Mathf.Max(.0001f,specularAtOne.maxColorComponent*.04f));
                Check(c.environmentSampling==(intensity>0),"Zero intensity still samples sky");
                if(intensity==0)Check(c.environmentPdf==null&&c.ris.count==1,"Zero intensity retains sky PDF/RIS storage");
                report.cases.Add(new EnvironmentIntensityCase{intensity=intensity,diffuse=diffuse,specular=specular,sampling=c.environmentSampling,risCount=c.ris.count});
            }
            Check(specularAtOne.maxColorComponent>.001f,"Specular scaling fixture has no signal");
            // With the sky completely disabled, retain the analytic point light.
            light.enabled=true;RenderCamera();var analytic=ReadMean(c.diffuse.rt);
            Near(analytic.r,ExpectedMean(light),"Zero sky intensity changed analytic lighting",.006f);report.checks++;
            feature.Settings.useEnvironment=false;feature.Settings.environmentIntensity=2;RenderCamera();RGB(ReadMean(c.diffuse.rt),analytic,"Disabled sky switch overrides positive intensity",.001f);
            feature.Settings.useEnvironment=true;
            foreach(float invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
            {feature.Settings.environmentIntensity=invalid;RenderCamera();Check(!c.environmentSampling,"Invalid sky gain enabled sampling");RGB(ReadMean(c.diffuse.rt),analytic,"Invalid sky gain contaminated analytic radiance",.001f);}
            feature.Settings.environmentIntensity=1;feature.Settings.denoise=true;feature.Settings.temporal=feature.Settings.spatial=true;RenderCamera();
            for(int i=1;i<=64;i++){c.nrd.ValidationTimeSeconds=1000+i/60.0;RenderCamera();}
            Check(c.nrd.StableDiffuseHistory&&c.lastTemporal,"Sky intensity history fixture failed to settle");
            feature.Settings.environmentIntensity=.25f;c.nrd.ValidationTimeSeconds=1000+65/60.0;RenderCamera();
            Check(c.lastNrdReset&&!c.lastTemporal,"Sky intensity change retained stale DI/NRD history");
            c.nrd.ValidationTimeSeconds=1000+66/60.0;RenderCamera();Check(!c.lastNrdReset&&c.lastTemporal,"Sky intensity change resets every frame");
            feature.Settings.environmentIntensity=0;c.nrd.ValidationTimeSeconds=1000+67/60.0;RenderCamera();
            Check(!c.environmentSampling&&c.environmentPdf==null,"Zero intensity did not release sky after NRD warmup");
            RGB(ReadMean(c.PublishedDiffuse.rt),analytic,"Sky off leaves no stale filtered environment",.001f);
            feature.Settings.environmentIntensity=1;c.nrd.ValidationTimeSeconds=1000+68/60.0;RenderCamera();
            Check(c.environmentSampling&&c.lastNrdReset,"Sky reenable did not rebuild/reset");c.nrd.ValidationTimeSeconds=null;
            // The backdrop and shared atmosphere producer belong to their own
            // consumers. Changing DI gain must not mutate either of them.
            feature.Settings.denoise=false;feature.Settings.composite=true;primaryReceiver.transform.localScale=Vector3.one*.5f;
            camera.backgroundColor=new Color(.07f,.14f,.21f,1);camera.clearFlags=CameraClearFlags.SolidColor;
            Color backdrop=default;foreach(float intensity in new[]{1f,.25f,0f,2f})
            {
                feature.Settings.environmentIntensity=intensity;RenderCamera();var pixel=CompositePixels(target)[4*64+4];
                if(intensity==1)backdrop=pixel;else RGB(pixel,backdrop,"DI sky gain changes visible background",.001f);
                Check(atmosphere.Settings.intensity==1&&atmosphere.Settings.constantColor==new Color(1,.5f,.25f,1),"DI sky gain modified shared producer settings");
                if(compatibility)Check(HoAtmosphereBindings.compatibilityParameters.z==1,"DI sky gain modified shared compatibility snapshot");
            }
            report.pass=true;File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-environment-intensity-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(report,true));
            Debug.Log("[HoRTDIValidation] PASS environment intensity: raw lobes, analytic independence, zero release, history transitions, shared producer and backdrop; checks="+report.checks);
        }
    }
}
