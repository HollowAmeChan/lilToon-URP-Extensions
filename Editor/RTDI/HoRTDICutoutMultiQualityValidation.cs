using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutMultiQuality;
        private static bool cutoutMultiAudit;
        private static string cutoutNrdDiagnostic;
        [Serializable] private sealed class MultiQualityReport {public bool qualityPass;public string path;public MultiQualityCase[] cases;}
        [Serializable] private sealed class MultiQualityCase
        {public string mode;public double rawRed,rawBlue,filteredRed,filteredBlue,rawMeanL1,filteredMeanL1,filteredFrameL1,filteredWorstFrameL1,transitionL1,transitionChangedL1;public bool rawPass,filteredPass;}
        public static void RunCutoutMultiQuality(){forceCutoutMultiQuality=true;RunCutoutQuality();}
        public static void RunCutoutMultiQualityCompatibility(){compatibility=true;RunCutoutMultiQuality();}
        public static void RunCutoutMultiAudit(){cutoutMultiAudit=true;RunCutoutMultiQuality();}
        public static void RunCutoutMultiAuditCompatibility(){compatibility=true;RunCutoutMultiAudit();}
        public static void RunCutoutNrdAtrousOne(){cutoutNrdDiagnostic="atrous1";RunCutoutMultiAudit();}
        public static void RunCutoutNrdHistoryEight(){cutoutNrdDiagnostic="history8";RunCutoutMultiAudit();}
        public static void RunCutoutNrdPhiHalf(){cutoutNrdDiagnostic="phiHalf";RunCutoutMultiAudit();}
        public static void RunCutoutNrdOriginal(){cutoutNrdDiagnostic="original";RunCutoutMultiAudit();}
        public static void RunCutoutMotionReset(){cutoutNrdDiagnostic="motionReset";RunCutoutMultiAudit();}
        public static void RunCutoutMotionKeep(){cutoutNrdDiagnostic="motionKeep";RunCutoutMultiAudit();}
        public static void RunCutoutMotionResetCompatibility(){compatibility=true;RunCutoutMotionReset();}
        public static void RunCutoutMotionKeepCompatibility(){compatibility=true;RunCutoutMotionKeep();}
        public static void RunCutoutMotionKeepQuality(){cutoutNrdDiagnostic="motionKeep";RunCutoutMultiQuality();}
        public static void RunCutoutMotionVisibility(){cutoutNrdDiagnostic="motionVisibility";RunCutoutMultiAudit();}
        public static void RunCutoutMotionVisibilityCompatibility(){compatibility=true;RunCutoutMotionVisibility();}
        public static void RunCutoutMotionVisibilitySharp(){cutoutNrdDiagnostic="motionVisibilitySharp";RunCutoutMultiAudit();}
        public static void RunCutoutMotionProductionQuality(){cutoutNrdDiagnostic="motionProduction";RunCutoutMultiQuality();}
        public static void RunCutoutMotionProductionQualityCompatibility(){compatibility=true;RunCutoutMotionProductionQuality();}
        public static void RunCutoutPhiOneQuality(){cutoutNrdDiagnostic="phiOneFull";RunCutoutMultiQuality();}
        public static void RunCutoutSigmaFourQuality(){cutoutNrdDiagnostic="sigmaFourFull";RunCutoutMultiQuality();}
        private static void ValidateCutoutMultiQuality()
        {
            light.color=Color.red;var far=Point(new Vector3(-1.5f,0,-1),Color.blue,4);
            feature.Settings.initialSamples=8;feature.Settings.denoise=true;
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var cases=new List<MultiQualityCase>();bool allPass=true;
            var rows=new System.Text.StringBuilder("mode,x,y,referenceRed,referenceBlue,rawRed,rawBlue,filteredRed,filteredBlue\n");
            bool visibilityAudit=cutoutNrdDiagnostic!=null&&cutoutNrdDiagnostic.StartsWith("motionVisibility",StringComparison.Ordinal);
            bool motionAudit=cutoutNrdDiagnostic=="motionReset"||cutoutNrdDiagnostic=="motionKeep"||visibilityAudit||cutoutNrdDiagnostic=="motionProduction";
            foreach(string mode in motionAudit?new[]{"caster","casterRapid","casterStep"}:cutoutNrdDiagnostic==null||cutoutNrdDiagnostic=="phiOneFull"||cutoutNrdDiagnostic=="sigmaFourFull"?new[]{"static","camera","caster","staticT","staticS","staticTS","unshadowed","unshadowedTS"}:new[]{"static","staticTS","unshadowedTS"})
            {
                if(cutoutNrdDiagnostic!=null)
                {
                    RenderCamera();if(cutoutNrdDiagnostic!="motionProduction")feature.LastContext.nrd.ValidationSettings=s=>
                    {s.diffPhi=motionAudit?.5f:2;s.fastSigma=2;if(cutoutNrdDiagnostic=="atrous1")s.atrous=1;if(cutoutNrdDiagnostic=="history8"){s.diffFrames=8;s.diffFast=1;}if(cutoutNrdDiagnostic=="phiHalf")s.diffPhi=.5f;if(cutoutNrdDiagnostic=="phiOneFull")s.diffPhi=1;if(cutoutNrdDiagnostic=="sigmaFourFull"){s.diffPhi=.5f;s.fastSigma=4;}return s;};
                    feature.LastContext.nrd.ValidationPreserveWorldHistory=cutoutNrdDiagnostic=="motionKeep"||visibilityAudit;
                    feature.LastContext.nrd.ValidationForceWorldReset=cutoutNrdDiagnostic=="motionReset";
                    feature.LastContext.nrd.ValidationSkipVisibilityConfidence=cutoutNrdDiagnostic=="motionReset"||cutoutNrdDiagnostic=="motionKeep";
                    feature.LastContext.nrd.ValidationTraceVisibilityConfidence=visibilityAudit;
                    feature.LastContext.nrd.ValidationVisibilityFilterPasses=cutoutNrdDiagnostic=="motionVisibilitySharp"?0:2;
                }
                if(mode=="unshadowed"||mode=="unshadowedTS")AlphaFill(alphaTexture,0);
                bool movingCaster=mode.StartsWith("caster",StringComparison.Ordinal);
                feature.Settings.temporal=mode=="staticT"||mode=="staticTS"||mode=="camera"||movingCaster||mode=="unshadowedTS";
                feature.Settings.spatial=mode=="staticS"||mode=="staticTS"||mode=="camera"||movingCaster||mode=="unshadowedTS";
                camera.transform.position=new Vector3(0,0,-3);skinBlockerBone.localPosition=Vector3.zero;
                if(feature.LastContext!=null){feature.LastContext.historyValid=false;feature.LastContext.nrdHistoryValid=false;}
                for(int i=0;i<192;i++)RenderCamera();
                double redRef=0,blueRef=0,rawRed=0,rawBlue=0,filterRed=0,filterBlue=0,filteredL1=0;
                var expectedMean=new double[64*64*2];var rawMean=new double[64*64*2];var filterMean=new double[64*64*2];
                double worstFrameL1=0,transitionError=0,transitionReference=0;
                double changedError=0,changedReference=0;
                var previousRed=new float[64*64];var changedPixels=new bool[64*64];
                int frames=mode=="camera"||movingCaster?256:512;
                for(int frame=0;frame<frames;frame++)
                {
                    if(mode=="camera")camera.transform.position=new Vector3(.18f*Mathf.Sin(frame*.01f),0,-3);
                    if(mode=="caster")skinBlockerBone.localPosition=Vector3.right*(.12f*Mathf.Sin(frame*.0175f));
                    if(mode=="casterRapid")skinBlockerBone.localPosition=Vector3.right*(.25f+.25f*Mathf.Sin(frame*.27f));
                    if(mode=="casterStep")skinBlockerBone.localPosition=Vector3.right*((frame/16&1)==0?0:.5f);
                    RenderCamera();var context=feature.LastContext;ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
                    double frameError=0,frameReference=0;
                    for(int y=16;y<48;y++)for(int x=16;x<44;x++)
                    {
                        float red=CutoutPlaneShadow(x,y)?0:Expected(light,x,y),blue=Expected(far,x,y);
                        if(motionAudit)Require(!CutoutPlaneShadow(far,x,y),"motion fixture must keep Far unoccluded");
                        var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);int index=(y*64+x)*2;
                        int pixel=y*64+x;
                        if(mode=="casterStep"&&frame%16==0)changedPixels[pixel]=frame>0&&Math.Abs(red-previousRed[pixel])>1e-6;
                        if(mode=="casterStep"&&frame%16<3&&changedPixels[pixel])
                        {changedError+=Math.Abs(b.r-red);changedReference+=Expected(light,x,y);}
                        previousRed[pixel]=red;
                        redRef+=red;blueRef+=blue;rawRed+=a.r;rawBlue+=a.b;filterRed+=b.r;filterBlue+=b.b;
                        filteredL1+=Math.Abs(b.r-red)+Math.Abs(b.b-blue);
                        frameError+=Math.Abs(b.r-red)+Math.Abs(b.b-blue);frameReference+=red+blue;
                        expectedMean[index]+=red;expectedMean[index+1]+=blue;rawMean[index]+=a.r;rawMean[index+1]+=a.b;filterMean[index]+=b.r;filterMean[index+1]+=b.b;
                    }
                    worstFrameL1=Math.Max(worstFrameL1,frameError/frameReference);
                    if(mode=="casterStep"&&frame%16<3){transitionError+=frameError;transitionReference+=frameReference;}
                }
                double rawMeanL1=0,filterMeanL1=0;for(int i=0;i<expectedMean.Length;i++){rawMeanL1+=Math.Abs(rawMean[i]-expectedMean[i]);filterMeanL1+=Math.Abs(filterMean[i]-expectedMean[i]);}
                double total=redRef+blueRef;
                for(int y=16;y<48;y++)for(int x=16;x<44;x++)
                {int i=(y*64+x)*2;rows.Append(FormattableString.Invariant($"{mode},{x},{y},{expectedMean[i]/frames:R},{expectedMean[i+1]/frames:R},{rawMean[i]/frames:R},{rawMean[i+1]/frames:R},{filterMean[i]/frames:R},{filterMean[i+1]/frames:R}\n"));}
                var result=new MultiQualityCase{mode=mode,rawRed=rawRed/redRef,rawBlue=rawBlue/blueRef,filteredRed=filterRed/redRef,filteredBlue=filterBlue/blueRef,
                    rawMeanL1=rawMeanL1/total,filteredMeanL1=filterMeanL1/total,filteredFrameL1=filteredL1/total,filteredWorstFrameL1=worstFrameL1,transitionL1=transitionReference>0?transitionError/transitionReference:0,transitionChangedL1=changedReference>0?changedError/changedReference:0};
                result.rawPass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03;
                result.filteredPass=Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.filteredMeanL1<.15&&result.filteredFrameL1<.35;
                if(mode=="casterStep")result.filteredPass&=changedReference>0&&result.transitionChangedL1<.35;
                allPass&=result.rawPass&&result.filteredPass;cases.Add(result);
                Debug.Log($"[HoRTDIValidation] CUTOUT_MULTI_QUALITY diagnostic={cutoutNrdDiagnostic??"baseline"} mode={mode} rawRed={rawRed/redRef:F6} rawBlue={rawBlue/blueRef:F6} filteredRed={filterRed/redRef:F6} filteredBlue={filterBlue/blueRef:F6} rawMeanL1={rawMeanL1/total:F6} filteredMeanL1={filterMeanL1/total:F6} filteredFrameL1={filteredL1/total:F6}");
                if(motionAudit)Debug.Log($"[HoRTDIValidation] CUTOUT_MOTION mode={mode} reset={feature.LastContext.lastNrdReset} traced={feature.LastContext.lastVisibilityGradients} worstFrameL1={worstFrameL1:F6} transitionL1={result.transitionL1:F6} transitionChangedL1={result.transitionChangedL1:F6}");
                Require(feature.LastContext.nrd.LastDispatchCount>0,"two nonzero lights bypassed RELAX");
                Require(Math.Abs(rawRed/redRef-1)<.03&&Math.Abs(rawBlue/blueRef-1)<.03,"multi-light raw energy mismatch");
            }
            var report=new MultiQualityReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",cases=cases.ToArray()};
            string prefix=motionAudit?(visibilityAudit||cutoutNrdDiagnostic=="motionProduction"?"rtdi-cutout32-":"rtdi-cutout31-")+cutoutNrdDiagnostic+"-"+(compatibility?"compat":"rg"):cutoutNrdDiagnostic==null?"rtdi-cutout32-current-"+(compatibility?"compat":"rg"):"rtdi-cutout30-"+cutoutNrdDiagnostic;
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+prefix+".json"),JsonUtility.ToJson(report,true));
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+prefix+".csv"),rows.ToString());
            Debug.Log($"[HoRTDIValidation] CUTOUT_MULTI_RESULT qualityPass={allPass} diagnostic={cutoutMultiAudit} compatibility={compatibility}");
            Require(cutoutMultiAudit||allPass,"nonzero multi-light filtered quality gates failed; inspect saved report");
        }
    }
}
