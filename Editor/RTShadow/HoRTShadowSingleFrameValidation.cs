using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool singleFrameAudit;
        [Serializable] private sealed class ExportReport
        {public bool pass;public int samples;public float singleMse,multiMse,singleRampMse,multiRampMse,arithmeticMaxError;public List<string> checks=new();}
        public static void RunSingleFrame(){singleFrameAudit=true;nprIntegration=true;rawOnly=true;Run();}
        public static void RunSingleFrameCompatibility(){compatibility=true;RunSingleFrame();}
        private static void ValidateSingleFrameQuality()
        {
            var evaluation=new ExportReport();
            far.shadowCastingMode=ShadowCastingMode.Off;shadow.Settings.angularDiameter=8;shadow.Settings.samplesPerPixel=4096;
            toon.SetFloat("_ShadowBlur",.01f);toon.SetFloat("_ShadowBorder",.5f);
            var reference=Render();var referenceBeauty=Read(target);
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;
            var oneRaw=Render();var one=Read(shadow.LastOutput.Visibility.rt);var oneBeauty=Read(target);
            var expected=new float[one.Length];var measured=new float[one.Length];Color[] lastRaw=null;
            for(int estimate=0;estimate<4;estimate++)
            {
                shadow.Settings.samplingSeed=unchecked(estimate*(int)0x85ebca6bu);lastRaw=Render();var filtered=Read(shadow.LastOutput.Visibility.rt);
                for(int i=0;i<filtered.Length;i++){expected[i]+=filtered[i].r*.25f;measured[i]+=lastRaw[i].r*.25f;}
            }
            shadow.Settings.samplingSeed=0;shadow.Settings.singleFrameEstimates=4;
            var batchRaw=Render();var batch=Read(shadow.LastOutput.Visibility.rt);
            for(int i=0;i<batch.Length;i++)
            {
                if(batch[i].b!=1)continue;
                float denominator=1+3.8416f/4,centre=(measured[i]+3.8416f/8)/denominator;
                float radius=1.96f*Mathf.Sqrt(measured[i]*(1-measured[i])/4+3.8416f/64)/denominator;
                float bounded=Mathf.Clamp(expected[i],Mathf.Max(0,centre-radius),Mathf.Min(1,centre+radius));
                evaluation.arithmeticMaxError=Mathf.Max(evaluation.arithmeticMaxError,Mathf.Abs(batch[i].r-bounded));
            }
            ExportCheck(Equal(batchRaw,lastRaw),"Raw debug product is the final independent single-sample input, never an averaged distance");
            ExportCheck(evaluation.arithmeticMaxError<.000001f,"Same-request output matches separately decoded SIGMA average bounded by actual raw sample statistics");
            shadow.Settings.singleFrameEstimates=16;Render();var multi=Read(shadow.LastOutput.Visibility.rt);var multiBeauty=Read(target);evaluation.samples=16;
            double single=0,multiple=0,singleRamp=0,multipleRamp=0;int count=0;
            for(int i=0;i<reference.Length;i++)if(reference[i].b==1&&reference[i].a==bodyId)
            {
                single+=Math.Pow(one[i].r-reference[i].r,2);multiple+=Math.Pow(multi[i].r-reference[i].r,2);
                singleRamp+=Math.Pow(oneBeauty[i].r-referenceBeauty[i].r,2);multipleRamp+=Math.Pow(multiBeauty[i].r-referenceBeauty[i].r,2);count++;
            }
            evaluation.singleMse=(float)(single/count);evaluation.multiMse=(float)(multiple/count);evaluation.singleRampMse=(float)(singleRamp/count);evaluation.multiRampMse=(float)(multipleRamp/count);
            ExportCheck(evaluation.multiMse<evaluation.singleMse,"Same-frame decoded visibility improves against 4096-spp independent reference: "+evaluation.singleMse+" -> "+evaluation.multiMse);
            ExportCheck(evaluation.multiRampMse<evaluation.singleRampMse,"Actual narrow NPR ramp improves in the same request: "+evaluation.singleRampMse+" -> "+evaluation.multiRampMse);
            ExportCheck(shadow.LastOutput.sigma.nrd.AppliedSigmaSettings.maxStabilizedFrameNum==0&&shadow.LastOutput.sigma.LastFrame==0,"All estimates explicitly disable temporal stabilization");
            Render();ExportCheck(Equal(multi,Read(shadow.LastOutput.Visibility.rt)),"Batch result repeats exactly in cold no-history mode");
            SaveExport("reference",reference,referenceBeauty);SaveExport("single",one,oneBeauty);SaveExport("multi",multi,multiBeauty);
            shadow.Settings.singleFrameEstimates=256;Render();ExportCheck(shadow.LastOutput.sigma.Estimates==256,"Maximum single-frame estimate count is supported");
            shadow.Settings.singleFrameEstimates=16;shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaPreview;Render();ExportCheck(shadow.LastOutput.sigma.Estimates==1&&shadow.LastOutput.sigma.visibilitySum==null,"Preview does not silently multiply its tracing budget");
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;Render();
            alpha.SetColor("_Color",new Color(1,1,1,0));Render();var unblocked=Read(shadow.LastOutput.Visibility.rt);ExportCheck(unblocked[32*64+32].r>.99f,"No-history export immediately follows alpha/caster change");alpha.SetColor("_Color",Color.white);Render();
            receiver.receiveWholeGroup=false;receiver.receiverParts.Add("detail");Render();ExportCheck(Read(shadow.LastOutput.Visibility.rt)[32*64+32]==Color.clear,"Same-frame averaging cannot create values for unrequested surfaces");receiver.receiverParts.Clear();receiver.receiveWholeGroup=true;
            lifetime.retire=true;Render(false);ExportCheck(lifetime.heldBeforeFence,"All same-frame sum resources survive execution-time feature recreation");Render();
            evaluation.pass=true;File.WriteAllText(Path.Combine(Folder(),"single-frame-report.json"),JsonUtility.ToJson(evaluation,true));
            Debug.Log("[HoRTShadowSingleFrame] PASS independent SIGMA estimates / linear average / real narrow ramp / deterministic no-history; V MSE="+evaluation.singleMse+" -> "+evaluation.multiMse+" ramp="+evaluation.singleRampMse+" -> "+evaluation.multiRampMse);
            void ExportCheck(bool pass,string message){Check(pass,message);evaluation.checks.Add(message);}
        }
        private static void SaveExport(string name,Color[] visibility,Color[] beauty)
        {
            File.WriteAllBytes(Path.Combine(Folder(),"export-"+name+"-visibility.rgba32"),Bytes(visibility));
            File.WriteAllBytes(Path.Combine(Folder(),"export-"+name+"-beauty.rgba32"),Bytes(beauty));
            var image=new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            try{image.SetPixels(beauty);image.Apply();File.WriteAllBytes(Path.Combine(Folder(),"export-"+name+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
