using System;
using System.IO;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.RayTracing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGIEnvironmentValidation
    {
        static bool cutoutAudit;
        static Color skyBaseline,skyHole,skyBlocked,lightBaseline,lightHole,lightBlocked,halfRaw,halfFiltered;
        static int checkedSegments,blockedSegments;
        public static void RunCutout(){cutoutAudit=true;Run();}
        public static void RunCutoutCompatibility(){compatibility=true;RunCutout();}
        [StructLayout(LayoutKind.Sequential)]struct PackedGI{public Vector3 position;public uint misc,radiance;public float weight;public uint normal;public float unused;}
        [StructLayout(LayoutKind.Sequential)]struct Secondary{public Vector3 position;public uint normal,diffuse,specular;public Vector3 emission;public float pdf;public uint geometricNormal,flags;}
        static void ValidateCutoutLighting()
        {
            gi.Settings.temporal=gi.Settings.spatial=gi.Settings.denoise=false;
            skyBaseline=Mean(32);
            var screen=Quad("World-only Cutout",-.5f,4000,1);var material=Keep(new Material(Shader.Find("Hidden/lilToonCutout")));
            material.SetOverrideTag("RenderType","TransparentCutout");material.renderQueue=2450;material.SetFloat("_Cull",0);material.SetFloat("_Cutoff",.5f);material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",0);material.SetFloat("_UseEmission",0);material.SetFloat("_UseEmission2nd",0);material.SetFloat("_AlphaMaskMode",0);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetColor("_Color",new Color(0,0,0,0));screen.GetComponent<Renderer>().sharedMaterial=material;
            skyHole=Mean(32);var context=gi.LastContext;var registry=(HoRTScene)Field(gi,"scene");
            Require(registry.CutoutCount==1&&context.alphaVisibility&&context.finalVisibility.count==Size*Size,"Cutout enables actual native secondary/reuse/final buffers");
            Relative(skyHole,skyBaseline,"Transparent Cutout initial/lighting/final matches opaque-only sky baseline");
            material.SetColor("_Color",Color.black);skyBlocked=Mean(4);
            var packets=new Secondary[Size*Size];context.secondary.GetData(packets);var packet=packets[32*Size+32];
            Debug.Log("[HoGICutout] SOLID_PACKET flags="+packet.flags+" pos="+packet.position+" diffuse="+packet.diffuse.ToString("X8")+" specular="+packet.specular.ToString("X8")+" emission="+packet.emission+" sourceHasF0="+material.HasProperty("_Reflectance")+" sourceF0="+material.GetFloat("_Reflectance"));
            bool initialHit=true;for(int y=16;y<48;y++)for(int x=16;x<48;x++){var value=packets[y*Size+x];initialHit&=value.flags==1&&Mathf.Abs(value.position.z+.5f)<.001f;}
            Require(initialHit,"All central initial rays hit the solid Cutout at z=-.5, rather than a sky miss or rear wall");
            // F0=0 still has Schlick's (1-V.H)^5 grazing term. Black albedo
            // does not make this supported rough-GGX surface non-reflecting.
            material.SetColor("_Color",new Color(0,0,0,0));screen.transform.position=new Vector3(0,0,.002f);skyHole=Mean(32);Relative(skyHole,skyBaseline,"Cutout behind primary remains transparent to secondary sky");
            material.SetColor("_Color",Color.black);skyBlocked=Mean(4);Near(skyBlocked,Color.black,"Cutout behind primary blocks secondary sky with no initial-path obstruction",.001f);
            gi.Settings.useEnvironment=false;analyticLight.type=LightType.Directional;analyticLight.transform.rotation=Quaternion.Euler(0,180,0);analyticLight.color=Color.white;analyticLight.intensity=1;analyticLight.enabled=true;
            screen.SetActive(false);lightBaseline=Mean(32);Require(lightBaseline.r>.02f,"Directional secondary reference is lit");
            screen.SetActive(true);material.SetColor("_Color",new Color(0,0,0,0));lightHole=Mean(32);Relative(lightHole,lightBaseline,"Transparent secondary directional shadow matches no-Cutout reference");
            material.SetColor("_Color",Color.black);lightBlocked=Mean(4);Near(lightBlocked,Color.black,"Solid secondary directional shadow uses true alpha visibility",.001f);
            // Distant direct sky is a separate bounded-range query.
            analyticLight.enabled=false;wall.SetActive(false);gi.Settings.useEnvironment=true;gi.Settings.includeDirectEnvironment=true;
            screen.transform.position=new Vector3(0,0,-1500);screen.transform.localScale=Vector3.one*4000000;material.SetColor("_Color",Color.black);
            Near(Mean(4),Color.black,"Cutout beyond finite bounce range still blocks direct sky",.001f);
            material.SetColor("_Color",new Color(0,0,0,0));Near(Mean(4),atmosphere.Settings.constantColor,"Distant alpha holes admit explicit direct sky",.01f);
            gi.Settings.useEnvironment=false;gi.Settings.includeDirectEnvironment=false;wall.SetActive(true);
            var wallMaterial=wall.GetComponent<Renderer>().sharedMaterial;wallMaterial.SetColor("_Color",Color.black);wallMaterial.SetFloat("_UseEmission",1);wallMaterial.SetColor("_EmissionColor",Color.red);wallMaterial.SetFloat("_EmissionBlend",1);wallMaterial.SetFloat("_EmissionMainStrength",0);
            screen.transform.position=new Vector3(0,0,-.5f);screen.transform.localScale=Vector3.one*4000;material.SetColor("_Color",Color.black);
            var pattern=Keep(new Texture2D(8,1,TextureFormat.RGBAFloat,false,true));pattern.filterMode=FilterMode.Point;pattern.wrapMode=TextureWrapMode.Repeat;var texels=new Color[8];for(int i=0;i<8;i++)texels[i]=new Color(1,1,1,i%2);pattern.SetPixels(texels);pattern.Apply();material.SetTexture("_MainTex",pattern);material.SetTextureScale("_MainTex",new Vector2(4000,1));
            Mean(2);context=gi.LastContext;
            Require(Marshal.SizeOf<PackedGI>()==32,"Readback uses actual pinned 32-byte GI reservoir layout");
            var candidates=new PackedGI[context.arrayPitch*3];context.reservoirs.GetData(candidates);PackedGI chosen=default;bool found=false;
            for(int i=context.arrayPitch;i<context.arrayPitch*2;i++)if(candidates[i].position.z<-.9f&&candidates[i].position.z>-1.1f&&candidates[i].weight>0){chosen=candidates[i];found=true;break;}
            Require(found,"Actual generated reservoir contains a lit point on the rear emitter");
            // Stage-only frozen valid wall point isolates final alpha visibility;
            // no injected frame is part of the subsequent estimator measurement.
            chosen.position=new Vector3(0,0,-1);var frozen=new PackedGI[context.arrayPitch];for(int i=0;i<frozen.Length;i++)frozen[i]=chosen;
            gi.Settings.denoise=true;Render();context=gi.LastContext;context.historyValid=false;
            context.ValidationBeforeFinal=(cmd,c)=>cmd.SetBufferData(c.reservoirs,frozen,0,c.arrayPitch,c.arrayPitch);
            try{Render();AuditFinalSegments(context,screen,material,pattern);}
            finally{context.ValidationBeforeFinal=null;context.historyValid=false;if(context.nrd!=null)context.nrd.historyValid=false;}
            gi.Settings.temporal=gi.Settings.spatial=true;
            for(int i=0;i<32;i++)Render();halfRaw=halfFiltered=Color.clear;
            for(int i=0;i<64;i++){halfRaw+=Render();halfFiltered+=ReadRoi(context.PublishedDiffuse.rt);}halfRaw/=64;halfFiltered/=64;
            Require(Mathf.Abs(halfRaw.r-.5f)<.025f,"Half-open periodic Cutout keeps ReSTIR raw energy within 5% of independent .5 integral: "+halfRaw.r);
            Relative(halfFiltered,halfRaw,"Filtered half-open Cutout keeps same-run energy");
            Require(context.lastTemporal,"Cutout native temporal history actually reused after reset");
            screen.SetActive(false);Render();Require(!context.alphaVisibility&&context.finalVisibility.count==1,"Removing last Cutout releases full-resolution native visibility storage");
            foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial")))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
            Debug.Log("[HoGICutout] PASS initial / secondary light+sky / distant sky / final alpha+distance / real SDK reuse energy");
        }
        static void Relative(Color value,Color expected,string label)
        {
            bool pass=true;for(int i=0;i<3;i++)pass&=Mathf.Abs(value[i]-expected[i])<Mathf.Max(.001f,Mathf.Abs(expected[i])*.05f);
            Require(pass,label+" actual="+value+" expected="+expected);
        }
        static Color ReadRoi(RenderTexture source)
        {
            var old=RenderTexture.active;try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();}finally{RenderTexture.active=old;}
            Color sum=Color.clear;for(int y=16;y<48;y++)for(int x=16;x<48;x++)sum+=image.GetPixel(x,y);return sum/1024;
        }
        static void AuditFinalSegments(lilToon.URP.Extensions.RTGI.HoRTGIContext context,GameObject screen,Material material,Texture2D pattern)
        {
            var visibility=new Vector2[Size*Size];context.finalVisibility.GetData(visibility);
            var raw=Capture(context.diffuse.rt);var signal=Capture(context.nrd.ValidationSignal.rt);
            var mesh=screen.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var uv=mesh.uv;var indices=mesh.GetTriangles(0);
            var a=vertices[indices[0]];var b=vertices[indices[1]];var c=vertices[indices[2]];var e=b-a;var f=c-a;float determinant=e.x*f.y-e.y*f.x;
            var scale=material.GetTextureScale("_MainTex");var offset=material.GetTextureOffset("_MainTex");
            for(int y=16;y<48;y++)for(int x=16;x<48;x++)
            {
                int index=y*Size+x;float px=(x+.5f)/Size*2-1,py=(y+.5f)/Size*2-1;
                var point=screen.transform.InverseTransformPoint(new Vector3(px*.5f,py*.5f,-.5f))-a;
                float w1=(point.x*f.y-point.y*f.x)/determinant,w2=(e.x*point.y-e.y*point.x)/determinant;
                var texcoord=uv[indices[0]]*(1-w1-w2)+uv[indices[1]]*w1+uv[indices[2]]*w2;
                float u=texcoord.x*scale.x+offset.x;u-=Mathf.Floor(u);bool solid=pattern.GetPixel((int)Mathf.Floor(u*pattern.width)%pattern.width,0).a>=material.GetFloat("_Cutoff");
                if((visibility[index].x<.5f)!=solid)throw new Exception("Native final alpha disagrees with independent segment/mesh-UV/point-sampler x="+x+" y="+y);
                float distance=Mathf.Sqrt(px*px+py*py+1)*(solid?.5f:1);
                if(Mathf.Abs(visibility[index].y-distance)>.01f||Mathf.Abs(signal[index].w-distance)>.01f)throw new Exception("Final/NRD distance does not represent actual Cutout or selected emitter hit");
                if(solid){if(raw[index].x>.0001f)throw new Exception("Final blocked connection leaked red radiance");blockedSegments++;}
                else if(raw[index].x<.01f)throw new Exception("Final hole lost valid emitter connection");
                checkedSegments++;
            }
            Require(blockedSegments>100&&checkedSegments==1024,"Frozen actual wall sample exercises both visible and blocked reconnects, including NRD distance");
        }
        static Vector4[] Capture(RenderTexture source)
        {
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGICutoutReadback.compute");
            using var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Size*Size,16);using var cmd=new CommandBuffer{name="GI cutout coordinate-preserving readback"};
            int kernel=shader.FindKernel("Capture");cmd.SetComputeVectorParam(shader,"_CaptureSize",new Vector4(Size,Size,0,0));cmd.SetComputeTextureParam(shader,kernel,"_Source",source);cmd.SetComputeBufferParam(shader,kernel,"_Output",buffer);cmd.DispatchCompute(shader,kernel,Size/8,Size/8,1);Graphics.ExecuteCommandBuffer(cmd);var result=new Vector4[Size*Size];buffer.GetData(result);return result;
        }
        static void SaveCutoutLightingReport(Exception error)
        {
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-cutout-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(new CutoutLightingReport{pass=error==null,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),failure=error?.ToString(),frames=frames,skyBaseline=skyBaseline,skyHole=skyHole,skyBlocked=skyBlocked,lightBaseline=lightBaseline,lightHole=lightHole,lightBlocked=lightBlocked,halfRaw=halfRaw,halfFiltered=halfFiltered,checkedSegments=checkedSegments,blockedSegments=blockedSegments,checks=checks.ToArray()},true));
        }
        [Serializable]sealed class CutoutLightingReport{public bool pass,compatibility;public string colorSpace,failure;public int frames,checkedSegments,blockedSegments;public Color skyBaseline,skyHole,skyBlocked,lightBaseline,lightHole,lightBlocked,halfRaw,halfFiltered;public string[] checks;}
    }
}
