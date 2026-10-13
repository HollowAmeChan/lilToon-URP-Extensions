using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using lilToon.URP.Extensions.Atmosphere;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool auxiliaryEnvironmentAudit,auxiliaryEnvironmentPartition;
        private static bool auxiliaryPerspectiveAudit;
        public static void RunAuxiliaryEnvironmentBaseline(){auxiliaryEnvironmentAudit=true;RunCutoutResolutionSpecular();}
        public static void RunAuxiliaryEnvironmentBaselineCompatibility(){compatibility=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunAuxiliaryEnvironmentNaive(){pairedSecondAuxiliary=pairedAuxiliaryPowerCDF=cutoutPairedFinalSample=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunAuxiliaryEnvironment(){auxiliaryEnvironmentPartition=true;RunAuxiliaryEnvironmentNaive();}
        public static void RunAuxiliaryEnvironmentCompatibility(){compatibility=true;RunAuxiliaryEnvironment();}
        public static void RunPerspectiveAuxiliaryEnvironmentBaseline(){auxiliaryPerspectiveAudit=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunPerspectiveAuxiliaryEnvironmentBaselineCompatibility(){compatibility=true;RunPerspectiveAuxiliaryEnvironmentBaseline();}
        public static void RunPerspectiveAuxiliaryEnvironment(){auxiliaryPerspectiveAudit=true;RunAuxiliaryEnvironment();}
        public static void RunPerspectiveAuxiliaryEnvironmentCompatibility(){compatibility=true;RunPerspectiveAuxiliaryEnvironment();}
        private static (string compute,string ray) PartitionAuxiliaryEnvironment(string compute,string ray)
        {
            compute=ReplaceOnce(compute,"diffuse=.5*diffuse+.25*(extraD+secondD);specular=.5*specular+.25*(extraS+secondS);diffuseLighting=.5*diffuseLighting+.25*(extraLighting+secondLighting);",@"
        float primaryWeight=RTXDI_GetDIReservoirLightIndex(reservoir)==HO_RTDI_ENVIRONMENT_LIGHT_INDEX?1:.5;
        diffuse=primaryWeight*diffuse+.25*(extraD+secondD);specular=primaryWeight*specular+.25*(extraS+secondS);diffuseLighting=primaryWeight*diffuseLighting+.25*(extraLighting+secondLighting);");
            ray=ReplaceOnce(ray,"if(RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)","if(RAB_IsSurfaceValid(surface))");
            ray=ReplaceOnce(ray,"        RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel", "        if((uint)_HoRTDIFrame.z>0)\n        {\n        RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel");
            ray=ReplaceOnce(ray,"        if(HoDITraceFinal(surface,sample2))bits|=4;","        if(HoDITraceFinal(surface,sample2))bits|=4;\n        }");
            return(compute,ray);
        }
        [Serializable] private sealed class AuxiliaryEnvironmentReport{public bool qualityPass;public string path,mode,projection;public AuxiliaryEnvironmentCase[] cases;}
        [Serializable] private sealed class AuxiliaryEnvironmentCase{public bool mixed,reuse,pass;public double minSignal,diffuseRed,diffuseGreen,diffuseBlue,filteredDiffuseRed,filteredDiffuseGreen,filteredDiffuseBlue,specularRed,specularGreen,specularBlue,filteredSpecularRed,filteredSpecularGreen,filteredSpecularBlue;}
        private static void ValidateAuxiliaryEnvironment(Light blue)
        {
            const int size=128,frames=512;target.Release();target.width=target.height=size;target.Create();camera.aspect=1;
            if(auxiliaryPerspectiveAudit){camera.orthographic=false;camera.fieldOfView=40;}
            AlphaFill(alphaTexture,0);AlphaFill(alphaMask,1);atmosphere.Settings.enabled=true;atmosphere.Settings.source=HoAtmosphereSource.Constant;atmosphere.Settings.constantColor=Color.green;
            feature.Settings.useEnvironment=true;feature.Settings.environmentSamples=feature.Settings.environmentBrdfSamples=4;
            var rawD=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var rawS=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var filteredD=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filteredS=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var computeField=feature.GetType().GetField("shader",flags);var rayField=feature.GetType().GetField("finalVisibilityShader",flags);var originalCompute=computeField.GetValue(feature);var originalRay=rayField.GetValue(feature);
            var cases=new List<AuxiliaryEnvironmentCase>();bool allPass=true;double skySpec=EnvironmentSpecularReference(102.0/255);
            var skyReferences=new Dictionary<int,double>();
            foreach(bool mixed in auxiliaryEnvironmentStages||auxiliarySpatialAudit?new[]{true}:new[]{false,true})foreach(bool reuse in new[]{false,true})
            {
                light.enabled=blue.enabled=mixed;feature.Settings.temporal=feature.Settings.spatial=reuse;computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);RenderCamera();var c=feature.LastContext;
                Require(c.environmentSampling&&c.finalVisibility!=null&&c.previousLightKeys.Length==(mixed?2:0),"Environment/alpha/light fixture invalid");
                var keys=(Light[])c.previousLightKeys.Clone();float[] cdf=mixed?BuildAuxiliaryCDF(keys):new[]{1f};
                using var cdfBuffer=cutoutPairedFinalSample?new GraphicsBuffer(GraphicsBuffer.Target.Structured,cdf.Length,4):null;
                using var second=cutoutPairedFinalSample?new GraphicsBuffer(GraphicsBuffer.Target.Structured,size*size,SecondAuxiliaryStride):null;
                if(cutoutPairedFinalSample)
                {
                    ApplyCutoutPairedFinalSample();cdfBuffer.SetData(cdf);pairedFinalShader.SetBuffer("_HoRTDIAuxiliaryCDF",cdfBuffer);pairedFinalShader.SetBuffer("_HoRTDISecondAuxiliary",second);
                    var compute=(ComputeShader)computeField.GetValue(feature);compute.SetBuffer(compute.FindKernel("ShadeNRD"),Shader.PropertyToID("_HoRTDISecondAuxiliary"),second);
                }
                using var lateSpecOverride=lateSpecExchangeAudit?ApplyLateSpecExchange(c):null;
                using var diffuseSegmentOverride=diffuseSegmentAudit?ApplyDiffuseSegment(c):null;
                if(auxiliarySpatialAudit){ValidateAuxiliarySpatial(c,reuse,blue);computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);continue;}
                if(auxiliaryEnvironmentStages){ValidateAuxiliaryEnvironmentStages(c,mixed,reuse,blue,skySpec);computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);continue;}
                c.historyValid=c.nrdHistoryValid=false;c.frame=0;for(int i=0;i<192;i++)RenderCamera();
                if(lateSpecWeightAudit)lateSpecOverride.ResetHistogram();
                var sums=new double[4,3];var references=new double[2,3];int samples=0;double minSignal=0;
                var referencePoints=new List<(int x,int y,double dr,double db,double sr,double sg,double sb)>();
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    Require(!CutoutPlaneShadow(light,x,y,size)&&!CutoutPlaneShadow(blue,x,y,size),"Transparent fixture occluded analytic reference");
                    double dr=mixed?F0BoundaryLamp(light,x,y,size):0,db=mixed?F0BoundaryLamp(blue,x,y,size):0,sr=mixed?ResolutionMotionLamp(light,x,y,size):0,sb=mixed?ResolutionMotionLamp(blue,x,y,size):0,sg=skySpec;
                    if(auxiliaryPerspectiveAudit)
                    {
                        var p=PlanePosition(x,y,size);var view=(camera.transform.position-p).normalized;int key=(int)Math.Round(Math.Max(0,Vector3.Dot(Vector3.back,view))*1000000);
                        if(!skyReferences.TryGetValue(key,out sg)){sg=EnvironmentObliqueReference(102.0/255,key/1000000.0);skyReferences[key]=sg;}
                        if(mixed){var a=ReferenceLocalLight(light,x,y,size,102f/255);var b=ReferenceLocalLight(blue,x,y,size,102f/255);dr=a.diffuse.r;db=b.diffuse.b;sr=a.specular.r;sb=b.specular.b;}
                    }
                    referencePoints.Add((x,y,dr,db,sr,sg,sb));
                }
                var images=new[]{rawD,filteredD,rawS,filteredS};
                for(int frame=0;frame<frames;frame++)
                {
                    RenderCamera();Require(c.nrd.LastDispatchCount>0&&c.environmentSampling,"Environment prototype bypassed RELAX/environment");ReadImage(c.diffuse.rt,rawD);ReadImage(c.specular.rt,rawS);ReadImage(c.filteredDiffuse.rt,filteredD);ReadImage(c.filteredSpecular.rt,filteredS);
                    foreach(var p in referencePoints)
                    {
                        references[0,0]+=p.dr;references[0,1]+=1;references[0,2]+=p.db;references[1,0]+=p.sr;references[1,1]+=p.sg;references[1,2]+=p.sb;
                        for(int signal=0;signal<4;signal++)
                        {
                            var value=images[signal].GetPixel(p.x,p.y);
                            for(int channel=0;channel<3;channel++){float v=value[channel];Require(float.IsFinite(v),"Environment non-finite signal");minSignal=Math.Min(minSignal,v);sums[signal,channel]+=v;}
                        }
                        samples++;
                    }
                }
                bool pass=minSignal>=0;var ratios=new double[4,3];for(int signal=0;signal<4;signal++)for(int channel=0;channel<3;channel++)
                {
                    double reference=references[signal/2,channel];ratios[signal,channel]=reference>0?sums[signal,channel]/reference:sums[signal,channel]/samples;
                    pass&=reference>0?Math.Abs(ratios[signal,channel]-1)<.05:Math.Abs(ratios[signal,channel])<.001;
                }
                cases.Add(new AuxiliaryEnvironmentCase{mixed=mixed,reuse=reuse,pass=pass,minSignal=minSignal,diffuseRed=ratios[0,0],diffuseGreen=ratios[0,1],diffuseBlue=ratios[0,2],filteredDiffuseRed=ratios[1,0],filteredDiffuseGreen=ratios[1,1],filteredDiffuseBlue=ratios[1,2],specularRed=ratios[2,0],specularGreen=ratios[2,1],specularBlue=ratios[2,2],filteredSpecularRed=ratios[3,0],filteredSpecularGreen=ratios[3,1],filteredSpecularBlue=ratios[3,2]});allPass&=pass;
                Debug.Log($"[HoRTDIValidation] AUXILIARY_ENV mixed={mixed} reuse={reuse} partition={auxiliaryEnvironmentPartition} rawD={ratios[0,0]:F6}/{ratios[0,1]:F6}/{ratios[0,2]:F6} filteredD={ratios[1,0]:F6}/{ratios[1,1]:F6}/{ratios[1,2]:F6} rawS={ratios[2,0]:F6}/{ratios[2,1]:F6}/{ratios[2,2]:F6} filteredS={ratios[3,0]:F6}/{ratios[3,1]:F6}/{ratios[3,2]:F6} pass={pass}");
                Debug.Log($"[HoRTDIValidation] AUXILIARY_ENV_MIN mixed={mixed} reuse={reuse} value={minSignal:E9}");
                if(lateSpecWeightAudit)WriteLateSpecWeights(lateSpecOverride,(mixed?"mixed":"sky")+(reuse?"-reuse":"-none"),size,frames);
                computeField.SetValue(feature,originalCompute);rayField.SetValue(feature,originalRay);c.nrd.ValidationTimeSeconds=null;
            }
            string mode=!cutoutPairedFinalSample?"baseline":auxiliaryEnvironmentPartition?"partition":"naive";
            if(lateSpecExchangeAudit)mode+=lateSpecWeightAudit?"-weights":lateSpecAdaptiveAudit?"-adaptive":allSpecOrthographicAudit?"-allortho":lateSpecOrthographicAudit?"-ortho":"-exchange";
            if(auxiliaryPerspectiveAudit)mode+="-perspective";
            if(specularRgbDistanceAudit)mode+="-rgbdistance";
            if(specularNormalBudgetAudit)mode+="-normalbudget";
            if(diffuseSegmentAudit)mode+="-segment83";
            if(auxiliarySpatialAudit){WriteAuxiliarySpatial(mode);return;}
            if(auxiliaryEnvironmentStages){WriteAuxiliaryEnvironmentStages(mode);return;}
            File.WriteAllText(Path.Combine(Application.dataPath,"../"+(runtimeMultiAudit?"rtdi-runtime78-env-":compactAuxiliaryAudit?"rtdi-compact74-env-":"rtdi-auxenv54-")+mode+"-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new AuxiliaryEnvironmentReport{qualityPass=allPass,path=compatibility?"compatibility":"RenderGraph",mode=mode,projection=auxiliaryPerspectiveAudit?"perspective":"orthographic",cases=cases.ToArray()},true));
            Require(allPass,"Environment partition energy gates failed");
        }
    }
}
