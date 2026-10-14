using UnityEngine;
using lilToon.URP.Extensions.RTDI;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool runtimeMultiAudit;
        private static string dynamicReuseMode;
        private static bool temperedTargetAudit,dynamicUniformSource;
        public static void RunRuntimeTemperedTarget(){temperedTargetAudit=true;RunRuntimeDynamicReuseBoth();}
        public static void RunRuntimeTemperedUniformTarget(){dynamicUniformSource=true;RunRuntimeTemperedTarget();}
        public static void RunRuntimeUniformTargetBaseline(){dynamicUniformSource=true;RunRuntimeDynamicReuseBoth();}
        public static void RunRuntimeTemperedTargetCompatibility(){compatibility=true;RunRuntimeTemperedTarget();}
        public static void RunTemperedF0BoundaryLarge(){temperedTargetAudit=true;RunF0BoundaryLarge();}
        public static void RunTemperedDiffuseMotion(){temperedTargetAudit=true;RunCutoutResolutionMotion();}
        public static void RunTemperedEnvironment(){temperedTargetAudit=true;RunEnvironment();}
        private static int presampleTilePixels;
        public static void RunRuntimePresampleTile4(){presampleTilePixels=4;RunRuntimeDynamicReuseBoth();}
        public static void RunRuntimePresampleTile1(){presampleTilePixels=1;RunRuntimeDynamicReuseBoth();}
        public static void RunRuntimePresampleTile4Domain(){presampleTilePixels=4;RunRuntimeMultiDomain();}
        public static void RunRuntimePresampleTile4Compatibility(){compatibility=true;RunRuntimePresampleTile4();}
        public static void RunRuntimeDynamicReuseBoth(){dynamicReuseMode="both";RunRuntimeMultiDynamic();}
        public static void RunRuntimeDynamicReuseNone(){dynamicReuseMode="none";RunRuntimeMultiDynamic();}
        public static void RunRuntimeDynamicReuseTemporal(){dynamicReuseMode="temporal";RunRuntimeMultiDynamic();}
        public static void RunRuntimeDynamicReuseSpatial(){dynamicReuseMode="spatial";RunRuntimeMultiDynamic();}
        public static void RunRuntimeDynamicReuseBothCompatibility(){compatibility=true;RunRuntimeDynamicReuseBoth();}
        private static bool surfaceAuxiliaryAudit;
        public static void RunSurfaceAuxiliaryDomain(){surfaceAuxiliaryAudit=true;RunRuntimeMultiDomain();}
        public static void RunSurfaceAuxiliaryDomainCompatibility(){compatibility=true;RunSurfaceAuxiliaryDomain();}
        public static void RunSurfaceAuxiliaryEnvironment(){surfaceAuxiliaryAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunSurfaceAuxiliaryDynamic(){surfaceAuxiliaryAudit=true;RunRuntimeMultiDynamic();}
        private static bool runtimeMultiStateAudit;
        public static void RunRuntimeMultiDomain(){runtimeMultiAudit=compactAuxiliaryAudit=true;RunPairedLightDomainBaseline();}
        public static void RunRuntimeMultiDomainCompatibility(){compatibility=true;RunRuntimeMultiDomain();}
        public static void RunRuntimeMultiOpaqueDomain(){pairedOpaqueAudit=true;RunRuntimeMultiDomain();}
        public static void RunRuntimeMultiOpaqueDomainCompatibility(){compatibility=true;RunRuntimeMultiOpaqueDomain();}
        public static void RunRuntimeMultiEnvironment(){runtimeMultiAudit=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunRuntimeMultiEnvironmentCompatibility(){compatibility=true;RunRuntimeMultiEnvironment();}
        public static void RunRuntimeMultiDynamic(){runtimeMultiAudit=compactAuxiliaryAudit=true;dynamicAuxiliaryCDFAudit=true;RunCutoutResolutionSpecular();}
        public static void RunRuntimeMultiDynamicCompatibility(){compatibility=true;RunRuntimeMultiDynamic();}
        public static void RunRuntimeMultiDynamicLong(){dynamicAuxiliaryLongAudit=true;RunRuntimeMultiDynamic();}
        public static void RunRuntimeMultiDynamicLongCompatibility(){compatibility=true;RunRuntimeMultiDynamicLong();}
        public static void RunRuntimeMultiOpaqueDynamic(){pairedOpaqueAudit=true;RunRuntimeMultiDynamic();}
        public static void RunRuntimeMultiCamera(){runtimeMultiAudit=true;RunMultiCameraDIMean();}
        public static void RunRuntimeMultiCameraCompatibility(){compatibility=true;RunRuntimeMultiCamera();}
        public static void RunRuntimeMultiState(){runtimeMultiAudit=runtimeMultiStateAudit=true;RunCutoutResolutionSpecular();}
        public static void RunRuntimeMultiStateCompatibility(){compatibility=true;RunRuntimeMultiState();}
        private static void ValidateRuntimeMultiState(Light blue)
        {
            feature.Settings.useEnvironment=false;surfaceMaterial.SetFloat("_Reflectance",0);AlphaFill(alphaTexture,1);var renderer=blocker.GetComponent<Renderer>();
            blocker.transform.position=Vector3.right*4;int nativeFrames=0,opaqueFrames=0,singleFrames=0,maxRetired=0;
            var weights=new HoRTDIRendererFeature.AnalyticLight[3];weights[0].radianceType=new Vector4(float.MaxValue,0,0,0);weights[1].radianceType=new Vector4(1e-20f,0,0,0);weights[2].radianceType=Vector4.zero;
            var fallback=HoRTDIAuxiliaryProposal.Build(weights,3);Require(fallback[0]>0&&fallback[1]>fallback[0]&&fallback[2]==1,"Extreme power CDF lost source support");
            // No GPU readback in this loop: resource retirement must use the
            // submitted fence, not the synchronous validation helper.
            for(int i=0;i<72;i++)
            {
                bool native=i%6<3,multi=i%4>=2;renderer.sharedMaterial=native?cutoutMaterial:surfaceMaterial;
                feature.Settings.finalSampling=multi?HoRTDIFinalSampling.TripleAnalytic:HoRTDIFinalSampling.Single;
                if(i%12==0){target.Release();target.width=i%24==0?64:80;target.height=64;target.Create();camera.aspect=target.width/64f;}
                RenderCamera();var c=feature.LastContext;Require((c.finalVisibility!=null)==native&&c.nrd.LastDispatchCount>0,"Runtime mode switch visibility or NRD missing");
                if(multi){Require(c.auxiliaryCDF!=null&&c.auxiliaryCDF.stride==4,"Runtime multi CDF missing");Require(native?c.auxiliarySecondary!=null&&c.auxiliarySecondary.count==c.width*c.height&&c.auxiliarySecondary.stride==8:c.auxiliarySecondary==null,"Runtime secondary allocation differs from actual geometry mode");if(native)nativeFrames++;else opaqueFrames++;}
                else{Require(c.auxiliaryCDF==null&&c.auxiliarySecondary==null,"Single sample retained active auxiliary allocations");singleFrames++;}
                maxRetired=Mathf.Max(maxRetired,c.RetiredAuxiliaryCount);
            }
            Read(feature.LastContext.diffuse.rt);feature.Settings.finalSampling=HoRTDIFinalSampling.Single;RenderCamera();
            Require(feature.LastContext.auxiliaryCDF==null&&feature.LastContext.auxiliarySecondary==null&&feature.LastContext.RetiredAuxiliaryCount==0,"Completed auxiliary fences did not reclaim retired buffers");
            feature.Settings.finalSampling=HoRTDIFinalSampling.TripleAnalytic;blue.enabled=false;RenderCamera();Require(feature.LastContext.auxiliaryCDF==null&&feature.LastContext.auxiliarySecondary==null,"Deterministic single light allocated auxiliary resources");
            light.enabled=false;RenderCamera();Require(feature.LastContext.auxiliaryCDF==null&&feature.LastContext.auxiliarySecondary==null,"Zero lights allocated auxiliary resources");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath,"../rtdi-runtime78-state-"+(compatibility?"compat":"rg")+".json"),$"{{\"pass\":true,\"frames\":72,\"singleFrames\":{singleFrames},\"nativeMultiFrames\":{nativeFrames},\"opaqueMultiFrames\":{opaqueFrames},\"maxRetired\":{maxRetired},\"extremeCdfSupport\":true}}");
            Debug.Log($"[HoRTDIValidation] RUNTIME_MULTI_STATE pass=true single={singleFrames} native={nativeFrames} opaque={opaqueFrames} retiredPeak={maxRetired}; no intermediate readback");
        }
        private static float[] ReadRuntimeAuxiliaryCDF(HoRTDIContext c)
        {
            Require(c.auxiliaryCDF!=null,"Runtime multi-sample CDF resource missing");var values=new float[c.previousLightKeys.Length];c.auxiliaryCDF.GetData(values,0,0,values.Length);return values;
        }
    }
}
