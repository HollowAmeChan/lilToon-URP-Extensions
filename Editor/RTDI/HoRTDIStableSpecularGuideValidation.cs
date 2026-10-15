using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool stableSpecularGuideAudit;
        private static bool firstSpecularGuideAudit,matchedSpecularGuideVariance,stableRgbGuideAudit;
        private static string StableGuideVariant=>stableRgbGuideAudit?"rgb106":matchedSpecularGuideVariance?"matched":"colour";
        public static void RunRgbFirstGuideEnvironment(){stableRgbGuideAudit=true;RunFirstSpecularGuideEnvironment();}
        public static void RunRgbFirstGuideMotion(){stableRgbGuideAudit=true;RunFirstSpecularGuideMotion();}
        public static void RunRgbFirstGuidePerspective(){stableRgbGuideAudit=true;RunFirstSpecularGuidePerspective();}
        public static void RunRgbFirstGuidePerspectiveCompatibility(){compatibility=true;RunRgbFirstGuidePerspective();}
        public static void RunFirstSpecularGuideEnvironment(){firstSpecularGuideAudit=true;RunStableSpecularGuideEnvironment();}
        public static void RunFirstSpecularGuideMotion(){firstSpecularGuideAudit=true;RunStableSpecularGuideMotion();}
        public static void RunFirstSpecularGuideEnvironmentCompatibility(){compatibility=true;RunFirstSpecularGuideEnvironment();}
        public static void RunFirstSpecularGuidePerspective(){auxiliaryPerspectiveAudit=true;RunFirstSpecularGuideEnvironment();}
        public static void RunMatchedSpecularGuideEnvironment(){matchedSpecularGuideVariance=true;RunFirstSpecularGuideEnvironment();}
        public static void RunMatchedSpecularGuideEnvironmentCompatibility(){compatibility=true;RunMatchedSpecularGuideEnvironment();}
        public static void RunMatchedSpecularGuideMotion(){matchedSpecularGuideVariance=true;RunFirstSpecularGuideMotion();}
        public static void RunMatchedSpecularGuidePerspective(){auxiliaryPerspectiveAudit=true;RunMatchedSpecularGuideEnvironment();}
        public static void RunStableSpecularGuideEnvironment(){stableSpecularGuideAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunStableSpecularGuideEnvironmentCompatibility(){compatibility=true;RunStableSpecularGuideEnvironment();}
        public static void RunStableSpecularGuideMotion(){stableSpecularGuideAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunStableSpecularGuidePerspective(){auxiliaryPerspectiveAudit=true;RunStableSpecularGuideEnvironment();}
        private static IDisposable ApplyStableSpecularGuide(HoRTDIContext context)
        {
            if(!stableSpecularGuideAudit)return null;
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Stable guide requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",bodyPath="Runtime/RTDI/Shaders/NRD/HoRTDINrdAtrous.hlsl",entryPath="Runtime/RTDI/Shaders/NRD/HoRTDINrd10.compute",asset="Assets/HoRTDIStableSpecularGuide";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string body="Texture2D<float4> _HoRTDIAuditStableSpecGuide;\n"+File.ReadAllText(Path.Combine(root,bodyPath));
            body=ReplaceOnce(body,"float centerSpecularLuminance = Color::Luminance(centerSpecularIlluminationAndVariance.rgb);","float centerSpecularLuminance = Color::Luminance(_HoRTDIAuditStableSpecGuide[pixelPos].rgb);");
            body=ReplaceOnce(body,"float sampleSpecularLuminance = Color::Luminance(sampleSpecularIlluminationAndVariance.rgb);","float sampleSpecularLuminance = Color::Luminance(_HoRTDIAuditStableSpecGuide[p].rgb);");
            if(stableRgbGuideAudit)body=ReplaceOnce(body,"abs(centerSpecularLuminance - sampleSpecularLuminance)","Color::Luminance(abs(_HoRTDIAuditStableSpecGuide[pixelPos].rgb - _HoRTDIAuditStableSpecGuide[p].rgb))");
            if(matchedSpecularGuideVariance)body=ReplaceOnce(body,"float pairSpecularVar = min(centerSpecularVar, max(0.0, sampleSpecularIlluminationAndVariance.a));","float pairSpecularVar = min(max(0.0, _HoRTDIAuditStableSpecGuide[pixelPos].a), max(0.0, _HoRTDIAuditStableSpecGuide[p].a));");
            body=Rebase(body,bodyPath);string compute=Rebase(File.ReadAllText(Path.Combine(root,entryPath)),entryPath).Replace(packagePath+"/"+bodyPath,asset+"/HoRTDINrdAtrous.hlsl");
            string folder=Path.Combine(Application.dataPath,"HoRTDIStableSpecularGuide");Directory.CreateDirectory(folder);
            WriteChanged(Path.Combine(folder,"HoRTDINrdAtrous.hlsl"),body);WriteChanged(Path.Combine(folder,"HoRTDINrd10.compute"),compute);
            foreach(string file in new[]{"HoRTDINrdAtrous.hlsl","HoRTDINrd10.compute"})AssetDatabase.ImportAsset(asset+"/"+file,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDINrd10.compute");Require(shader!=null&&shader.FindKernel("main")==0,"Stable guide shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            return new StableSpecularGuideOverride(context,shader,firstSpecularGuideAudit?9:6);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Rebase(string text,string relative)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Guide include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        private sealed class StableSpecularGuideOverride:IDisposable
        {
            private readonly HoRTDIContext context;private readonly ComputeShader[] shaders;private readonly ComputeShader original;
            private readonly Action<CommandBuffer,int,RenderTargetIdentifier> originalCallback;
            private readonly RenderTexture guide;private int captures,latePasses,lastCaptureLatePasses;
            internal StableSpecularGuideOverride(HoRTDIContext context,ComputeShader replacement,int captureStage)
            {
                this.context=context;shaders=(ComputeShader[])context.nrd.GetType().GetField("shaders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.nrd);original=shaders[10];originalCallback=context.nrd.ValidationSpecularStage;
                guide=Keep(new RenderTexture(context.width,context.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));guide.Create();replacement.SetTexture(0,Shader.PropertyToID("_HoRTDIAuditStableSpecGuide"),guide);
                context.nrd.ValidationSpecularStage=(cmd,stage,texture)=>
                {
                    originalCallback?.Invoke(cmd,stage,texture);
                    if(stage==captureStage){Require(captures==0||latePasses>lastCaptureLatePasses,"Prior guide snapshot was not consumed");cmd.CopyTexture(texture,new RenderTargetIdentifier(guide));captures++;lastCaptureLatePasses=latePasses;}
                    if(stage==10){Require(captures>0&&latePasses-lastCaptureLatePasses<4,"Late guide missing current clamping snapshot");latePasses++;}
                };
                shaders[10]=replacement;
            }
            public void Dispose(){shaders[10]=original;context.nrd.ValidationSpecularStage=originalCallback;Require(captures>0&&latePasses>=captures,"Stable guide capture/consumption missing");Debug.Log($"[HoRTDIValidation] STABLE_SPEC_GUIDE captures={captures} latePasses={latePasses} bytes={context.width*context.height*8} first={firstSpecularGuideAudit} matchedVariance={matchedSpecularGuideVariance} rgb={stableRgbGuideAudit}; isolated guide");}
        }
    }
}
