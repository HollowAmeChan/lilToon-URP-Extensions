using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool cutoutSpecularRgbProposal;
        public static void RunCutoutResolutionSpecularRgbProposal(){cutoutSpecularRgbProposal=true;RunCutoutResolutionSpecular();}
        public static void RunCutoutResolutionSpecularRgbProposalCompatibility(){compatibility=true;RunCutoutResolutionSpecularRgbProposal();}
        public static void RunCutoutResolutionSpecularRgbProposalConfidence(){cutoutSpecularRgbProposal=true;RunCutoutResolutionSpecularConfidence();}
        private static void ApplyCutoutSpecularRgbProposal()
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Proposal experiment requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");Require(package!=null,"Proposal package source missing");
            string root=package.resolvedPath,shaderRelative="Runtime/RTDI/Shaders/HoRTDI.compute",bridgeRelative="Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl";
            string shader=File.ReadAllText(Path.Combine(root,shaderRelative)),bridge=File.ReadAllText(Path.Combine(root,bridgeRelative));
            const string original="return HO_RTDI_RGB_IMPORTANCE_ENABLED != 0 && all(material.specularF0 == 0);";
            Require(bridge.IndexOf(original,System.StringComparison.Ordinal)>=0&&bridge.IndexOf(original,System.StringComparison.Ordinal)==bridge.LastIndexOf(original,System.StringComparison.Ordinal),"Proposal source changed; re-review before generating experiment");
            bridge=bridge.Replace(original,"return HO_RTDI_RGB_IMPORTANCE_ENABLED != 0;");
            const string auditPath="Assets/HoRTDIProposalAudit",auditBridge=auditPath+"/HoRTDIApplicationBridge.hlsl",auditShader=auditPath+"/HoRTDISpecularRgb.compute";
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"HoRTDIProposalAudit"));
            File.WriteAllText(Path.Combine(Application.dataPath,"HoRTDIProposalAudit/HoRTDIApplicationBridge.hlsl"),Rebase(bridge,bridgeRelative));
            shader=Rebase(shader,shaderRelative).Replace(packagePath+"/Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl",auditBridge);
            File.WriteAllText(Path.Combine(Application.dataPath,"HoRTDIProposalAudit/HoRTDISpecularRgb.compute"),shader);
            AssetDatabase.ImportAsset(auditBridge,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(auditShader,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(auditShader);Require(compute!=null,"Proposal experiment shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(compute))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            typeof(lilToon.URP.Extensions.RTDI.HoRTDIRendererFeature).GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(feature,compute);
            Debug.Log("[HoRTDIValidation] PROPOSAL rgb-all; isolated compute override; physical BRDF/Shade and NRD unchanged");
            string Rebase(string source,string relative)
                =>Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
                {
                    string resolved=Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value));
                    string rebased=Path.GetRelativePath(root,resolved).Replace('\\','/');Require(!rebased.StartsWith("../",System.StringComparison.Ordinal),"Proposal include escaped package");
                    return m.Groups[1].Value+"\""+packagePath+"/"+rebased+"\"";
                });
        }
    }
}
