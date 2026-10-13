using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool cutoutPairedFinalSample;
        private static RayTracingShader pairedFinalShader;
        public static void RunCutoutResolutionPairedFinal(){cutoutPairedFinalSample=true;RunCutoutResolutionSpecular();}
        public static void RunCutoutResolutionPairedFinalCompatibility(){compatibility=true;RunCutoutResolutionPairedFinal();}
        public static void RunCutoutResolutionPairedFinalDiffuse(){cutoutPairedFinalSample=true;RunCutoutResolutionMotion();}
        public static void RunCutoutResolutionPairedFinalDiffuseCompatibility(){compatibility=true;RunCutoutResolutionPairedFinalDiffuse();}
        public static void RunCutoutPairedFinalMultiQuality(){cutoutPairedFinalSample=true;RunCutoutMultiQuality();}
        public static void RunCutoutPairedFinalMultiQualityCompatibility(){compatibility=true;RunCutoutPairedFinalMultiQuality();}
        private static void ApplyCutoutPairedFinalSample()
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Paired prototype requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",shaderRelative="Runtime/RTDI/Shaders/HoRTDI.compute",rayRelative="Runtime/RTDI/Shaders/HoRTDIFinalVisibility.raytrace",auditPath="Assets/HoRTDIPairedFinalAudit";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");Require(package!=null,"Paired package source missing");string root=package.resolvedPath;
            string compute=File.ReadAllText(Path.Combine(root,shaderRelative)).Replace("\r\n","\n"),ray=File.ReadAllText(Path.Combine(root,rayRelative)).Replace("\r\n","\n");
            compute=ReplaceOnce(compute,"_HoRTDIFinalVisibility.Load(int3(id.xy,0)):","((uint)round(_HoRTDIFinalVisibility.Load(int3(id.xy,0))*3)&1):");
            compute=ReplaceOnce(compute,"    float receiver=RAB_IsSurfaceValid(surface)?1:0;\n    #if !defined(HO_RTDI_NRD_ACTIVE)",@"
    if(_HoRTDIFinalVisibilityEnabled!=0&&RAB_IsSurfaceValid(surface))
    {
        RTXDI_DIReservoir extra=RTXDI_LoadDIReservoir(HoDILayout(),id.xy,2);
        float3 extraD=0,extraS=0,extraLighting=0;
        if(RTXDI_IsValidDIReservoir(extra))
        {
            RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(extra),false),surface,RTXDI_GetDIReservoirSampleUV(extra));
            uint bits=(uint)round(_HoRTDIFinalVisibility.Load(int3(id.xy,0))*3);
            float3 radiance=sample.radiance*((bits>>1)&1)*RTXDI_GetDIReservoirInvPdf(extra)/max(sample.solidAnglePdf,1e-12);
            float3 L;float distance;RAB_GetLightDirDistance(surface,sample,L,distance);
            if(dot(L,surface.geoNormal)>0)
            {
                extraLighting=Lambert(surface.normal,-L)*radiance;extraD=extraLighting*surface.material.diffuseAlbedo;
                if(surface.material.roughness>=.03)extraS=GGX_times_NdotL(surface.viewDir,L,surface.normal,max(.03,surface.material.roughness),surface.material.specularF0)*radiance;
            }
        }
        diffuse=.5*(diffuse+extraD);specular=.5*(specular+extraS);diffuseLighting=.5*(diffuseLighting+extraLighting);
    }
    float receiver=RAB_IsSurfaceValid(surface)?1:0;
    #if !defined(HO_RTDI_NRD_ACTIVE)");
            int begin=ray.IndexOf("[shader(\"raygeneration\")]\nvoid FinalVisibility()",StringComparison.Ordinal);
            if(begin<0)begin=ray.IndexOf("[shader(\"raygeneration\")]\r\nvoid FinalVisibility()",StringComparison.Ordinal);
            int end=ray.IndexOf("// Re-evaluate exactly",StringComparison.Ordinal);Require(begin>=0&&end>begin,"Paired visibility source changed");
            ray=ray.Substring(0,begin)+@"
int _HoRTDIExtraSampleFrame;
[shader(""raygeneration"")]
void FinalVisibility()
{
    uint2 pixel=DispatchRaysIndex().xy;
    RTXDI_ReservoirBufferParameters layout=(RTXDI_ReservoirBufferParameters)0;
    layout.reservoirBlockRowPitch=(uint)_HoRTDIReservoirLayout.x;layout.reservoirArrayPitch=(uint)_HoRTDIReservoirLayout.y;
    RTXDI_DIReservoir extra=RTXDI_EmptyDIReservoir();uint bits=0;
    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);
    if(RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)
    {
        RTXDI_DIReservoir primary=RTXDI_LoadDIReservoir(layout,pixel,1);
        if(RTXDI_IsValidDIReservoir(primary))
        {
            RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(primary),false),surface,RTXDI_GetDIReservoirSampleUV(primary));
            bits=HoDITraceFinal(surface,sample)?1:0;
        }
        RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(pixel,(uint)_HoRTDIExtraSampleFrame,0x6f527bbdu);
        uint count=(uint)_HoRTDIFrame.z,index=min((uint)(RTXDI_GetNextRandom(rng)*count),count-1);
        RTXDI_StreamSample(extra,index,float2(.5,.5),0,1,count);RTXDI_FinalizeResampling(extra,1,1);
        RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(index,false),surface,float2(.5,.5));
        if(HoDITraceFinal(surface,sample))bits|=2;
    }
    // Slot 2 is no longer read after the spatial dispatch has completed.
    RTXDI_StoreDIReservoir(extra,layout,pixel,2);
    _HoRTDIFinalVisibility[pixel]=bits/3.0;
}
float2 HoDICachedPrimaryLuminance(RTXDI_DIReservoir reservoir,RAB_Surface surface)
{
    if(!RTXDI_IsValidDIReservoir(reservoir))return 0;
    RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(reservoir),true),surface,RTXDI_GetDIReservoirSampleUV(reservoir));
    float visibility=float(reservoir.packedVisibility&RTXDI_PackedDIReservoir_VisibilityChannelMax)/RTXDI_PackedDIReservoir_VisibilityChannelMax;
    float3 L;float distance;RAB_GetLightDirDistance(surface,sample,L,distance);
    if(dot(L,surface.geoNormal)<=0)return 0;
    float3 radiance=sample.radiance*visibility*RTXDI_GetDIReservoirInvPdf(reservoir)/max(sample.solidAnglePdf,1e-12);
    float3 d=Lambert(surface.normal,-L)*surface.material.diffuseAlbedo*radiance,p=0;
    if(surface.material.roughness>=.03)p=GGX_times_NdotL(surface.viewDir,L,surface.normal,max(.03,surface.material.roughness),surface.material.specularF0)*radiance;
    d=f16tof32(f32tof16(d));p=f16tof32(f32tof16(p));return f16tof32(f32tof16(float2(HoDILuminance(d),HoDILuminance(p))));
}
"+ray.Substring(end);
            ray=ReplaceOnce(ray,"        surface.worldPos=current.worldPos;","        RAB_Surface oldSampleSurface=surface;surface.worldPos=current.worldPos;");
            ray=ReplaceOnce(ray,"float2 previous=_HoRTDIPreviousLuminance.Load(int3(old,0)),traced=0;","float2 previous=HoDICachedPrimaryLuminance(reservoir,oldSampleSurface),traced=0;");
            ray=ray.Replace("\r\n","\n");
            if(pairedAuxiliaryPowerCDF)
            {
                ray=ReplaceOnce(ray,"int _HoRTDIExtraSampleFrame;","StructuredBuffer<float> _HoRTDIAuxiliaryCDF;\nint _HoRTDIExtraSampleFrame;");
                ray=ReplaceOnce(ray,"uint count=(uint)_HoRTDIFrame.z,index=min((uint)(RTXDI_GetNextRandom(rng)*count),count-1);\n        RTXDI_StreamSample(extra,index,float2(.5,.5),0,1,count);",@"uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;float u=RTXDI_GetNextRandom(rng);
        while(lo<hi){uint middle=(lo+hi)/2;if(_HoRTDIAuxiliaryCDF[middle]<=u)lo=middle+1;else hi=middle;}
        uint index=min(lo,count-1);float pdf=_HoRTDIAuxiliaryCDF[index]-(index>0?_HoRTDIAuxiliaryCDF[index-1]:0);
        RTXDI_StreamSample(extra,index,float2(.5,.5),0,1,1/pdf);");
            }
            string folder=Path.Combine(Application.dataPath,"HoRTDIPairedFinalAudit");Directory.CreateDirectory(folder);
            if(pairedSecondAuxiliary)(compute,ray)=AddSecondAuxiliary(compute,ray);
            if(pairedOpaqueAudit)compute=AddOpaqueAuxiliary(compute);
            if(auxiliaryEnvironmentPartition)(compute,ray)=PartitionAuxiliaryEnvironment(compute,ray);
            if(compactAuxiliaryAudit)(compute,ray)=CompactSecondAuxiliary(compute,ray);
            File.WriteAllText(Path.Combine(folder,"HoRTDIPaired.compute"),Rebase(compute,shaderRelative));File.WriteAllText(Path.Combine(folder,"HoRTDIPaired.raytrace"),Rebase(ray,rayRelative));
            AssetDatabase.ImportAsset(auditPath+"/HoRTDIPaired.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(auditPath+"/HoRTDIPaired.raytrace",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(auditPath+"/HoRTDIPaired.compute");pairedFinalShader=AssetDatabase.LoadAssetAtPath<RayTracingShader>(auditPath+"/HoRTDIPaired.raytrace");Require(shader!=null&&pairedFinalShader!=null,"Paired shader import failed");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            var type=typeof(lilToon.URP.Extensions.RTDI.HoRTDIRendererFeature);var flags=BindingFlags.Instance|BindingFlags.NonPublic;type.GetField("shader",flags).SetValue(feature,shader);type.GetField("finalVisibilityShader",flags).SetValue(feature,pairedFinalShader);
            Debug.Log("[HoRTDIValidation] PAIRED_FINAL prototype: production reservoir + independent uniform analytic sample; two native alpha rays; no additional buffers");
            string Rebase(string source,string relative)=>Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Paired include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        private static string ReplaceOnce(string source,string oldText,string newText)
        {int index=source.IndexOf(oldText,StringComparison.Ordinal);Require(index>=0&&index==source.LastIndexOf(oldText,StringComparison.Ordinal),"Paired source changed; re-review: "+oldText);return source.Replace(oldText,newText);}
    }
}
