using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forcePrimaryMotion;
        private static bool primaryOriginQuality,primaryFullQuality;
        [Serializable] private sealed class PrimaryMotionReport{public bool qualityPass,originPass;public string path;public PrimaryMotionCase[] cases;}
        [Serializable] private sealed class PrimaryMotionCase{public string mode,signal;public double rawRed,rawBlue,filteredRed,filteredBlue,frameL1;public int nrdResets,tracedFrames;public bool rawPass,filteredPass;}
        public static void RunPrimaryMotionAudit(){forcePrimaryMotion=true;RunCutoutQuality();}
        public static void RunPrimaryMotionAuditCompatibility(){compatibility=true;RunPrimaryMotionAudit();}
        public static void RunPrimaryMotionOriginQuality(){primaryOriginQuality=true;RunPrimaryMotionAudit();}
        public static void RunPrimaryMotionOriginQualityCompatibility(){compatibility=true;RunPrimaryMotionOriginQuality();}
        public static void RunPrimaryMotionQuality(){primaryFullQuality=true;RunPrimaryMotionAudit();}
        public static void RunPrimaryMotionQualityCompatibility(){compatibility=true;RunPrimaryMotionQuality();}
        private static void ValidatePrimaryMotion()
        {
            skinBlockerBone.localPosition=Vector3.right*100;light.color=Color.red;var far=Point(new Vector3(-1.5f,0,-1),Color.blue,4);
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.6f);
            feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=true;
            if(primaryStageMode!=null){ValidatePrimaryStages(far);return;}
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var cases=new List<PrimaryMotionCase>();bool allPass=true,originPass=true;
            foreach(bool spec in new[]{false,true})foreach(string mode in new[]{"static","smoothZ","stepZ"})
            {
                primaryReceiver.transform.position=Vector3.zero;for(int i=0;i<192;i++)RenderCamera();
                double redReference=0,blueReference=0,redRaw=0,blueRaw=0,redFiltered=0,blueFiltered=0,error=0;int resets=0,traced=0;
                for(int frame=0;frame<256;frame++)
                {
                    float z=mode=="smoothZ"?.025f*Mathf.Sin(frame*.11f):mode=="stepZ"?((frame/16&1)==0?-.012f:.012f):0;
                    primaryReceiver.transform.position=Vector3.forward*z;RenderCamera();var c=feature.LastContext;
                    resets+=c.lastNrdReset?1:0;traced+=c.lastVisibilityGradients?1:0;
                    ReadImage(spec?c.specular.rt:c.diffuse.rt,raw);ReadImage(spec?c.filteredSpecular.rt:c.filteredDiffuse.rt,filtered);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {
                        float r=PrimaryLampReference(light,x,y,z,spec),b=PrimaryLampReference(far,x,y,z,spec);var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);
                        redReference+=r;blueReference+=b;redRaw+=a.r;blueRaw+=a.b;redFiltered+=f.r;blueFiltered+=f.b;error+=Math.Abs(f.r-r)+Math.Abs(f.b-b);
                    }
                }
                var result=new PrimaryMotionCase{mode=mode,signal=spec?"specular":"diffuse",rawRed=redRaw/redReference,rawBlue=blueRaw/blueReference,filteredRed=redFiltered/redReference,filteredBlue=blueFiltered/blueReference,frameL1=error/(redReference+blueReference),nrdResets=resets,tracedFrames=traced};
                result.rawPass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03;
                result.filteredPass=Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.frameL1<.35;
                allPass&=result.rawPass&&result.filteredPass;cases.Add(result);
                if(mode!="static")originPass&=result.rawPass&&result.frameL1<.15;
                Debug.Log($"[HoRTDIValidation] PRIMARY_MOTION mode={mode} signal={result.signal} rawR={result.rawRed:F6} rawB={result.rawBlue:F6} filteredR={result.filteredRed:F6} filteredB={result.filteredBlue:F6} frameL1={result.frameL1:F6} resets={resets} traced={traced}");
                Require(result.rawPass,"moving primary independent raw energy mismatch");
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-primary34-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new PrimaryMotionReport{qualityPass=allPass,originPass=originPass,path=compatibility?"compatibility":"RenderGraph",cases=cases.ToArray()},true));
            Debug.Log($"[HoRTDIValidation] PRIMARY_MOTION_RESULT qualityPass={allPass} originPass={originPass} compatibility={compatibility}");
            Require(!primaryOriginQuality||originPass,"primary-origin self-visibility noise regression");
            Require(!primaryFullQuality||allPass,"primary full filtered quality gates failed");
        }
        private static float PrimaryLampReference(Light source,int x,int y,float z,bool spec)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*((z-ray.origin.z)/ray.direction.z);var delta=source.transform.position-p;float d2=delta.sqrMagnitude;
            float response=spec?ReferenceGGX(Vector3.back,-camera.transform.forward,delta.normalized,102f/255,new Color(10f/255,10f/255,10f/255,1)).r:Mathf.Max(0,-delta.normalized.z)/Mathf.PI;
            float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return response*source.intensity/d2*fade*fade;
        }
    }
}
