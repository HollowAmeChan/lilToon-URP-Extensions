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
        private static bool demodulatedProposalAudit;
        private static bool demodulatedRgbAudit;
        public static void RunDemodulatedRgbEnvironment(){demodulatedRgbAudit=true;RunDemodulatedProposalEnvironment();}
        public static void RunDemodulatedRgbMotion(){demodulatedRgbAudit=true;RunDemodulatedProposalMotion();}
        public static void RunDemodulatedRgbPrimary(){demodulatedRgbAudit=true;RunDemodulatedProposalPrimary();}
        public static void RunDemodulatedProposalEnvironment(){demodulatedProposalAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunDemodulatedProposalEnvironmentCompatibility(){compatibility=true;RunDemodulatedProposalEnvironment();}
        public static void RunDemodulatedProposalMotion(){demodulatedProposalAudit=true;RunCutoutSpecularMotionQuality();}
        public static void RunDemodulatedProposalPrimary(){demodulatedProposalAudit=true;RunPrimaryMotionQuality();}
        private static IDisposable ApplyDemodulatedProposal()
        {
            if(!demodulatedProposalAudit)return null;
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Demodulated proposal requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",bridge="Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl",compute="Runtime/RTDI/Shaders/HoRTDI.compute",asset="Assets/HoRTDIDemodulatedProposal";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,bridge));
            const string anchor="float3 direction;float distance;RAB_GetLightDirDistance(surface,sample,direction,distance);";
            int start=source.IndexOf("float RAB_GetLightSampleTargetPdfForSurface(",StringComparison.Ordinal),end=source.IndexOf("bool RAB_GetConservativeVisibility(",StringComparison.Ordinal);
            Require(start>=0&&end>start,"Proposal callback changed");string target=source.Substring(start,end-start);
            Require(target.Split(new[]{anchor},StringSplitOptions.None).Length==2,"Proposal callback anchor changed");
            target=target.Replace(anchor,anchor+@"
    // Isolated signal-domain proposal; physical Shade and source PDFs stay intact.
    if(!HoDIUsesRgbImportance(surface.material))
    {
        float3 diffuseFactor,specularFactor;
        NRD_MaterialFactors(surface.normal,surface.viewDir,surface.material.diffuseAlbedo,surface.material.specularF0,surface.material.roughness,diffuseFactor,specularFactor);
        float3 diffuse=0,specular=0;
        if(dot(direction,surface.geoNormal)>0)
        {
            diffuse=Lambert(surface.normal,-direction)*surface.material.diffuseAlbedo;
            if(surface.material.roughness>=.03)specular=GGX_times_NdotL(surface.viewDir,direction,surface.normal,max(.03,surface.material.roughness),surface.material.specularF0);
        }
        float3 contribution=(diffuse/diffuseFactor+specular/specularFactor)*sample.radiance;
        return max(0,HoDILuminance(contribution))/max(sample.solidAnglePdf,1e-12);
    }
");
            if(demodulatedRgbAudit)
            {
                const string luminance="float3 contribution=(diffuse/diffuseFactor+specular/specularFactor)*sample.radiance;\n        return max(0,HoDILuminance(contribution))/max(sample.solidAnglePdf,1e-12);";
                string canonical=target.Replace("\r\n","\n");Require(canonical.Split(new[]{luminance},StringSplitOptions.None).Length==2,"Demodulated RGB source changed");
                target=canonical.Replace(luminance,"float3 d=max(0,diffuse/diffuseFactor*sample.radiance),s=max(0,specular/specularFactor*sample.radiance);\n        return sqrt(dot(d,d)+dot(s,s))/max(sample.solidAnglePdf,1e-12);");
            }
            source=Rebase(source.Substring(0,start)+target+source.Substring(end),bridge);
            string cs=Rebase(File.ReadAllText(Path.Combine(root,compute)),compute).Replace(packagePath+"/"+bridge,asset+"/HoRTDIApplicationBridge.hlsl");
            cs="#include \""+packagePath+"/Runtime/RTDI/ThirdParty/NRD/NRD.hlsli\"\n"+cs;
            string folder=Path.Combine(Application.dataPath,"HoRTDIDemodulatedProposal");Directory.CreateDirectory(folder);
            WriteChanged(Path.Combine(folder,"HoRTDIApplicationBridge.hlsl"),source);WriteChanged(Path.Combine(folder,"HoRTDI.compute"),cs);
            AssetDatabase.ImportAsset(asset+"/HoRTDIApplicationBridge.hlsl",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(asset+"/HoRTDI.compute",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset+"/HoRTDI.compute");Require(shader!=null,"Demodulated compute missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            return new DemodulatedOverride(feature,shader);
            void WriteChanged(string path,string content){if(!File.Exists(path)||File.ReadAllText(path)!=content)File.WriteAllText(path,content);}
            string Rebase(string text,string relative)=>Regex.Replace(text,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');Require(!path.StartsWith("../",StringComparison.Ordinal),"Proposal include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
        }
        private sealed class DemodulatedOverride:IDisposable
        {
            private readonly HoRTDIRendererFeature target;private readonly FieldInfo field;private readonly object original;
            internal DemodulatedOverride(HoRTDIRendererFeature target,ComputeShader shader){this.target=target;field=target.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);original=field.GetValue(target);field.SetValue(target,shader);}
            public void Dispose()=>field.SetValue(target,original);
        }
    }
}
