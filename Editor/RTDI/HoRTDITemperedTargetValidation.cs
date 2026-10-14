using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    internal static class HoRTDITemperedTargetValidation
    {
        internal static IDisposable Apply(HoRTDIRendererFeature feature)
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Tempered target requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",bridge="Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl",compute="Runtime/RTDI/Shaders/HoRTDI.compute",asset="Assets/HoRTDITemperedTargetAudit";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,bridge));
            const string original="float importance = HoDIUsesRgbImportance(surface.material) ? length(max(0, contribution)) : max(0, HoDILuminance(contribution));";
            const string tempered="float importance = HoDIUsesRgbImportance(surface.material) ? sqrt(length(max(0, contribution))) : max(0, HoDILuminance(contribution));";
            if(source.Split(new[]{original},StringSplitOptions.None).Length==2)source=source.Replace(original,tempered);
            else if(!source.Contains("HO_RTDI_TEMPERED_ZERO_F0_TARGET")||source.Split(new[]{tempered},StringSplitOptions.None).Length!=2)throw new Exception("RGB target changed; review candidate");
            // Nonzero support and the SDK normalization remain intact. The
            // square root tempers only the zero-F0 proposal, never shaded radiance.
            source=Rebase(source,bridge);
            string cs=Rebase(File.ReadAllText(Path.Combine(root,compute)),compute).Replace(packagePath+"/"+bridge,asset+"/HoRTDIApplicationBridge.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDITemperedTargetAudit");Directory.CreateDirectory(folder);
            WriteChanged(Path.Combine(folder,"HoRTDIApplicationBridge.hlsl"),source);WriteChanged(Path.Combine(folder,"HoRTDI.compute"),cs);
            AssetDatabase.ImportAsset(asset+"/HoRTDIApplicationBridge.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(asset+"/HoRTDI.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDI.compute");
            if(shader==null)throw new Exception("Tempered candidate shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            return new Override(feature,shader);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Rebase(string text,string relative)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');
                if(path.StartsWith("../",StringComparison.Ordinal))throw new Exception("Target include escaped package");
                return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        private sealed class Override:IDisposable
        {
            private readonly HoRTDIRendererFeature feature;private readonly FieldInfo field;private readonly object original;
            internal Override(HoRTDIRendererFeature feature,ComputeShader shader){this.feature=feature;field=feature.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);original=field.GetValue(feature);field.SetValue(feature,shader);}
            public void Dispose()=>field.SetValue(feature,original);
        }
    }
}
