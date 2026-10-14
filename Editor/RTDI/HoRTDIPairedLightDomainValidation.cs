using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool pairedLightDomainAudit;
        public static void RunPairedLightDomainBaseline(){pairedLightDomainAudit=true;RunCutoutResolutionSpecular();}
        public static void RunPairedLightDomainBaselineCompatibility(){compatibility=true;RunPairedLightDomainBaseline();}
        public static void RunPairedLightDomain(){cutoutPairedFinalSample=true;RunPairedLightDomainBaseline();}
        public static void RunPairedLightDomainCompatibility(){compatibility=true;RunPairedLightDomain();}
        [Serializable] private sealed class PairedLightDomainReport{public bool qualityPass;public string path,auxiliaryPdf;public int finalSamples;public PairedLightDomainCase[] cases;}
        [Serializable] private sealed class PairedLightDomainCase
        {
            public string profile,outputHash;public int size,lights,points,tracedFrames,resetFrames,cdfChecks,auxiliaryAgreements;public long auxiliaryBytes;public bool pass;
            public long primaryFinalRays,firstAuxiliaryRays,secondAuxiliaryRays;
            public double rawRed,rawBlue,filteredRed,filteredBlue,frameL1,changedL1,rawNormalizedMse,filteredNormalizedMse,minScale;
        }
        private sealed class PairedLightDomainPoint{internal int x,y;internal Vector3 nearIntersection;internal double r,b;}
        private static void ValidatePairedLightDomain(Light blue)
        {
            const int size=256,frames=256;target.Release();target.width=target.height=size;target.Create();camera.aspect=1;feature.Settings.useEnvironment=false;
            if(pairedOpaqueAudit){blocker.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;AlphaFill(alphaTexture,1);}
            var weak=new List<Light>();for(int i=0;i<30;i++){var lamp=Point(new Vector3(-1.5f-.06f*i,.3f*Mathf.Sin(i*.7f),-1),Color.blue,.01f);lamp.enabled=false;weak.Add(lamp);}
            var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var visibility=pairedSecondAuxiliary?Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true)):null;
            var results=new List<PairedLightDomainCase>();bool allPass=true;int clock=0;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=feature.GetType();var computeField=type.GetField("shader",flags);var rayField=type.GetField("finalVisibilityShader",flags);var originalCompute=computeField.GetValue(feature);var originalRay=rayField.GetValue(feature);
            foreach(string profile in new[]{"spotRapid","spotStep","manyWeakRapid","hdrRapid"})
            {
                bool many=profile=="manyWeakRapid",spot=profile.StartsWith("spot",StringComparison.Ordinal),step=profile=="spotStep",hdr=profile=="hdrRapid";
                foreach(var lamp in weak)lamp.enabled=many;
                foreach(var lamp in new[]{light,blue})
                {
                    lamp.type=spot?LightType.Spot:LightType.Point;lamp.intensity=hdr?40000:4;lamp.useColorTemperature=false;lamp.range=100;
                    lamp.spotAngle=55;lamp.innerSpotAngle=15;lamp.transform.rotation=Quaternion.LookRotation(-lamp.transform.position,Vector3.up);
                }
                feature.Settings.localSampling=many?HoRTDILocalSampling.Power:HoRTDILocalSampling.Uniform;
                if(pairedAuxiliaryPowerCDF){computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);}
                skinBlockerBone.localPosition=Vector3.zero;RenderCamera();var c=feature.LastContext;
                Require((pairedOpaqueAudit?c.finalVisibility==null:c.finalVisibility!=null)&&!c.environmentSampling&&c.previousLightKeys.Length==(many?32:2),"Light-domain visibility/light count invalid");
                if(runtimeMultiAudit)Require(pairedOpaqueAudit?c.auxiliarySecondary==null:c.auxiliarySecondary!=null&&c.auxiliarySecondary.stride==8,"Runtime auxiliary resource mode invalid");
                var cdfKeys=(Light[])c.previousLightKeys.Clone();float[] cdf=pairedAuxiliaryPowerCDF?BuildAuxiliaryCDF(cdfKeys):null;
                using var diffuseSegmentOverride=diffuseSegmentAudit?ApplyDiffuseSegment(c):null;
                if(runtimeMultiAudit)cdf=ReadRuntimeAuxiliaryCDF(c);
                using var cdfBuffer=pairedAuxiliaryPowerCDF?new GraphicsBuffer(GraphicsBuffer.Target.Structured,cdf.Length,4):null;
                using var secondAuxiliary=pairedSecondAuxiliary?new GraphicsBuffer(GraphicsBuffer.Target.Structured,SecondAuxiliaryCount(size*size),SecondAuxiliaryStride):null;
                using var rayCounter=secondAuxiliaryRayAudit?new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,4):null;var rayCounts=new uint[3];
                if(pairedAuxiliaryPowerCDF)
                {
                    ApplyCutoutPairedFinalSample();cdfBuffer.SetData(cdf);pairedFinalShader.SetBuffer("_HoRTDIAuxiliaryCDF",cdfBuffer);
                    if(pairedSecondAuxiliary){var compute=(ComputeShader)computeField.GetValue(feature);BindSecondAuxiliary(pairedFinalShader,compute,compute.FindKernel("ShadeNRD"),secondAuxiliary);if(pairedOpaqueAudit)compute.SetBuffer(compute.FindKernel("ShadeNRD"),Shader.PropertyToID("_HoRTDIAuxiliaryCDF"),cdfBuffer);}
                    if(secondAuxiliaryRayAudit){rayCounter.SetData(rayCounts);pairedFinalShader.SetBuffer("_HoRTDIValidationRayCounts",rayCounter);}
                }
                var points=new List<PairedLightDomainPoint>();float minCone=1,maxCone=0;
                for(int y=size/4;y<3*size/4;y+=2)for(int x=size/4;x<11*size/16;x+=2)
                {
                    var p=PlanePosition(x,y,size);var delta=light.transform.position-p;float t=(blocker.transform.position.z-p.z)/delta.z;
                    double r=PairedDomainLamp(light,p,out float cone),b=PairedDomainLamp(blue,p,out _);minCone=Mathf.Min(minCone,cone);maxCone=Mathf.Max(maxCone,cone);
                    if(many)foreach(var lamp in weak)b+=PairedDomainLamp(lamp,p,out _);
                    // All blue emitters are left of the complete blocker, for
                    // the entire non-negative horizontal bone trajectory.
                    Require(OutsideLeft(blue,p),"Far lamp intersects swept blocker");
                    if(many)foreach(var lamp in weak)Require(OutsideLeft(lamp,p),"Weak lamp intersects swept blocker");
                    points.Add(new PairedLightDomainPoint{x=x,y=y,nearIntersection=blocker.transform.InverseTransformPoint(p+delta*t),r=r,b=b});
                }
                if(spot)Require(minCone<.9f&&maxCone>.99f,"Spot fixture did not cover cone attenuation");
                c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)Draw();
                if(secondAuxiliaryRayAudit){rayCounter.GetData(rayCounts);Array.Clear(rayCounts,0,rayCounts.Length);rayCounter.SetData(rayCounts);}
                double refR=0,refB=0,rr=0,rb=0,fr=0,fb=0,l1=0,rawMse=0,filteredMse=0,squares=0,changedError=0,changedReference=0,minScale=1;
                int traced=0,resets=0,cdfChecks=0,agreements=0;var previous=new double[points.Count];var changed=new bool[points.Count];var scale=new uint[c.nrdScale.count];
                using var imageHash=AuxiliaryImageHash?System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256):null;
                for(int frame=0;frame<frames;frame++)
                {
                    float offset=step?((frame/16&1)==0?0:.5f):.25f+.25f*Mathf.Sin(frame*.27f);skinBlockerBone.localPosition=Vector3.right*offset;Draw();
                    Require(c.nrd.LastDispatchCount>0,"Light-domain bypassed RELAX");traced+=c.lastVisibilityGradients?1:0;resets+=c.lastNrdReset?1:0;ReadImage(c.specular.rt,raw);ReadImage(c.filteredSpecular.rt,filtered);
                    if(opaqueDirectAuxiliaryAudit)Require(c.finalVisibility==null,"Direct opaque prototype cannot enter native Cutout visibility");
                    if(imageHash!=null){imageHash.AppendData(raw.GetRawTextureData<byte>().ToArray());imageHash.AppendData(filtered.GetRawTextureData<byte>().ToArray());}
                    if(pairedAuxiliaryPowerCDF||runtimeMultiAudit)
                    {
                        Require(c.previousLightKeys.Length==cdfKeys.Length,"Auxiliary CDF light count changed");for(int i=0;i<cdfKeys.Length;i++)Require(c.previousLightKeys[i]==cdfKeys[i],"Auxiliary CDF light ordering changed");
                        if(frame%16==0){if((pairedSecondAuxiliary||runtimeMultiAudit)&&!pairedOpaqueAudit){if(visibility==null)visibility=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));ReadImage(c.finalVisibility.rt,visibility);}if(runtimeMultiAudit)cdf=ReadRuntimeAuxiliaryCDF(c);cdfChecks+=CheckAuxiliaryCDFSamples(c,cdf,runtimeMultiAudit?c.auxiliarySecondary:secondAuxiliary,visibility,out int observedAgreements);agreements+=observedAgreements;}
                    }
                    c.nrdScale.GetData(scale);double s=BitConverter.Int32BitsToSingle((int)scale[1]);Require(double.IsFinite(s)&&s>0&&s<=1,"Light-domain scale invalid");minScale=Math.Min(minScale,s);
                    for(int i=0;i<points.Count;i++)
                    {
                        var p=points[i];var local=p.nearIntersection-skinBlockerBone.localPosition;
                        bool shadow=Mathf.Abs(local.x)<=.5f&&Mathf.Abs(local.y)<=.5f&&CutoutCpuSample(alphaTexture,new Vector2(local.x+.5f,local.y+.5f))>=.5f;
                        if(i%256==0)Require(shadow==CutoutPlaneShadow(light,p.x,p.y,size),"Precomputed CPU shadow differs from original ray reference");
                        double r=shadow?0:p.r,b=p.b;var a=raw.GetPixel(p.x,p.y);var f=filtered.GetPixel(p.x,p.y);
                        Require(float.IsFinite(a.r)&&float.IsFinite(a.g)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.g)&&float.IsFinite(f.b),"Light-domain non-finite signal");
                        if(step&&frame%16==0)changed[i]=frame>0&&Math.Abs(r-previous[i])>1e-6;
                        if(step&&frame%16<3&&changed[i]){changedError+=Math.Abs(f.r-r);changedReference+=p.r;}
                        previous[i]=r;refR+=r;refB+=b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;l1+=Math.Abs(f.r-r)+Math.Abs(f.b-b);
                        rawMse+=(a.r-r)*(a.r-r)+a.g*a.g+(a.b-b)*(a.b-b);filteredMse+=(f.r-r)*(f.r-r)+f.g*f.g+(f.b-b)*(f.b-b);squares+=r*r+b*b;
                    }
                }
                if(pairedSecondAuxiliary)Require(agreements>0&&agreements<64,"Two auxiliary draws are identical or unexpectedly disjoint");
                if(secondAuxiliaryRayAudit){rayCounter.GetData(rayCounts);Require(rayCounts[0]>0&&rayCounts[1]>0&&rayCounts[1]==rayCounts[2],"Final ray counters did not cover both auxiliary draws");}
                var result=new PairedLightDomainCase{profile=profile,size=size,lights=c.previousLightKeys.Length,points=points.Count,tracedFrames=traced,resetFrames=resets,cdfChecks=cdfChecks,auxiliaryAgreements=agreements,auxiliaryBytes=(secondAuxiliary?.count??0)*(long)SecondAuxiliaryStride+(cdfBuffer?.count??0)*4L,rawRed=rr/refR,rawBlue=rb/refB,filteredRed=fr/refR,filteredBlue=fb/refB,frameL1=l1/(refR+refB),changedL1=changedReference>0?changedError/changedReference:0,rawNormalizedMse=rawMse/squares,filteredNormalizedMse=filteredMse/squares,minScale=minScale};
                bool visibilityPath=pairedOpaqueAudit?traced==0&&resets>0:traced>0;
                result.pass=visibilityPath&&Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05&&result.frameL1<.35&&(!step||changedReference>0&&result.changedL1<.35)&&(!hdr||minScale<1);
                result.primaryFinalRays=rayCounts[0];result.firstAuxiliaryRays=rayCounts[1];result.secondAuxiliaryRays=rayCounts[2];result.auxiliaryBytes+=(rayCounter?.count??0)*4L;
                if(imageHash!=null)result.outputHash=BitConverter.ToString(imageHash.GetHashAndReset()).Replace("-","");
                allPass&=result.pass;results.Add(result);c.nrd.ValidationTimeSeconds=null;
                if(pairedAuxiliaryPowerCDF){computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);}
                Debug.Log($"[HoRTDIValidation] PAIRED_LIGHT_DOMAIN paired={cutoutPairedFinalSample} profile={profile} lights={result.lights} raw={result.rawRed:F6}/{result.rawBlue:F6} filtered={result.filteredRed:F6}/{result.filteredBlue:F6} frameL1={result.frameL1:F6} changedL1={result.changedL1:F6} rawMse={result.rawNormalizedMse:F6} filteredMse={result.filteredNormalizedMse:F6} scale={minScale} pass={result.pass}");
                if(secondAuxiliaryRayAudit)Debug.Log($"[HoRTDIValidation] FINAL_RAY_COUNTS profile={profile} primary={rayCounts[0]} firstAuxiliary={rayCounts[1]} secondAuxiliary={rayCounts[2]} frames={frames}; excludes gradients/inline queries");
                void Draw(){c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();}
            }
            string name="rtdi-lightdomain51-"+(cutoutPairedFinalSample?"paired-":"baseline-")+(compatibility?"compat":"rg")+".json";
            if(pairedAuxiliaryPowerCDF)name="rtdi-powercdf52-"+(compatibility?"compat":"rg")+".json";
            if(pairedSecondAuxiliary)name="rtdi-triplecdf53-"+(compatibility?"compat":"rg")+".json";
            if(secondAuxiliaryRayAudit)name="rtdi-triplecdf53-rays-"+(compatibility?"compat":"rg")+".json";
            if(pairedOpaqueAudit)name="rtdi-opaqueaux68-"+(compatibility?"compat":"rg")+".json";
            if(compactAuxiliaryAudit)name="rtdi-compact74-lightdomain-"+(secondAuxiliaryRayAudit?"rays-":"")+(pairedOpaqueAudit?"opaque-":"cutout-")+(compatibility?"compat":"rg")+".json";
            if(AuxiliaryImageHash)name="rtdi-direct75-lightdomain-"+(opaqueDirectAuxiliaryAudit?"direct-":"baseline-")+(compatibility?"compat":"rg")+".json";
            if(runtimeMultiAudit)name="rtdi-runtime78-lightdomain-"+(pairedOpaqueAudit?"opaque-":"cutout-")+(compatibility?"compat":"rg")+".json";
            if(diffuseSegmentAudit)name="rtdi-diffusesegment83-lightdomain-"+(compatibility?"compat":"rg")+".json";
            if(surfaceAuxiliaryAudit)name="rtdi-surfaceris88-lightdomain-"+(compatibility?"compat":"rg")+".json";
            if(presampleTilePixels>0)name="rtdi-coherence91-tile"+presampleTilePixels+"-lightdomain-"+(compatibility?"compat":"rg")+".json";
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+name),JsonUtility.ToJson(new PairedLightDomainReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",auxiliaryPdf=runtimeMultiAudit||pairedAuxiliaryPowerCDF?"rgb-power-cdf":cutoutPairedFinalSample?"uniform":"none",finalSamples=runtimeMultiAudit||pairedSecondAuxiliary?3:cutoutPairedFinalSample?2:1,cases=results.ToArray()},true));
            Require(allPass,"Light-domain energy/noise/step/HDR gates failed");
            bool OutsideLeft(Light source,Vector3 p){var delta=source.transform.position-p;float t=(blocker.transform.position.z-p.z)/delta.z;return t>0&&t<1&&blocker.transform.InverseTransformPoint(p+delta*t).x<-.5f;}
        }
        private static double PairedDomainLamp(Light source,Vector3 p,out float cone)
        {
            var delta=source.transform.position-p;double d2=delta.sqrMagnitude;cone=1;
            if(source.type==LightType.Spot){float inner=Mathf.Cos(source.innerSpotAngle*Mathf.Deg2Rad*.5f),outer=Mathf.Cos(source.spotAngle*Mathf.Deg2Rad*.5f);cone=Mathf.Clamp01((Vector3.Dot(-delta.normalized,source.transform.forward)-outer)/(inner-outer));}
            double response=ReferenceGGX(Vector3.back,-camera.transform.forward,delta.normalized,102f/255,new Color(10f/255,10f/255,10f/255,1)).r,fade=Math.Max(0,1-Math.Pow(d2/(source.range*source.range),2));
            return response*source.intensity/d2*fade*fade*cone*cone;
        }
    }
}
