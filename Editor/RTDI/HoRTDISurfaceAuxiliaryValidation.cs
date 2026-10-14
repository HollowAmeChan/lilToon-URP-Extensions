using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    internal static class HoRTDISurfaceAuxiliaryValidation
    {
        internal static bool Enabled;
        internal static IDisposable Apply(HoRTDIRendererFeature feature)
        {
            if(Enabled)throw new InvalidOperationException("Nested surface auxiliary overrides are unsupported");
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Surface auxiliary candidate requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",aux="Runtime/RTDI/Shaders/HoRTDIAuxiliary.hlsl",compute="Runtime/RTDI/Shaders/HoRTDI.compute",ray="Runtime/RTDI/Shaders/HoRTDIFinalVisibility.raytrace",asset="Assets/HoRTDISurfaceAuxiliaryAudit";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");string root=package.resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,aux)).Replace("\r\n","\n");int start=source.IndexOf("RTXDI_DIReservoir HoDIFreshAuxiliary(",StringComparison.Ordinal),end=source.IndexOf("uint2 HoDIPackRuntimeAuxiliary",StringComparison.Ordinal);if(start<0||end<=start)throw new Exception("Fresh auxiliary source changed; review candidate");
            string body=@"RTXDI_DIReservoir HoDIFreshAuxiliary(inout RTXDI_RandomSamplerState rng,RAB_Surface surface)
{
    RTXDI_DIReservoir reservoir=RTXDI_EmptyDIReservoir();
    // Fresh RIS only: finalized weight is sum(target/pdf)/(4*selectedTarget).
    // No auxiliary temporal/spatial reuse or additional visibility candidates.
    for(uint candidate=0;candidate<4;candidate++)
    {
        uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;float u=RTXDI_GetNextRandom(rng);
        while(lo<hi){uint middle=(lo+hi)/2;if(_HoRTDIRuntimeAuxiliaryCDF[middle]<=u)lo=middle+1;else hi=middle;}
        uint index=min(lo,count-1);float pdf=_HoRTDIRuntimeAuxiliaryCDF[index]-(index>0?_HoRTDIRuntimeAuxiliaryCDF[index-1]:0);
        RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(index,false),surface,float2(.5,.5));
        float target=RAB_GetLightSampleTargetPdfForSurface(sample,surface);if(!isfinite(target))target=0;
        RTXDI_StreamSample(reservoir,index,float2(.5,.5),RTXDI_GetNextRandom(rng),target,1/pdf);
    }
    RTXDI_FinalizeResampling(reservoir,1,4);return reservoir;
}
";
            source=source.Substring(0,start)+body+source.Substring(end);string folder=Path.Combine(Application.dataPath,"HoRTDISurfaceAuxiliaryAudit");Directory.CreateDirectory(folder);WriteChanged(Path.Combine(folder,"HoRTDIAuxiliary.hlsl"),source);
            string cs=Rebase(File.ReadAllText(Path.Combine(root,compute)),compute).Replace(packagePath+"/"+aux,asset+"/HoRTDIAuxiliary.hlsl").Replace("HoDIFreshAuxiliary(rng)","HoDIFreshAuxiliary(rng,surface)");
            string rs=Rebase(File.ReadAllText(Path.Combine(root,ray)),ray).Replace(packagePath+"/"+aux,asset+"/HoRTDIAuxiliary.hlsl").Replace("HoDIFreshAuxiliary(rng)","HoDIFreshAuxiliary(rng,surface)");
            // Match the compute bridge target; this only changes the proposal.
            rs=rs.Replace("#define HO_RTDI_ENVIRONMENT_ENABLED 1","#define HO_RTDI_ENVIRONMENT_ENABLED 1\n#define HO_RTDI_SPECULAR_PROPOSAL_IMPORTANCE 4.0");
            const string unguarded="extra=HoDIFreshAuxiliary(rng,surface);RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(extra),false),surface,RTXDI_GetDIReservoirSampleUV(extra));visible=HoDITraceFinal(surface,sample);";
            if(!rs.Contains(unguarded))throw new Exception("Native auxiliary trace changed; review empty RIS handling");
            rs=rs.Replace(unguarded,"extra=HoDIFreshAuxiliary(rng,surface);if(RTXDI_IsValidDIReservoir(extra)){RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(extra),false),surface,RTXDI_GetDIReservoirSampleUV(extra));visible=HoDITraceFinal(surface,sample);}");
            WriteChanged(Path.Combine(folder,"HoRTDI.compute"),cs);WriteChanged(Path.Combine(folder,"HoRTDI.raytrace"),rs);
            AssetDatabase.ImportAsset(asset+"/HoRTDIAuxiliary.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(asset+"/HoRTDI.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(asset+"/HoRTDI.raytrace",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDI.compute");var rayShader=AssetDatabase.LoadAssetAtPath<RayTracingShader>(asset+"/HoRTDI.raytrace");if(shader==null||rayShader==null)throw new Exception("Surface RIS shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            return new Override(feature,shader,rayShader);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Rebase(string text,string relative)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>{string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');if(path.StartsWith("../",StringComparison.Ordinal))throw new Exception("RIS include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";});
        }
        private sealed class Override:IDisposable
        {
            private readonly HoRTDIRendererFeature feature;private readonly FieldInfo compute,ray;private readonly object oldCompute,oldRay;
            internal Override(HoRTDIRendererFeature feature,ComputeShader replacement,RayTracingShader rayReplacement){this.feature=feature;const BindingFlags inspect=BindingFlags.Instance|BindingFlags.NonPublic;compute=feature.GetType().GetField("shader",inspect);ray=feature.GetType().GetField("finalVisibilityShader",inspect);oldCompute=compute.GetValue(feature);oldRay=ray.GetValue(feature);compute.SetValue(feature,replacement);ray.SetValue(feature,rayReplacement);Enabled=true;}
            public void Dispose(){compute.SetValue(feature,oldCompute);ray.SetValue(feature,oldRay);Enabled=false;}
        }
    }
}
