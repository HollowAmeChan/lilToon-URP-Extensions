using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Exact stock math with an additional diagnostic UAV, private to one GI core.
    internal sealed class HoRTGIAtrousTrace:IDisposable
    {
        internal const int VectorsPerPoint=37;
        internal static readonly Vector2Int[] Pixels=HoRTGIDenseReferenceAudit.LocalPixels.Concat(new[]{new Vector2Int(141,157)}).Concat(
            Enumerable.Range(156,3).SelectMany(y=>Enumerable.Range(140,3).Select(x=>new Vector2Int(x,y))).Where(p=>p!=new Vector2Int(141,157))).ToArray();
        [Serializable]internal sealed class Report{public string originalSha256,bodySha256,wrapperSha256,bodyPath,wrapperPath;public bool restoredOriginal,replacementActive,noopClone;public int pointCount,vectorsPerPoint;}
        readonly HoRTDINrdContext core;readonly ComputeShader[] shaders;readonly int[] kernels;
        readonly ComputeShader original,trace;readonly int originalKernel,kernel;
        readonly GraphicsBuffer pixels,data;internal bool enabled;internal readonly bool replacementActive;internal readonly bool noopClone;internal readonly Report report;
        internal HoRTGIAtrousTrace(HoRTDINrdContext core,bool replacementActive=true,bool noopClone=false)
        {
            this.core=core;this.replacementActive=replacementActive;this.noopClone=noopClone;
            string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            if(!project.Replace('\\','/').EndsWith("/.codex-research/RTGIValidation"))throw new Exception("Dedicated trace project required");
            string directory=noopClone?"Assets/HoRTGIAtrousNoop":"Assets/HoRTGIAtrousTrace";Directory.CreateDirectory(Path.Combine(project,directory));
            string source=File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../..","Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl"))).Replace("\r\n","\n");
            string body=source;
            body=ReplaceOnce(body,"[numthreads(GROUP_X, GROUP_Y, 1)]",Declarations+"\n[numthreads(GROUP_X, GROUP_Y, 1)]");
            body=ReplaceOnce(body,"    [unroll]\n    for (int j = -1; j <= 1; j++)",Setup+"\n    [unroll]\n    for (int j = -1; j <= 1; j++)");
            body=ReplaceOnce(body,"            if (wDiffuse > 1e-4)",BeforeTap+"\n            if (wDiffuse > 1e-4)");
            body=ReplaceOnce(body,"                wDiffuse *= exp(-diffuseLuminanceW);","                wDiffuse *= exp(-diffuseLuminanceW);\n"+AfterTap);
            body=ReplaceOnce(body,"    gOut_Diff_Variance[pixelPos] = filteredDiffuseIlluminationAndVariance;","    gOut_Diff_Variance[pixelPos] = filteredDiffuseIlluminationAndVariance;\n"+Finish);
            if(noopClone)body=source;
            string package="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/";
            body=System.Text.RegularExpressions.Regex.Replace(body,"#include \"([^\"]+)\"",m=>"#include \""+package+m.Groups[1].Value+"\"");
            string bodyPath=directory+"/AtrousTrace.hlsl",wrapperPath=directory+"/AtrousTrace.compute";
            File.WriteAllText(Path.Combine(project,bodyPath),body,new System.Text.UTF8Encoding(false));
            string wrapper="#pragma kernel main\n#pragma use_dxc\n#pragma target 6.0\n#define NRD_INTERNAL 1\n#define NRD_COMPILER_DXC 1\n#define NRD_SIGNAL DIFF\n#define NRD_MODE RADIANCE\n#include \"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrdBindings.hlsl\"\n#include \"AtrousTrace.hlsl\"\n";
            File.WriteAllText(Path.Combine(project,wrapperPath),wrapper,new System.Text.UTF8Encoding(false));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            trace=AssetDatabase.LoadAssetAtPath<ComputeShader>(wrapperPath);if(trace==null)throw new Exception("Trace shader import failed");kernel=trace.FindKernel("main");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(trace))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            shaders=(ComputeShader[])typeof(HoRTDINrdContext).GetField("shaders",flags).GetValue(core);kernels=(int[])typeof(HoRTDINrdContext).GetField("kernels",flags).GetValue(core);
            original=shaders[10];originalKernel=kernels[10];
            pixels=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Pixels.Length,8);pixels.SetData(Pixels);
            data=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Pixels.Length*4*VectorsPerPoint,16);
            if(replacementActive){shaders[10]=trace;kernels[10]=kernel;}
            report=new Report{noopClone=noopClone,replacementActive=replacementActive,originalSha256=Hash(System.Text.Encoding.UTF8.GetBytes(source)),bodySha256=Hash(File.ReadAllBytes(Path.Combine(project,bodyPath))),wrapperSha256=Hash(File.ReadAllBytes(Path.Combine(project,wrapperPath))),bodyPath=bodyPath,wrapperPath=wrapperPath,pointCount=Pixels.Length,vectorsPerPoint=VectorsPerPoint};
        }
        internal void Bind(CommandBuffer cmd)
        {
            if(!replacementActive||noopClone)return;
            cmd.SetComputeIntParam(trace,"_HoGITraceEnabled",enabled?1:0);cmd.SetComputeIntParam(trace,"_HoGITracePointCount",Pixels.Length);
            cmd.SetComputeBufferParam(trace,kernel,"_HoGITracePixels",pixels);cmd.SetComputeBufferParam(trace,kernel,"_HoGITrace",data);
        }
        internal Vector4[] Read(){var values=new Vector4[data.count];data.GetData(values);return values;}
        public void Dispose(){shaders[10]=original;kernels[10]=originalKernel;report.restoredOriginal=shaders[10]==original&&kernels[10]==originalKernel;pixels.Dispose();data.Dispose();}
        static string Hash(byte[] value){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(value)).Replace("-","");}
        static string ReplaceOnce(string value,string token,string replacement){int at=value.IndexOf(token,StringComparison.Ordinal);if(at<0||value.IndexOf(token,at+token.Length,StringComparison.Ordinal)>=0)throw new Exception("Trace injection anchor drift: "+token);return value.Substring(0,at)+replacement+value.Substring(at+token.Length);}
        const string Declarations=@"// HO_TRACE_BEGIN declarations
StructuredBuffer<uint2> _HoGITracePixels;
RWStructuredBuffer<float4> _HoGITrace;
int _HoGITraceEnabled, _HoGITracePointCount;
// HO_TRACE_END declarations";
        const string Setup=@"    // HO_TRACE_BEGIN setup
    int hoPoint = -1;
    if (_HoGITraceEnabled != 0) for (int hoI = 0; hoI < _HoGITracePointCount; hoI++) if (all(pixelPos == _HoGITracePixels[hoI])) hoPoint = hoI;
    uint hoBase = 0;
    if (hoPoint >= 0) {
        hoBase = ((firstbithigh((uint)gStepSize) - 1) * _HoGITracePointCount + hoPoint) * 37;
        _HoGITrace[hoBase] = float4(pixelPos, gStepSize, gIsLastPass);
        _HoGITrace[hoBase + 1] = centerDiffuseIlluminationAndVariance;
        _HoGITrace[hoBase + 2] = float4(centerNormal, centerViewZ);
        for (uint hoK = 5; hoK < 37; hoK++) _HoGITrace[hoBase + hoK] = 0;
    }
    // HO_TRACE_END setup";
        const string BeforeTap=@"            // HO_TRACE_BEGIN beforetap
            int hoTap = (j + 1) * 3 + i + 1; if (hoTap > 4) hoTap--;
            if (hoPoint >= 0) {
                uint hoAt = hoBase + 5 + hoTap * 4;
                _HoGITrace[hoAt] = float4(p, 0, isInside ? 1 : 0);
                _HoGITrace[hoAt + 2] = float4(geometryW, normalWDiffuse, CompareMaterials(sampleMaterialID, centerMaterialID, gDiffMinMaterial), 0);
                _HoGITrace[hoAt + 3] = float4(sampleNormal, sampleViewZ);
            }
            // HO_TRACE_END beforetap";
        const string AfterTap=@"                // HO_TRACE_BEGIN aftertap
                if (hoPoint >= 0) {
                    uint hoAt = hoBase + 5 + hoTap * 4;
                    _HoGITrace[hoAt].z = wDiffuse;
                    _HoGITrace[hoAt + 1] = sampleDiffuseIlluminationAndVariance;
                    _HoGITrace[hoAt + 2].w = exp(-diffuseLuminanceW);
                }
                // HO_TRACE_END aftertap";
        const string Finish=@"    // HO_TRACE_BEGIN finish
    if (hoPoint >= 0) {
        _HoGITrace[hoBase + 3] = float4(offset, sumWDiffuse, historyLength);
        _HoGITrace[hoBase + 4] = filteredDiffuseIlluminationAndVariance;
    }
    // HO_TRACE_END finish";
    }
}
