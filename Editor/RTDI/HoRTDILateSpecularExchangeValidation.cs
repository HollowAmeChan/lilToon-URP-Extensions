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
        private static bool lateSpecWeightAudit;
        private static bool lateSpecAdaptiveAudit;
        private static bool lateSpecOrthographicAudit;
        private static bool allSpecOrthographicAudit;
        private static bool specularRgbDistanceAudit;
        public static void RunLateSpecularExchangeEnvironment(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironment();}
        public static void RunLateSpecularExchangeEnvironmentCompatibility(){compatibility=true;RunLateSpecularExchangeEnvironment();}
        public static void RunLateSpecularExchangeEnvironmentBaseline(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunLateSpecularExchangeEnvironmentStages(){lateSpecExchangeAudit=true;RunAuxiliaryEnvironmentStages();}
        public static void RunLateSpecularExchangeMotion(){lateSpecExchangeAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunLateSpecularExchangeMotionCompatibility(){compatibility=true;RunLateSpecularExchangeMotion();}
        public static void RunLateSpecularWeightEnvironment(){lateSpecWeightAudit=lateSpecExchangeAudit=true;RunAuxiliaryEnvironmentBaseline();}
        public static void RunLateSpecularWeightMotion(){lateSpecWeightAudit=lateSpecExchangeAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunLateSpecularWeightMotionCompatibility(){compatibility=true;RunLateSpecularWeightMotion();}
        public static void RunAdaptiveSpecularEnvironment(){lateSpecAdaptiveAudit=true;RunLateSpecularExchangeEnvironment();}
        public static void RunAdaptiveSpecularEnvironmentCompatibility(){compatibility=true;RunAdaptiveSpecularEnvironment();}
        public static void RunAdaptiveSpecularMotion(){lateSpecAdaptiveAudit=true;RunLateSpecularExchangeMotion();}
        public static void RunAdaptiveSpecularMotionCompatibility(){compatibility=true;RunAdaptiveSpecularMotion();}
        public static void RunOrthographicSpecularEnvironment(){lateSpecOrthographicAudit=lateSpecExchangeAudit=true;RunAuxiliaryEnvironment();}
        public static void RunOrthographicSpecularEnvironmentCompatibility(){compatibility=true;RunOrthographicSpecularEnvironment();}
        public static void RunOrthographicSpecularMotion(){lateSpecOrthographicAudit=lateSpecExchangeAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunOrthographicSpecularMotionCompatibility(){compatibility=true;RunOrthographicSpecularMotion();}
        public static void RunAllOrthographicSpecularEnvironment(){allSpecOrthographicAudit=true;RunOrthographicSpecularEnvironment();}
        public static void RunAllOrthographicSpecularMotion(){allSpecOrthographicAudit=true;RunOrthographicSpecularMotion();}
        public static void RunAllOrthographicSpecularEnvironmentCompatibility(){compatibility=true;RunAllOrthographicSpecularEnvironment();}
        public static void RunRgbSpecularPerspectiveEnvironment(){specularRgbDistanceAudit=lateSpecExchangeAudit=true;RunPerspectiveAuxiliaryEnvironment();}
        public static void RunRgbSpecularPerspectiveEnvironmentCompatibility(){compatibility=true;RunRgbSpecularPerspectiveEnvironment();}
        public static void RunRgbSpecularMotion(){specularRgbDistanceAudit=lateSpecExchangeAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunRgbSpecularMotionCompatibility(){compatibility=true;RunRgbSpecularMotion();}
        private sealed class LateSpecOverride:IDisposable
        {
            private readonly ComputeShader[] shaders;private readonly ComputeShader original;
            private readonly ComputeShader originalFirst;
            internal readonly GraphicsBuffer histogram;
            internal LateSpecOverride(ComputeShader[] shaders,ComputeShader replacement,ComputeShader first=null){this.shaders=shaders;original=shaders[10];originalFirst=shaders[9];shaders[10]=replacement;if(first!=null)shaders[9]=first;if(lateSpecWeightAudit){histogram=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2048,4);histogram.SetData(new uint[2048]);replacement.SetBuffer(0,Shader.PropertyToID("_HoRTDISpecWeightHistogram"),histogram);}}
            internal void ResetHistogram(){var data=new uint[2048];histogram.GetData(data);Array.Clear(data,0,data.Length);histogram.SetData(data);}
            public void Dispose(){shaders[10]=original;shaders[9]=originalFirst;histogram?.Dispose();}
        }
        private static LateSpecOverride ApplyLateSpecExchange(HoRTDIContext c)
        {
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Late specular audit requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/Shaders/NRD/HoRTDINrdAtrous.hlsl",entry="Runtime/RTDI/Shaders/NRD/HoRTDINrd10.compute",asset="Assets/HoRTDILateSpecAudit";
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json");string root=package.resolvedPath;
            string body=File.ReadAllText(Path.Combine(root,relative)),compute=File.ReadAllText(Path.Combine(root,entry));
            if(!lateSpecWeightAudit&&!lateSpecOrthographicAudit&&!specularRgbDistanceAudit)body=ReplaceOnce(body,"    float4 filteredSpecularIlluminationAndVariance = float4(sumSpecularIlluminationAndVariance / float4(sumWSpecular.xxx, sumWSpecular * sumWSpecular));",@"
    // Same fixed-kernel exchange form already used by the diffuse path.
    // Rejected neighbor mass remains at the center; do not renormalize rows.
    const float specularCenterKernel = 0.44198 * 0.44198;
    float retainedSpecularWeight = saturate(1.0 - (sumWSpecular - specularCenterKernel));
    float4 specularNeighborSum = sumSpecularIlluminationAndVariance - centerSpecularIlluminationAndVariance * float4(specularCenterKernel.xxx, specularCenterKernel * specularCenterKernel);
    float4 filteredSpecularIlluminationAndVariance = specularNeighborSum + centerSpecularIlluminationAndVariance * float4(retainedSpecularWeight.xxx, retainedSpecularWeight * retainedSpecularWeight);");
            if(lateSpecAdaptiveAudit)
            {
                body=ReplaceOnce(body,"    float retainedSpecularWeight = saturate(1.0 - (sumWSpecular - specularCenterKernel));",@"
    float acceptedFraction=saturate((sumWSpecular-specularCenterKernel)/(1.0-specularCenterKernel));
    float confidence=saturate(gIn_SpecConfidence.SampleLevel(gLinearClamp,pixelUv,0));
    // Retain normalization when little neighboring mass is accepted.
    // The smooth confidence/acceptance policy is an experiment, not an
    // upstream optimum or a classification by environment presence.
    float exchangeAmount=acceptedFraction*acceptedFraction*confidence;
    float specularNeighborScale=lerp(rcp(sumWSpecular),1.0,exchangeAmount);
    float retainedSpecularWeight=saturate(1.0-(sumWSpecular-specularCenterKernel)*specularNeighborScale);");
                body=ReplaceOnce(body,"    float4 filteredSpecularIlluminationAndVariance = specularNeighborSum + centerSpecularIlluminationAndVariance * float4(retainedSpecularWeight.xxx, retainedSpecularWeight * retainedSpecularWeight);","    float4 filteredSpecularIlluminationAndVariance = specularNeighborSum * float4(specularNeighborScale.xxx,specularNeighborScale*specularNeighborScale) + centerSpecularIlluminationAndVariance * float4(retainedSpecularWeight.xxx, retainedSpecularWeight*retainedSpecularWeight);");
            }
            if(lateSpecOrthographicAudit)
            {
                body=ReplaceOnce(body,"float3 centerV = -normalize(centerWorldPos);","float3 centerV = gOrthoMode == 0 ? -normalize(centerWorldPos) : -normalize(gFrustumForward.xyz);");
                body=ReplaceOnce(body,"float3 sampleV = -normalize(sampleWorldPos + gRoughnessEdgeStoppingRelaxation * centerWorldPos);","float3 sampleV = gOrthoMode == 0 ? -normalize(sampleWorldPos + gRoughnessEdgeStoppingRelaxation * centerWorldPos) : centerV;");
            }
            if(specularRgbDistanceAudit)body=ReplaceOnce(body,"abs(centerSpecularLuminance - sampleSpecularLuminance) / max(1.0e-4, gSpecPhiLuminance * sqrt(pairSpecularVar))","Color::Luminance(abs(centerSpecularIlluminationAndVariance.rgb - sampleSpecularIlluminationAndVariance.rgb)) / max(1.0e-4, gSpecPhiLuminance * sqrt(pairSpecularVar))");
            if(lateSpecWeightAudit)
            {
                body=ReplaceOnce(body,"[numthreads(GROUP_X, GROUP_Y, 1)]","RWStructuredBuffer<uint> _HoRTDISpecWeightHistogram;\n[numthreads(GROUP_X, GROUP_Y, 1)]");
                body=ReplaceOnce(body,"    float4 filteredSpecularIlluminationAndVariance = float4(sumSpecularIlluminationAndVariance / float4(sumWSpecular.xxx, sumWSpecular * sumWSpecular));",@"
    if(pixelPos.x>=gRectSize.x*.25&&pixelPos.x<gRectSize.x*.6875&&pixelPos.y>=gRectSize.y*.25&&pixelPos.y<gRectSize.y*.75)
    {
        uint pass=min(3u,(uint)round(log2(float(gStepSize)))-1u);
        uint weightBin=min(63u,(uint)(saturate(sumWSpecular)*64));
        float confidence=saturate(gIn_SpecConfidence.SampleLevel(gLinearClamp,pixelUv,0));
        uint confidenceBin=min(7u,(uint)(confidence*8));
        InterlockedAdd(_HoRTDISpecWeightHistogram[pass*512+confidenceBin*64+weightBin],1);
    }
    float4 filteredSpecularIlluminationAndVariance = float4(sumSpecularIlluminationAndVariance / float4(sumWSpecular.xxx, sumWSpecular * sumWSpecular));");
            }
            body=Rebase(body,relative);compute=Rebase(compute,entry).Replace(packagePath+"/"+relative,asset+"/HoRTDILateSpec.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDILateSpecAudit");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"HoRTDILateSpec.hlsl"),body);File.WriteAllText(Path.Combine(folder,"HoRTDILateSpec.compute"),compute);
            AssetDatabase.ImportAsset(asset+"/HoRTDILateSpec.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(asset+"/HoRTDILateSpec.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDILateSpec.compute");Require(shader!=null&&shader.FindKernel("main")==0,"Late specular shader/kernel mismatch");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            ComputeShader firstShader=null;
            if(allSpecOrthographicAudit)
            {
                const string firstBody="Runtime/RTDI/Shaders/NRD/HoRTDINrdAtrousSmem.hlsl",firstEntry="Runtime/RTDI/Shaders/NRD/HoRTDINrd09.compute";
                string first=File.ReadAllText(Path.Combine(root,firstBody));first=ReplaceOnce(first,"float3 centerV = -normalize(centerWorldPos);","float3 centerV = gOrthoMode == 0 ? -normalize(centerWorldPos) : -normalize(gFrustumForward.xyz);");
                first=ReplaceOnce(first,"float3 sampleV = -normalize(sampleWorldPos + gRoughnessEdgeStoppingRelaxation * centerWorldPos);","float3 sampleV = gOrthoMode == 0 ? -normalize(sampleWorldPos + gRoughnessEdgeStoppingRelaxation * centerWorldPos) : centerV;");
                string firstCompute=Rebase(File.ReadAllText(Path.Combine(root,firstEntry)),firstEntry).Replace(packagePath+"/"+firstBody,asset+"/HoRTDIFirstSpec.hlsl");
                File.WriteAllText(Path.Combine(folder,"HoRTDIFirstSpec.hlsl"),Rebase(first,firstBody));File.WriteAllText(Path.Combine(folder,"HoRTDIFirstSpec.compute"),firstCompute);
                AssetDatabase.ImportAsset(asset+"/HoRTDIFirstSpec.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(asset+"/HoRTDIFirstSpec.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                firstShader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDIFirstSpec.compute");Require(firstShader!=null&&firstShader.FindKernel("main")==0,"First ortho shader/kernel missing");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(firstShader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            }
            var shaders=(ComputeShader[])c.nrd.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.nrd);
            Debug.Log("[HoRTDIValidation] LATE_SPEC_EXCHANGE isolated NRD pipeline10 override; histogram="+lateSpecWeightAudit+" firstOrtho="+allSpecOrthographicAudit);return new LateSpecOverride(shaders,shader,firstShader);
            string Rebase(string text,string source)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string include=m.Groups[2].Value;if(include.StartsWith("Packages/",StringComparison.Ordinal))return m.Value;
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(source),include))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Late specular include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
    }
}
