using System;
using System.IO;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Separate processes prove that repair survives saving/reloading a renderer subasset.
    public static class HoRTDIResourceValidation
    {
        private const string DirectoryPath="Assets/RTDIPlayerProbe/";
        private const string ShaderRoot="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/";
        private static void Guard()
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))
                throw new InvalidOperationException("Resource validation requires the isolated research project");
        }
        public static void Stage()=>Execute(()=>
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            for(int index=0;index<2;index++)
            {
                string path=PathFor(index);var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,path);
                var feature=ScriptableObject.CreateInstance<HoRTDIRendererFeature>();feature.name="Legacy DI";feature.Settings.initialSamples=7;
                AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);EditorUtility.SetDirty(renderer);
                var serialized=new SerializedObject(feature);serialized.FindProperty("shader").objectReferenceValue=null;
                serialized.FindProperty("finalVisibilityShader").objectReferenceValue=null;serialized.FindProperty("visibilityMaterialShader").objectReferenceValue=null;
                serialized.FindProperty("debugShader").objectReferenceValue=null;serialized.FindProperty("debugOverlayShader").objectReferenceValue=null;var library=serialized.FindProperty("nrdShaders");library.arraySize=index==0?0:2;
                if(index==1)
                {
                    // An assigned override must survive expanding a partial library.
                    library.GetArrayElementAtIndex(0).objectReferenceValue=AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot+"NRD/HoRTDINrd14.compute");
                    library.GetArrayElementAtIndex(1).objectReferenceValue=null;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(feature);AssetDatabase.SaveAssets();
                Require(Regex.IsMatch(File.ReadAllText(path),@"(?m)^  debugShader: \{fileID: 0\}"),"Legacy staging unexpectedly filled debug shader");
                feature.Dispose();
            }
        });
        public static void Repair()=>Execute(()=>
        {
            for(int index=0;index<2;index++)
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PathFor(index));var feature=(HoRTDIRendererFeature)renderer.rendererFeatures[0];
                if(index==0)feature.Create();
                else {var inspector=UnityEditor.Editor.CreateEditor(feature);UnityEngine.Object.DestroyImmediate(inspector);}
                Require(EditorUtility.IsDirty(feature),"Reference repair did not mark the renderer subasset dirty");
                int dirty=EditorUtility.GetDirtyCount(feature);
                Require(!feature.EnsureEditorShaderReferences()&&dirty==EditorUtility.GetDirtyCount(feature),"Idempotent repair dirtied a complete asset");
                AssetDatabase.SaveAssets();VerifyDisk(index);feature.Dispose();
            }
        });
        public static void Verify()=>Execute(()=>
        {
            for(int index=0;index<2;index++)
            {
                // Check disk first; OnEnable/Create filling memory cannot satisfy this.
                VerifyDisk(index);var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PathFor(index));var feature=(HoRTDIRendererFeature)renderer.rendererFeatures[0];
                Require(!feature.EnsureEditorShaderReferences(),"Saved renderer still needed shader repair");
                Require(feature.Settings.initialSamples==7,"Resource repair changed DI settings");
                feature.Dispose();
            }
        });
        private static void VerifyDisk(int index)
        {
            string text=File.ReadAllText(PathFor(index));
            Require(text.Contains("guid: "+AssetDatabase.AssetPathToGUID(ShaderRoot+"HoRTDI.compute")),"DI shader missing from disk");
            Require(text.Contains("guid: "+AssetDatabase.AssetPathToGUID(ShaderRoot+"HoRTDIFinalVisibility.raytrace")),"DI final visibility shader missing from disk");
            Require(text.Contains("guid: "+AssetDatabase.AssetPathToGUID(ShaderRoot+"HoRTDIVisibilityMaterial.shader")),"DI visibility material shader missing from disk");
            Require(text.Contains("guid: "+AssetDatabase.AssetPathToGUID(ShaderRoot+"HoRTDIDebug.shader")),"Debug shader missing from disk");
            Require(text.Contains("guid: "+AssetDatabase.AssetPathToGUID("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugGridOverlay.shader")),"Debug grid shader missing from disk");
            string library=Regex.Match(text,@"(?m)^  nrdShaders:\r?\n(?<refs>(?:  - [^\r\n]*\r?\n){15})").Groups["refs"].Value;
            Require(library.Length>0&&!library.Contains("fileID: 0"),"Incomplete saved NRD library");
            string first=AssetDatabase.AssetPathToGUID(ShaderRoot+$"NRD/HoRTDINrd{(index==0?0:14):00}.compute");
            Require(library.Split('\n')[0].Contains("guid: "+first),"Assigned NRD override was replaced");
            for(int slot=1;slot<15;slot++)Require(library.Contains("guid: "+AssetDatabase.AssetPathToGUID(ShaderRoot+$"NRD/HoRTDINrd{slot:00}.compute")),"Missing saved NRD slot "+slot);
        }
        private static string PathFor(int index)=>DirectoryPath+(index==0?"LegacyEmptyRenderer.asset":"LegacyPartialRenderer.asset");
        private static void Require(bool condition,string error){if(!condition)throw new Exception(error);}
        private static void Execute(Action action)
        {
            try{Guard();action();Debug.Log("[HoRTDIResourceValidation] PASS legacy renderer resources");EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
