using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool dynamicAuxiliaryCDFAudit,dynamicAuxiliaryLongAudit,dynamicAuxiliaryDomainReset,dynamicAuxiliaryBaseline,dynamicDomainProductionAudit;
        public static void RunDynamicAuxiliaryCDF(){dynamicAuxiliaryCDFAudit=pairedSecondAuxiliary=pairedAuxiliaryPowerCDF=cutoutPairedFinalSample=true;RunCutoutResolutionSpecular();}
        public static void RunDynamicAuxiliaryCDFCompatibility(){compatibility=true;RunDynamicAuxiliaryCDF();}
        public static void RunDynamicOpaqueAuxiliaryCDF(){pairedOpaqueAudit=true;RunDynamicAuxiliaryCDF();}
        public static void RunDynamicOpaqueAuxiliaryCDFCompatibility(){compatibility=true;RunDynamicOpaqueAuxiliaryCDF();}
        public static void RunDynamicAuxiliaryCDFLong(){dynamicAuxiliaryLongAudit=true;RunDynamicAuxiliaryCDF();}
        public static void RunDynamicAuxiliaryCDFLongCompatibility(){compatibility=true;RunDynamicAuxiliaryCDFLong();}
        public static void RunDynamicOpaqueAuxiliaryCDFLong(){pairedOpaqueAudit=true;RunDynamicAuxiliaryCDFLong();}
        public static void RunDynamicOpaqueAuxiliaryCDFLongCompatibility(){compatibility=true;RunDynamicOpaqueAuxiliaryCDFLong();}
        public static void RunDynamicAuxiliaryDomainReset(){dynamicAuxiliaryDomainReset=true;RunDynamicAuxiliaryCDF();}
        public static void RunDynamicAuxiliaryDomainResetCompatibility(){compatibility=true;RunDynamicAuxiliaryDomainReset();}
        public static void RunDynamicOpaqueAuxiliaryDomainReset(){pairedOpaqueAudit=true;RunDynamicAuxiliaryDomainReset();}
        public static void RunDynamicOpaqueAuxiliaryDomainResetCompatibility(){compatibility=true;RunDynamicOpaqueAuxiliaryDomainReset();}
        public static void RunDynamicLightDomainBaseline(){dynamicAuxiliaryBaseline=dynamicAuxiliaryCDFAudit=true;RunCutoutResolutionSpecular();}
        public static void RunDynamicLightDomainBaselineCompatibility(){compatibility=true;RunDynamicLightDomainBaseline();}
        public static void RunDynamicLightDomainReset(){dynamicAuxiliaryDomainReset=true;RunDynamicLightDomainBaseline();}
        public static void RunDynamicLightDomainResetCompatibility(){compatibility=true;RunDynamicLightDomainReset();}
        public static void RunDynamicOpaqueLightDomainBaseline(){pairedOpaqueAudit=true;RunDynamicLightDomainBaseline();}
        public static void RunDynamicOpaqueLightDomainBaselineCompatibility(){compatibility=true;RunDynamicOpaqueLightDomainBaseline();}
        public static void RunDynamicOpaqueLightDomainReset(){pairedOpaqueAudit=true;RunDynamicLightDomainReset();}
        public static void RunDynamicOpaqueLightDomainResetCompatibility(){compatibility=true;RunDynamicOpaqueLightDomainReset();}
        public static void RunDynamicLightDomainProduction(){dynamicDomainProductionAudit=true;RunDynamicLightDomainBaseline();}
        public static void RunDynamicLightDomainProductionCompatibility(){compatibility=true;RunDynamicLightDomainProduction();}
        public static void RunDynamicOpaqueLightDomainProduction(){pairedOpaqueAudit=true;RunDynamicLightDomainProduction();}
        public static void RunDynamicOpaqueLightDomainProductionCompatibility(){compatibility=true;RunDynamicOpaqueLightDomainProduction();}

        [Serializable] private sealed class DynamicCDFReport{public bool qualityPass;public string path,visibility;public DynamicCDFCase[] cases;}
        [Serializable] private sealed class DynamicCDFCase
        {
            public string phase,outputHash,decompositionRegion;public int lights,frames,cdfChecks,mappingChecks,resets,confidenceFrames;public bool pass;
            public double rawRed,rawGreen,rawBlue,filteredRed,filteredGreen,filteredBlue,maxDarkSignal;
            public double primaryRed,primaryGreen,primaryBlue,auxiliaryRed,auxiliaryGreen,auxiliaryBlue,maxShadeRelativeError;
            public string reuseMode;
            public int varianceFrames;
            public double primaryRedRelativeTemporalVariance,rawRedRelativeTemporalVariance;
            public double[] primaryRedBlockRatios;
        }

        private static void ValidateDynamicAuxiliaryCDF(Light blue)
        {
            const int size=128;target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
            feature.Settings.useEnvironment=false;feature.Settings.localSampling=HoRTDILocalSampling.Power;
            if(dynamicUniformSource)feature.Settings.localSampling=HoRTDILocalSampling.Uniform;
            if(dynamicReuseMode!=null){feature.Settings.temporal=dynamicReuseMode=="both"||dynamicReuseMode=="temporal";feature.Settings.spatial=dynamicReuseMode=="both"||dynamicReuseMode=="spatial";}
            surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Metallic",0);
            blocker.transform.position=Vector3.right*4;
            if(pairedOpaqueAudit)blocker.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;
            var green=Point(new Vector3(.3f,.3f,-1.2f),Color.green,4);var lamps=new[]{light,green,blue};
            var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var computeField=feature.GetType().GetField("shader",flags);var rayField=feature.GetType().GetField("finalVisibilityShader",flags);
            var originalCompute=computeField.GetValue(feature);var originalRay=rayField.GetValue(feature);
            RenderCamera();var c=feature.LastContext;Require((c.finalVisibility==null)==pairedOpaqueAudit&&!c.environmentSampling,"Dynamic CDF visibility domain mismatch");
            using var cdfBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,lamps.Length,4);
            using var second=new GraphicsBuffer(GraphicsBuffer.Target.Structured,SecondAuxiliaryCount(size*size),SecondAuxiliaryStride);
            if(!dynamicAuxiliaryBaseline&&!runtimeMultiAudit)
            {
                ApplyCutoutPairedFinalSample();var compute=(ComputeShader)computeField.GetValue(feature);int kernel=compute.FindKernel("ShadeNRD");
                pairedFinalShader.SetBuffer("_HoRTDIAuxiliaryCDF",cdfBuffer);BindSecondAuxiliary(pairedFinalShader,compute,kernel,second);
                if(pairedOpaqueAudit)compute.SetBuffer(kernel,Shader.PropertyToID("_HoRTDIAuxiliaryCDF"),cdfBuffer);
            }
            var cases=new List<DynamicCDFCase>();bool all=true;int clock=0;
            var reservoirs=new PairedPackedReservoir[c.reservoirs.count];var secondSamples=new PairedPackedReservoir[size*size];
            using var colourStageCapture=diffuseColourStageAudit?new ColourStageCapture(c):null;
            using var diffuseSegmentOverride=diffuseSegmentAudit?ApplyDiffuseSegment(c):null;
            try
            {
                foreach(string phase in new[]{"balanced","powerTemperature","disableBlue","blueDirectional","redSpot","disableRed","allDisabled","restore","replaceGreen"})
                {
                    Configure(phase);var expectedKeys=new List<Light>();
                    int frames=dynamicAuxiliaryLongAudit&&phase=="powerTemperature"?2048:96;
                    foreach(var lamp in lamps)if(lamp.enabled&&lamp.type!=LightType.Directional)expectedKeys.Add(lamp);
                    expectedKeys.Sort((a,b)=>a.GetInstanceID().CompareTo(b.GetInstanceID()));
                    var infinite=new List<Light>();foreach(var lamp in lamps)if(lamp.enabled&&lamp.type==LightType.Directional)infinite.Add(lamp);
                    infinite.Sort((a,b)=>a.GetInstanceID().CompareTo(b.GetInstanceID()));expectedKeys.AddRange(infinite);
                    var keys=expectedKeys.ToArray();float[] cdf=keys.Length>0?BuildAuxiliaryCDF(keys):new[]{1f};
                    // Same update boundary as this fixture's lamp mutation. After
                    // rendering, independently require the actual collector order.
                    var upload=new float[lamps.Length];Array.Fill(upload,1f);Array.Copy(cdf,upload,cdf.Length);cdfBuffer.SetData(upload);
                    var reference=new double[3];var sums=new double[2,3];var primarySums=new double[3];var auxiliarySums=new double[3];double dark=0,shadeError=0;int checks=0,maps=0,resets=0,confidence=0;
                    bool observeVariance=dynamicReuseMode!=null&&phase=="powerTemperature";
                    var pointMeans=new double[2,256];var pointM2=new double[2,256];var blockPrimary=new double[(frames-16+15)/16];var blockReference=new double[blockPrimary.Length];
                    var decompositionReference=new double[3];using var imageHash=AuxiliaryImageHash?System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256):null;
                    for(int frame=0;frame<frames;frame++)
                    {
                        var previous=(Light[])c.previousLightKeys.Clone();bool changedDomain=LightDomainChanged(c,keys);
                        if(dynamicAuxiliaryDomainReset&&changedDomain)c.nrdHistoryValid=false;
                        c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();
                        Require(c.nrd.LastDispatchCount>0&&c.previousLightKeys.Length==keys.Length,"Dynamic CDF dispatch/count mismatch");
                        if(dynamicDomainProductionAudit)Require(c.lastNrdReset==changedDomain,"Production NRD domain reset did not match actual source identities/types");
                        for(int i=0;i<keys.Length;i++)Require(ReferenceEquals(c.previousLightKeys[i],keys[i]),"Dynamic CDF current collector ordering mismatch");
                        maps+=CheckDynamicLightMaps(c,previous,keys);resets+=c.lastNrdReset?1:0;confidence+=c.lastVisibilityGradients?1:0;
                        bool singleRuntime=runtimeMultiAudit&&keys.Length<=1;
                        if(runtimeMultiAudit&&keys.Length>0)cdf=ReadRuntimeAuxiliaryCDF(c);
                        if(!dynamicAuxiliaryBaseline&&!singleRuntime)checks+=CheckDynamicAuxiliarySamples(c,cdf,keys.Length,runtimeMultiAudit?c.auxiliarySecondary:second);
                        c.reservoirs.GetData(reservoirs);if(!dynamicAuxiliaryBaseline&&(!runtimeMultiAudit||c.auxiliarySecondary!=null))ReadSecondAuxiliary(runtimeMultiAudit?c.auxiliarySecondary:second,secondSamples,0,0,secondSamples.Length);
                        ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);
                        colourStageCapture?.Observe(phase,frame,raw,filtered);
                        if(opaqueDirectAuxiliaryAudit)Require(c.finalVisibility==null,"Direct dynamic prototype cannot enter native Cutout visibility");
                        if(imageHash!=null){imageHash.AppendData(raw.GetRawTextureData<byte>().ToArray());imageHash.AppendData(filtered.GetRawTextureData<byte>().ToArray());}
                        for(int y=48;y<80;y+=2)for(int x=48;x<80;x+=2)
                        {
                            Color a=raw.GetPixel(x,y),f=filtered.GetPixel(x,y);Color expected=Color.clear;
                            foreach(var lamp in keys)expected+=DynamicDiffuseReference(lamp,x,y,size);
                            int address=(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;
                            Color primary=Estimate(reservoirs[c.arrayPitch+address]),auxiliary=!dynamicAuxiliaryBaseline&&!singleRuntime&&keys.Length>0?(Estimate(reservoirs[2*c.arrayPitch+address])+Estimate(secondSamples[y*size+x]))*.5f:Color.clear;
                            Color predicted=dynamicAuxiliaryBaseline||singleRuntime?primary:(primary+auxiliary)*.5f;
                            if(observeVariance&&frame>=16)
                            {
                                Require(expected.r>0,"Reuse variance fixture requires positive analytic red");
                                int point=(y-48)/2*16+(x-48)/2,n=frame-15,block=(frame-16)/16;
                                for(int signal=0;signal<2;signal++){double value=(signal==0?primary.r:a.r)/expected.r,delta=value-pointMeans[signal,point];pointMeans[signal,point]+=delta/n;pointM2[signal,point]+=delta*(value-pointMeans[signal,point]);}
                                blockPrimary[block]+=primary.r;blockReference[block]+=expected.r;
                            }
                            bool decompositionPoint=(!AuxiliaryImageHash||IsAuxiliaryProbe(x,y))&&(!runtimeMultiAudit||!pairedOpaqueAudit||singleRuntime);
                            for(int channel=0;channel<3;channel++)
                            {
                                Require(float.IsFinite(a[channel])&&float.IsFinite(f[channel])&&a[channel]>=0&&f[channel]>=0,"Dynamic CDF non-finite/negative radiance");
                                if(expected[channel]==0)dark=Math.Max(dark,a[channel]);
                                if(decompositionPoint){double error=Math.Abs(a[channel]-predicted[channel])/(Math.Max(Math.Abs(predicted[channel]),expected[channel])+.001);shadeError=Math.Max(shadeError,error);Require(error<.003,"Actual raw shading differs from GPU packed sample decomposition");}
                                if(frame>=16){reference[channel]+=expected[channel];sums[0,channel]+=a[channel];sums[1,channel]+=f[channel];if(decompositionPoint){decompositionReference[channel]+=expected[channel];primarySums[channel]+=primary[channel];auxiliarySums[channel]+=auxiliary[channel];}if(expected[channel]==0)dark=Math.Max(dark,f[channel]);}
                            }
                            Color Estimate(PairedPackedReservoir reservoir)
                            {
                                if((reservoir.lightData&0x80000000)==0)return Color.clear;
                                uint index=reservoir.lightData&0x7fffffff;Require(index<keys.Length&&float.IsFinite(reservoir.weight)&&reservoir.weight>=0,"Dynamic shading sample/key invalid");
                                return DynamicDiffuseReference(keys[index],x,y,size)*reservoir.weight;
                            }
                        }
                    }
                    var ratios=new double[2,3];bool pass=dark<.001;
                    for(int signal=0;signal<2;signal++)for(int channel=0;channel<3;channel++)
                    {ratios[signal,channel]=reference[channel]>0?sums[signal,channel]/reference[channel]:sums[signal,channel]/((frames-16)*256);pass&=reference[channel]>0?Math.Abs(ratios[signal,channel]-1)<(signal==0?.03:.05):ratios[signal,channel]<.001;}
                    var result=new DynamicCDFCase{phase=phase,lights=keys.Length,frames=frames,cdfChecks=checks,mappingChecks=maps,resets=resets,confidenceFrames=confidence,pass=pass,rawRed=ratios[0,0],rawGreen=ratios[0,1],rawBlue=ratios[0,2],filteredRed=ratios[1,0],filteredGreen=ratios[1,1],filteredBlue=ratios[1,2],maxDarkSignal=dark};cases.Add(result);all&=pass;
                    result.primaryRed=Ratio(primarySums,0);result.primaryGreen=Ratio(primarySums,1);result.primaryBlue=Ratio(primarySums,2);result.auxiliaryRed=Ratio(auxiliarySums,0);result.auxiliaryGreen=Ratio(auxiliarySums,1);result.auxiliaryBlue=Ratio(auxiliarySums,2);result.maxShadeRelativeError=shadeError;
                    if(observeVariance)
                    {
                        result.reuseMode=dynamicReuseMode;result.varianceFrames=frames-16;result.primaryRedBlockRatios=new double[blockPrimary.Length];
                        for(int point=0;point<256;point++){result.primaryRedRelativeTemporalVariance+=pointM2[0,point]/((frames-17)*256);result.rawRedRelativeTemporalVariance+=pointM2[1,point]/((frames-17)*256);}
                        for(int block=0;block<blockPrimary.Length;block++)result.primaryRedBlockRatios[block]=blockPrimary[block]/blockReference[block];
                    }
                    double Ratio(double[] values,int channel)=>decompositionReference[channel]>0?values[channel]/decompositionReference[channel]:0;
                    result.decompositionRegion=runtimeMultiAudit&&pairedOpaqueAudit?"local-second-not-captured":AuxiliaryImageHash?"four-gpu-probes":"full-roi";if(imageHash!=null)result.outputHash=BitConverter.ToString(imageHash.GetHashAndReset()).Replace("-","");
                    Debug.Log($"[HoRTDIValidation] DYNAMIC_AUX_CDF opaque={pairedOpaqueAudit} phase={phase} lights={keys.Length} raw={result.rawRed:F6}/{result.rawGreen:F6}/{result.rawBlue:F6} filtered={result.filteredRed:F6}/{result.filteredGreen:F6}/{result.filteredBlue:F6} dark={dark:G6} resets={resets} confidence={confidence} pass={pass}");
                    Debug.Log($"[HoRTDIValidation] DYNAMIC_CDF_DECOMPOSITION phase={phase} primary={result.primaryRed:F6}/{result.primaryGreen:F6}/{result.primaryBlue:F6} auxiliary={result.auxiliaryRed:F6}/{result.auxiliaryGreen:F6}/{result.auxiliaryBlue:F6} maxShadeRelativeError={shadeError:G6}");
                }
                string variant=dynamicAuxiliaryBaseline?"single-":"triple-";
                string prefix=dynamicAuxiliaryDomainReset||dynamicAuxiliaryBaseline?"rtdi-domainreset70-"+variant+(dynamicAuxiliaryDomainReset?"candidate-":"baseline-"):"rtdi-dynamiccdf69-";
                if(dynamicDomainProductionAudit)prefix="rtdi-domainreset70-single-production-";
                if(compactAuxiliaryAudit)prefix="rtdi-compact74-dynamic-";
                if(AuxiliaryImageHash)prefix="rtdi-direct75-dynamic-"+(opaqueDirectAuxiliaryAudit?"direct-":"baseline-");
                if(runtimeMultiAudit)prefix="rtdi-runtime78-dynamic-";
                if(diffuseSegmentAudit)prefix="rtdi-diffusesegment83-dynamic-";
                if(surfaceAuxiliaryAudit)prefix="rtdi-surfaceris88-dynamic-";
                if(dynamicReuseMode!=null)prefix="rtdi-reuse90-"+dynamicReuseMode+"-";
                if(presampleTilePixels>0)prefix="rtdi-coherence91-tile"+presampleTilePixels+(presampleTilePixels==1?"-separated-":"-");
                if(temperedTargetAudit||dynamicUniformSource)prefix="rtdi-tempered92-"+(dynamicUniformSource?"uniform-":"power-")+(temperedTargetAudit?"candidate-":"baseline-");
                File.WriteAllText(Path.Combine(Application.dataPath,"../"+prefix+(dynamicAuxiliaryLongAudit?"long-":"")+(pairedOpaqueAudit?"opaque-":"cutout-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new DynamicCDFReport{qualityPass=all,path=compatibility?"compatibility":"RenderGraph",visibility=pairedOpaqueAudit?"inline-opaque":"native-cutout",cases=cases.ToArray()},true));
                Require(all,"Dynamic auxiliary CDF energy/dark-signal gates failed");
            }
            finally{c.nrd.ValidationTimeSeconds=null;computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);}

            void Configure(string phase)
            {
                foreach(var lamp in lamps){lamp.enabled=true;lamp.type=LightType.Point;lamp.intensity=4;lamp.range=100;lamp.useColorTemperature=false;lamp.transform.rotation=Quaternion.LookRotation(-lamp.transform.position,Vector3.up);}
                light.color=Color.red;green.color=Color.green;blue.color=Color.blue;
                if(phase=="powerTemperature"){light.intensity=.4f;blue.intensity=40;green.useColorTemperature=true;green.colorTemperature=3000;}
                if(phase=="disableBlue")blue.enabled=false;
                if(phase=="blueDirectional"){blue.type=LightType.Directional;blue.transform.rotation=Quaternion.identity;}
                if(phase=="redSpot"){light.type=LightType.Spot;light.spotAngle=70;light.innerSpotAngle=30;}
                if(phase=="disableRed")light.enabled=false;
                if(phase=="allDisabled")foreach(var lamp in lamps)lamp.enabled=false;
                if(phase=="replaceGreen"){green.enabled=false;green=Point(new Vector3(.3f,.3f,-1.2f),Color.green,4);lamps[1]=green;}
            }
        }

        private static bool LightDomainChanged(HoRTDIContext c,Light[] keys)
        {
            if(keys.Length!=c.previousLightKeys.Length)return true;
            var previousData=(HoRTDIRendererFeature.AnalyticLight[])typeof(HoRTDIContext).GetField("previousLightData",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
            foreach(var lamp in keys)
            {
                int old=Array.FindIndex(c.previousLightKeys,p=>ReferenceEquals(p,lamp));if(old<0)return true;
                float type=lamp.type==LightType.Point?0:lamp.type==LightType.Spot?1:2;
                if(previousData[old].radianceType.w!=type)return true;
            }
            return false;
        }

        private static int CheckDynamicLightMaps(HoRTDIContext c,Light[] previous,Light[] current)
        {
            var forward=new int[Math.Max(1,current.Length)];var reverse=new int[Math.Max(1,previous.Length)];c.currentToPrevious.GetData(forward);c.previousToCurrent.GetData(reverse);
            int checks=0;for(int i=0;i<current.Length;i++){Require(forward[i]==Array.FindIndex(previous,p=>ReferenceEquals(p,current[i])),"Dynamic current-to-previous identity mismatch");checks++;}
            for(int i=0;i<previous.Length;i++){Require(reverse[i]==Array.FindIndex(current,p=>ReferenceEquals(p,previous[i])),"Dynamic previous-to-current identity mismatch");checks++;}return checks;
        }

        private static int CheckDynamicAuxiliarySamples(HoRTDIContext c,float[] cdf,int count,GraphicsBuffer second)
        {
            var packed=new PairedPackedReservoir[1];int checks=0;
            foreach(int x in new[]{52,60,68,76})
            {
                const int y=64;int address=2*c.arrayPitch+(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;
                for(int draw=0;draw<2;draw++)
                {
                    if(draw==0)c.reservoirs.GetData(packed,0,address,1);else {if(second==null)continue;ReadSecondAuxiliary(second,packed,0,y*c.width+x,1);}
                    // Opaque zero-light shading deliberately skips both stale
                    // scratch slots; image checks establish that they contribute 0.
                    if(count==0)continue;
                    uint index=packed[0].lightData&0x7fffffff;Require((packed[0].lightData&0x80000000)!=0&&index<count,"Dynamic auxiliary light index invalid");
                    float pdf=cdf[index]-(index>0?cdf[index-1]:0),expected=1/pdf;
                    if(HoRTDISurfaceAuxiliaryValidation.Enabled)Require(float.IsFinite(packed[0].weight)&&packed[0].weight>=0&&(draw!=0||(packed[0].mVisibility>>18)==4),"Dynamic fresh RIS weight/metadata invalid");
                    else Require(float.IsFinite(packed[0].weight)&&Math.Abs(packed[0].weight-expected)<expected*3e-5,"Dynamic auxiliary inverse PDF mismatch");checks++;
                }
            }
            return checks;
        }

        private static Color DynamicDiffuseReference(Light lamp,int x,int y,int size)
        {
            Vector3 p=PlanePosition(x,y,size),delta=lamp.transform.position-p;double attenuation=1;Vector3 direction=-lamp.transform.forward;
            if(lamp.type!=LightType.Directional)
            {
                direction=delta.normalized;double d2=delta.sqrMagnitude,fade=Math.Max(0,1-Math.Pow(d2/(lamp.range*lamp.range),2));attenuation=fade*fade/d2;
                if(lamp.type==LightType.Spot){double inner=Math.Cos(lamp.innerSpotAngle*Math.PI/360),outer=Math.Cos(lamp.spotAngle*Math.PI/360),cone=Math.Clamp((Vector3.Dot(-direction,lamp.transform.forward)-outer)/(inner-outer),0,1);attenuation*=cone*cone;}
            }
            var color=QualitySettings.activeColorSpace==ColorSpace.Linear?lamp.color.linear:lamp.color;if(lamp.useColorTemperature)color*=Mathf.CorrelatedColorTemperatureToRGB(lamp.colorTemperature);
            return color*(float)(Math.Max(0,Vector3.Dot(Vector3.back,direction))*Math.Max(0,lamp.intensity)*attenuation/Math.PI);
        }
    }
}
