using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool lateSpecExchangeAudit;
        public static void RunLateSpecularExchangeEnvironment(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironment();}
        public static void RunLateSpecularExchangeEnvironmentCompatibility(){compatibility=true;RunLateSpecularExchangeEnvironment();}
        public static void RunLateSpecularExchangeEnvironmentBaseline(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunLateSpecularExchangeEnvironmentStages(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironmentStages();}
        public static void RunLateSpecularExchangeMotion(){lateSpecExchangeAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunLateSpecularExchangeMotionCompatibility(){compatibility=true;RunLateSpecularExchangeMotion();}
        private sealed class LateSpecOverride:IDisposable
        {
            private readonly ComputeShader[] shaders;private readonly ComputeShader original;
            internal LateSpecOverride(ComputeShader[] shaders,ComputeShader replacement){this.shaders=shaders;original=shaders[10];shaders[10]=replacement;}
            public void Dispose(){shaders[10]=original;}
        }
        private static IDisposable ApplyLateSpecExchange(HoRTDIContext c)
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Late specular audit requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/Shaders/NRD/HoRTDINrdAtrous.hlsl",entry="Runtime/RTDI/Shaders/NRD/HoRTDINrd10.compute",asset="Assets/HoRTDILateSpecAudit";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");string root=package.resolvedPath;
            string body=File.ReadAllText(Path.Combine(root,relative)),compute=File.ReadAllText(Path.Combine(root,entry));
            body=ReplaceOnce(body,"    float4 filteredSpecularIlluminationAndVariance = float4(sumSpecularIlluminationAndVariance / float4(sumWSpecular.xxx, sumWSpecular * sumWSpecular));",@"
    // Same fixed-kernel exchange form already used by the diffuse path.
    // Rejected neighbor mass remains at the center; do not renormalize rows.
    const float specularCenterKernel = 0.44198 * 0.44198;
    float retainedSpecularWeight = saturate(1.0 - (sumWSpecular - specularCenterKernel));
    float4 specularNeighborSum = sumSpecularIlluminationAndVariance - centerSpecularIlluminationAndVariance * float4(specularCenterKernel.xxx, specularCenterKernel * specularCenterKernel);
    float4 filteredSpecularIlluminationAndVariance = specularNeighborSum + centerSpecularIlluminationAndVariance * float4(retainedSpecularWeight.xxx, retainedSpecularWeight * retainedSpecularWeight);");
            body=Rebase(body,relative);compute=Rebase(compute,entry).Replace(packagePath+"/"+relative,asset+"/HoRTDILateSpec.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDILateSpecAudit");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"HoRTDILateSpec.hlsl"),body);File.WriteAllText(Path.Combine(folder,"HoRTDILateSpec.compute"),compute);
            AssetDatabase.ImportAsset(asset+"/HoRTDILateSpec.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(asset+"/HoRTDILateSpec.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDILateSpec.compute");Require(shader!=null&&shader.FindKernel("main")==0,"Late specular shader/kernel mismatch");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            var shaders=(ComputeShader[])c.nrd.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.nrd);
            Debug.Log("[HoRTDIValidation] LATE_SPEC_EXCHANGE isolated NRD pipeline10 override; same settings/bindings");return new LateSpecOverride(shaders,shader);
            string Rebase(string text,string source)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string include=m.Groups[2].Value;if(include.StartsWith("Packages/",StringComparison.Ordinal))return m.Value;
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(source),include))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Late specular include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
    }
}
