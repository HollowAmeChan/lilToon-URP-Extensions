using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Same scoped shader-array override used by DI's step 83 candidate, but
    // generated from the original GI vendor body, not DI's other policies.
    internal sealed class HoRTGIClampingCandidate:IDisposable
    {
        [Serializable]internal sealed class Metadata
        {
            public string policy="Fast-to-slow convex segment in unchanged YCoCg box",sourceSha256,diReferenceSha256,bodyPath,bodySha256,wrapperPath,wrapperSha256;
            public int replacementCount;public bool restoredOriginal;
        }
        internal readonly Metadata metadata=new();
        readonly ComputeShader[] shaders;readonly ComputeShader original;bool disposed;
        internal HoRTGIClampingCandidate(HoRTGINrdContext context)
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("GI candidate requires dedicated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryClamping.cs.hlsl",entry="Runtime/RTGI/Shaders/NRD/HoRTGINrd06.compute",asset="Assets/HoRTGIClampingCandidate";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=Path.Combine(root,relative),body=File.ReadAllText(source).Replace("\r\n","\n");metadata.sourceSha256=Hash(source);
            metadata.diReferenceSha256=Hash(Path.Combine(root,"Runtime/RTDI/Shaders/NRD/HoRTDINrdHistoryClamping.hlsl"));
            const string old="    if (gDiffMaxFastAccumulatedFrameNum < gDiffMaxAccumulatedFrameNum)\n        clampedDiffuseYCoCg = clamp(diffuseYCoCg, diffuseResponsiveColorMinYCoCg, diffuseResponsiveColorMaxYCoCg);";
            const string replacement=@"    if (gDiffMaxFastAccumulatedFrameNum < gDiffMaxAccumulatedFrameNum)
    {
        // DI's established scalar clip: preserve endpoint RGB planes.
        float3 anchor = diffuseResponsiveCenterYCoCg.rgb;
        float3 delta = diffuseYCoCg - anchor;
        float amount = 1.0;
        [unroll] for (uint axis = 0; axis < 3; axis++)
        {
            if (delta[axis] > 0.0) amount = min(amount, (diffuseResponsiveColorMaxYCoCg[axis] - anchor[axis]) / delta[axis]);
            else if (delta[axis] < 0.0) amount = min(amount, (diffuseResponsiveColorMinYCoCg[axis] - anchor[axis]) / delta[axis]);
        }
        clampedDiffuseYCoCg = lerp(anchor, diffuseYCoCg, saturate(amount));
    }";
            if(body.IndexOf(old,StringComparison.Ordinal)<0||body.IndexOf(old,StringComparison.Ordinal)!=body.LastIndexOf(old,StringComparison.Ordinal))throw new Exception("Original GI clamp anchor drifted");
            body=body.Replace(old,replacement.Replace("\r\n","\n"));metadata.replacementCount=1;
            string compute=Rebase(File.ReadAllText(Path.Combine(root,entry)).Replace("\r\n","\n"),entry).Replace(packagePath+"/"+relative,asset+"/HoRTGIClamping.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTGIClampingCandidate");Directory.CreateDirectory(folder);metadata.bodyPath=Path.Combine(folder,"HoRTGIClamping.hlsl");metadata.wrapperPath=Path.Combine(folder,"HoRTGIClamping.compute");
            File.WriteAllText(metadata.bodyPath,Rebase(body,relative));File.WriteAllText(metadata.wrapperPath,compute);metadata.bodySha256=Hash(metadata.bodyPath);metadata.wrapperSha256=Hash(metadata.wrapperPath);
            AssetDatabase.ImportAsset(asset+"/HoRTGIClamping.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTGIClamping.compute");if(shader==null||shader.FindKernel("main")!=0)throw new Exception("Candidate main kernel mismatch");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            shaders=(ComputeShader[])context.core.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.core);if(shaders.Length!=15||shaders[6].FindKernel("main")!=0)throw new Exception("Original GI library mismatch");
            original=shaders[6];shaders[6]=shader;context.historyValid=false;
            string Rebase(string value,string file)=>Regex.Replace(value,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",match=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(file),match.Groups[2].Value))).Replace('\\','/');
                if(path.StartsWith("../",StringComparison.Ordinal))throw new Exception("Candidate include escaped package");return match.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        public void Dispose(){if(disposed)return;shaders[6]=original;metadata.restoredOriginal=ReferenceEquals(shaders[6],original);disposed=true;}
        static string Hash(string path){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    }
}
