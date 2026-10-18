using System;
using System.Reflection;
using UnityEngine;
using lilToon.URP.Extensions.RTDI;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceSettingsSnapshot;
        public static void RunSettingsSnapshot(){forceSettingsSnapshot=true;Run();}
        public static void RunSettingsSnapshotCompatibility(){compatibility=true;RunSettingsSnapshot();}
        private static void ValidateSettingsSnapshot()
        {
            const BindingFlags inspect=BindingFlags.Instance|BindingFlags.NonPublic;
            RenderCamera();var pass=feature.GetType().GetField("pass",inspect).GetValue(feature);var snapshot=pass.GetType().GetMethod("Snapshot",inspect);
            object Capture()=>snapshot.Invoke(pass,new object[]{camera,camera.worldToCameraMatrix,camera.projectionMatrix});
            object Settings(object data)=>data.GetType().GetField("settings",inspect).GetValue(data);
            var oldData=Capture();var oldValues=Settings(oldData);var members=oldValues.GetType().GetFields(inspect);var expected=new object[members.Length];for(int i=0;i<members.Length;i++)expected[i]=members[i].GetValue(oldValues);
            var settings=feature.Settings;settings.useEnvironment=!settings.useEnvironment;settings.temporal=!settings.temporal;settings.spatial=!settings.spatial;
            settings.initialSamples=31;settings.environmentSamples=17;settings.environmentBrdfSamples=13;settings.environmentIntensity=.25f;settings.maxHistory=23;settings.spatialSamples=7;settings.spatialRadius=9;settings.rayBias=.017f;settings.finalSampling=HoRTDIFinalSampling.TripleAnalytic;
            var beforeNewFrame=Capture();int checks=0;
            foreach(var data in new[]{oldData,beforeNewFrame})
            {
                var values=Settings(data);for(int i=0;i<members.Length;i++){Require(Equals(members[i].GetValue(values),expected[i]),"Recorded setting changed after live object mutation: "+members[i].Name);checks++;}
                Require((int)data.GetType().GetField("finalSamples",inspect).GetValue(data)==1,"Recorded final budget changed before next Setup");checks++;
            }
            RenderCamera();var next=Capture();var nextValues=Settings(next);
            foreach(var member in members)
            {
                var source=settings.GetType().GetField(member.Name);Require(source!=null&&Equals(member.GetValue(nextValues),source.GetValue(settings)),"Next frame did not capture current setting: "+member.Name);checks++;
            }
            Require((int)next.GetType().GetField("finalSamples",inspect).GetValue(next)==3,"Next frame did not capture new final budget");
            var raw=Read(feature.LastContext.diffuse.rt);Require(float.IsFinite(raw.r)&&raw.a==1,"Next-frame GPU shade invalid after settings mutation");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath,"../rtdi-settings85-"+(compatibility?"compat":"rg")+".json"),$"{{\"pass\":true,\"checks\":{checks},\"oldBudget\":1,\"newBudget\":3}}");
            Debug.Log($"[HoRTDIValidation] SETTINGS_SNAPSHOT pass=true checks={checks} old requested budget=1 / next=3");
        }
    }
}
