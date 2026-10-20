using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Isolated batch validation; deliberately not an addable renderer feature.
    public static partial class HoRTGISecondaryValidation
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Ray { public Vector4 originMin, directionMax; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Hit
        {
            public float distance; public uint status;
            public Vector3 normal, geometricNormal, baseColor;
            public float roughness, metallic, reflectance;
            public Vector3 emission;
        }
        private static readonly List<Object> objects = new();
        private static HoRTScene scene;
        private static HoRTGISecondaryMaterials materials;
        private static GraphicsBuffer rays, hits;
        private static RayTracingShader shader;
        private static Renderer renderer;
        private static Material source;
        private static bool previousAsync;

        public static void Run()
        {
            try
            {
                if (!HoRTScene.IsSupported || !SystemInfo.supportsRayTracing) throw new Exception("DXR validation requires D3D12 hardware RT");
                previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                shader = AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGISecondaryValidation.raytrace");
                var hitShader = Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial");
                Require(shader != null && hitShader != null, "Missing GI hit shaders");
                materials = new HoRTGISecondaryMaterials(hitShader); scene = new HoRTScene(materials.Resolve,HoRTGISecondaryMaterials.SupportsCutout);
                var go = Keep(new GameObject("Offscreen GI receiver"));
                go.transform.position = new Vector3(2, 0, 0);
                var mesh = Keep(new Mesh());
                mesh.vertices = new[] { new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0) };
                mesh.triangles = new[] { 0,2,1, 2,3,1 };
                mesh.normals = new[] { Vector3.back,Vector3.back,Vector3.back,Vector3.back };
                mesh.tangents = new[] { new Vector4(1,0,0,-1),new Vector4(1,0,0,-1),new Vector4(1,0,0,-1),new Vector4(1,0,0,-1) };
                mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.up,Vector2.one };
                mesh.uv2 = mesh.uv3 = mesh.uv4 = mesh.uv;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; renderer = go.AddComponent<MeshRenderer>();
                source = Keep(new Material(Shader.Find("lilToon"))); renderer.sharedMaterial = source;
                source.SetColor("_Color", new Color(.2f,.4f,.6f,1)); source.SetTexture("_MainTex", Texture2D.whiteTexture);
                source.SetFloat("_Smoothness", .7f); source.SetFloat("_Metallic", .3f); source.SetFloat("_Reflectance", .08f);
                source.SetFloat("_UseEmission", 1); source.SetFloat("_EmissionBlend", .5f); source.SetColor("_EmissionColor", new Color(2,1,.5f,1));
                source.SetFloat("_UseEmission2nd", 1); source.SetFloat("_Emission2ndBlend", .25f); source.SetColor("_Emission2ndColor", new Color(.4f,.6f,.8f,1));
                source.SetTexture("_EmissionMap",Texture2D.whiteTexture); source.SetTexture("_Emission2ndMap",Texture2D.whiteTexture);
                source.SetTexture("_EmissionBlendMask",Texture2D.whiteTexture); source.SetTexture("_Emission2ndBlendMask",Texture2D.whiteTexture);
                rays = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, Marshal.SizeOf<Ray>());
                hits = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, Marshal.SizeOf<Hit>());
                rays.SetData(new[] { new Ray { originMin=new Vector4(2.2f,.3f,-1,.001f),directionMax=new Vector4(0,0,1,10) }, new Ray { originMin=new Vector4(-2,0,-1,.001f),directionMax=new Vector4(0,0,1,10) } });
                EditorApplication.delayCall += Validate;
            }
            catch (Exception error) { Finish(error); }
        }

        private static Hit Trace(bool visibilityOnly=false)
        {
            materials.BeginFrame(); scene.Synchronize(new Renderer[] { renderer }); materials.EndFrame();
            var cmd = new CommandBuffer { name = "HoRTGI secondary material validation" };
            try
            {
                scene.ReadCompatibility().BuildAndBind(cmd, shader);
                cmd.SetRayTracingShaderPass(shader, "HoRTGISecondarySurface");
                cmd.SetRayTracingBufferParam(shader, "_HoGITestRays", rays); cmd.SetRayTracingBufferParam(shader, "_HoGITestHits", hits);
                cmd.DispatchRays(shader, visibilityOnly?"HoGIVisibilityValidate":"HoGIValidate", 2, 1, 1); Graphics.ExecuteCommandBuffer(cmd);
                var result = new Hit[2]; hits.GetData(result);
                Require(result[1].status == 0, "World-space miss was not preserved");
                return result[0];
            }
            finally { cmd.Dispose(); }
        }

        private static void Validate()
        {
            try
            {
                if(cutoutAudit){ValidateCutout();Finish(null);return;}
                if(skinnedAudit){ValidateSkinnedHit();Finish(null);return;}
                var hit = Trace(); Debug.Log($"[HoRTGISecondaryValidation] first status={hit.status} base={hit.baseColor} emission={hit.emission} normal={hit.normal}");
                Require(hit.status == 1, "Opaque lilToon hit was not evaluated"); Near(hit.distance, 1, "hit distance");
                var baseColor=source.GetColor("_Color");
                if(QualitySettings.activeColorSpace==ColorSpace.Linear)baseColor=baseColor.linear;
                Near3(hit.baseColor, new Vector3(baseColor.r,baseColor.g,baseColor.b), "linear material base");
                Near3(hit.emission, new Vector3(1.1f,.65f,.45f), "dedicated two-group HDR emission");
                Near3(hit.normal, Vector3.back, "interpolated normal"); Near3(hit.geometricNormal, Vector3.back, "triangle normal");
                bool linear=QualitySettings.activeColorSpace==ColorSpace.Linear;
                Near(hit.roughness,.3f,"roughness");
                Near(hit.metallic,linear?Mathf.GammaToLinearSpace(.3f):.3f,"Gamma-tagged metallic");
                Near(hit.reflectance,linear?Mathf.GammaToLinearSpace(.08f):.08f,"Gamma-tagged reflectance");
                source.SetFloat("_EmissionMainStrength",1); source.SetFloat("_UseEmission2nd",0);
                hit=Trace(); Near3(hit.emission,new Vector3(baseColor.r,baseColor.g*.5f,baseColor.b*.25f),"emission main-strength component");
                source.SetFloat("_EmissionMainStrength",0);
                var map=Keep(new Texture2D(2,2,TextureFormat.RGBAFloat,false,true));
                map.filterMode=FilterMode.Point;map.wrapMode=TextureWrapMode.Repeat;
                map.SetPixels(new[] { Color.red,Color.green,Color.blue,new Color(.25f,.5f,.75f,.5f) });map.Apply();
                source.SetTexture("_EmissionMap",map);source.SetFloat("_EmissionMap_UVMode",1);
                hit=Trace();Near3(hit.emission,new Vector3(.125f,.125f,.09375f),"UV1 texture alpha weights emission group");
                source.SetTextureOffset("_EmissionMap",new Vector2(.5f,0));
                hit=Trace();Near3(hit.emission,new Vector3(0,0,.25f),"emission ST follows selected UV");
                source.SetTexture("_EmissionMap",Texture2D.whiteTexture);source.SetTextureOffset("_EmissionMap",Vector2.zero);
                var mask=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));mask.SetPixel(0,0,new Color(.5f,.5f,.5f,.5f));mask.Apply();
                source.SetTexture("_EmissionBlendMask",mask);hit=Trace();Near3(hit.emission,new Vector3(.25f,.125f,.0625f),"shared emission mask RGB and alpha");
                source.SetTexture("_EmissionBlendMask",Texture2D.whiteTexture);
                source.SetFloat("_UseEmission",0); hit=Trace(); Near3(hit.emission,Vector3.zero,"emission disabled");
                Require(source.shader.name=="lilToon" && source.GetColor("_Color")==new Color(.2f,.4f,.6f,1),"Source raster material was changed");
                source.SetFloat("_UseMain2ndTex",1); hit=Trace(); Require(hit.status==2,"Unsupported material must remain a hit");
                source.SetFloat("_UseMain2ndTex",0); renderer.sharedMaterial=Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                hit=Trace(); Require(hit.status==2,"Unsupported shader became an environment miss");
                renderer.sharedMaterial=source; renderer.transform.localScale=new Vector3(-2,.5f,1); hit=Trace();
                Require(hit.status==1,"Mirrored, nonuniform world transform lost hit"); Near3(hit.geometricNormal,Vector3.back,"mirrored triangle normal");
                foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial")))
                    if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error || message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
                Debug.Log("[HoRTGISecondaryValidation] PASS real DXR secondary hits, source material preserved, dedicated emission groups, fallback occlusion, mirrored transform");
                Finish(null);
            }
            catch(Exception error) { Finish(error); }
        }
        private static T Keep<T>(T value) where T:Object { objects.Add(value); return value; }
        private static void Require(bool value,string message) { if(!value)throw new Exception(message);if(cutoutAudit){cutoutChecks.Add(message);Debug.Log("[HoGICutoutHit] CHECK "+message);}if(skinnedAudit){skinnedChecks.Add(message);Debug.Log("[HoGISkinnedHit] CHECK "+message);} }
        private static void Near(float actual,float expected,string name) => Require(Mathf.Abs(actual-expected)<.005f,$"{name}: {actual} != {expected}");
        private static void Near3(Vector3 actual,Vector3 expected,string name) { Near(actual.x,expected.x,name+" X");Near(actual.y,expected.y,name+" Y");Near(actual.z,expected.z,name+" Z"); }
        private static void Finish(Exception error)
        {
            if(cutoutAudit)SaveCutoutReport(error);
            if(skinnedAudit)SaveSkinnedHitReport(error);
            scene?.Dispose(); materials?.Dispose(); rays?.Dispose(); hits?.Dispose(); ShaderUtil.allowAsyncCompilation=previousAsync;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]); objects.Clear();
            if(error!=null)Debug.LogException(error); EditorApplication.Exit(error==null?0:1);
        }
    }
}
