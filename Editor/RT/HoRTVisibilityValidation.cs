#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.RT
{
    public static class HoRTVisibilityValidation
    {
        [StructLayout(LayoutKind.Sequential)] internal struct TestRay
        { public Vector4 originMin,directionMax;public uint mask,pad0,pad1,pad2; }
        [Serializable] private sealed class Report
        {public bool pass,compatibility;public string failure;public int requests;public List<string> checks=new();}
        private static readonly List<Object> objects=new();
        private static HoRTVisibilityProbeFeature probe;
        private static HoRTRendererFeature host;
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static bool compatibility,previousAsync;
        private static Camera camera;
        private static RenderTexture target;
        private static RTHandle output;
        private static GraphicsBuffer rays;
        private static Renderer near,alpha,castOff,farCaster,single,twoSided;
        private static Material cutout;
        private static TestRay[] inputs;
        private static Report report;
        private static int tick;
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            try
            {
                if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Independent batch project required");
                if(!HoRTScene.IsSupported||!SystemInfo.supportsRayTracing)throw new Exception("Native D3D12 RT required");
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
                previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                report=new Report{compatibility=compatibility};
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                host=Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>());
                probe=Keep(ScriptableObject.CreateInstance<HoRTVisibilityProbeFeature>());
                probe.shader=AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RT/HoRTVisibilityValidation.raytrace");
                probe.materialShader=Shader.Find("Hidden/HoRT/VisibilityMaterial");
                Check(probe.shader!=null&&probe.materialShader!=null,"Native query shaders loaded");
                renderer.rendererFeatures.Add(host);renderer.rendererFeatures.Add(probe);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.msaaSampleCount=1;
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                camera=Keep(new GameObject("Native query camera")).AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.transform.position=Vector3.back*3;target=Keep(new RenderTexture(32,32,24));target.Create();
                var texture=Keep(new RenderTexture(16,3,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){enableRandomWrite=true});texture.Create();
                output=RTHandles.Alloc(texture);probe.output=output;
                rays=new GraphicsBuffer(GraphicsBuffer.Target.Structured,16,Marshal.SizeOf<TestRay>());probe.rays=rays;
                near=Quad(0,0);near.shadowCastingMode=ShadowCastingMode.TwoSided;
                Quad(0,1).shadowCastingMode=ShadowCastingMode.TwoSided;
                alpha=Quad(2,0);Quad(2,1);
                cutout=Keep(new Material(Shader.Find("Hidden/lilToonCutout")));cutout.renderQueue=2450;cutout.SetOverrideTag("RenderType","TransparentCutout");
                cutout.SetColor("_Color",Color.white);cutout.SetTexture("_MainTex",Texture2D.whiteTexture);cutout.SetFloat("_Cutoff",.5f);
                foreach(var name in new[]{"_UseMain2ndTex","_UseMain3rdTex","_UseParallax","_UseDither","_AlphaMaskMode"})cutout.SetFloat(name,0);
                cutout.SetVector("_MainTex_ScrollRotate",Vector4.zero);cutout.SetVector("_DissolveParams",Vector4.zero);cutout.SetVector("_DistanceFade",Vector4.zero);
                Check(HoRTCutoutSupport.Supports(cutout),"Cutout support domain accepted");alpha.sharedMaterial=cutout;
                castOff=Quad(4,0);castOff.shadowCastingMode=ShadowCastingMode.Off;
                farCaster=Quad(4,1);farCaster.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                single=Quad(6,0);single.shadowCastingMode=ShadowCastingMode.On;
                twoSided=Quad(8,0);twoSided.shadowCastingMode=ShadowCastingMode.TwoSided;
                var materialOff=Quad(10,0);materialOff.sharedMaterial.SetFloat("_Cull",0);
                var materialFront=Quad(12,0);materialFront.sharedMaterial.SetFloat("_Cull",1);
                inputs=new[]{Ray(0),Ray(0,true),Ray(2),Ray(4),Ray(6),Ray(6,true),Ray(8,true),Ray(10,true),Ray(12),Ray(12,true),Ray(0),Ray(0),Ray(0),Ray(0),Ray(0),Ray(0)};
                inputs[10].directionMax=Vector4.zero;inputs[10].directionMax.w=20;
                inputs[11].mask=2;inputs[12].directionMax.w=2;inputs[13].originMin.w=4.1f;
                inputs[14].originMin.w=-1;inputs[15].originMin.x=float.NaN;rays.SetData(inputs);
                EditorApplication.update+=Tick;
                Debug.Log("[HoRTVisibilityValidation] START compatibility="+compatibility);
            }
            catch(Exception error){Finish(error);}
        }
        private static Renderer Quad(float x,float z)
        {
            var go=Keep(new GameObject("Caster "+x+" "+z));go.layer=29;go.transform.position=new Vector3(x,0,z);
            var mesh=Keep(new Mesh());mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateNormals();mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));renderer.sharedMaterial.SetFloat("_Cull",2);
            return renderer;
        }
        private static TestRay Ray(float x,bool back=false)=>new TestRay{originMin=new Vector4(x+.1f,.1f,back?3:-3,.001f),directionMax=new Vector4(0,0,back?-7:7,20),mask=1};
        private static void Tick()
        {
            try
            {
                if(++tick<3){Render();return;}
                var image=Render();Hit(image,0,3,near);Hit(image,1,2,null);Hit(image,2,3,alpha);Hit(image,3,4,farCaster);
                Hit(image,4,3,single);Status(image,5,0,"single-sided backface miss");Hit(image,6,3,twoSided);
                Hit(image,7,3,null);Status(image,8,0,"front material cull");Hit(image,9,3,null);
                Status(image,10,3,"zero direction");Status(image,11,0,"instance mask");Status(image,12,0,"finite ray interval");
                Status(image,13,0,"minimum distance");Status(image,14,3,"negative minimum");Status(image,15,3,"NaN input");
                cutout.SetColor("_Color",new Color(1,1,1,0));image=Render();Hit(image,2,4,null);
                Check(probe.view.Scene.CutoutCount==1,"Native alpha uses non-opaque AS flag");
                cutout.SetColor("_Color",Color.white);image=Render();Hit(image,2,3,alpha);
                near.transform.position=Vector3.forward*1.5f;image=Render();Hit(image,0,4,null);
                near.transform.position=Vector3.zero;
                probe.policy=HoRTGeometryPolicy.Surface;image=Render();Hit(image,3,3,castOff);Hit(image,5,3,single);Hit(image,8,3,null);
                probe.policy=HoRTGeometryPolicy.ShadowCasters;image=Render();Hit(image,3,4,farCaster);Status(image,5,0,"restored shadow policy");
                single.shadowCastingMode=ShadowCastingMode.TwoSided;image=Render();Hit(image,5,3,single);
                single.shadowCastingMode=ShadowCastingMode.On;image=Render();Status(image,5,0,"caster culling change rebuilds view");
                single.transform.localScale=new Vector3(-1,1,1);image=Render();
                bool rasterFront=RasterFace(false),rasterBack=RasterFace(true);
                Check(rasterFront!=rasterBack,"Mirrored single-sided raster reference has one visible side");
                Check((image[4].r==1)==rasterFront&&(image[5].r==1)==rasterBack,"Mirrored native culling agrees with actual raster front/back");
                foreach(var message in ShaderUtil.GetShaderMessages(probe.materialShader))
                    Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                Debug.Log("[HoRTVisibilityValidation] PASS closest/blocker/alpha/purpose/culling/masks/invalid; checks="+report.checks.Count+" requests="+report.requests);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static Color[] Render()
        {
            report.requests++;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            Check(probe.acquired,"Host query view acquired: "+probe.reason);
            var old=RenderTexture.active;var image=new Texture2D(16,3,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=output.rt;image.ReadPixels(new Rect(0,0,16,3),0,0);image.Apply();return image.GetPixels();}
            finally{RenderTexture.active=old;Object.DestroyImmediate(image);}
        }
        private static bool RasterFace(bool back)
        {
            camera.cullingMask=1<<29;camera.transform.position=new Vector3(6.1f,.1f,back?3:-3);
            camera.transform.rotation=Quaternion.Euler(0,back?180:0,0);Render();
            var old=RenderTexture.active;var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(16,16,1,1),0,0);image.Apply();var c=image.GetPixel(0,0);Debug.Log("[HoRTVisibilityValidation] mirrored raster back="+back+" rgb="+c);return c.maxColorComponent>.1f;}
            finally{RenderTexture.active=old;Object.DestroyImmediate(image);camera.cullingMask=0;}
        }
        private static void Status(Color[] image,int ray,int value,string label)
        {Check(image[ray].r==value&&image[32+ray].r==value,label+": closest="+image[ray].r+" occlusion="+image[32+ray].r);Check(image[16+ray].a==probe.generation,"Fresh GPU output");if(value!=1)Check(image[ray].g==0&&image[ray].b==0&&image[ray].a==0,"Failure/miss clears hit payload");}
        private static void Hit(Color[] image,int ray,float distance,Renderer expected)
        {Status(image,ray,1,"hit "+ray);Check(Mathf.Abs(image[ray].g-distance)<.002f,"Nearest world distance "+ray+": "+image[ray].g);Check(probe.view.Scene.TryResolve((uint)image[ray].b,out var instance),"Resolved nearest identity");if(expected!=null)Check(instance.renderer==expected,"Correct nearest caster");}
        private static void Check(bool success,string text){if(!success)throw new Exception(text);report.checks.Add(text);}
        private static T Keep<T>(T item)where T:Object{objects.Add(item);return item;}
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;
            if(report!=null){report.pass=error==null;report.failure=error?.ToString();var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-visibility"));Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"report.json"),JsonUtility.ToJson(report,true));}
            GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            host?.Dispose();rays?.Dispose();rays=null;output?.Release();output=null;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();
            if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
        }
    }

    [SupportedOnRenderer()]
    internal sealed class HoRTVisibilityProbeFeature:ScriptableRendererFeature
    {
        internal RayTracingShader shader;internal Shader materialShader;internal GraphicsBuffer rays;internal RTHandle output;
        internal HoRTGeometryPolicy policy=HoRTGeometryPolicy.ShadowCasters;internal HoRTView view;internal bool acquired;internal string reason;internal int generation;
        private object profileKey;private QueryPass pass;
        public override void Create(){pass=new QueryPass();profileKey=new object();}
        public override void OnCameraPreCull(ScriptableRenderer renderer,in CameraData data)
        {HoRTRendererFeature.Declare(renderer,data.camera,this,new HoRTViewRequest(1<<29,mask:1,profileKey:profileKey,createProfile:()=>new HoRTVisibilityMaterials(materialShader,"_HoRTCutout"),geometryPolicy:policy),out reason);}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {acquired=HoRTRendererFeature.TryAcquire(renderer,data.cameraData.camera,this,out view,out reason);if(acquired){generation++;pass.owner=this;renderer.EnqueuePass(pass);}}
        private sealed class QueryPass:ScriptableRenderPass
        {
            internal HoRTVisibilityProbeFeature owner;
            internal QueryPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            private sealed class Data{internal HoRTScene.Inputs scene;internal RayTracingShader shader;internal GraphicsBuffer rays;internal TextureHandle output;internal int generation;}
            private static void Query(CommandBuffer cmd,HoRTScene.Inputs scene,RayTracingShader shader,GraphicsBuffer rays,RenderTargetIdentifier output,int generation)
            {scene.BuildAndBind(cmd,shader);cmd.SetRayTracingShaderPass(shader,"HoRTVisibility");cmd.SetRayTracingBufferParam(shader,"_TestRays",rays);cmd.SetRayTracingTextureParam(shader,"_TestOutput",output);cmd.SetRayTracingFloatParam(shader,"_TestGeneration",generation);cmd.DispatchRays(shader,"Query",16,1,1);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                using(var builder=graph.AddUnsafePass<Data>("Ho-RT native visibility qualification",out var d))
                {d.scene=owner.view.Read(graph,builder);d.shader=owner.shader;d.rays=owner.rays;d.generation=owner.generation;d.output=graph.ImportTexture(owner.output);builder.UseTexture(d.output,AccessFlags.WriteAll);builder.UseBuffer(graph.ImportBuffer(d.rays));
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext context)=>{var cmd=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);d.scene.BuildAndBind(cmd,d.shader);cmd.SetRayTracingShaderPass(d.shader,"HoRTVisibility");cmd.SetRayTracingBufferParam(d.shader,"_TestRays",d.rays);cmd.SetRayTracingTextureParam(d.shader,"_TestOutput",d.output);cmd.SetRayTracingFloatParam(d.shader,"_TestGeneration",d.generation);cmd.DispatchRays(d.shader,"Query",16,1,1);});}
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {var cmd=CommandBufferPool.Get("Ho-RT native visibility qualification");Query(cmd,owner.view.ReadCompatibility(),owner.shader,owner.rays,owner.output.nameID,owner.generation);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);}
        }
    }
}
