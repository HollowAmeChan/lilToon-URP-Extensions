#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.RTGI.NRD;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Real D3D12 production path: no replacement signal or denoiser kernel.
    public static class HoRTGINrdValidation
    {
        const int Size=64;
        static readonly List<UnityEngine.Object> owned=new();
        static readonly List<string> checks=new();
        static Camera camera;
        static RenderTexture target;
        static Texture2D image;
        static Material receiverMaterial;
        static GameObject receiver;
        static HoRTGIRendererFeature gi;
        static HoAtmosphereRendererFeature sky;
        static HoRTGIVolume volume;
        static RenderPipelineAsset oldDefault,oldQuality;
        static bool compatibility,oldAsync,finished;
        static int frames,warmup;
        static string failure,output;
        static double rawVariance,filteredVariance;
        static Color rawMean,filteredMean,hdrRaw,hdrFiltered;
        static T Keep<T>(T obj) where T:UnityEngine.Object{owned.Add(obj);return obj;}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated isolated GI project required");
                oldDefault=GraphicsSettings.defaultRenderPipeline;oldQuality=QualitySettings.renderPipeline;oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-nrd-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.fog=false;
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                sky=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());sky.Settings.source=HoAtmosphereSource.Constant;sky.Settings.constantColor=new Color(.5f,.25f,.125f,1);
                gi=Keep(ScriptableObject.CreateInstance<HoRTGIRendererFeature>());gi.Settings.temporal=gi.Settings.spatial=false;gi.Settings.secondaryLightSamples=1;gi.Settings.secondaryEnvironmentSamples=4;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(sky);renderer.rendererFeatures.Add(gi);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                camera=Keep(new GameObject("GI NRD validation camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.cullingMask=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                target=Keep(new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();image=Keep(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
                var holder=Keep(new GameObject("GI NRD Volume")).AddComponent<Volume>();holder.isGlobal=true;holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());volume=holder.sharedProfile.Add<HoRTGIVolume>();
                receiver=Quad("Receiver",0,2,0);receiverMaterial=receiver.GetComponent<Renderer>().sharedMaterial;
                var wall=Quad("World only bounce",-1,4000,1);wall.transform.rotation=Quaternion.Euler(0,180,0);
                Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        static GameObject Quad(string name,float z,float size,int layer)
        {
            var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.layer=layer;obj.transform.position=new Vector3(0,0,z);obj.transform.localScale=new Vector3(size,size,1);
            var mat=Keep(new Material(Shader.Find("lilToon")));mat.EnableKeyword("UNITY_UI_ALPHACLIP");mat.SetFloat("_Cull",0);mat.SetFloat("_AsUnlit",1);mat.SetColor("_Color",Color.white);mat.SetTexture("_MainTex",Texture2D.whiteTexture);
            mat.SetFloat("_Metallic",0);mat.SetFloat("_Reflectance",0);mat.SetFloat("_Smoothness",0);mat.SetFloat("_UseEmission",0);mat.SetFloat("_UseEmission2nd",0);obj.GetComponent<Renderer>().sharedMaterial=mat;return obj;
        }
        static void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=message;}
        static void Render()
        {
            if(failure!=null)throw new Exception("Render diagnostic: "+failure);
            if(gi.LastContext?.nrd!=null)gi.LastContext.nrd.ValidationTimeSeconds=frames/60.0;
            VolumeManager.instance.Update(camera.transform,camera.GetUniversalAdditionalCameraData().volumeLayerMask);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});frames++;
            if(gi.LastContext==null)throw new Exception("GI missing: "+gi.Status);
        }
        static Color[] Read(RenderTexture texture)
        {
            var previous=RenderTexture.active;
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();return image.GetPixels();}
            finally{RenderTexture.active=previous;}
        }
        static Color Mean(RenderTexture texture)
        {
            var pixels=Read(texture);var sum=Color.clear;
            for(int y=16;y<48;y++)for(int x=16;x<48;x++)
            {var c=pixels[y*Size+x];if(!float.IsFinite(c.r)||!float.IsFinite(c.g)||!float.IsFinite(c.b))throw new Exception("Nonfinite signal");sum+=c;}
            return sum/1024;
        }
        static void Require(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Debug.Log("[HoGINrd] CHECK "+name);}
        static void Near(Color actual,Color expected,string name,float relative=.08f,float absolute=.002f)
        {Require(Mathf.Abs(actual.r-expected.r)<=Mathf.Abs(expected.r)*relative+absolute&&Mathf.Abs(actual.g-expected.g)<=Mathf.Abs(expected.g)*relative+absolute&&Mathf.Abs(actual.b-expected.b)<=Mathf.Abs(expected.b)*relative+absolute,name+" actual="+actual+" expected="+expected);}
        static void Save(RenderTexture texture,string name){Read(texture);File.WriteAllBytes(Path.Combine(output,name+".exr"),image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
        static void Sample(int warm,int count,out Color raw,out Color filtered,out double rawVar,out double filteredVar)
        {
            for(int i=0;i<warm;i++)Render();
            double[] sums=new double[1024],squares=new double[1024],fsums=new double[1024],fsquares=new double[1024];raw=filtered=Color.clear;
            for(int i=0;i<count;i++)
            {
                Render();var a=Read(gi.LastContext.diffuse.rt);var b=Read(gi.LastContext.PublishedDiffuse.rt);int p=0;
                for(int y=16;y<48;y++)for(int x=16;x<48;x++,p++)
                {
                    Color c=a[y*Size+x],f=b[y*Size+x];if(!float.IsFinite(c.r)||!float.IsFinite(f.r)||c.a<.99f||f.a<.99f)throw new Exception("Invalid measured sample");
                    raw+=c;filtered+=f;sums[p]+=c.r;squares[p]+=c.r*c.r;fsums[p]+=f.r;fsquares[p]+=f.r*f.r;
                }
            }
            raw/=count*1024;filtered/=count*1024;rawVar=filteredVar=0;
            for(int p=0;p<1024;p++){rawVar+=squares[p]/count-Math.Pow(sums[p]/count,2);filteredVar+=fsquares[p]/count-Math.Pow(fsums[p]/count,2);}
            rawVar/=1024;filteredVar/=1024;
        }
        static void Tick()
        {
            if(warmup++<3){try{Render();}catch(Exception error){Finish(error);}return;}
            EditorApplication.update-=Tick;
            try
            {
                Require(!gi.Settings.denoise&&gi.LastContext.nrd==null,"Opt-in default allocates no GI denoiser");
                gi.Settings.denoise=true;Render();var c=gi.LastContext;Require(c.nrd!=null,"Original RELAX_DIFFUSE instance created: "+c.nrdStatus);
                Require(c.PublishedDiffuse==c.nrd.filtered&&c.PublishedDiffuse!=c.diffuse,"Filtered publication retains separate raw texture");
                Require(c.nrd.core.LastDispatchCount>5&&c.nrd.lastReset,"First dispatch resets independent GI history");
                Sample(48,64,out rawMean,out filteredMean,out rawVariance,out filteredVariance);
                Near(filteredMean,rawMean,"Stationary diffuse mean within 8% of same-frame raw");
                Require(rawVariance>1e-5&&filteredVariance<rawVariance*.15,"Measured per-pixel temporal variance reduced at least 85%: raw="+rawVariance+" filtered="+filteredVariance);
                Save(c.diffuse.rt,"raw");Save(c.PublishedDiffuse.rt,"filtered");
                var normal=Mean(c.nrd.normal.rt);var depth=Mean(c.nrd.viewZ.rt);var signal=Mean(c.nrd.signal.rt);
                Require(depth.r>2.9f&&depth.r<3.1f&&signal.a>1&&signal.a<20,"Real primary viewZ and finite bounce hit distance are supplied");
                Near(signal*HoRTGINrdContext.SignalScale,Mean(c.diffuse.rt),"Demodulated input is produced before primary albedo",.002f,.0001f);
                Render();Require(!c.nrd.lastReset,"Stable frames retain denoiser history");
                camera.transform.position+=new Vector3(.1f,0,0);Render();Require(!c.nrd.lastReset,"Static-world camera motion uses NRD matrix reprojection");Near(Mean(c.PublishedDiffuse.rt),filteredMean,"Camera move keeps stationary wall energy",.12f);
                sky.Settings.constantColor=Color.black;Render();Require(c.nrd.lastReset,"Sky change resets independent GI history immediately");Near(Mean(c.PublishedDiffuse.rt),Color.black,"Old bright history removed on first dark frame",0,.0001f);
                sky.Settings.constantColor=new Color(.5f,.25f,.125f,1);Render();Require(c.nrd.lastReset,"Restored sky resets history");
                receiverMaterial.SetColor("_Color",new Color(0,.5f,1,1));Sample(16,32,out var zeroRaw,out var zeroFiltered,out _,out _);
                Near(zeroFiltered,zeroRaw,"Zero primary albedo channel stays zero after remodulation",.1f);Require(Mathf.Abs(zeroFiltered.r)<.00001f&&Mean(c.nrd.signal.rt).r>0,"Zero albedo never destroys incoming red signal by division");
                receiverMaterial.SetColor("_Color",Color.white);sky.Settings.constantColor=new Color(1000,500,250,1);
                Sample(32,32,out hdrRaw,out hdrFiltered,out _,out _);Near(hdrFiltered,hdrRaw,"HDR 1000 sky retains mean and finite moments",.1f,.01f);
                sky.Settings.constantColor=new Color(.5f,.25f,.125f,1);Render();
                gi.Settings.composite=false;Render();var basis=Mean(target);gi.Settings.composite=true;gi.Settings.intensity=1;Render();
                var composed=Mean(target);var added=Mean(c.PublishedDiffuse.rt);
                if(QualitySettings.activeColorSpace==ColorSpace.Linear)Near(composed,basis+added,"Normal camera composites filtered GI",.01f);
                else Require(composed.r>basis.r,"Gamma normal camera receives filtered GI");
                receiver.transform.localScale=Vector3.one*.5f;Render();var pixels=Read(c.PublishedDiffuse.rt);Require(pixels[4*Size+4].a==0&&pixels[4*Size+4].r==0,"Invalid/background pixels stay unlit after resolve");receiver.transform.localScale=Vector3.one*2;
                gi.Settings.denoise=false;Render();Require(c.nrd==null&&c.PublishedDiffuse==c.diffuse,"Disable releases independent NRD and restores raw publication");
                volume.denoise.Override(true);Render();Require(c.nrd!=null&&c.nrd.lastReset&&!gi.Settings.denoise,"Volume enables fresh independent NRD without modifying shared defaults");
                volume.denoise.Override(false);Render();Require(c.nrd==null,"Volume disables independent NRD");volume.denoise.overrideState=false;
                gi.Settings.denoise=true;Render();
                var first=c.nrd;var other=Keep(new GameObject("Other GI NRD camera")).AddComponent<Camera>();other.CopyFrom(camera);other.enabled=false;other.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
                var original=camera;camera=other;Render();Require(gi.LastContext!=c&&gi.LastContext.nrd!=first&&gi.LastContext.nrd.core!=first.core,"Actual second camera owns independent native instance and textures");camera=original;Render();Require(gi.LastContext==c,"Original camera retains its own denoiser");
                var smaller=Keep(new RenderTexture(32,32,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));smaller.Create();target=smaller;Render();Require(c.nrd.lastReset&&c.nrd.signal.rt.width==32&&c.nrd.filtered.rt.width==32,"Resolution change reallocates all GI guides and resets history");
                foreach(var shader in AssetDatabase.FindAssets("t:ComputeShader",new[]{"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI"}).Select(x=>AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(x))))Require(ShaderUtil.GetComputeShaderMessages(shader).Length==0,"Compute has no diagnostics: "+shader.name);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;if(error!=null)Debug.LogException(error);
            if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(new Report{pass=error==null,failure=error?.ToString(),frames=frames,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),rawMean=rawMean,filteredMean=filteredMean,rawVariance=rawVariance,filteredVariance=filteredVariance,hdrRaw=hdrRaw,hdrFiltered=hdrFiltered,checks=checks.ToArray()},true));
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            Debug.Log("[HoGINrd] END pass="+(error==null));EditorApplication.Exit(error==null?0:1);
        }
        [Serializable] sealed class Report{public bool pass,compatibility;public string failure,colorSpace;public int frames;public Color rawMean,filteredMean,hdrRaw,hdrFiltered;public double rawVariance,filteredVariance;public string[] checks;}
    }
}
