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
    public static partial class HoRTDIValidation
    {
        private static bool stratifiedAuxiliaryAudit;
        public static void RunStratifiedAuxiliaryEnvironment(){stratifiedAuxiliaryAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunStratifiedAuxiliaryDomain(){stratifiedAuxiliaryAudit=true;RunRuntimeMultiDomain();}
        public static void RunStratifiedAuxiliaryDynamic(){stratifiedAuxiliaryAudit=true;RunRuntimeMultiDynamic();}
        public static void RunStratifiedAuxiliaryOpaqueDomain(){stratifiedAuxiliaryAudit=true;RunRuntimeMultiOpaqueDomain();}
        public static void RunStratifiedAuxiliaryEnvironmentCompatibility(){compatibility=true;RunStratifiedAuxiliaryEnvironment();}
        private static IDisposable ApplyStratifiedAuxiliary()
        {
            if(!stratifiedAuxiliaryAudit)return null;
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Stratified auxiliary requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",aux="Runtime/RTDI/Shaders/HoRTDIAuxiliary.hlsl",compute="Runtime/RTDI/Shaders/HoRTDI.compute",ray="Runtime/RTDI/Shaders/HoRTDIFinalVisibility.raytrace",asset="Assets/HoRTDIStratifiedAuxiliary";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,aux));
            if(source.Contains("HO_RTDI_STRATIFIED_AUXILIARY"))
            {
                Require(source.Contains("HoDIFreshAuxiliary(float u)")&&File.ReadAllText(Path.Combine(root,compute)).Contains("HoDIFreshAuxiliary(auxiliaryStrata[draw])")&&File.ReadAllText(Path.Combine(root,ray)).Contains("HoDIFreshAuxiliary(auxiliaryStrata[draw])"),"Production stratification marker has incomplete consumers");
                return null;
            }
            source=ReplaceOnce(source,"RTXDI_DIReservoir HoDIFreshAuxiliary(inout RTXDI_RandomSamplerState rng)",@"float2 HoDIAuxiliaryStrata(inout RTXDI_RandomSamplerState rng)
{
    float lower=.5*RTXDI_GetNextRandom(rng);
    float upper=.5+.5*RTXDI_GetNextRandom(rng);
    bool swap=RTXDI_GetNextRandom(rng)<.5;
    return swap?float2(upper,lower):float2(lower,upper);
}
RTXDI_DIReservoir HoDIFreshAuxiliary(float u)");
            source=ReplaceOnce(source,"uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;float u=RTXDI_GetNextRandom(rng);","uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;");
            string cs=Adapt(File.ReadAllText(Path.Combine(root,compute)),compute),rs=Adapt(File.ReadAllText(Path.Combine(root,ray)),ray);
            string folder=Path.Combine(Application.dataPath,"HoRTDIStratifiedAuxiliary");Directory.CreateDirectory(folder);
            WriteChanged(Path.Combine(folder,"HoRTDIAuxiliary.hlsl"),source);WriteChanged(Path.Combine(folder,"HoRTDI.compute"),cs);WriteChanged(Path.Combine(folder,"HoRTDI.raytrace"),rs);
            foreach(string file in new[]{"HoRTDIAuxiliary.hlsl","HoRTDI.compute","HoRTDI.raytrace"})AssetDatabase.ImportAsset(asset+"/"+file,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDI.compute");var rayShader=AssetDatabase.LoadAssetAtPath<RayTracingShader>(asset+"/HoRTDI.raytrace");Require(shader!=null&&rayShader!=null,"Stratified shaders missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            return new StratifiedOverride(feature,shader,rayShader);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Adapt(string text,string relative)
            {
                text=Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
                {
                    string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Stratified include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
                }).Replace(packagePath+"/"+aux,asset+"/HoRTDIAuxiliary.hlsl");
                string pair=relative==compute?"float2 auxiliaryStrata=0;\n        if(_HoRTDIFinalVisibilityEnabled==0)auxiliaryStrata=HoDIAuxiliaryStrata(rng);":"float2 auxiliaryStrata=HoDIAuxiliaryStrata(rng);";
                text=ReplaceOnce(text,"for(uint draw=0;draw<2;draw++)",pair+"\n        for(uint draw=0;draw<2;draw++)");
                return ReplaceOnce(text,"HoDIFreshAuxiliary(rng)","HoDIFreshAuxiliary(auxiliaryStrata[draw])");
            }
        }
        private sealed class StratifiedOverride:IDisposable
        {
            private readonly HoRTDIRendererFeature target;private readonly FieldInfo compute,ray;private readonly object originalCompute,originalRay;
            internal StratifiedOverride(HoRTDIRendererFeature target,ComputeShader shader,RayTracingShader rayShader){this.target=target;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;compute=target.GetType().GetField("shader",flags);ray=target.GetType().GetField("finalVisibilityShader",flags);originalCompute=compute.GetValue(target);originalRay=ray.GetValue(target);compute.SetValue(target,shader);ray.SetValue(target,rayShader);}
            public void Dispose(){compute.SetValue(target,originalCompute);ray.SetValue(target,originalRay);}
        }
    }
}
