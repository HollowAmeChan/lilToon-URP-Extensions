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
        private static bool cutoutSpecularQuality;
        [Serializable] private sealed class MultiQualityReport {public bool qualityPass;public int requestedFinalSamples;public string path,signal;public MultiQualityCase[] cases;}
        [Serializable] private sealed class MultiQualityCase
        {public string mode;public double rawRed,rawBlue,filteredRed,filteredBlue,rawMeanL1,filteredMeanL1,filteredFrameL1,filteredWorstFrameL1,transitionL1,transitionChangedL1,rawShadowCoreLeak,shadowCoreLeak,shadowCoreMeanLeak;public int shadowCorePixels;public bool rawPass,filteredPass;}
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
        public static void RunCutoutSpecularAudit(){cutoutSpecularQuality=true;RunCutoutMultiAudit();}
        public static void RunCutoutSpecularAuditCompatibility(){compatibility=true;RunCutoutSpecularAudit();}
        public static void RunCutoutSpecularQuality(){cutoutSpecularQuality=true;RunCutoutMultiQuality();}
        public static void RunCutoutSpecularQualityCompatibility(){compatibility=true;RunCutoutSpecularQuality();}
        public static void RunCutoutSpecularMotionAudit(){cutoutSpecularQuality=true;cutoutNrdDiagnostic="motionProduction";RunCutoutMultiAudit();}
        public static void RunCutoutSpecularMotionQuality(){cutoutSpecularQuality=true;cutoutNrdDiagnostic="motionProduction";RunCutoutMultiQuality();}
        public static void RunCutoutSpecularMotionQualityCompatibility(){compatibility=true;RunCutoutSpecularMotionQuality();}
        private static void ValidateCutoutMultiQuality()
        {
            light.color=Color.red;var far=Point(new Vector3(-1.5f,0,-1),Color.blue,4);
            if(cutoutSpecularQuality){surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.6f);}
            if(cutoutPairedFinalSample)ApplyCutoutPairedFinalSample();
            feature.Settings.initialSamples=8;feature.Settings.denoise=true;
            if(forceCutoutShadowStages){ValidateCutoutShadowStages();return;}
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
                    {s.atrous=5;s.diffPhi=motionAudit?.5f:2;s.fastSigma=2;if(cutoutNrdDiagnostic=="atrous1")s.atrous=2;if(cutoutNrdDiagnostic=="history8"){s.diffFrames=8;s.diffFast=1;}if(cutoutNrdDiagnostic=="phiHalf")s.diffPhi=.5f;if(cutoutNrdDiagnostic=="phiOneFull")s.diffPhi=1;if(cutoutNrdDiagnostic=="sigmaFourFull"){s.diffPhi=.5f;s.fastSigma=4;}return s;};
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
                using var lateSpecOverride=lateSpecExchangeAudit?ApplyLateSpecExchange(feature.LastContext):null;
                for(int i=0;i<192;i++)RenderCamera();
                if(lateSpecWeightAudit)lateSpecOverride.ResetHistogram();
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
                    RenderCamera();var context=feature.LastContext;ReadImage(cutoutSpecularQuality?context.specular.rt:context.diffuse.rt,raw);ReadImage(cutoutSpecularQuality?context.filteredSpecular.rt:context.filteredDiffuse.rt,filtered);
                    double frameError=0,frameReference=0;
                    for(int y=16;y<48;y++)for(int x=16;x<44;x++)
                    {
                        float red=CutoutPlaneShadow(x,y)?0:CutoutLampReference(light,x,y),blue=CutoutLampReference(far,x,y);
                        if(motionAudit)Require(!CutoutPlaneShadow(far,x,y),"motion fixture must keep Far unoccluded");
                        var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);int index=(y*64+x)*2;
                        int pixel=y*64+x;
                        if(mode=="casterStep"&&frame%16==0)changedPixels[pixel]=frame>0&&Math.Abs(red-previousRed[pixel])>1e-6;
                        if(mode=="casterStep"&&frame%16<3&&changedPixels[pixel])
                        {changedError+=Math.Abs(b.r-red);changedReference+=CutoutLampReference(light,x,y);}
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
                if(!cutoutSpecularQuality&&mode.StartsWith("static",StringComparison.Ordinal))
                {
                    // A true multi-light NRD gate: the single-delta thin-shadow
                    // fixture bypasses RELAX and cannot validate spatial filtering.
                    // Use only observed pixels at least two pixels inside the CPU
                    // shadow mask, then score each pixel's converged mean independently.
                    for(int y=18;y<46;y++)for(int x=18;x<42;x++)
                    {
                        bool core=true;
                        for(int dy=-2;dy<=2&&core;dy++)for(int dx=-2;dx<=2;dx++)
                            if(!CutoutPlaneShadow(x+dx,y+dy)){core=false;break;}
                        if(!core)continue;
                        double reference=CutoutLampReference(light,x,y);Require(reference>0,"shadow core unoccluded reference missing");
                        int i=(y*64+x)*2;double leak=filterMean[i]/(frames*reference);
                        Require(double.IsFinite(leak)&&leak>=0,"shadow core non-finite or negative filtered signal");
                        result.rawShadowCoreLeak=Math.Max(result.rawShadowCoreLeak,rawMean[i]/(frames*reference));
                        result.shadowCoreLeak=Math.Max(result.shadowCoreLeak,leak);result.shadowCoreMeanLeak+=leak;result.shadowCorePixels++;
                    }
                    result.shadowCoreMeanLeak/=Math.Max(1,result.shadowCorePixels);
                    result.rawPass&=result.shadowCorePixels>0&&result.rawShadowCoreLeak<.003;
                    // Preserve the existing single-light 2% core criterion;
                    // stochastic samples are averaged, not hidden in the ROI mean.
                    result.filteredPass&=result.shadowCorePixels>0&&result.shadowCoreLeak<.02;
                    Debug.Log($"[HoRTDIValidation] CUTOUT_SHADOW_CORE mode={mode} pixels={result.shadowCorePixels} rawMax={result.rawShadowCoreLeak:F6} filteredMax={result.shadowCoreLeak:F6} filteredMean={result.shadowCoreMeanLeak:F6}");
                }
                allPass&=result.rawPass&&result.filteredPass;cases.Add(result);
                Debug.Log($"[HoRTDIValidation] CUTOUT_MULTI_QUALITY signal={(cutoutSpecularQuality?"specular":"diffuse")} diagnostic={cutoutNrdDiagnostic??"baseline"} mode={mode} rawRed={rawRed/redRef:F6} rawBlue={rawBlue/blueRef:F6} filteredRed={filterRed/redRef:F6} filteredBlue={filterBlue/blueRef:F6} rawMeanL1={rawMeanL1/total:F6} filteredMeanL1={filterMeanL1/total:F6} filteredFrameL1={filteredL1/total:F6}");
                if(lateSpecWeightAudit)WriteLateSpecWeights(lateSpecOverride,mode,64,frames);
                if(motionAudit)Debug.Log($"[HoRTDIValidation] CUTOUT_MOTION mode={mode} reset={feature.LastContext.lastNrdReset} traced={feature.LastContext.lastVisibilityGradients} worstFrameL1={worstFrameL1:F6} transitionL1={result.transitionL1:F6} transitionChangedL1={result.transitionChangedL1:F6}");
                Require(feature.LastContext.nrd.LastDispatchCount>0,"two nonzero lights bypassed RELAX");
                Require(Math.Abs(rawRed/redRef-1)<.03&&Math.Abs(rawBlue/blueRef-1)<.03,"multi-light raw energy mismatch");
            }
            var report=new MultiQualityReport{qualityPass=allPass,requestedFinalSamples=cutoutPairedFinalSample?2:1,path=compatibility?"compatibility":"RenderGraph",signal=cutoutSpecularQuality?"specular":"diffuse",cases=cases.ToArray()};
            string prefix=motionAudit?(visibilityAudit||cutoutNrdDiagnostic=="motionProduction"?"rtdi-cutout32-":"rtdi-cutout31-")+cutoutNrdDiagnostic+"-"+(compatibility?"compat":"rg"):cutoutNrdDiagnostic==null?"rtdi-cutout32-current-"+(compatibility?"compat":"rg"):"rtdi-cutout30-"+cutoutNrdDiagnostic;
            if(cutoutSpecularQuality)prefix="rtdi-cutout33-spec-"+(motionAudit?"motion-":"all-")+(compatibility?"compat":"rg");
            if(cutoutPairedFinalSample)prefix="rtdi-finalpair50-multi-"+(compatibility?"compat":"rg");
            if(lateSpecExchangeAudit)prefix="rtdi-latespec58-motion-"+(compatibility?"compat":"rg");
            if(lateJitterAudit)prefix="rtdi-nojitter94-motion-"+(compatibility?"compat":"rg");
            if(lateSpecWeightAudit)prefix="rtdi-specweights59-motion-"+(compatibility?"compat":"rg");
            if(lateSpecAdaptiveAudit)prefix="rtdi-adaptivespec60-motion-"+(compatibility?"compat":"rg");
            if(lateSpecOrthographicAudit)prefix="rtdi-orthospec61-motion-"+(compatibility?"compat":"rg");
            if(allSpecOrthographicAudit)prefix="rtdi-allorthospec61-motion-"+(compatibility?"compat":"rg");
            if(specularRgbDistanceAudit)prefix="rtdi-rgbspec64-motion-"+(compatibility?"compat":"rg");
            if(specularNormalBudgetAudit)prefix="rtdi-normalbudget66-motion-"+(compatibility?"compat":"rg");
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+prefix+".json"),JsonUtility.ToJson(report,true));
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+prefix+".csv"),rows.ToString());
            Debug.Log($"[HoRTDIValidation] CUTOUT_MULTI_RESULT qualityPass={allPass} diagnostic={cutoutMultiAudit} compatibility={compatibility}");
            Require(cutoutMultiAudit||allPass,"nonzero multi-light filtered quality gates failed; inspect saved report");
        }
        private static float CutoutLampReference(Light source,int x,int y)
        {
            if(!cutoutSpecularQuality)return Expected(source,x,y);
            var p=PlanePosition(x,y,64);var delta=source.transform.position-p;float d2=delta.sqrMagnitude;
            var V=camera.orthographic?-camera.transform.forward:(camera.transform.position-p).normalized;
            var response=ReferenceGGX(Vector3.back,V,delta.normalized,102f/255,new Color(10f/255,10f/255,10f/255,1));
            float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return response.r*source.intensity/d2*fade*fade;
        }
    }
}
