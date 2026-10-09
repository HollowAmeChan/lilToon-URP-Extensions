#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static class HoRTDIValidation
    {
        private static readonly List<Object> objects=new();
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static bool compatibility,previousAsync,unsupported,forcePower,forceDenoise,forceBudget,forceQuality;
        private static Camera camera;
        private static RenderTexture target;
        private static HoRTDIRendererFeature feature;
        private static HoGeometryBufferRendererFeature gb;
        private static HoRTDIVolume volume;
        private static Light light;
        private static Material surfaceMaterial;
        private static GameObject blocker;
        private static int tick;
        [StructLayout(LayoutKind.Sequential)]
        private struct Reservoir {public uint light,uv,visibility,age;public float target,weight;}
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void RunPower(){forcePower=true;Run();}
        public static void RunPowerCompatibility(){forcePower=true;compatibility=true;Run();}
        public static void RunDenoise(){forceDenoise=true;Run();}
        public static void RunDenoiseCompatibility(){forceDenoise=true;compatibility=true;Run();}
        public static void RunBudget(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceBudget=forceDenoise=true;Run();}
        public static void RunBudgetCompatibility(){compatibility=true;RunBudget();}
        public static void RunQuality(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceQuality=forceDenoise=true;Run();}
        public static void RunQualityCompatibility(){compatibility=true;RunQuality();}
        public static void RunUnsupported(){unsupported=true;Run();}
        public static void Run()
        {
            try
            {
                Debug.Log("[HoRTDIValidation] START "+SystemInfo.graphicsDeviceType);
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;
                previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                feature=Keep(ScriptableObject.CreateInstance<HoRTDIRendererFeature>());feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.initialSamples=1;
                if(forcePower)feature.Settings.localSampling=HoRTDILocalSampling.Power;
                feature.Settings.denoise=forceDenoise;
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(feature);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #else
                Require(!compatibility,"Compatibility symbol missing");
                #endif
                var holder=Keep(new GameObject("RTDI validation volume")).AddComponent<Volume>();holder.isGlobal=true;
                holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());volume=holder.sharedProfile.Add<HoRTDIVolume>(true);
                camera=Keep(new GameObject("RTDI validation camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.aspect=1;camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                target=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                var surface=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));surface.transform.localScale=new Vector3(2,2,1);
                var material=Mat("lilToon");material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);
                surfaceMaterial=material;
                material.SetFloat("_Reflectance",0);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.5f);surface.GetComponent<Renderer>().sharedMaterial=material;
                light=Point(new Vector3(1.5f,0,-1),Color.white,4);
                blocker=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));blocker.transform.position=new Vector3(.75f,0,-.5f);blocker.transform.localScale=new Vector3(.4f,.4f,.2f);
                blocker.GetComponent<Renderer>().sharedMaterial=Mat("Universal Render Pipeline/Unlit");blocker.SetActive(false);
                EditorApplication.update+=Tick;
            }
            catch(Exception error){Finish(error);}
        }
        private static void Tick()
        {
            try
            {
                if(unsupported)
                {
                    RenderCamera();Require(feature.LastContext==null&&Shader.GetGlobalFloat("_HoRTDIActive")==0,"unsupported platform allocated/published DI");
                    Debug.Log("[HoRTDIValidation] PASS unsupported platform: "+feature.Status);Finish(null);return;
                }
                if(++tick<3){Render();return;}
                if(forceBudget){ValidateBudget();Finish(null);return;}
                if(forceQuality){ValidateQuality();Finish(null);return;}
                if(forceDenoise){ValidateDenoiser();Finish(null);return;}
                var single=Render();float expected=Expected(light);Near(single.r,expected,"single-light reference",.015f);Near(single.a,1,"receiver valid",.01f);
                var baseline=Read(target);feature.SetActive(false);RenderCamera();NearRGB(Read(target),baseline,"no automatic camera composite");feature.SetActive(true);
                blocker.SetActive(true);var shadow=Render();Near(shadow.r,0,"world visibility",.002f);Near(shadow.a,1,"shadow is still a valid receiver",.01f);
                blocker.SetActive(false);Near(Render().r,expected,"occluder removed",.015f);
                light.shadows=LightShadows.None;blocker.SetActive(true);Near(Render().r,0,"RT visibility independent of raster shadow switch",.002f);blocker.SetActive(false);light.shadows=LightShadows.Hard;
                surfaceMaterial.SetFloat("_Metallic",1);Near(Render().r,0,"metal has no diffuse response",.002f);Require(Read(feature.LastContext.specular.rt).r>0,"reference GGX specular output missing");surfaceMaterial.SetFloat("_Metallic",0);
                light.enabled=false;var directional=Keep(new GameObject("RTDI directional")).AddComponent<Light>();directional.type=LightType.Directional;directional.intensity=.5f;
                Near(Render().r,.5f/Mathf.PI,"directional delta reference",.012f);directional.enabled=false;
                var spot=Point(new Vector3(0,0,-2),Color.white,4);spot.type=LightType.Spot;spot.spotAngle=60;spot.innerSpotAngle=30;
                Near(Render().r,Expected(spot),"spot on-axis",.012f);spot.transform.rotation=Quaternion.Euler(0,90,0);Near(Render().r,0,"spot outside cone",.002f);spot.enabled=false;
                surfaceMaterial.SetFloat("_UseEmission",1);surfaceMaterial.SetColor("_EmissionColor",new Color(10,4,2,1));Near(Render().r,0,"emission is not direct lighting",.002f);surfaceMaterial.SetFloat("_UseEmission",0);light.enabled=true;
                light.intensity=0;Near(Render().r,0,"zero intensity",.002f);light.intensity=4;
                var red=Point(new Vector3(-1.5f,0,-1),Color.red,3);var blue=Point(new Vector3(0,1.5f,-1),Color.blue,3);
                feature.Settings.initialSamples=8;Color average=Color.clear;
                for(int i=0;i<32;i++){Render();average+=ReadMean(feature.LastContext.diffuse.rt);}average/=32;
                Color total=Color.clear;
                for(int y=24;y<40;y++)for(int x=24;x<40;x++)total+=new Color(Expected(light,x,y)+Expected(red,x,y),Expected(light,x,y),Expected(light,x,y)+Expected(blue,x,y),1);
                total/=256;Debug.Log("[HoRTDIValidation] multi-light mean="+average+" reference="+total);
                NearRGB(average,total,"multi-light SDK initial sampling",.035f);
                feature.Settings.temporal=true;feature.Settings.spatial=true;
                for(int i=0;i<6;i++)Render();
                var data=new Reservoir[feature.LastContext.reservoirs.count];feature.LastContext.reservoirs.GetData(data);
                int address=(32/16)*feature.LastContext.rowPitch+(32/16)*256+(32%16)*16+32%16;
                Require((data[address].visibility>>18)>1,"reference temporal/spatial did not accumulate samples");
                red.enabled=blue.enabled=false;for(int i=0;i<3;i++)Render();
                blocker.SetActive(true);Near(Render().r,0,"geometry change rejects temporal history",.003f);blocker.SetActive(false);
                feature.Settings.spatial=false;Render();light.intensity=2;Near(RenderMean().r,ExpectedMean(light),"light intensity retargets history mean",.015f);
                Require(feature.LastContext.lastTemporal,"Light intensity change cleared all history");
                ValidateDynamicLights();
                ValidateLightRegions();
                ValidatePowerSpread();
                volume.debugMode.value=HoRTDIDebugMode.Combined;var debug=Render();NearRGB(Read(target),debug,"debug presentation",.01f);volume.debugMode.value=HoRTDIDebugMode.Off;
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;Near(Render().a,0,"domain mismatch receiver invalid",.01f);
                volume.debugMode.value=HoRTDIDebugMode.Receiver;Render();NearRGB(Read(target),Color.red,"invalid receiver debug",.01f);
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;Render();NearRGB(Read(target),Color.green,"valid receiver debug",.01f);volume.debugMode.value=HoRTDIDebugMode.Off;
                volume.enable.Override(false);RenderCamera();Require(Shader.GetGlobalFloat("_HoRTDIActive")==0,"volume disabled reset");volume.enable.Override(true);Render();
                camera.transform.position+=new Vector3(.1f,0,-.5f);Render();Require(feature.LastContext.lastTemporal,"static-world camera movement should use reference reprojection");
                Color moved=ReadMean(feature.LastContext.diffuse.rt);float movedReference=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++)movedReference+=Expected(light,x,y);movedReference/=256;
                Near(moved.r,movedReference,"camera reprojection mean",.025f);
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDI.compute");
                foreach(var message in ShaderUtil.GetComputeShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
                feature.Dispose();Require(Shader.GetGlobalFloat("_HoRTDIActive")==0,"dispose reset");
                Debug.Log("[HoRTDIValidation] PASS reference RTXDI initial/temporal/spatial/shade, local/infinite regions, dynamic light snapshots/remapping/retargeting, analytic energy, world visibility, multi-light mean, history/reset, independent output/debug; API="+SystemInfo.graphicsDeviceType+"; compatibility="+compatibility);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static void ValidateDynamicLights()
        {
            // Actual temporal reuse: spatial is off, while the same light moves every frame.
            for(int i=0;i<8;i++)
            {
                light.transform.position=new Vector3(-1.2f+i*.1f,.1f,-1.5f);
                Near(RenderMean().r,ExpectedMean(light),"moving single-light energy mean",.015f);
                Require(feature.LastContext.lastTemporal,"Moving light cleared history");
            }
            var reservoirs=new Reservoir[feature.LastContext.reservoirs.count];feature.LastContext.reservoirs.GetData(reservoirs);int aged=0;
            for(int y=24;y<40;y++)for(int x=24;x<40;x++)
            {
                // Inspect temporal output (slot 2): final visibility refresh
                // resets SDK visibility age to zero in the history slot.
                int pointer=2*feature.LastContext.arrayPitch+(y/16)*feature.LastContext.rowPitch+(x/16)*256+(y%16)*16+x%16;
                if((reservoirs[pointer].visibility>>18)>1&&((reservoirs[pointer].age>>16)&255)>0)aged++;
            }
            Require(aged>128,"Moving-light temporal stage did not retain actual samples");
            var oldPosition=light.transform.position;
            light.color=new Color(.2f,.7f,.4f,1);NearRGB(RenderMean(),LightColor(light)*ExpectedMean(light),"light color retarget mean",.02f);
            Require(feature.LastContext.lastTemporal,"Light color change cleared history");
            var previousData=new HoRTDIRendererFeature.AnalyticLight[feature.LastContext.previousLights.count];feature.LastContext.previousLights.GetData(previousData);
            Near(previousData[0].radianceType.x,2,"old light radiance was overwritten",.001f);
            Near(previousData[0].positionRange.x,oldPosition.x,"old light position was overwritten",.001f);
            light.color=Color.white;light.type=LightType.Directional;light.intensity=.5f;light.transform.rotation=Quaternion.identity;
            Near(RenderMean().r,.5f/Mathf.PI,"light type retarget mean",.015f);Require(feature.LastContext.lastTemporal,"Delta light type change cleared history");
            light.type=LightType.Point;light.intensity=2;light.transform.position=new Vector3(-1.5f,0,-1);
            blocker.SetActive(true);Near(Render().r,Expected(light),"static occluder visible lamp",.015f);
            light.transform.position=new Vector3(1.5f,0,-1);Near(Render().r,0,"moving light enters world shadow",.002f);
            Require(feature.LastContext.lastTemporal,"Moving shadowed light cleared history");blocker.SetActive(false);Render();

            var oldKeys=feature.LastContext.previousLightKeys;
            var added=Point(new Vector3(-1.5f,0,-1),Color.red,3);
            Render();Require(feature.LastContext.lastTemporal,"Light insertion / buffer growth cleared history");CheckLightMappings(oldKeys);
            Color average=Color.clear;
            for(int i=0;i<48;i++){Render();average+=ReadMean(feature.LastContext.diffuse.rt);}average/=48;
            Color expected=Color.clear;for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                expected+=Color.white*Expected(light,x,y)+Color.red*Expected(added,x,y);
            expected/=256;NearRGB(average,expected,"inserted light temporal statistical energy",.035f);
            oldKeys=feature.LastContext.previousLightKeys;added.enabled=false;Render();CheckLightMappings(oldKeys);
            Require(feature.LastContext.lastTemporal,"Light removal cleared all history");
            // Replacement with the same light count must never treat slot 0 as the old identity.
            oldKeys=feature.LastContext.previousLightKeys;Object.DestroyImmediate(light.gameObject);
            light=Point(new Vector3(.4f,0,-2),Color.green,2);NearRGB(Render(),Color.green*Expected(light),"replacement has no old-light ghost",.02f);
            CheckLightMappings(oldKeys);Require(feature.LastContext.lastTemporal,"Replacement unnecessarily cleared surface history");
            oldKeys=feature.LastContext.previousLightKeys;light.enabled=false;NearRGB(Render(),Color.black,"empty light set",.002f);CheckLightMappings(oldKeys);
            oldKeys=feature.LastContext.previousLightKeys;light.enabled=true;NearRGB(Render(),Color.green*Expected(light),"empty to nonempty lights",.02f);CheckLightMappings(oldKeys);
            light.color=Color.white;light.transform.position=new Vector3(1.5f,0,-1);Render();
        }
        private static void ValidateQuality()
        {
            feature.Settings.temporal=true;feature.Settings.spatial=true;feature.Settings.initialSamples=8;
            for(int i=0;i<40;i++)RenderCamera();var context=feature.LastContext;Require(context.nrd!=null,"Quality NRD unavailable");
            light.color=Color.red;
            for(int frame=0;frame<16;frame++)
            {
                RenderCamera();var recolored=ReadCenter(context.filteredDiffuse.rt);
                if(frame==0)Require(Mathf.Max(recolored.g,recolored.b)<.15f,"NRD retained too much old light color");
                if(frame==15)Require(Mathf.Max(recolored.g,recolored.b)<.004f,"NRD old light color did not settle");
                if(frame==0||frame==3||frame==7||frame==15)Debug.Log($"[HoRTDIValidation] QUALITY recolor frame={frame} raw={ReadCenter(context.diffuse.rt)} filtered={recolored}");
            }
            light.enabled=false;
            for(int frame=0;frame<8;frame++)
            {
                RenderCamera();var raw=ReadCenter(context.diffuse.rt);var filtered=ReadCenter(context.filteredDiffuse.rt);Near(raw.r,0,"switched-off raw light",.002f);
                if(frame==0||frame==3||frame==7)Debug.Log($"[HoRTDIValidation] QUALITY off frame={frame} filtered={filtered.r:F6}");
                if(frame==3)Require(filtered.r<.002f,"NRD switched-off light ghost persisted");
            }
            light.enabled=true;light.color=Color.white;for(int i=0;i<40;i++)RenderCamera();
            double worst=0,mean=0;
            for(int frame=0;frame<24;frame++)
            {
                light.transform.position=new Vector3(1.5f-frame*.04f,0,-1);RenderCamera();var raw=ReadCenter(context.diffuse.rt);var filtered=ReadCenter(context.filteredDiffuse.rt);
                double relative=Math.Abs(filtered.r-Expected(light))/Expected(light);worst=Math.Max(worst,relative);mean+=relative;
                if(frame==0||frame==11||frame==23)Debug.Log($"[HoRTDIValidation] QUALITY moving frame={frame} raw={raw.r:F6} filtered={filtered.r:F6} expected={Expected(light):F6}");
            }
            Debug.Log($"[HoRTDIValidation] QUALITY moving relative mean={mean/24:F6} worst={worst:F6}");
            Require(mean/24<.21,"NRD moving-light response exceeded measured relative error budget");
            Debug.Log($"[HoRTDIValidation] QUALITY moving finalROI raw={ReadMean(context.diffuse.rt).r:F6} filtered={ReadMean(context.filteredDiffuse.rt).r:F6} expected={ExpectedMean(light):F6}");
            for(int i=0;i<40;i++)RenderCamera();
            Debug.Log($"[HoRTDIValidation] QUALITY stopped finalROI raw={ReadMean(context.diffuse.rt).r:F6} filtered={ReadMean(context.filteredDiffuse.rt).r:F6} expected={ExpectedMean(light):F6}");
            target.Release();Object.DestroyImmediate(target);target=Keep(new RenderTexture(256,256,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            // Isolate deterministic raw visibility from stochastic ReSTIR reuse.
            feature.Settings.temporal=false;feature.Settings.spatial=false;
            camera.cullingMask=1;blocker.layer=1;blocker.transform.position=new Vector3(.25f,0,-.5f);blocker.transform.localScale=new Vector3(1,4,.1f);blocker.SetActive(true);
            light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.identity;
            for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
            var rawImage=Keep(new Texture2D(256,256,TextureFormat.RGBAFloat,false,true));var filteredImage=Keep(new Texture2D(256,256,TextureFormat.RGBAFloat,false,true));
            ReadImage(context.diffuse.rt,rawImage);ReadImage(context.filteredDiffuse.rt,filteredImage);
            double leak=0,litError=0;int shadowCount=0,litCount=0;float maximum=0;
            for(int y=64;y<192;y++)for(int x=0;x<256;x++)
            {
                var a=rawImage.GetPixel(x,y);var b=filteredImage.GetPixel(x,y);Require(float.IsFinite(b.r),"Non-finite filtered visibility");
                if(x>=104&&x<216){Near(a.r,0,"analytic world shadow",.002f);leak+=b.r;maximum=Mathf.Max(maximum,b.r);shadowCount++;}
                if(x>=16&&x<88){Near(a.r,1/Mathf.PI,"analytic lit side",.003f);litError+=Math.Abs(b.r-a.r);litCount++;}
            }
            Debug.Log($"[HoRTDIValidation] QUALITY shadow interior8px mean={leak/shadowCount:F6} max={maximum:F6} litError={litError/litCount:F6}");
            Require(leak/shadowCount<.0005&&maximum<.003,"NRD blurred deterministic world shadow into visible light");
            Require(litError/litCount<.003,"NRD attenuated the deterministic lit side");
            var preview=Keep(new Texture2D(512,256,TextureFormat.RGB24,false,true));
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float a=Mathf.LinearToGammaSpace(Mathf.Clamp01(rawImage.GetPixel(x,y).r*Mathf.PI));float b=Mathf.LinearToGammaSpace(Mathf.Clamp01(filteredImage.GetPixel(x,y).r*Mathf.PI));
                preview.SetPixel(x,y,new Color(a,a,a));preview.SetPixel(x+256,y,new Color(b,b,b));
            }
            preview.Apply();string root=System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root,compatibility?"nrd-shadow-quality-compat.png":"nrd-shadow-quality-rg.png"),preview.EncodeToPNG());
            light.transform.rotation=Quaternion.Euler(0,30,0);
            for(int frame=0;frame<16;frame++)
            {
                RenderCamera();ReadImage(context.diffuse.rt,rawImage);ReadImage(context.filteredDiffuse.rt,filteredImage);
                if(frame==0||frame==3||frame==7||frame==15)
                {
                    double shadow=0,lit=0;for(int x=232;x<248;x++){Near(rawImage.GetPixel(x,128).r,0,"moving sun shadow truth",.002f);shadow+=filteredImage.GetPixel(x,128).r;}
                    for(int x=100;x<116;x++)lit+=filteredImage.GetPixel(x,128).r;
                    Debug.Log($"[HoRTDIValidation] QUALITY movingSun frame={frame} newShadow={shadow/16:F6} newlyLit={lit/16:F6}");
                    Require(shadow/16<.003,"NRD newly shadowed region kept old illumination");
                }
            }
            Debug.Log("[HoRTDIValidation] PASS quality response gates: recolor/off/moving-light and offscreen-world visibility; compatibility="+compatibility);
        }
        private static void ReadImage(RenderTexture source,Texture2D image)
        {
            var previous=RenderTexture.active;
            try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();}
            finally{RenderTexture.active=previous;}
        }
        private static void ValidateBudget()
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                target.Release();Object.DestroyImmediate(target);target=Keep(new RenderTexture(size.x,size.y,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                for(int i=0;i<12;i++)RenderCamera();var context=feature.LastContext;
                Require(context!=null&&context.nrd!=null,"Target-size NRD unavailable: "+feature.Status);
                Require(context.width==size.x&&context.height==size.y,"Target dimensions were not used");
                ReadCenter(context.filteredDiffuse.rt); // Finish warm-up work before starting the timed batch.
                const int samples=8;var timer=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<samples;i++)RenderCamera();var filtered=ReadCenter(context.filteredDiffuse.rt);timer.Stop();
                var raw=ReadCenter(context.diffuse.rt);var ray=camera.ViewportPointToRay(new Vector3((size.x/2+.5f)/size.x,(size.y/2+.5f)/size.y,0));
                var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=light.transform.position-p;float d2=delta.sqrMagnitude;
                float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(light.range*light.range),2));float reference=light.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
                Near(raw.r,reference,"target-size raw energy",.015f);Near(filtered.r,reference,"target-size filtered energy",.025f);Near(filtered.a,1,"target-size receiver alpha",.01f);
                Require(float.IsFinite(filtered.r)&&context.nrd.LastDispatchCount>0,"Target-size NRD output invalid");
                long bytes=OwnedPayload(context);
                Debug.Log($"[HoRTDIValidation] BUDGET {size.x}x{size.y} ownedPayloadMiB={bytes/1048576.0:F3} synchronizedBatchMsPerRender={timer.Elapsed.TotalMilliseconds/samples:F3} raw={raw.r:F6} filtered={filtered.r:F6} dispatches={context.nrd.LastDispatchCount} compatibility={compatibility}; one quad/one lamp/local candidate=1/no reuse; excludes input/AS/driver/cache memory; time includes CPU and final GPU readback, not isolated GPU timing");
            }
            Debug.Log("[HoRTDIValidation] PASS target-size DI/NRD allocation, resize/reset, actual energy and synchronized batch");
        }
        private static long OwnedPayload(HoRTDIContext context)
        {
            long result=0;
            foreach(var buffer in new[]{context.reservoirs,context.history,context.lights,context.neighbors,context.previousLights,context.currentToPrevious,context.previousToCurrent,context.ris})if(buffer!=null)result+=(long)buffer.count*buffer.stride;
            foreach(var texture in new[]{context.diffuse,context.specular,context.nrdDiffuse,context.nrdSpecular,context.nrdNormal,context.nrdViewZ,context.nrdOutputDiffuse,context.nrdOutputSpecular,context.filteredDiffuse,context.filteredSpecular})result+=Payload(texture);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(string name in new[]{"permanent","transient"})foreach(var texture in (RTHandle[])context.nrd.GetType().GetField(name,flags).GetValue(context.nrd))result+=Payload(texture);
            foreach(var buffer in (GraphicsBuffer[])context.nrd.GetType().GetField("constants",flags).GetValue(context.nrd))result+=(long)buffer.count*buffer.stride;
            return result;
        }
        private static long Payload(RTHandle texture)=>texture?.rt==null?0:(long)texture.rt.width*texture.rt.height*UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(texture.rt.graphicsFormat);
        private static Color ReadCenter(RenderTexture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=source;image.ReadPixels(new Rect(source.width/2,source.height/2,1,1),0,0);image.Apply();return image.GetPixel(0,0);}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static void ValidateDenoiser()
        {
            for(int i=0;i<8;i++)Render();var context=feature.LastContext;
            Require(context.nrd!=null,"NRD unavailable: "+feature.Status);Require(context.nrd.LastDispatchCount>0,"No native NRD dispatches recorded");
            Debug.Log("[HoRTDIValidation] NRD single raw="+ReadMean(context.diffuse.rt)+" filtered="+ReadMean(context.filteredDiffuse.rt)+" dispatches="+context.nrd.LastDispatchCount);
            Near(ReadMean(context.filteredDiffuse.rt).r,ExpectedMean(light),"NRD single-light energy",.03f);
            var signal=Read(context.nrdDiffuse.rt);Require(signal.a>1.5f&&signal.a<2.1f,"NRD alpha is not selected-light distance");
            Near(Read(context.nrdViewZ.rt).r,3,"NRD positive linear view Z",.01f);
            light.color=Color.red;var blue=Point(light.transform.position,Color.blue,light.intensity);
            for(int i=0;i<40;i++)Render();double rawError=0,filteredError=0,sumRed=0,sumBlue=0;int count=0;
            var raw=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);var filtered=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            try
            {
                for(int frame=0;frame<32;frame++)
                {
                    Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=context.diffuse.rt;raw.ReadPixels(new Rect(0,0,64,64),0,0);raw.Apply();RenderTexture.active=context.filteredDiffuse.rt;filtered.ReadPixels(new Rect(0,0,64,64),0,0);filtered.Apply();}
                    finally{RenderTexture.active=previous;}
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {
                        float reference=Expected(light,x,y);var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);
                        Require(float.IsFinite(b.r)&&float.IsFinite(b.b),"Non-finite NRD output");rawError+=(a.r-reference)*(a.r-reference);filteredError+=(b.r-reference)*(b.r-reference);
                        sumRed+=b.r;sumBlue+=b.b;count++;
                    }
                }
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(filtered);}
            Debug.Log($"[HoRTDIValidation] NRD noise rawMSE={rawError/count:F6} filteredMSE={filteredError/count:F6} red={sumRed/count:F6} blue={sumBlue/count:F6}");
            Require(filteredError<rawError*.3,"RELAX did not substantially reduce colored-light noise");
            Near((float)(sumRed/count),ExpectedMean(light),"NRD red energy",.04f);Near((float)(sumBlue/count),ExpectedMean(blue),"NRD blue energy",.04f);
            blue.enabled=false;light.color=Color.white;light.transform.position=new Vector3(0,0,-2);light.intensity=1;surfaceMaterial.SetFloat("_Metallic",1);
            for(int i=0;i<40;i++)Render();float rawSpecular=ReadMean(context.specular.rt).r,filteredSpecular=ReadMean(context.filteredSpecular.rt).r;
            Debug.Log($"[HoRTDIValidation] NRD specular raw={rawSpecular:F6} filtered={filteredSpecular:F6}");
            Require(filteredSpecular>0,"NRD specular channel missing");Near(filteredSpecular,rawSpecular,"NRD specular energy",rawSpecular*.15f+.005f);
            Near(ReadMean(context.filteredDiffuse.rt).r,0,"NRD metallic diffuse",.005f);surfaceMaterial.SetFloat("_Metallic",0);
            camera.transform.position+=new Vector3(.1f,0,-.5f);for(int i=0;i<8;i++)Render();Near(ReadMean(context.filteredDiffuse.rt).r,ExpectedMean(light),"NRD static-world camera movement",.05f);
            volume.debugMode.value=HoRTDIDebugMode.Diffuse;Render();NearRGB(Read(target),Read(context.filteredDiffuse.rt),"NRD debug publishes filtered result",.015f);volume.debugMode.value=HoRTDIDebugMode.Off;
            volume.debugMode.value=HoRTDIDebugMode.RawDiffuse;Render();NearRGB(Read(target),Read(context.diffuse.rt),"NRD raw debug",.015f);volume.debugMode.value=HoRTDIDebugMode.Off;
            gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;Near(Render().a,0,"NRD raw receiver invalid");Near(Read(context.filteredDiffuse.rt).a,0,"NRD filtered receiver invalid");gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;Render();
            feature.Settings.denoise=false;Render();Require(context.nrd==null&&context.filteredDiffuse==null,"NRD disable retained resources");
            feature.Settings.denoise=true;Render();Require(context.nrd!=null&&context.nrd.LastDispatchCount>0,"NRD reenable failed");
            camera.orthographic=false;camera.fieldOfView=40;for(int i=0;i<12;i++)Render();Near(ReadMean(context.filteredDiffuse.rt).r,ExpectedMean(light),"NRD perspective energy",.04f);
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDI.compute");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(compute))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error&&message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,message.message);
            Debug.Log("[HoRTDIValidation] PASS real NRD core/Unity RELAX dispatch, frontend distance/viewZ, energy/noise, camera motion, output/debug/lifecycle; compatibility="+compatibility);
        }
        private static Color LightColor(Light source)=>QualitySettings.activeColorSpace==ColorSpace.Linear?source.color.linear:source.color;
        private static void ValidateLightRegions()
        {
            feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.initialSamples=1;light.enabled=false;
            var sun=Keep(new GameObject("Independent infinite-light region")).AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.8f;
            // Local proposals are all out of range. A shared uniform pool would
            // produce either 0 or 65x sunlight; a separate infinite candidate
            // produces the correct sun integral on the very first frame.
            var irrelevant=new List<Light>();
            for(int i=0;i<64;i++){var item=Point(new Vector3(100+i*3,0,0),Color.white,1);item.range=.1f;irrelevant.Add(item);}
            Near(Render().r,.8f/Mathf.PI,"sun survives 64 irrelevant local lights at one local candidate",.004f);
            light.enabled=true;Color average=Color.clear;
            for(int i=0;i<64;i++){Render();average+=ReadMean(feature.LastContext.diffuse.rt);}average/=64;
            float mixedReference=.8f/Mathf.PI+ExpectedMean(light);
            Debug.Log("[HoRTDIValidation] local/infinite mixed mean="+average.r+" reference="+mixedReference);
            NearRGB(average,Color.white*mixedReference,"local/infinite SDK combination energy",.035f);
            var oldKeys=feature.LastContext.previousLightKeys;int oldSun=ReferenceIndex(oldKeys,sun);
            feature.Settings.temporal=true;sun.type=LightType.Point;sun.transform.position=new Vector3(0,0,-2);sun.range=100;
            Render();CheckLightMappings(oldKeys);Require(feature.LastContext.lastTemporal,"Changing light region cleared history");
            var previous=new HoRTDIRendererFeature.AnalyticLight[feature.LastContext.previousLights.count];feature.LastContext.previousLights.GetData(previous);
            Near(previous[oldSun].radianceType.w,2,"previous infinite-light type was overwritten",.001f);
            sun.enabled=false;foreach(var item in irrelevant)item.enabled=false;
            feature.Settings.initialSamples=8;Render();
        }
        private static Color RenderMean(){Render();return ReadMean(feature.LastContext.diffuse.rt);}
        private static void ValidatePowerSpread()
        {
            var previousMode=feature.Settings.localSampling;feature.Settings.localSampling=HoRTDILocalSampling.Uniform;
            feature.Settings.temporal=false;feature.Settings.spatial=false;feature.Settings.initialSamples=1;
            var position=light.transform.position;float intensity=light.intensity;
            light.transform.position=new Vector3(0,0,-2);light.intensity=1;
            var weak=new List<Light>();for(int i=0;i<63;i++)weak.Add(Point(light.transform.position,Color.white,.001f));
            try
            {
                var uniform=MeasureNormalizedNoise(128,1.063f);
                Debug.Log($"[HoRTDIValidation] power-spread uniform mean={uniform.mean:F6} variance={uniform.variance:F6} wallMs={uniform.ms:F1}");
                Near((float)uniform.mean,1,"uniform power-spread statistical energy",.13f);
                Require(uniform.variance>10,"Power-spread benchmark did not expose uniform variance");
                feature.Settings.localSampling=HoRTDILocalSampling.Power;
                var power=MeasureNormalizedNoise(128,1.063f);
                Require(feature.LastContext.powerSampling,"Power mode unexpectedly fell back");
                Debug.Log($"[HoRTDIValidation] power-spread SDK power mean={power.mean:F6} variance={power.variance:F6} wallMs={power.ms:F1}");
                Near((float)power.mean,1,"power sampling statistical energy",.02f);
                Require(power.variance<uniform.variance*.01,"SDK power sampling did not materially reduce variance");
                Require(feature.LastContext.powerPdf.rt.mipmapCount==1+(int)Mathf.Log(feature.LastContext.pdfSize,2),"PDF mip chain incomplete");
                light.intensity=0;foreach(var item in weak)item.intensity=0;
                NearRGB(Render(),Color.black,"all-black PDF has no stale samples",.002f);
                // Swap which identity carries the power, preserving the light
                // set and temporal history; PDF/RIS must use current values.
                light.intensity=.001f;foreach(var item in weak)item.intensity=.001f;weak[0].intensity=1;
                feature.Settings.temporal=true;Render();Require(feature.LastContext.lastTemporal,"Power change cleared history");
                var swapped=MeasureNormalizedNoise(32,1.063f/.001f);
                Near((float)swapped.mean,1,"power changes update PDF/RIS energy",.02f);
                Require(swapped.variance<.01,"Power proposal did not follow the new bright light");
            }
            finally
            {
                foreach(var item in weak)item.enabled=false;light.transform.position=position;light.intensity=intensity;
                feature.Settings.localSampling=previousMode;
                feature.Settings.initialSamples=8;feature.Settings.temporal=true;Render();
                if(previousMode==HoRTDILocalSampling.Uniform)
                    Require(feature.LastContext.powerPdf==null&&feature.LastContext.ris.count==1,"Uniform mode retained power resources");
            }
        }
        private static (double mean,double variance,double ms) MeasureNormalizedNoise(int frames,float totalIntensity)
        {
            double sum=0,sumSquares=0;int count=0;var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for(int frame=0;frame<frames;frame++)
                {
                    Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=feature.LastContext.diffuse.rt;image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();}
                    finally{RenderTexture.active=previous;}
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {
                        double value=image.GetPixel(x,y).r/(Expected(light,x,y)*totalIntensity);
                        Require(!double.IsNaN(value)&&!double.IsInfinity(value),"Non-finite sampling estimate");sum+=value;sumSquares+=value*value;count++;
                    }
                }
                double mean=sum/count;return(mean,Math.Max(0,sumSquares/count-mean*mean),watch.Elapsed.TotalMilliseconds);
            }
            finally{Object.DestroyImmediate(image);}
        }
        private static float ExpectedMean(Light source)
        {float sum=0;for(int y=24;y<40;y++)for(int x=24;x<40;x++)sum+=Expected(source,x,y);return sum/256;}
        private static void CheckLightMappings(Light[] oldKeys)
        {
            var context=feature.LastContext;var current=context.previousLightKeys;
            var forward=new int[context.currentToPrevious.count];var reverse=new int[context.previousToCurrent.count];
            context.currentToPrevious.GetData(forward);context.previousToCurrent.GetData(reverse);
            for(int i=0;i<current.Length;i++)Require(forward[i]==ReferenceIndex(oldKeys,current[i]),"GPU current-to-previous mapping mismatch");
            for(int i=0;i<oldKeys.Length;i++)Require(reverse[i]==ReferenceIndex(current,oldKeys[i]),"GPU previous-to-current mapping mismatch");
        }
        private static int ReferenceIndex(Light[] keys,Light key)
        {for(int i=0;i<keys.Length;i++)if(ReferenceEquals(keys[i],key))return i;return -1;}
        private static Light Point(Vector3 position,Color color,float intensity)
        {var result=Keep(new GameObject("RTDI point light")).AddComponent<Light>();result.type=LightType.Point;result.transform.position=position;result.color=color;result.intensity=intensity;result.range=100;result.shadows=LightShadows.Hard;return result;}
        private static float Expected(Light source,int x=32,int y=32)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var position=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);var delta=source.transform.position-position;float d2=delta.sqrMagnitude;
            float fade=Mathf.Clamp01(1-Mathf.Pow(d2/(source.range*source.range),2));return source.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
        }
        private static void RenderCamera(){VolumeManager.instance.Update(camera.transform,-1);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
        private static Color Render(){RenderCamera();Require(feature.LastContext!=null,"DI context missing: "+feature.Status);return Read(feature.LastContext.diffuse.rt);}
        private static Color Read(RenderTexture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();return image.GetPixel(source.width/2,source.height/2);}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static Color ReadMean(RenderTexture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();Color result=Color.clear;for(int y=24;y<40;y++)for(int x=24;x<40;x++)result+=image.GetPixel(x,y);return result/256;}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static Material Mat(string name){var shader=Shader.Find(name);Require(shader!=null,"Missing shader "+name);return Keep(new Material(shader));}
        private static T Keep<T>(T value)where T:Object{objects.Add(value);return value;}
        private static void Near(float actual,float expected,string label,float tolerance=.02f)=>Require(Mathf.Abs(actual-expected)<tolerance,label+": expected "+expected+", got "+actual);
        private static void NearRGB(Color actual,Color expected,string label,float tolerance=.02f){Near(actual.r,expected.r,label+" R",tolerance);Near(actual.g,expected.g,label+" G",tolerance);Near(actual.b,expected.b,label+" B",tolerance);}
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();
            if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
        }
    }
}
