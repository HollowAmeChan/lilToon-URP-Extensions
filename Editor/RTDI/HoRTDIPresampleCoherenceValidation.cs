using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Isolated cache-coherence experiment; source PDF and RIS budget stay intact.
    internal static class HoRTDIPresampleCoherenceValidation
    {
        internal static IDisposable Apply(HoRTDIRendererFeature feature,int tilePixels)
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/")||(tilePixels!=1&&tilePixels!=4))
                throw new InvalidOperationException("Presample coherence candidate requires isolated project and 1/4 pixel tiles");
            const string packagePath="Packages/jp.lilxyzw.liltoon.urp.extensions";
            const string relative="Runtime/RTDI/Shaders/HoRTDI.compute";
            const string asset="Assets/HoRTDIPresampleCoherenceAudit/HoRTDI.compute";
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath+"/package.json").resolvedPath;
            string source=File.ReadAllText(Path.Combine(root,relative));
            const string original="RTXDI_RandomSamplerState tileRng=RTXDI_InitRandomSampler(id.xy/16,";
            if(source.Split(new[]{original},StringSplitOptions.None).Length!=2)throw new Exception("Initial tile RNG changed; review candidate");
            source=source.Replace(original,"RTXDI_RandomSamplerState tileRng=RTXDI_InitRandomSampler(id.xy/"+tilePixels+",");
            // Pixel tiles otherwise initialize both samplers identically:
            // tile index and first within-tile sample would share the same draw.
            if(tilePixels==1)
            {
                const string shared="tileRng=RTXDI_InitRandomSampler(id.xy/1,(uint)_HoRTDIFrameIndex,RTXDI_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED);";
                if(!source.Contains(shared))throw new Exception("Pixel tile seed changed; review independent stream");
                source=source.Replace(shared,"tileRng=RTXDI_InitRandomSampler(id.xy/1,(uint)_HoRTDIFrameIndex,RTXDI_DI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED^0x5bd1e995u);");
            }
            source=Regex.Replace(source,"(?m)^(\\s*#include\\s+)\"([^\"]+)\"",m=>
            {
                string path=Path.GetRelativePath(root,Path.GetFullPath(Path.Combine(root,Path.GetDirectoryName(relative),m.Groups[2].Value))).Replace('\\','/');
                if(path.StartsWith("../",StringComparison.Ordinal))throw new Exception("Presample include escaped package");
                return m.Groups[1].Value+"\""+packagePath+"/"+path+"\"";
            });
            string destination=Path.Combine(Application.dataPath,"HoRTDIPresampleCoherenceAudit/HoRTDI.compute");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if(!File.Exists(destination)||File.ReadAllText(destination)!=source)File.WriteAllText(destination,source);
            AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(asset);
            if(shader==null)throw new Exception("Presample candidate shader missing");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))
                if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            return new Override(feature,shader);
        }
        private sealed class Override:IDisposable
        {
            private readonly HoRTDIRendererFeature feature;
            private readonly FieldInfo field;
            private readonly object original;
            internal Override(HoRTDIRendererFeature feature,ComputeShader shader)
            {
                this.feature=feature;field=feature.GetType().GetField("shader",BindingFlags.Instance|BindingFlags.NonPublic);
                original=field.GetValue(feature);field.SetValue(feature,shader);
            }
            public void Dispose()=>field.SetValue(feature,original);
        }
    }
}
