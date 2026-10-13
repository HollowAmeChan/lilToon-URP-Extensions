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
        private static bool diffuseSegmentAudit;
        public static void RunDiffuseSegmentStages(){diffuseSegmentAudit=true;RunDiffuseColourStages();}
        public static void RunDiffuseSegmentStagesCompatibility(){compatibility=true;RunDiffuseSegmentStages();}
        public static void RunDiffuseSegmentDomain(){diffuseSegmentAudit=true;RunRuntimeMultiDomain();}
        public static void RunDiffuseSegmentEnvironment(){diffuseSegmentAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunDiffuseSegmentMotion(){diffuseSegmentAudit=true;RunCutoutResolutionMotion();}
        private sealed class DiffuseSegmentOverride:IDisposable
        {
            private readonly ComputeShader[] shaders;private readonly ComputeShader original;
            internal DiffuseSegmentOverride(ComputeShader[] shaders,ComputeShader replacement){this.shaders=shaders;original=shaders[6];shaders[6]=replacement;}
            public void Dispose()=>shaders[6]=original;
        }
        private static DiffuseSegmentOverride ApplyDiffuseSegment(HoRTDIContext c)
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Diffuse segment override requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/Shaders/NRD/HoRTDINrdHistoryClamping.hlsl",entry="Runtime/RTDI/Shaders/NRD/HoRTDINrd06.compute",asset="Assets/HoRTDIDiffuseSegmentAudit";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");string root=package.resolvedPath;
            string body=File.ReadAllText(Path.Combine(root,relative)).Replace("\r\n","\n");
            if(!body.Contains("HoRTDI diffuse convex colour clipping"))body=ReplaceOnce(body,"    if (gDiffMaxFastAccumulatedFrameNum < gDiffMaxAccumulatedFrameNum)\n        clampedDiffuseYCoCg = clamp(diffuseYCoCg, diffuseResponsiveColorMinYCoCg, diffuseResponsiveColorMaxYCoCg);",@"
    if (gDiffMaxFastAccumulatedFrameNum < gDiffMaxAccumulatedFrameNum)
    {
        // Intersect the fast-to-normal colour segment with the same box.
        // A scalar blend preserves RGB planes shared by both endpoint colours.
        float3 anchor = diffuseResponsiveCenterYCoCg.rgb;
        float3 delta = diffuseYCoCg - anchor;
        float amount = 1.0;
        [unroll] for (uint axis = 0; axis < 3; axis++)
        {
            if (delta[axis] > 0.0) amount = min(amount, (diffuseResponsiveColorMaxYCoCg[axis] - anchor[axis]) / delta[axis]);
            else if (delta[axis] < 0.0) amount = min(amount, (diffuseResponsiveColorMinYCoCg[axis] - anchor[axis]) / delta[axis]);
        }
        clampedDiffuseYCoCg = lerp(anchor, diffuseYCoCg, saturate(amount));
    }");
            string compute=Rebase(File.ReadAllText(Path.Combine(root,entry)),entry).Replace(packagePath+"/"+relative,asset+"/HoRTDIClamping.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDIDiffuseSegmentAudit");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"HoRTDIClamping.hlsl"),Rebase(body,relative));File.WriteAllText(Path.Combine(folder,"HoRTDIClamping.compute"),compute);
            AssetDatabase.ImportAsset(asset+"/HoRTDIClamping.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDIClamping.compute");Require(shader!=null&&shader.FindKernel("main")==0,"Diffuse segment shader missing or unexpected kernel index");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            var shaders=(ComputeShader[])c.nrd.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.nrd);return new DiffuseSegmentOverride(shaders,shader);
            string Rebase(string source,string file)=>Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>{string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(file),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Diffuse include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";});
        }
    }
}
