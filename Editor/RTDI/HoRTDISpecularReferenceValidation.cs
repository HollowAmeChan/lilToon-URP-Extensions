using System;
using System.Collections.Generic;
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
        private static bool referenceSpecularAudit;
        private static readonly List<SpecularReferenceCase> specularReferenceCases=new();
        public static void RunSpecularReferenceEnvironment(){referenceSpecularAudit=true;RunRuntimeMultiEnvironment();}
        public static void RunSpecularReferenceEnvironmentCompatibility(){compatibility=true;RunSpecularReferenceEnvironment();}
        public static void RunSpecularReferenceStages(){referenceSpecularAudit=auxiliaryEnvironmentStages=auxiliaryEnvironmentMomentAudit=true;RunRuntimeMultiEnvironment();}
        [Serializable] private sealed class SpecularReferenceReport{public bool diagnosticOnly=true;public string path;public SpecularReferenceCase[] cases;}
        [Serializable] private sealed class SpecularReferenceCase{public bool mixed,reuse;public int frames,pixels;public double maxRelativeInputError;}
        private sealed class SpecularReferenceOverride:IDisposable
        {
            private readonly HoRTDIRendererFeature target;private readonly FieldInfo field;private readonly object original;
            private readonly Color[] reference;private readonly SpecularReferenceCase result;
            internal SpecularReferenceOverride(Light blue,bool mixed,bool reuse)
            {
                Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/")&&camera.orthographic&&cutoutResolutionSpecular,"Specular reference requires isolated orthographic fixture");
                const int size=128;reference=new Color[size*size];double sky=EnvironmentSpecularReference(102.0/255);
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)reference[y*size+x]=new Color(mixed?(float)ResolutionMotionLamp(light,x,y,size):0,(float)sky,mixed?(float)ResolutionMotionLamp(blue,x,y,size):0,1);
                var texture=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));texture.filterMode=FilterMode.Point;texture.SetPixels(reference);texture.Apply(false,true);
                const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions",relative="Runtime/RTDI/Shaders/HoRTDI.compute",asset="Assets/HoRTDISpecReference/HoRTDI.compute";
                string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
                string source=File.ReadAllText(Path.Combine(root,relative)).Replace("\r\n","\n");
                const string originalLine="float receiver=RAB_IsSurfaceValid(surface)?1:0;\n    #if !defined(HO_RTDI_NRD_ACTIVE)";
                Require(source.Split(new[]{originalLine},StringSplitOptions.None).Length==2,"Reference injection source changed");
                source="Texture2D<float4> _HoRTDIAuditSpecReference;\n"+source.Replace(originalLine,"float receiver=RAB_IsSurfaceValid(surface)?1:0;\n    specular=receiver>.5?_HoRTDIAuditSpecReference.Load(int3(id.xy,0)).rgb:0;\n    #if !defined(HO_RTDI_NRD_ACTIVE)");
                source=Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
                {
                    string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');
                    Require(!path.StartsWith("../",StringComparison.Ordinal),"Reference include escaped package");return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
                });
                string destination=Path.Combine(Application.dataPath,"HoRTDISpecReference/HoRTDI.compute");Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if(!File.Exists(destination)||File.ReadAllText(destination)!=source)File.WriteAllText(destination,source);
                AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset);Require(shader!=null,"Reference compute missing");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                shader.SetTexture(shader.FindKernel("ShadeNRD"),Shader.PropertyToID("_HoRTDIAuditSpecReference"),texture);
                target=feature;field=target.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);original=field.GetValue(target);field.SetValue(target,shader);
                result=new SpecularReferenceCase{mixed=mixed,reuse=reuse};
            }
            internal void Observe(int frame,Texture2D raw)
            {
                if(frame%16!=0)return;result.frames++;
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {
                    Color actual=raw.GetPixel(x,y),expected=reference[y*128+x];Require(actual.a>.5f,"Reference fixture lacks primary coverage");
                    double error=(Math.Abs(actual.r-expected.r)+Math.Abs(actual.g-expected.g)+Math.Abs(actual.b-expected.b))/(expected.r+expected.g+expected.b+.001);
                    Require(double.IsFinite(error)&&error<.003,"Actual raw specular differs from independent reference input");result.maxRelativeInputError=Math.Max(result.maxRelativeInputError,error);result.pixels++;
                }
            }
            public void Dispose(){field.SetValue(target,original);if(result.frames>0){Require(result.frames==32&&result.pixels==32*128*128,"Reference observation count mismatch");specularReferenceCases.Add(result);}}
        }
        private static void WriteSpecularReference()=>File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-specreference97-input-"+(auxiliaryEnvironmentStages?"stages-":"energy-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new SpecularReferenceReport{path=compatibility?"compatibility":"RenderGraph",cases=specularReferenceCases.ToArray()},true));
    }
}
