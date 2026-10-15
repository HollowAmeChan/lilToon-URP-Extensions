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
        private static bool vndfProposalAudit;
        public static void RunVndfEnvironment(){vndfProposalAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunVndfEnvironmentCompatibility(){compatibility=true;RunVndfEnvironment();}
        public static void RunVndfPerspectiveEnvironment(){auxiliaryPerspectiveAudit=true;RunVndfEnvironment();}
        public static void RunVndfDirections(){environmentDirectionAudit=true;RunVndfEnvironment();}
        private static IDisposable ApplyVndfProposal()
        {
            if(!vndfProposalAudit)return null;
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"VNDF proposal requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",bridge="Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl",compute="Runtime/RTDI/Shaders/HoRTDI.compute",asset="Assets/HoRTDIVndfProposal";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,bridge));
            source=ReplaceOnce(source,"return diffuse?(specular?.5:1):0;","return diffuse?(specular?0:1):0; // Isolated VNDF candidate; sampler and PDF share this probability.");
            source=Rebase(source,bridge);
            string cs=Rebase(File.ReadAllText(Path.Combine(root,compute)),compute).Replace(packagePath+"/"+bridge,asset+"/HoRTDIApplicationBridge.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDIVndfProposal");Directory.CreateDirectory(folder);
            WriteChanged(Path.Combine(folder,"HoRTDIApplicationBridge.hlsl"),source);WriteChanged(Path.Combine(folder,"HoRTDI.compute"),cs);
            foreach(string file in new[]{"HoRTDIApplicationBridge.hlsl","HoRTDI.compute"})AssetDatabase.ImportAsset(asset+"/"+file,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDI.compute");Require(shader!=null,"VNDF candidate compute missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            return new VndfOverride(feature,shader);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Rebase(string text,string relative)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"VNDF include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        private sealed class VndfOverride:IDisposable
        {
            private readonly HoRTDIRendererFeature target;private readonly FieldInfo field;private readonly object original;
            internal VndfOverride(HoRTDIRendererFeature target,ComputeShader shader){this.target=target;field=target.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);original=field.GetValue(target);field.SetValue(target,shader);}
            public void Dispose()=>field.SetValue(target,original);
        }
    }
}
