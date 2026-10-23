using lilToon.URP.Extensions.NRD;
using System;
using lilToon.URP.Extensions.RTDI.NRD;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDINrdValidation
    {
        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation=false;EditorApplication.delayCall+=Validate;
        }
        private static void Validate()
        {
            IntPtr instance=IntPtr.Zero;
            try
            {
                var importer=AssetImporter.GetAtPath("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll") as PluginImporter;
                Debug.Log("[HoRTDINrdValidation] importer="+(importer!=null)+" editor="+(importer!=null&&importer.GetCompatibleWithEditor()));
                if(importer!=null)
                {
                    importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(true);
                    importer.SetEditorData("CPU","x86_64");importer.SetEditorData("OS","Windows");
                    importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64,true);importer.SaveAndReimport();
                    importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64,false);importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX,false);importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows,false);importer.SaveAndReimport();
                }
                instance=HoNrdNative.Create();var desc=HoNrdNative.Read<HoNrdNative.InstanceDesc>(HoNrdNative.GetInstanceDesc(instance));
                if(desc.pipelineCount!=HoRTDINrdManifest.Identifiers.Length)throw new Exception("Pipeline count mismatch");
                for(int i=0;i<desc.pipelineCount;i++)
                {
                    var pipeline=HoNrdNative.Read<HoNrdNative.PipelineDesc>(desc.pipelines,i);
                    if(pipeline.identifier!=HoRTDINrdManifest.Identifiers[i])throw new Exception("Shader identifier mismatch");
                    var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>($"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/NRD/HoRTDINrd{i:00}.compute");
                    if(shader==null)throw new Exception("Missing NRD Unity kernel "+i);
                    int kernel=shader.FindKernel("main");shader.GetKernelThreadGroupSizes(kernel,out uint x,out uint y,out uint z);
                    foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
                    Debug.Log($"[HoRTDINrdValidation] kernel {i}: {x}x{y}x{z} {pipeline.identifier}");
                }
                Debug.Log("[HoRTDINrdValidation] PASS pinned CPU ABI and Unity kernel compilation");
                HoNrdNative.DestroyInstance(instance);instance=IntPtr.Zero;EditorApplication.Exit(0);
            }
            catch(Exception error){if(instance!=IntPtr.Zero)HoNrdNative.DestroyInstance(instance);Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
