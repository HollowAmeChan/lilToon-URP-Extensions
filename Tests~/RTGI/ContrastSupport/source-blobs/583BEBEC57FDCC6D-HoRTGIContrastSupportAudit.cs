using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGIContrastSupportAudit:IDisposable
    {
        internal const string Guard="                if (gStepSize > 4 && (sampleDiffuseLuminance < centerDiffuseLuminance * 0.5 || sampleDiffuseLuminance > centerDiffuseLuminance * 2.0)) wDiffuse = 0;";
        [Serializable]internal sealed class Report{public string originalPath,originalSha256,bodyPath,bodySha256,wrapperPath,wrapperSha256;public bool restoredOriginal;public int pipeline=10;}
        internal readonly Report report;
        readonly ComputeShader[] shaders;readonly int[] kernels;readonly ComputeShader original;readonly int originalKernel;bool disposed;
        internal HoRTGIContrastSupportAudit(HoRTDINrdContext core)
        {
            string workspace=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            if(!project.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated candidate project required");
            const string relative="Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl",directory="Assets/HoRTGIContrastSupport";
            byte[] raw=File.ReadAllBytes(Path.Combine(workspace,relative));string source=Encoding.UTF8.GetString(raw).TrimStart('\ufeff');
            const string anchor="                wDiffuse *= exp(-diffuseLuminanceW);";int at=source.IndexOf(anchor,StringComparison.Ordinal);
            if(at<0||source.IndexOf(anchor,at+anchor.Length,StringComparison.Ordinal)>=0)throw new Exception("Contrast guard anchor drift");
            string body=source.Replace(anchor,anchor+"\n"+Guard);body=Regex.Replace(body,"#include \"([^\"]+)\"",m=>"#include \"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/"+m.Groups[1].Value+"\"");
            Directory.CreateDirectory(Path.Combine(project,directory));string bodyPath=directory+"/ContrastSupport.hlsl",wrapperPath=directory+"/ContrastSupport.compute";
            string wrapper="#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n#define NRD_SIGNAL DIFF\n#define NRD_MODE RADIANCE\n#include \"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl\"\n#include \"ContrastSupport.hlsl\"\n";
            File.WriteAllText(Path.Combine(project,bodyPath),body,new UTF8Encoding(false));File.WriteAllText(Path.Combine(project,wrapperPath),wrapper,new UTF8Encoding(false));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(wrapperPath);if(shader==null)throw new Exception("Guard shader import failed");int kernel=shader.FindKernel("main");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(core);kernels=(int[])typeof(HoRTDINrdContext).GetField("kernels",flags).GetValue(core);
            original=shaders[10];originalKernel=kernels[10];shaders[10]=shader;kernels[10]=kernel;
            report=new Report{originalPath=relative,originalSha256=Hash(raw),bodyPath=bodyPath,bodySha256=Hash(File.ReadAllBytes(Path.Combine(project,bodyPath))),wrapperPath=wrapperPath,wrapperSha256=Hash(File.ReadAllBytes(Path.Combine(project,wrapperPath)))};
        }
        public void Dispose(){if(disposed)return;disposed=true;shaders[10]=original;kernels[10]=originalKernel;report.restoredOriginal=shaders[10]==original&&kernels[10]==originalKernel;}
        static string Hash(byte[] value){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(value)).Replace("-","");}
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunContrastSupportQualification()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,contrastSupport:true,seedWarmup:16,seedMeasured:16,validationTag:"contrast-support-qualification");
        public static void RunContrastSupport()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,contrastSupport:true,validationTag:"contrast-support");
    }
}
