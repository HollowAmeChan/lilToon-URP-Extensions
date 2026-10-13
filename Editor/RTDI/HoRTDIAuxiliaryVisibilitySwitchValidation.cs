using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool auxiliaryVisibilitySwitchAudit,auxiliaryVisibilitySwitchBaseline;
        public static void RunAuxiliaryVisibilitySwitch(){auxiliaryVisibilitySwitchAudit=compactAuxiliaryAudit=pairedSecondAuxiliary=pairedAuxiliaryPowerCDF=cutoutPairedFinalSample=true;RunCutoutResolutionSpecular();}
        public static void RunAuxiliaryVisibilitySwitchCompatibility(){compatibility=true;RunAuxiliaryVisibilitySwitch();}
        public static void RunAuxiliaryVisibilitySwitchBaseline(){auxiliaryVisibilitySwitchBaseline=true;RunAuxiliaryVisibilitySwitch();}
        public static void RunAuxiliaryVisibilitySwitchBaselineCompatibility(){compatibility=true;RunAuxiliaryVisibilitySwitchBaseline();}
        [Serializable] private sealed class VisibilitySwitchReport{public bool resourcePass,qualityPass;public string path,variant;public VisibilitySwitchCase[] cases;}
        [Serializable] private sealed class VisibilitySwitchCase
        {
            public string phase,outputHash;public bool native,pass;public long sidecarBytes;public int frames,checks,resets,confidenceFrames;
            public double rawRed,rawBlue,filteredRed,filteredBlue,transitionL1;
        }
        private static void ValidateAuxiliaryVisibilitySwitch(Light blue)
        {
            const int size=128,frames=48;target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
            feature.Settings.useEnvironment=false;surfaceMaterial.SetFloat("_Reflectance",0);AlphaFill(alphaTexture,1);AlphaFill(alphaMask,1);
            blocker.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;RenderCamera();var c=feature.LastContext;
            Require(c.finalVisibility==null&&c.previousLightKeys.Length==2,"Visibility-switch bootstrap must be opaque with two lights");
            var keys=(Light[])c.previousLightKeys.Clone();var cdf=BuildAuxiliaryCDF(keys);using var cdfBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cdf.Length,4);cdfBuffer.SetData(cdf);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var computeField=feature.GetType().GetField("shader",flags);var rayField=feature.GetType().GetField("finalVisibilityShader",flags);
            var originalCompute=computeField.GetValue(feature);var originalRay=rayField.GetValue(feature);
            var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var vis=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var results=new List<VisibilitySwitchCase>();GraphicsBuffer sidecar=null;bool quality=true;int clock=0;
            try
            {
                pairedOpaqueAudit=true;opaqueDirectAuxiliaryAudit=!auxiliaryVisibilitySwitchBaseline;pairedPrototypeAssetName="HoRTDISwitchOpaque"+(auxiliaryVisibilitySwitchBaseline?"Baseline":"Direct");ApplyCutoutPairedFinalSample();
                var opaqueCompute=(ComputeShader)computeField.GetValue(feature);var opaqueRay=pairedFinalShader;
                pairedOpaqueAudit=opaqueDirectAuxiliaryAudit=false;pairedPrototypeAssetName="HoRTDISwitchCutout";ApplyCutoutPairedFinalSample();
                var nativeCompute=(ComputeShader)computeField.GetValue(feature);var nativeRay=pairedFinalShader;
                Require(!ReferenceEquals(opaqueCompute,nativeCompute),"Switch variants were reimported into the same shader asset");
                foreach(string phase in new[]{"opaqueSolid","cutoutSolid","cutoutHoles","cutoutOpen","opaqueAgain","cutoutAgain","disabledCaster","reenabledCutout","opaqueFinal"})
                {
                    bool native=phase.StartsWith("cutout",StringComparison.Ordinal)||phase=="reenabledCutout";bool active=phase!="disabledCaster";
                    blocker.SetActive(active);blocker.GetComponent<Renderer>().sharedMaterial=native?cutoutMaterial:surfaceMaterial;
                    if(phase=="cutoutOpen")AlphaFill(alphaTexture,0);
                    else if(phase=="cutoutHoles"){for(int y=0;y<16;y++)for(int x=0;x<16;x++)alphaTexture.SetPixel(x,y,new Color(1,1,1,x>=5&&x<11?0:1));alphaTexture.Apply();}
                    else AlphaFill(alphaTexture,1);
                    // Fixture-owned selection happens before rendering. Actual
                    // scene classification is independently checked afterward.
                    pairedOpaqueAudit=!native;opaqueDirectAuxiliaryAudit=!native&&!auxiliaryVisibilitySwitchBaseline;
                    var compute=native?nativeCompute:opaqueCompute;pairedFinalShader=native?nativeRay:opaqueRay;computeField.SetValue(feature,compute);rayField.SetValue(feature,pairedFinalShader);
                    int count=SecondAuxiliaryCount(size*size);if(sidecar==null||sidecar.count!=count){sidecar?.Dispose();sidecar=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,8);}
                    BindSecondAuxiliary(pairedFinalShader,compute,compute.FindKernel("ShadeNRD"),sidecar);pairedFinalShader.SetBuffer("_HoRTDIAuxiliaryCDF",cdfBuffer);
                    if(!native)compute.SetBuffer(compute.FindKernel("ShadeNRD"),Shader.PropertyToID("_HoRTDIAuxiliaryCDF"),cdfBuffer);
                    var result=new VisibilitySwitchCase{phase=phase,native=native,sidecarBytes=sidecar.count*8L,frames=frames};double rr=0,rb=0,fr=0,fb=0,refR=0,refB=0,transition=0,transitionRef=0;
                    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    for(int frame=0;frame<frames;frame++)
                    {
                        skinBlockerBone.localPosition=Vector3.right*(.25f+.25f*Mathf.Sin(clock*.27f));c.nrd.ValidationTimeSeconds=++clock/60.0;RenderCamera();
                        Require((c.finalVisibility!=null)==native&&c.nrd.LastDispatchCount>0,"Actual visibility/NRD dispatch differs from selected variant");
                        Require(sidecar.count==(opaqueDirectAuxiliaryAudit?4:size*size)&&sidecar.stride==8,"Visibility switch sidecar capacity mismatch");
                        Require(c.previousLightKeys.Length==2&&ReferenceEquals(c.previousLightKeys[0],keys[0])&&ReferenceEquals(c.previousLightKeys[1],keys[1]),"Visibility switch changed light ordering");
                        result.resets+=c.lastNrdReset?1:0;result.confidenceFrames+=c.lastVisibilityGradients?1:0;
                        ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);hash.AppendData(raw.GetRawTextureData<byte>().ToArray());hash.AppendData(filtered.GetRawTextureData<byte>().ToArray());
                        if(native)ReadImage(c.finalVisibility.rt,vis);result.checks+=CheckSwitchAuxiliary(c,cdf,sidecar,native?vis:null,active);
                        for(int y=48;y<80;y+=2)for(int x=48;x<80;x+=2)
                        {
                            double r=active&&CutoutPlaneShadow(light,x,y,size)?0:DynamicDiffuseReference(light,x,y,size).r,b=active&&CutoutPlaneShadow(blue,x,y,size)?0:DynamicDiffuseReference(blue,x,y,size).b;
                            var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);Require(float.IsFinite(a.r)&&float.IsFinite(a.b)&&float.IsFinite(f.r)&&float.IsFinite(f.b)&&a.r>=0&&a.b>=0&&f.r>=0&&f.b>=0,"Visibility switch non-finite/negative signal");
                            if(frame<3){transition+=Math.Abs(f.r-r)+Math.Abs(f.b-b);transitionRef+=r+b;}
                            if(frame>=16){refR+=r;refB+=b;rr+=a.r;rb+=a.b;fr+=f.r;fb+=f.b;}
                        }
                    }
                    result.outputHash=BitConverter.ToString(hash.GetHashAndReset()).Replace("-","");result.rawRed=rr/refR;result.rawBlue=rb/refB;result.filteredRed=fr/refR;result.filteredBlue=fb/refB;result.transitionL1=transition/transitionRef;
                    result.pass=Math.Abs(result.rawRed-1)<.03&&Math.Abs(result.rawBlue-1)<.03&&Math.Abs(result.filteredRed-1)<.05&&Math.Abs(result.filteredBlue-1)<.05;quality&=result.pass;results.Add(result);
                    Debug.Log($"[HoRTDIValidation] VISIBILITY_SWITCH phase={phase} native={native} bytes={result.sidecarBytes} reset={result.resets} confidence={result.confidenceFrames} raw={result.rawRed:F6}/{result.rawBlue:F6} filtered={result.filteredRed:F6}/{result.filteredBlue:F6} pass={result.pass}");
                }
                File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-switch76-"+(auxiliaryVisibilitySwitchBaseline?"baseline-":"optimized-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new VisibilitySwitchReport{resourcePass=true,qualityPass=quality,path=compatibility?"compatibility":"RenderGraph",variant=auxiliaryVisibilitySwitchBaseline?"always-full-compact":"opaque-local-native-compact",cases=results.ToArray()},true));
                Require(quality,"Visibility-switch energy gates failed; resource findings saved");
            }
            finally{sidecar?.Dispose();c.nrd.ValidationTimeSeconds=null;computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);opaqueDirectAuxiliaryAudit=pairedOpaqueAudit=false;pairedPrototypeAssetName="HoRTDIPaired";}
        }
        private static int CheckSwitchAuxiliary(HoRTDIContext c,float[] cdf,GraphicsBuffer second,Texture2D visibility,bool active)
        {
            var sample=new PairedPackedReservoir[1];int checks=0;
            foreach(int x in directDynamicXs)
            {
                const int y=64;ReadSecondAuxiliary(second,sample,0,y*c.width+x,1);var value=sample[0];uint index=value.lightData&0x7fffffff;
                Require((value.lightData&0x80000000)!=0&&index<cdf.Length,"Switched auxiliary index invalid");float pdf=cdf[index]-(index>0?cdf[index-1]:0);
                Require(Math.Abs(value.weight-1/pdf)<3e-5/pdf,"Switched auxiliary inverse PDF invalid");bool visible=!active||!CutoutPlaneShadow(c.previousLightKeys[index],x,y,c.width);
                Require(((value.mVisibility&63)!=0)==visible,"Switched auxiliary cached visibility differs from CPU alpha/opaque reference");
                if(visibility!=null)Require((((uint)Mathf.RoundToInt(visibility.GetPixel(x,y).r*7)>>2)&1)==(visible?1u:0u),"Switched native final visibility mismatch");checks++;
            }
            return checks;
        }
    }
}
