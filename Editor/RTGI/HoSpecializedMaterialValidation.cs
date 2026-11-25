using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using PackageInfo=UnityEditor.PackageManager.PackageInfo;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoSpecializedMaterialValidation
    {
        [StructLayout(LayoutKind.Sequential)] private struct Ray {public Vector4 originMin,directionMax;}
        [StructLayout(LayoutKind.Sequential)] private struct VisibilityRay {public Vector4 originMin,directionMax;public uint mask,pad0,pad1,pad2;}
        [StructLayout(LayoutKind.Sequential)] private struct Hit
        {public float distance;public uint status;public Vector3 normal,geometricNormal,baseColor;public float roughness,metallic,reflectance;public Vector3 emission;}
        [StructLayout(LayoutKind.Sequential)] private struct Entry {public uint offset,count,valid,reserved;}
        private static readonly List<Object> owned=new();
        private static MeshRenderer renderer;
        private static HoRTScene scene;
        private static HoRTGISecondaryMaterials adapters;
        private static RayTracingShader raysShader;
        private static GraphicsBuffer rays,hits;
        public static void Setup(){PlayerSettings.colorSpace=ColorSpace.Linear;Debug.Log("HO_RT_HOST_LINEAR_READY");}

        public static void Run()
        {
            Require(!PackageInfo.GetAllRegisteredPackages().Any(p=>p.name=="jp.lilxyzw.liltoon"),"physical host has no lilToon material package");
            Require(HoRTScene.IsSupported&&SystemInfo.supportsRayTracing,"D3D12 DXR device");
            Require(QualitySettings.activeColorSpace==ColorSpace.Linear,"actual Linear device color space");
            bool previousAsync=ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation=false;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            raysShader=AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGISecondaryValidation.raytrace");
            adapters=new HoRTGISecondaryMaterials(Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial"));
            scene=new HoRTScene(adapters.Resolve,HoRTCutoutSupport.Supports);
            var mesh=Keep(new Mesh {vertices=new[]{new Vector3(-1,-1),new Vector3(1,-1),new Vector3(-1,1),new Vector3(1,1)},
                triangles=new[]{0,2,1,2,3,1},normals=Enumerable.Repeat(Vector3.back,4).ToArray(),
                tangents=Enumerable.Repeat(new Vector4(1,0,0,-1),4).ToArray(),uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one}});
            mesh.uv2=mesh.uv3=mesh.uv4=mesh.uv;
            var obj=Keep(new GameObject("Specialized offscreen RT surface"));obj.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=obj.AddComponent<MeshRenderer>();
            rays=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,Marshal.SizeOf<Ray>());
            hits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,Marshal.SizeOf<Hit>());
            rays.SetData(new[]{new Ray {originMin=new Vector4(-.5f,0,-1,.001f),directionMax=new Vector4(0,0,1,4)},new Ray{originMin=new Vector4(.5f,0,-1,.001f),directionMax=new Vector4(0,0,1,4)}});
            try
            {
                var cloth=Material("HoShader/Cloth URP");cloth.SetColor("_BaseColor",new Color(.2f,.4f,.6f,1));
                var packed=Texture(new Color(.8f,.5f,1,.7f));cloth.SetTexture("_PBRMap",packed);cloth.SetFloat("_PBRInputMode",1);
                Sync(cloth);var baseline=Trace(cloth)[0];Require(baseline.status==1,"Cloth secondary evaluated");
                ColorNear(baseline.baseColor,new Vector3(.2f,.4f,.6f),"Cloth base");Near(baseline.metallic,.8f,"Cloth default map multiplier");Near(baseline.roughness,.3f,"Cloth packed roughness");Near(baseline.reflectance,.02f,"Cloth packed F0");
                cloth.SetTexture("_PBRMap",null);Sync(cloth);Near(Trace(cloth)[0].metallic,0,"Cloth unbound packed fallback");
                cloth.SetFloat("_PBRInputMode",0);cloth.SetFloat("_Metallic",0);cloth.SetFloat("_Smoothness",.5f);
                CheckClothAlpha(cloth);
                cloth.SetFloat("_SurfaceMode",0);cloth.SetColor("_BaseColor",new Color(.2f,.4f,.6f,1));Sync(cloth);
                CheckTension(cloth);
                var hair=Material("HoShader/Hair URP");hair.SetColor("_BaseColor",new Color(.3f,.2f,.1f,.01f));hair.SetFloat("_SurfaceMode",1);Sync(hair);
                var hairHit=Trace(hair)[0];Require(hairHit.status==1,"Hair solid coverage ignores tint alpha");Near(hairHit.roughness,1,"Hair numeric roughness");Near(hairHit.reflectance,0,"Hair numeric F0");
                var skin=Material("HoShader/Skin URP");skin.SetColor("_BaseColor",new Color(.2f,.4f,.6f,1));skin.SetFloat("_SpecularStrength",1.5f);
                skin.SetTexture("_SmoothnessMap",Texture(new Color(.8f,0,0,1)));skin.SetFloat("_SmoothnessMapScale",.5f);
                CheckEmission(skin);var skinHit=Trace(skin)[0];Near(skinHit.reflectance,.06f,"Skin absolute F0 weight");Near(skinHit.roughness,.6f,"Skin mapped roughness");
                skin.SetFloat("_AlphaClip",1);skin.SetTexture("_CoverageMap",Texture(Color.clear,Color.white));Sync(skin);
                var skinCoverage=Trace(skin);Require(skinCoverage[0].status==0&&skinCoverage[1].status==1,"Skin independent cutout coverage");
                var eye=Material("HoShader/Eye URP");eye.SetFloat("_SurfaceMode",1);eye.SetTexture("_BaseMap",Texture(new Color(1,1,1,.01f)));
                eye.SetTexture("_CoverageMap",Texture(Color.clear,Color.white));Sync(eye);var eyeCoverage=Trace(eye);
                Require(eyeCoverage[0].status==0&&eyeCoverage[1].status==1,"Eye R coverage independent of base A AO");Near(eyeCoverage[1].reflectance,.04f,"Eye F0");
                CheckEyeSmoothness(eye);
                var environment=Material("HoShader/Environment URP");environment.SetFloat("_Metallic",.4f);environment.SetFloat("_Smoothness",.6f);environment.SetFloat("_Reflectance",.08f);
                environment.SetTexture("_MetallicGlossMap",Texture(new Color(.5f,0,0,1)));environment.SetTexture("_SmoothnessTex",Texture(new Color(.5f,0,0,1)));Sync(environment);
                var envHit=Trace(environment)[0];Near(envHit.metallic,.2f,"Environment legacy map multiplier");Near(envHit.roughness,.7f,"Environment roughness");Near(envHit.reflectance,.08f,"Environment F0");
                CheckKeywordHistory(environment);
                CheckVisibility(eye);
                var coverage=Texture(Color.clear,Color.white);coverage.wrapMode=TextureWrapMode.Clamp;
                eye.SetTexture("_CoverageMap",coverage);eye.SetTextureScale("_BaseMap",new Vector2(2,1));
                var baseMap=(Texture2D)eye.GetTexture("_BaseMap");baseMap.wrapMode=TextureWrapMode.Repeat;
                Require(Trace(eye)[0].status==1&&Trace(eye)[1].status==1,"coverage uses own Clamp sampler rather than base Repeat");
                CheckVisibility(eye,true);
                CheckDIVisibility(eye,true);
                eye.SetTextureScale("_BaseMap",Vector2.one);CheckDIVisibility(eye,false);
                CheckNormals(mesh,new[]{cloth,hair,skin,eye,environment});
                Debug.Log("HO_SPECIALIZED_RT_MATERIALS_OK "+SystemInfo.graphicsDeviceType+" no-lil Cloth/Hair/Skin/Eye/Environment data, alpha, emission and Tension");
            }
            finally
            {
                Shader.SetGlobalFloat("_HoGDTensionAvailable",0);scene?.Dispose();adapters?.Dispose();rays?.Dispose();hits?.Dispose();
                ShaderUtil.allowAsyncCompilation=previousAsync;
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();
            }
        }

        private static Hit[] Trace(Material material)
        {
            renderer.sharedMaterial=material;string before=EditorJsonUtility.ToJson(material);
            adapters.BeginFrame();scene.Synchronize(new Renderer[]{renderer});adapters.EndFrame();
            using var cmd=new CommandBuffer();scene.ReadCompatibility().BuildAndBind(cmd,raysShader);cmd.SetRayTracingShaderPass(raysShader,"HoRTGISecondarySurface");
            cmd.SetRayTracingBufferParam(raysShader,"_HoGITestRays",rays);cmd.SetRayTracingBufferParam(raysShader,"_HoGITestHits",hits);
            cmd.DispatchRays(raysShader,"HoGIValidate",2,1,1);Graphics.ExecuteCommandBuffer(cmd);
            var result=new Hit[2];hits.GetData(result);Require(before==EditorJsonUtility.ToJson(material),"source raster material unchanged");
            Debug.Log("HO_RT_PROBE "+material.shader.name+" status="+result[0].status+"/"+result[1].status+" base="+result[0].baseColor+" M="+result[0].metallic+" R="+result[0].roughness+" F0="+result[0].reflectance);
            return result;
        }
        private static void CheckEyeSmoothness(Material eye)
        {
            var map=Texture(new Color(.2f,0,1,1),new Color(.8f,1,0,0));
            eye.SetFloat("_SurfaceMode",0);eye.SetFloat("_Smoothness",.65f);eye.SetFloat("_SmoothnessMapScale",.5f);
            eye.SetTexture("_SmoothnessMap",map);eye.SetTextureScale("_BaseMap",new Vector2(2,1));Sync(eye);
            var mapped=Trace(eye);Near(mapped[0].roughness,.9f,"Eye map R left independent of Base ST/A");Near(mapped[1].roughness,.6f,"Eye map R right");
            eye.SetTextureScale("_SmoothnessMap",new Vector2(-1,1));eye.SetTextureOffset("_SmoothnessMap",Vector2.right);Sync(eye);
            mapped=Trace(eye);Near(mapped[0].roughness,.6f,"Eye own map ST left");Near(mapped[1].roughness,.9f,"Eye own map ST right");
            ((Texture2D)eye.GetTexture("_BaseMap")).wrapMode=TextureWrapMode.Repeat;
            eye.SetTextureScale("_SmoothnessMap",new Vector2(2,1));eye.SetTextureOffset("_SmoothnessMap",new Vector2(.5f,0));Sync(eye);
            mapped=Trace(eye);Near(mapped[0].roughness,.6f,"Eye own Clamp sampler left");Near(mapped[1].roughness,.6f,"Eye own Clamp sampler differs from Base Repeat");
            eye.SetFloat("_SmoothnessMapScale",0);Sync(eye);Near(Trace(eye)[0].roughness,1,"Eye zero map multiplier");
            eye.SetTexture("_SmoothnessMap",null);Sync(eye);Near(Trace(eye)[0].roughness,.35f,"Eye map removal scalar fallback");
            eye.SetTextureScale("_BaseMap",Vector2.one);eye.SetFloat("_SurfaceMode",1);Sync(eye);
            Debug.Log("HO_EYE_RT_SMOOTHNESS_OK own ST/sampler, R, multiplier, zero and default scalar");
        }
        private static void CheckClothAlpha(Material material)
        {
            material.SetFloat("_SurfaceMode",1);material.SetFloat("_Cutoff",.15f);material.SetColor("_BaseColor",new Color(1,1,1,.25f));
            material.SetTexture("_BaseMap",Texture(new Color(1,1,1,.2f)));material.SetTexture("_AlphaMask",Texture(new Color(.8f,0,0,1)));
            foreach(var pair in new[]{(0,false),(1,true),(2,false),(3,true),(4,false)})
            {material.SetFloat("_AlphaMaskMode",pair.Item1);Sync(material);Require((Trace(material)[0].status==1)==pair.Item2,"Cloth alpha operation "+pair.Item1);}
            material.SetTexture("_AlphaMask",Texture(Color.clear,Color.white));material.SetFloat("_AlphaMaskMode",1);
            material.SetTextureScale("_BaseMap",new Vector2(-1,1));material.SetTextureOffset("_BaseMap",Vector2.right);Sync(material);
            var result=Trace(material);Require(result[0].status==0&&result[1].status==1,"Cloth mask raw UV not chained through base ST");
        }
        private static void CheckEmission(Material material)
        {
            material.SetFloat("_UseEmission",1);material.SetTexture("_EmissionMap",Texture(new Color(.3f,.2f,.1f,.5f)));
            material.SetColor("_EmissionColor",new Color(2,1,.5f,.8f));material.SetFloat("_EmissionMainStrength",.25f);Sync(material);
            Color linear=material.GetColor("_BaseColor").linear;
            ColorNear(Trace(material)[0].emission,Vector3.Scale(new Vector3(.6f,.2f,.05f)*.4f,new Vector3(.75f+.25f*linear.r,.75f+.25f*linear.g,.75f+.25f*linear.b)),"Skin/Eye RGBA emission and albedo influence");
        }
        private static void CheckTension(Material material)
        {
            material.SetFloat("_HoTesionEnabled",1);Sync(material);
            Shader.SetGlobalFloat("_HoGDTensionAvailable",0);
            ColorNear(Trace(material)[0].baseColor,new Vector3(.2f,.4f,.6f),"Tension enabled without producer bindings");
            using var entries=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,16);using var values=new GraphicsBuffer(GraphicsBuffer.Target.Structured,8,16);
            entries.SetData(new[]{new Entry(),new Entry{count=4,valid=1},new Entry{offset=4,count=4,valid=1}});
            values.SetData(Enumerable.Repeat(new Vector4(1,0,0,1),4).Concat(Enumerable.Repeat(new Vector4(0,1,0,1),4)).ToArray());
            Shader.SetGlobalBuffer("_HoGDTensionEntries",entries);Shader.SetGlobalBuffer("_HoGDTensionValues",values);Shader.SetGlobalInt("_HoGDTensionEntryCount",3);Shader.SetGlobalFloat("_HoGDTensionAvailable",1);
            material.SetFloat("_HoTesionEnabled",1);material.SetTexture("_HoTesionStretchBaseColor",Texture2D.whiteTexture);material.SetColor("_HoTesionStretchColor",Color.red);
            material.SetTexture("_HoTesionCompressionBaseColor",Texture2D.whiteTexture);material.SetColor("_HoTesionCompressionColor",Color.blue);Sync(material);
            renderer.SetShaderUserValue(1u<<16);ColorNear(Trace(material)[0].baseColor,Vector3.right,"ray Tension stretch slot");
            int enabledSignature=adapters.Signature;
            material.DisableKeyword("_HO_GD_TENSION");ColorNear(Trace(material)[0].baseColor,new Vector3(.2f,.4f,.6f),"Tension keyword off matches raster");
            Require(adapters.Signature!=enabledSignature,"Tension keyword-only change invalidates radiance history");
            material.EnableKeyword("_HO_GD_TENSION");
            renderer.SetShaderUserValue(2u<<16);ColorNear(Trace(material)[0].baseColor,Vector3.forward,"ray Tension compression slot");
            Shader.SetGlobalFloat("_HoGDTensionAvailable",0);ColorNear(Trace(material)[0].baseColor,new Vector3(.2f,.4f,.6f),"Tension producer unavailable");
            material.SetFloat("_HoTesionEnabled",0);Sync(material);renderer.SetShaderUserValue(0);
        }
        private static void CheckKeywordHistory(Material material)
        {
            foreach(string keyword in new[]{"_NORMALMAP","_EMISSION"})
            {
                material.DisableKeyword(keyword);Trace(material);int disabled=adapters.Signature;
                material.EnableKeyword(keyword);Trace(material);int enabled=adapters.Signature;
                Require(enabled!=disabled,"Environment "+keyword+" keyword-only change invalidates history");
                material.DisableKeyword(keyword);
            }
        }
        private static void CheckNormals(Mesh sourceMesh,Material[] materials)
        {
            const int size=65;
            using var probe=new HoSpecializedNormalProbe(renderer,size);
            Vector3[] scales={new Vector3(2,1,.5f),new Vector3(-2,1,.5f),new Vector3(2,-1,.5f)};
            foreach(bool mixedHandedness in new[]{false,true})
            {
                var mesh=Keep(Object.Instantiate(sourceMesh));
                mesh.normals=new[]{Vector3.back,new Vector3(.9f,0,-Mathf.Sqrt(.19f)),new Vector3(0,.9f,-Mathf.Sqrt(.19f)),new Vector3(-.5f,.3f,-Mathf.Sqrt(.66f))};
                mesh.tangents=new[]{new Vector4(1,0,0,-1),new Vector4(.4f,.7f,.8f,-1),new Vector4(.8f,.2f,.3f,mixedHandedness?1:-1),new Vector4(.3f,.9f,.1f,mixedHandedness?1:-1)};
                renderer.GetComponent<MeshFilter>().sharedMesh=mesh;
                foreach(Material material in materials)
                {
                    if(material.HasProperty("_SurfaceMode"))material.SetFloat("_SurfaceMode",0);
                    if(material.HasProperty("_AlphaClip"))material.SetFloat("_AlphaClip",0);
                    if(material.HasProperty("_FlipNormal"))material.SetFloat("_FlipNormal",1);
                    material.SetFloat("_Cull",0);material.SetFloat("_BumpScale",1);
                    material.SetTexture("_BumpMap",Texture(new Color(.7f,.35f,.9f,1)));Sync(material);
                    foreach(Vector3 scale in scales)foreach(bool back in new[]{false,true})
                    {
                        renderer.transform.localScale=scale;
                        var samples=probe.Configure(back);
                        rays.SetData(samples.Select(ray=>new Ray{originMin=new Vector4(ray.origin.x,ray.origin.y,ray.origin.z,.001f),directionMax=new Vector4(ray.direction.x,ray.direction.y,ray.direction.z,10)}).ToArray());
                        var actual=Trace(material);Vector3[] expected=probe.Read(material);
                        for(int i=0;i<2;i++)
                        {
                            Require(actual[i].status==1,"normal fixture hit");
                            ColorNear(actual[i].normal,expected[i],material.shader.name+" native normal scale="+scale+" back="+back+" mixedW="+mixedHandedness+" sample="+i);
                        }
                    }
                }
            }
            renderer.transform.localScale=Vector3.one;renderer.GetComponent<MeshFilter>().sharedMesh=sourceMesh;
            Debug.Log("HO_SPECIALIZED_NATIVE_NORMALS_OK 120 raster/DXR samples nonuniform-scale mirrored/backface varying vertex normals/tangents/handedness");
        }

        private static void CheckVisibility(Material source,bool bothHit=false)
        {
            renderer.sharedMaterial=source;
            using var visibility=new HoRTVisibilityMaterials(Shader.Find("Hidden/HoRT/VisibilityMaterial"));visibility.BeginFrame();
            var adapter=visibility.Resolve(source);Require(adapter.GetFloat("_HoRTAlphaDomain")==3,"visibility uses Eye coverage domain");
            using var visibilityScene=new HoRTScene(visibility.Resolve,HoRTCutoutSupport.Supports);
            visibilityScene.Synchronize(new Renderer[]{renderer});
            var shader=AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RT/HoRTVisibilityValidation.raytrace");
            using var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,Marshal.SizeOf<VisibilityRay>());
            input.SetData(new[]{new VisibilityRay{originMin=new Vector4(-.5f,0,-1,.001f),directionMax=new Vector4(0,0,1,4),mask=1},new VisibilityRay{originMin=new Vector4(.5f,0,-1,.001f),directionMax=new Vector4(0,0,1,4),mask=1}});
            var output=Keep(new RenderTexture(2,3,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){enableRandomWrite=true});output.Create();
            using var cmd=new CommandBuffer();visibilityScene.ReadCompatibility().BuildAndBind(cmd,shader);cmd.SetRayTracingShaderPass(shader,"HoRTVisibility");
            cmd.SetRayTracingBufferParam(shader,"_TestRays",input);cmd.SetRayTracingTextureParam(shader,"_TestOutput",output);cmd.DispatchRays(shader,"Query",2,1,1);Graphics.ExecuteCommandBuffer(cmd);
            var previous=RenderTexture.active;var image=Keep(new Texture2D(2,3,TextureFormat.RGBAFloat,false,true));
            try{RenderTexture.active=output;image.ReadPixels(new Rect(0,0,2,3),0,0);image.Apply();}
            finally{RenderTexture.active=previous;}
            var pixels=image.GetPixels();Require(pixels[0].r==(bothHit?1:0)&&pixels[1].r==1,"native closest visibility obeys coverage");
            Require(pixels[4].r==(bothHit?1:0)&&pixels[5].r==1,"native occlusion obeys coverage");
            visibility.EndFrame();
        }
        private static void CheckDIVisibility(Material source,bool bothHit)
        {
            renderer.sharedMaterial=source;
            using var profile=new HoRTVisibilityMaterials(Shader.Find("Hidden/HoRTDI/VisibilityMaterial"),"_HoRTDICutout");
            using var world=new HoRTScene(profile.Resolve,HoRTCutoutSupport.Supports);
            profile.BeginFrame();world.Synchronize(new Renderer[]{renderer});profile.EndFrame();
            var shader=AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoSpecializedVisibilityValidation.raytrace");
            using var blocked=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);
            using var cmd=new CommandBuffer();world.ReadCompatibility().BuildAndBind(cmd,shader);cmd.SetRayTracingShaderPass(shader,"HoRTDIVisibility");
            cmd.SetRayTracingBufferParam(shader,"_HoGITestRays",rays);cmd.SetRayTracingBufferParam(shader,"_HoDIBlocked",blocked);
            cmd.DispatchRays(shader,"Query",2,1,1);Graphics.ExecuteCommandBuffer(cmd);
            var result=new uint[2];blocked.GetData(result);
            Require(result[0]==(bothHit?1:0)&&result[1]==1,"RTDI native occlusion obeys independent coverage sampler");
        }
        private static void Sync(Material material)
        {
            string type=material.shader.name switch
            {"HoShader/Cloth URP"=>"HoShader.ClothURP.Editor.ClothShaderGUI, HoShader.ClothURP.Editor","HoShader/Hair URP"=>"HoShader.HairURP.Editor.HairShaderGUI, HoShader.HairURP.Editor",
             "HoShader/Skin URP"=>"HoShader.SkinStylizedURP.Editor.SkinShaderGUI, HoShader.SkinStylizedURP.Editor","HoShader/Eye URP"=>"HoShader.SkinStylizedURP.Editor.EyeShaderGUI, HoShader.SkinStylizedURP.Editor",
             _=>"HoShader.EnvironmentURP.Editor.EnvironmentShaderGUI, HoShader.EnvironmentURP.Editor"};
            var gui=Type.GetType(type,true);var method=gui.GetMethod("SyncMaterial",BindingFlags.Public|BindingFlags.Static)??gui.GetMethod("SyncSurfaceMode",BindingFlags.Public|BindingFlags.Static);
            method.Invoke(null,new object[]{material});
        }
        private static Material Material(string name){var shader=Shader.Find(name);Require(shader!=null,"installed shader "+name);return Keep(new Material(shader));}
        private static Texture2D Texture(params Color[] colors){var t=Keep(new Texture2D(colors.Length,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp});t.SetPixels(colors);t.Apply();return t;}
        private static T Keep<T>(T value)where T:Object{owned.Add(value);return value;}
        private static void Near(float a,float b,string label)=>Require(Mathf.Abs(a-b)<.006f,label+" actual="+a+" expected="+b);
        private static void ColorNear(Vector3 a,Vector3 b,string label){Near(a.x,b.x,label+" R");Near(a.y,b.y,label+" G");Near(a.z,b.z,label+" B");}
        private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException("Specialized RT: "+label);}
    }
}

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoSpecializedNormalProbe:IDisposable
    {
        private readonly MeshRenderer renderer;
        private readonly int size;
        private readonly Camera camera;
        private readonly UniversalRendererData data;
        private readonly UniversalRenderPipelineAsset pipeline;
        private readonly HoSpecializedNormalFeature feature;
        private readonly RenderPipelineAsset priorDefault=GraphicsSettings.defaultRenderPipeline,priorQuality=QualitySettings.renderPipeline;
        private readonly RenderTexture[] colors=new RenderTexture[7];
        private readonly RenderTexture depth,destination;
        private readonly Texture2D readback;
        private readonly int priorLayer;
        internal HoSpecializedNormalProbe(MeshRenderer renderer,int size)
        {
            this.renderer=renderer;this.size=size;priorLayer=renderer.gameObject.layer;renderer.gameObject.layer=31;
            camera=new GameObject("Specialized normal parity camera").AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<31;
            data=ScriptableObject.CreateInstance<UniversalRendererData>();feature=ScriptableObject.CreateInstance<HoSpecializedNormalFeature>();data.rendererFeatures.Add(feature);
            pipeline=UniversalRenderPipelineAsset.Create(data);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            for(int i=0;i<7;i++){colors[i]=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);colors[i].Create();}
            depth=new RenderTexture(size,size,24,RenderTextureFormat.Depth);depth.Create();destination=new RenderTexture(size,size,24);destination.Create();
            readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
        }
        internal UnityEngine.Ray[] Configure(bool back)
        {
            camera.transform.SetPositionAndRotation(new Vector3(0,0,back?3:-3),Quaternion.Euler(0,back?180:0,0));
            return new[]{camera.ViewportPointToRay(new Vector3((size/4+.5f)/size,.5f,0)),camera.ViewportPointToRay(new Vector3((size*3/4+.5f)/size,.5f,0))};
        }
        internal Vector3[] Read(Material material)
        {
            renderer.sharedMaterial=material;
            // Compile the exact keyword variant before a RendererList capture;
            // otherwise a cold Environment _NORMALMAP draw can use its fallback.
            ShaderUtil.CompilePass(material,material.FindPass("HO_SURFACE_BUFFER"),true);
            using var capture=new HoSpecializedNormalFeature.CapturePass(colors,depth);feature.Active=capture;
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});}
            finally{feature.Active=null;}
            var prior=RenderTexture.active;
            try
            {
                RenderTexture.active=colors[1];readback.ReadPixels(new Rect(0,0,size,size),0,0,false);
                return new[]{Decode(readback.GetPixel(size/4,size/2)),Decode(readback.GetPixel(size*3/4,size/2))};
            }finally{RenderTexture.active=prior;}
        }
        private static Vector3 Decode(Color value)
        {
            Vector3 n=new Vector3(value.r*2-1,value.g*2-1,1-Mathf.Abs(value.r*2-1)-Mathf.Abs(value.g*2-1));
            float t=Mathf.Clamp01(-n.z);n.x+=n.x>=0?-t:t;n.y+=n.y>=0?-t:t;return n.normalized;
        }
        public void Dispose()
        {
            renderer.gameObject.layer=priorLayer;GraphicsSettings.defaultRenderPipeline=priorDefault;QualitySettings.renderPipeline=priorQuality;
            foreach(var t in colors)Object.DestroyImmediate(t);Object.DestroyImmediate(depth);Object.DestroyImmediate(destination);Object.DestroyImmediate(readback);
            Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(pipeline);Object.DestroyImmediate(feature);Object.DestroyImmediate(data);
        }
    }
    internal sealed class HoSpecializedNormalFeature:ScriptableRendererFeature
    {
        public CapturePass Active;
        public override void Create(){}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(Active!=null)renderer.EnqueuePass(Active);}
        internal sealed class CapturePass:ScriptableRenderPass,IDisposable
        {
            private readonly RTHandle[] colors;private readonly RTHandle depth;
            internal CapturePass(RenderTexture[] colors,RenderTexture depth){this.colors=colors.Select(t=>RTHandles.Alloc(t)).ToArray();this.depth=RTHandles.Alloc(depth);renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var rendering=frame.Get<UniversalRenderingData>();var camera=frame.Get<UniversalCameraData>();var light=frame.Get<UniversalLightData>();
                var drawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("HoSurfaceBuffer"),rendering,camera,light,SortingCriteria.CommonOpaque);
                using var builder=graph.AddRasterRenderPass<Data>("Specialized native normal parity",out var d);
                // Cloth's published HoSurface pass uses ZTest Equal, so capture
                // its native depth producer on the same private attachment first.
                var depthDrawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("DepthOnly"),rendering,camera,light,SortingCriteria.CommonOpaque);
                var filter=new FilteringSettings(RenderQueueRange.opaque,1<<31);
                d.depthRenderers=graph.CreateRendererList(new RendererListParams(rendering.cullResults,depthDrawing,filter));
                d.renderers=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filter));
                builder.UseRendererList(d.depthRenderers);builder.UseRendererList(d.renderers);
                for(int i=0;i<colors.Length;i++)builder.SetRenderAttachment(graph.ImportTexture(colors[i]),i,AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(graph.ImportTexture(depth),AccessFlags.WriteAll);builder.AllowPassCulling(false);
                builder.SetRenderFunc(static(Data d,RasterGraphContext ctx)=>{ctx.cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);ctx.cmd.DrawRendererList(d.depthRenderers);ctx.cmd.DrawRendererList(d.renderers);});
            }
            public void Dispose(){foreach(var t in colors)RTHandles.Release(t);RTHandles.Release(depth);}
            private sealed class Data{public RendererListHandle depthRenderers,renderers;}
        }
    }
}
