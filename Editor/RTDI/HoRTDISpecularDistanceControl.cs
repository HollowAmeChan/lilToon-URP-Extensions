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
        private static float specularDistanceControl;
        private static string DistanceControlName=>specularDistanceControl==1?"near":"far";
        public static void RunNearDistanceEnvironment(){specularDistanceControl=1;guideDistanceAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunFarDistanceEnvironment(){specularDistanceControl=65504;guideDistanceAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunNearDistanceMotion(){specularDistanceControl=1;RunCutoutSpecularMotionQuality();}
        public static void RunFarDistanceMotion(){specularDistanceControl=65504;RunCutoutSpecularMotionQuality();}
        public static void RunNearDistancePrimary(){specularDistanceControl=1;RunPrimaryMotionQuality();}
        public static void RunFarDistancePrimary(){specularDistanceControl=65504;RunPrimaryMotionQuality();}
        public static void RunNearDistanceCurved(){specularDistanceControl=1;RunCurvedMotion();}
        public static void RunFarDistanceCurved(){specularDistanceControl=65504;RunCurvedMotion();}
        private static IDisposable ApplySpecularDistanceControl()
        {
            if(specularDistanceControl==0)return null;
            Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/")&&(specularDistanceControl==1||specularDistanceControl==65504),"Distance control requires isolated project");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/Shaders/HoRTDI.compute",asset="Assets/HoRTDISpecDistanceControl/HoRTDI.compute";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,relative));
            const string original="specSignal=float4(all(isfinite(specLighting))?max(0,specLighting):0,selectedDistance);";
            Require(source.Split(new[]{original},StringSplitOptions.None).Length==2,"Specular distance source changed; review control");
            source=source.Replace(original,"specSignal=float4(all(isfinite(specLighting))?max(0,specLighting):0,"+(specularDistanceControl==1?"1.0":"65504.0")+");");
            source=Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');
                Require(!path.StartsWith("../",StringComparison.Ordinal),"Distance include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
            string destination=Path.Combine(Application.dataPath,"HoRTDISpecDistanceControl/HoRTDI.compute");Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if(!File.Exists(destination)||File.ReadAllText(destination)!=source)File.WriteAllText(destination,source);
            AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset);Require(shader!=null,"Distance control shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            return new SpecDistanceOverride(feature,shader);
        }
        private sealed class SpecDistanceOverride:IDisposable
        {
            private readonly HoRTDIRendererFeature target;private readonly FieldInfo field;private readonly object original;
            internal SpecDistanceOverride(HoRTDIRendererFeature target,ComputeShader shader){this.target=target;field=target.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);original=field.GetValue(target);field.SetValue(target,shader);}
            public void Dispose()=>field.SetValue(target,original);
        }
    }
}
