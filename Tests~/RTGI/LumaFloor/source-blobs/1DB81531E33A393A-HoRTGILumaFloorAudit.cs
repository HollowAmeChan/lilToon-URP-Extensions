using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.RTGI.NRD;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // One private candidate; original shaders/vendor assets are never edited.
    internal sealed class HoRTGILumaFloorAudit:IDisposable
    {
        [Serializable]internal sealed class Source{public int pipeline;public string originalPath,originalSha256,bodyPath,bodySha256,wrapperPath,wrapperSha256;}
        [Serializable]internal sealed class Report{public float signalScale,floor;public bool restoredOriginal;public Source[] sources;}
        internal readonly Report report;
        readonly ComputeShader[] shaders;readonly int[] kernels;readonly ComputeShader[] original=new ComputeShader[2];readonly int[] oldKernels=new int[2];bool disposed;
        internal HoRTGILumaFloorAudit(HoRTGINrdContext context)
        {
            if(context.AppliedSignalScale!=64)throw new Exception("Candidate requires pinned scale64");
            string workspace=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            if(!project.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated candidate project required");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(context.core);kernels=(int[])typeof(HoRTDINrdContext).GetField("kernels",flags).GetValue(context.core);
            report=new Report{signalScale=64,floor=1e-4f/64,sources=new Source[2]};
            const string directory="Assets/HoRTGILumaFloor";Directory.CreateDirectory(Path.Combine(project,directory));
            var replacements=new ComputeShader[2];var replacementKernels=new int[2];
            for(int i=0;i<2;i++)
            {
                int pipeline=9+i;string file=i==0?"RELAX_AtrousSmem.cs.hlsl":"RELAX_Atrous.cs.hlsl";
                string relative="Runtime/RTDI/ThirdParty/NRD/"+file;byte[] raw=File.ReadAllBytes(Path.Combine(workspace,relative));string source=Encoding.UTF8.GetString(raw).TrimStart('\ufeff');
                const string before="diffusePhiLIlluminationInv = 1.0 / max(1.0e-4,";const string after="diffusePhiLIlluminationInv = 1.0 / max((1.0e-4 / 64.0),";
                int at=source.IndexOf(before,StringComparison.Ordinal);if(at<0||source.IndexOf(before,at+before.Length,StringComparison.Ordinal)>=0)throw new Exception("Diffuse luma floor anchor drift");
                string body=source.Replace(before,after);body=Regex.Replace(body,"#include \"([^\"]+)\"",m=>"#include \"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/"+m.Groups[1].Value+"\"");
                string bodyPath=directory+"/Floor"+pipeline+".hlsl",wrapperPath=directory+"/Floor"+pipeline+".compute";
                string wrapper="#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n#define NRD_SIGNAL DIFF\n#define NRD_MODE RADIANCE\n#include \"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl\"\n#include \"Floor"+pipeline+".hlsl\"\n";
                File.WriteAllText(Path.Combine(project,bodyPath),body,new UTF8Encoding(false));File.WriteAllText(Path.Combine(project,wrapperPath),wrapper,new UTF8Encoding(false));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(wrapperPath);if(shader==null)throw new Exception("Candidate shader import failed");replacementKernels[i]=shader.FindKernel("main");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
                replacements[i]=shader;report.sources[i]=new Source{pipeline=pipeline,originalPath=relative,originalSha256=Hash(raw),bodyPath=bodyPath,bodySha256=Hash(File.ReadAllBytes(Path.Combine(project,bodyPath))),wrapperPath=wrapperPath,wrapperSha256=Hash(File.ReadAllBytes(Path.Combine(project,wrapperPath)))};
            }
            for(int i=0;i<2;i++){original[i]=shaders[9+i];oldKernels[i]=kernels[9+i];shaders[9+i]=replacements[i];kernels[9+i]=replacementKernels[i];}
        }
        public void Dispose(){if(disposed)return;disposed=true;for(int i=0;i<2;i++){shaders[9+i]=original[i];kernels[9+i]=oldKernels[i];}report.restoredOriginal=shaders[9]==original[0]&&shaders[10]==original[1]&&kernels[9]==oldKernels[0]&&kernels[10]==oldKernels[1];}
        static string Hash(byte[] value){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(value)).Replace("-","");}
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunLumaFloorQualification()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,lumaFloor:true,seedWarmup:16,seedMeasured:16,validationTag:"luma-floor-qualification");
        public static void RunLumaFloor()=>Run(false,256,denseScheduled:true,localStages:true,propagation:true,traceControl:true,lumaFloor:true,validationTag:"luma-floor");
        public static void RunLumaFloorSeedQualification()=>Run(false,256,denseScheduled:true,seedAudit:true,seedProduction:true,lumaFloor:true,seedCount:2,seedWarmup:16,seedMeasured:16,validationTag:"luma-floor-seed-qualification");
        public static void RunLumaFloorSeeds()=>Run(false,256,denseScheduled:true,seedAudit:true,seedProduction:true,lumaFloor:true,validationTag:"luma-floor-seeds");
    }
}
