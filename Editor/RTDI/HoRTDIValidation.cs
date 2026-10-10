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
        private static bool compatibility,previousAsync,unsupported,forcePower,forceDenoise,forceBudget,forceQuality,forceTiming,forceBoundaries,forceSpecular,requireHDR,forceLocalLighting,forceLocalPair,requireLocalPairQuality,forceLocalFilterAudit,forceLocalSpatialAudit,forceLocalPairShadow;
        private static Camera camera;
        private static RenderTexture target;
        private static HoRTDIRendererFeature feature;
        private static HoGeometryBufferRendererFeature gb;
        private static HoRTDIVolume volume;
        private static Light light;
        private static Material surfaceMaterial;
        private static GameObject blocker;
        private static int tick;
        private static int boundarySample;
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
        public static void RunTiming(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceTiming=forceDenoise=true;Run();}
        public static void RunTimingCompatibility(){compatibility=true;RunTiming();}
        public static void RunBoundaries(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceBoundaries=forceDenoise=true;Run();}
        public static void RunBoundariesCompatibility(){compatibility=true;RunBoundaries();}
        public static void RunSpecular(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceSpecular=forceDenoise=true;Run();}
        public static void RunSpecularCompatibility(){compatibility=true;RunSpecular();}
        public static void RunSpecularHDR(){requireHDR=true;RunSpecular();}
        public static void RunSpecularHDRCompatibility(){compatibility=true;RunSpecularHDR();}
        public static void RunLocalLighting(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceLocalLighting=forceDenoise=true;Run();}
        public static void RunLocalLightingCompatibility(){compatibility=true;RunLocalLighting();}
        public static void RunLocalPair(){forceLocalPair=true;RunLocalLighting();}
        public static void RunLocalPairQuality(){requireLocalPairQuality=true;RunLocalLighting();}
        public static void RunLocalPairQualityCompatibility(){compatibility=true;RunLocalPairQuality();}
        public static void RunLocalFilterAudit(){forceLocalFilterAudit=true;RunLocalPair();}
        public static void RunLocalSpatialAudit(){forceLocalSpatialAudit=true;RunLocalPair();}
        public static void RunLocalPairShadow(){forceLocalPairShadow=true;RunLocalPair();}
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
                if(forceTiming){ValidateTiming();Finish(null);return;}
                if(forceBoundaries){ValidateBoundaries();Finish(null);return;}
                if(forceSpecular){ValidateSpecular();Finish(null);return;}
                if(forceLocalLighting){ValidateLocalLighting();Finish(null);return;}
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
        private static void ValidateTiming()
        {
            var timing=new lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdTiming();timing.Advance(0);
            for(int i=1;i<90;i++)Require(!timing.Advance(i/30.0),"Continuous clock unexpectedly reset");
            var before=timing.FilteredSeconds;Require(!timing.Advance(89/30.0)&&timing.FilteredSeconds==before,"Duplicate render changed FPS estimate");
            Require(timing.Advance(0),"Clock rewind did not reset history");Require(timing.Advance(2),"Long camera gap did not reset history");
            Require(timing.Advance(double.NaN),"Invalid clock did not reset history");
            double minError=double.MaxValue,maxError=0;
            foreach(int fps in new[]{30,60,120})
            {
                feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;RenderCamera();var context=feature.LastContext;
                feature.Settings.temporal=true;feature.Settings.spatial=true;feature.Settings.initialSamples=8;light.transform.position=new Vector3(1.5f,0,-1);
                double time=0;
                for(int i=0;i<(int)(fps*.7);i++){context.nrd.ValidationTimeSeconds=time;RenderCamera();time+=1.0/fps;}
                var settings=context.nrd.AppliedSettings;
                uint expected=(uint)Math.Floor(.05*fps+.5);Require(settings.diffFrames==expected,"Time-based diffuse history budget mismatch");
                Require(settings.specFrames==(uint)(fps/2),"Time-based specular history budget mismatch");
                double error=0;int steps=(int)(fps*.4);
                for(int i=1;i<=steps;i++)
                {
                    light.transform.position=new Vector3(1.5f-(float)(i*2.4/fps),0,-1);context.nrd.ValidationTimeSeconds=time;RenderCamera();time+=1.0/fps;
                    float reference=ExpectedMean(light);error+=Math.Abs(ReadMean(context.filteredDiffuse.rt).r-reference)/reference;
                }
                error/=steps;minError=Math.Min(minError,error);maxError=Math.Max(maxError,error);
                Require(error<.3,"Time-based moving-light response unexpectedly degraded");
                Debug.Log($"[HoRTDIValidation] TIMING fps={fps} diff={settings.diffFrames}/{settings.diffFast} spec={settings.specFrames}/{settings.specFast} movingMeanRelative={error:F6} rawFinal={ReadMean(context.diffuse.rt).r:F6} filteredFinal={ReadMean(context.filteredDiffuse.rt).r:F6} reference={ExpectedMean(light):F6}");
            }
            Require(maxError-minError<.1,"Equivalent-speed response changed excessively across FPS");
            var last=feature.LastContext;light.enabled=false;last.nrd.ValidationTimeSeconds+=2;RenderCamera();
            Near(ReadCenter(last.filteredDiffuse.rt).r,0,"Long camera gap did not discard stale illumination",.002f);
            light.enabled=true;last.nrd.ValidationTimeSeconds=0;RenderCamera();
            Require(float.IsFinite(ReadCenter(last.filteredDiffuse.rt).r),"Clock rewind broke actual NRD output");
            Debug.Log("[HoRTDIValidation] PASS time contract: 30/60/120 Hz actual GPU, camera clock duplicate/rewind/gap/invalid; compatibility="+compatibility);
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
        private static void ValidateBoundaries()
        {
            const int size=128;
            target.Release();Object.DestroyImmediate(target);target=Keep(new RenderTexture(size,size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
            light.type=LightType.Directional;light.intensity=.5f;light.transform.rotation=Quaternion.identity;
            feature.Settings.temporal=false;feature.Settings.spatial=false;
            var raw=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var filtered=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var rawSpec=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var filteredSpec=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var pattern=Keep(new Texture2D(size,1,TextureFormat.RGBA32,false,true));pattern.filterMode=FilterMode.Point;pattern.wrapMode=TextureWrapMode.Clamp;
            for(int x=0;x<size;x++)pattern.SetPixel(x,0,(x/2)%2==0?Color.red:Color.blue);pattern.Apply();surfaceMaterial.SetTexture("_MainTex",pattern);
            for(int i=0;i<48;i++)RenderCamera();var context=feature.LastContext;
            ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
            double textureError=0;float textureWorst=0;int redPixels=0,bluePixels=0;
            for(int y=32;y<96;y++)for(int x=16;x<112;x++)
            {
                var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);
                Near(Mathf.Max(a.r,a.b),.5f/Mathf.PI,"two-pixel albedo raw truth",.003f);Near(a.g,0,"albedo green raw",.001f);
                Near(Mathf.Min(a.r,a.b),0,"albedo absent raw channel",.001f);if(a.r>.1f)redPixels++;if(a.b>.1f)bluePixels++;
                float error=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));textureError+=error;textureWorst=Mathf.Max(textureWorst,error);
            }
            Debug.Log($"[HoRTDIValidation] BOUNDARY albedo2px meanAbsolute={textureError/(64*96):F6} maxAbsolute={textureWorst:F6}");
            Require(redPixels==64*48&&bluePixels==64*48,"Fixture did not produce both alternating albedo colors");
            Debug.Log($"[HoRTDIValidation] BOUNDARY albedo sample raw={raw.GetPixel(32,64)} filtered={filtered.GetPixel(32,64)}");surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);
            var strip=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
            var metal=Keep(new Material(surfaceMaterial));metal.SetFloat("_Metallic",1);strip.GetComponent<Renderer>().sharedMaterial=metal;
            // Almost coplanar: isolate material response from depth rejection.
            strip.transform.position=new Vector3(0,0,-.001f);
            foreach(int width in new[]{2,4,8})
            {
                strip.transform.localScale=new Vector3(width*2f/size,2,1);
                for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
                ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
                ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                Near(ReadCenter(context.nrdNormal.rt).a,1,"Zero-diffuse class FP16 transport",.0001f);
                double adjacentLoss=0,metalSpecError=0;float specLeak=0,metalDiffuse=0;int samples=0,metalSamples=0;
                int start=(size-width)/2,end=start+width;
                for(int y=32;y<96;y++)for(int x=start-4;x<end+4;x++)
                {
                    var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);var s=rawSpec.GetPixel(x,y);var t=filteredSpec.GetPixel(x,y);
                    if(x>=start&&x<end){Near(a.r,0,"metal diffuse raw truth",.002f);metalDiffuse=Mathf.Max(metalDiffuse,b.r);Require(s.r>.1f,"Thin metal specular raw missing");metalSpecError+=Math.Abs(t.r-s.r)/s.r;metalSamples++;}
                    else{Near(a.r,.5f/Mathf.PI,"dielectric diffuse raw truth",.003f);Near(s.r,0,"zero-F0 dielectric specular raw truth",.002f);adjacentLoss+=Math.Abs(b.r-a.r)/a.r;specLeak=Mathf.Max(specLeak,t.r);samples++;}
                    Require(float.IsFinite(t.r)&&b.a>.99f,"Boundary signal invalid");
                }
                Debug.Log($"[HoRTDIValidation] BOUNDARY metal width={width}px adjacentDiffuseRelative={adjacentLoss/samples:F6} metalSpecRelative={metalSpecError/metalSamples:F6} dielectricSpecLeak={specLeak:F6} metalDiffuse={metalDiffuse:F6}");
                Require(adjacentLoss/samples<.02&&metalSpecError/metalSamples<.05&&specLeak<.0005f&&metalDiffuse<.0005f,"NRD mixed active/zero diffuse material classes");
            }
            // Reuse has a stochastic normalization. Measure its own raw signal
            // separately from the deterministic NRD boundary gates above.
            feature.Settings.temporal=feature.Settings.spatial=true;strip.transform.localScale=new Vector3(4*2f/size,2,1);
            for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
            double reusedRaw=0,reusedFiltered=0,reusedSpec=0,reusedFilteredSpec=0;int diffuseSamples=0,specSamples=0;
            float specReference=rawSpec.GetPixel(64,64).r;
            for(int frame=0;frame<64;frame++)
            {
                RenderCamera();ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
                ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=58;x<70;x++)
                {
                    if(x>=62&&x<66){reusedSpec+=rawSpec.GetPixel(x,y).r;reusedFilteredSpec+=filteredSpec.GetPixel(x,y).r;specSamples++;}
                    else{reusedRaw+=raw.GetPixel(x,y).r;reusedFiltered+=filtered.GetPixel(x,y).r;diffuseSamples++;}
                }
            }
            Require(context.lastTemporal,"Material reuse observation did not enable temporal sampling");
            Debug.Log($"[HoRTDIValidation] OBSERVATION material reuse frames=64 rawDiffuse={reusedRaw/diffuseSamples:F6} filteredDiffuse={reusedFiltered/diffuseSamples:F6} diffuseReference={.5f/Mathf.PI:F6} rawSpecular={reusedSpec/specSamples:F6} filteredSpecular={reusedFilteredSpec/specSamples:F6} specularReference={specReference:F6}; statistical observation, not a per-pixel deterministic gate");
            // Real thin foreground at a different depth, with a colored material.
            metal.SetFloat("_Metallic",0);metal.SetColor("_Color",Color.blue);strip.transform.position=new Vector3(0,0,-.35f);
            strip.transform.localScale=new Vector3(4*2f/size,2,1);
            feature.Settings.temporal=true;feature.Settings.spatial=true;
            for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
            ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
            float depthWorst=0;
            for(int y=32;y<96;y++)for(int x=56;x<72;x++)
            {
                var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);var expected=x>=62&&x<66?Color.blue:Color.white;
                NearRGB(a,expected*(.5f/Mathf.PI),"thin foreground raw truth",.003f);
                depthWorst=Mathf.Max(depthWorst,Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.b-b.b)));
            }
            Debug.Log($"[HoRTDIValidation] BOUNDARY depth4px reuse maxAbsolute={depthWorst:F6}");Require(depthWorst<.008f,"Thin depth boundary lost deterministic lighting");
            // Different incoming irradiance at the two depths, including the
            // strip's wider projected world shadow on the background plane.
            feature.Settings.temporal=feature.Settings.spatial=false;
            light.type=LightType.Point;light.transform.position=new Vector3(0,0,-1);light.intensity=.5f;
            for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
            ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
            float pointDepthWorst=0,shadowLeak=0;double pointDepthError=0;int pointDepthCount=0;
            for(int y=32;y<96;y++)for(int x=56;x<72;x++)
            {
                bool foreground=x>=62&&x<66;
                var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/size,(y+.5f)/size,0));
                float z=foreground?-.35f:0;var p=ray.origin+ray.direction*((z-ray.origin.z)/ray.direction.z);
                var delta=light.transform.position-p;float d2=delta.sqrMagnitude,fade=Mathf.Clamp01(1-Mathf.Pow(d2/(light.range*light.range),2));
                bool shadow=!foreground&&Mathf.Abs(p.x*.65f)<4f/size;
                float irradiance=shadow?0:light.intensity/d2*(-delta.normalized.z)/Mathf.PI*fade*fade;
                var expected=(foreground?Color.blue:Color.white)*irradiance;
                var a=raw.GetPixel(x,y);var b=filtered.GetPixel(x,y);NearRGB(a,expected,"point-lit thin depth raw truth",.003f);
                float error=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.b-b.b));pointDepthWorst=Mathf.Max(pointDepthWorst,error);pointDepthError+=error;pointDepthCount++;
                if(shadow)shadowLeak=Mathf.Max(shadowLeak,Mathf.Max(b.r,b.b));
            }
            Debug.Log($"[HoRTDIValidation] BOUNDARY pointDepth4px meanAbsolute={pointDepthError/pointDepthCount:F6} maxAbsolute={pointDepthWorst:F6} shadowMax={shadowLeak:F6}");
            // Same NRD class: no material-ID barrier hides chromatic specular loss.
            strip.SetActive(false);surfaceMaterial.SetFloat("_Metallic",1);surfaceMaterial.SetTexture("_MainTex",pattern);
            light.type=LightType.Directional;light.intensity=.5f;light.transform.rotation=Quaternion.identity;
            for(int i=0;i<48;i++)RenderCamera();context=feature.LastContext;
            ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
            double specColorError=0;float absentSpec=0;int specColorCount=0;
            for(int y=32;y<96;y++)for(int x=16;x<112;x++)
            {
                var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);float peak=Mathf.Max(a.r,a.b);
                Require(peak>.1f,"Colored metal specular fixture missing");Near(Mathf.Min(a.r,a.b),0,"Colored metal absent raw specular",.001f);
                specColorError+=Mathf.Abs(Mathf.Max(b.r,b.b)-peak)/peak;specColorCount++;
                absentSpec=Mathf.Max(absentSpec,Mathf.Max(b.g,a.r>.1f?b.b:b.r));
            }
            Debug.Log($"[HoRTDIValidation] OBSERVATION coloredSpec2px relative={specColorError/specColorCount:F6} absentChannelMax={absentSpec:F6} raw={rawSpec.GetPixel(32,64)} filtered={filteredSpec.GetPixel(32,64)}");
            Debug.Log($"[HoRTDIValidation] COLOR_BUDGET diff={context.nrd.AppliedSettings.diffFrames} spec={context.nrd.AppliedSettings.specFrames} fix={context.nrd.AppliedSettings.historyFixFrames}");
            Require(specColorError/specColorCount<.02&&absentSpec<.0005f,"NRD lost deterministic colored specular detail");
            float singleSpec=Mathf.Max(rawSpec.GetPixel(32,64).r,rawSpec.GetPixel(32,64).b);
            var second=Keep(new GameObject("Noisy colored specular directional")).AddComponent<Light>();second.type=LightType.Directional;second.intensity=.25f;
            for(int i=0;i<64;i++)RenderCamera();double noisyRaw=0,noisyFiltered=0,noisyLeak=0;int noisyCount=0;
            for(int frame=0;frame<64;frame++)
            {
                RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=32;x<96;x++)
                {
                    var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);noisyRaw+=Mathf.Max(a.r,a.b);noisyFiltered+=Mathf.Max(b.r,b.b);
                    noisyLeak+=Mathf.Max(b.g,a.r>.1f?b.b:b.r);noisyCount++;
                }
            }
            Debug.Log($"[HoRTDIValidation] OBSERVATION coloredSpec2px twoWhiteLamps rawMean={noisyRaw/noisyCount:F6} filteredMean={noisyFiltered/noisyCount:F6} reference={singleSpec*1.5f:F6} absentChannelMean={noisyLeak/noisyCount:F6}; stochastic material boundary remains a separate support audit");
            Near((float)(noisyRaw/noisyCount),singleSpec*1.5f,"Noisy colored specular raw statistical energy",.02f);
            Require(Math.Abs(noisyFiltered/noisyCount/(singleSpec*1.5f)-1)<.05&&noisyLeak/noisyCount<.0005,"Noisy two-pixel specular boundary lost energy or color");
            foreach(int band in new[]{8,64,128})
            {
                for(int x=0;x<size;x++)pattern.SetPixel(x,0,(x/band)%2==0?Color.red:Color.blue);pattern.Apply();
                feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
                for(int i=0;i<96;i++)RenderCamera();context=feature.LastContext;
                double sumRaw=0,sumFiltered=0,sumLeak=0;int count=0;
                for(int frame=0;frame<64;frame++)
                {
                    RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                    for(int y=48;y<80;y++)for(int x=32;x<96;x++)
                    {
                        var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);
                        sumRaw+=Mathf.Max(a.r,a.b);sumFiltered+=Mathf.Max(b.r,b.b);sumLeak+=Mathf.Max(b.g,a.r>.1f?b.b:b.r);count++;
                    }
                }
                Debug.Log($"[HoRTDIValidation] SPEC_BANDS width={band} rawMean={sumRaw/count:F6} filteredMean={sumFiltered/count:F6} reference={singleSpec*1.5f:F6} absentChannelMean={sumLeak/count:F6}");
                Require(Math.Abs(sumFiltered/count/(singleSpec*1.5f)-1)<.05,"Specular material guide changed band energy");
            }
            foreach(float scale in new[]{.04f,.2f})
            {
                for(int x=0;x<size;x++)pattern.SetPixel(x,0,((x/2)%2==0?Color.red:Color.blue)*scale);pattern.Apply();
                feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
                for(int i=0;i<96;i++)RenderCamera();context=feature.LastContext;
                double sumRaw=0,sumFiltered=0,sumLeak=0;int count=0;
                for(int frame=0;frame<64;frame++)
                {
                    RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                    for(int y=48;y<80;y++)for(int x=32;x<96;x++)
                    {
                        var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);sumRaw+=Mathf.Max(a.r,a.b);sumFiltered+=Mathf.Max(b.r,b.b);sumLeak+=Mathf.Max(b.g,a.r>a.b?b.b:b.r);count++;
                    }
                }
                float reference=singleSpec*1.5f*Mathf.Round(scale*255)/255;
                Debug.Log($"[HoRTDIValidation] SPEC_LOW_F0 scale={scale} rawMean={sumRaw/count:F6} filteredMean={sumFiltered/count:F6} reference={reference:F6} absentChannelMean={sumLeak/count:F6}");
                Require(Math.Abs(sumFiltered/count/reference-1)<.06&&sumLeak/count/reference<.02,"Low F0 material guide lost relative energy/color");
            }
            for(int x=0;x<size;x++)pattern.SetPixel(x,0,new Color((x+.5f)/size,0,1-(x+.5f)/size,1));pattern.Apply();
            feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
            for(int i=0;i<96;i++)RenderCamera();context=feature.LastContext;
            double rampRaw=0,rampFiltered=0;int rampCount=0;
            for(int frame=0;frame<64;frame++)
            {
                RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=32;x<96;x++){var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);rampRaw+=a.r+a.b;rampFiltered+=b.r+b.b;rampCount++;}
            }
            Debug.Log($"[HoRTDIValidation] SPEC_CONTINUOUS rawSumMean={rampRaw/rampCount:F6} filteredSumMean={rampFiltered/rampCount:F6} reference={singleSpec*1.5f:F6}");
            Require(Math.Abs(rampFiltered/rampCount/(singleSpec*1.5f)-1)<.05,"Continuous F0 guide changed total energy");
            for(int x=0;x<size;x++)pattern.SetPixel(x,0,(x/2)%2==0?Color.red:Color.blue);pattern.Apply();
            feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
            for(int i=0;i<96;i++)RenderCamera();context=feature.LastContext;double movingRaw=0,movingFiltered=0,movingLeak=0,movingTint=0;int movingCount=0;
            for(int frame=0;frame<32;frame++)
            {
                camera.transform.position+=new Vector3(2f/size*.125f,0,0);RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=32;x<96;x++){var a=rawSpec.GetPixel(x,y);var b=filteredSpec.GetPixel(x,y);float rawSum=a.r+a.b;movingRaw+=rawSum;movingFiltered+=b.r+b.b;float filteredSum=b.r+b.b;movingLeak+=Mathf.Max(0,Mathf.Min(b.r,b.b)/Mathf.Max(filteredSum,1e-6f)-Mathf.Min(a.r,a.b)/Mathf.Max(rawSum,1e-6f));movingTint+=Mathf.Abs(b.r/Mathf.Max(filteredSum,1e-6f)-a.r/Mathf.Max(rawSum,1e-6f));movingCount++;}
            }
            Debug.Log($"[HoRTDIValidation] SPEC_CAMERA quarterCycle rawMean={movingRaw/movingCount:F6} filteredMean={movingFiltered/movingCount:F6} reference={singleSpec*1.5f:F6} excessChannelMean={movingLeak/movingCount:F6} tintFraction={movingTint/movingCount:F6}; temporal material transport observation");
            Require(Math.Abs(movingFiltered/movingCount/(singleSpec*1.5f)-1)<.05&&movingTint/movingCount<.02,"Moving F0 boundary lost energy or tint");
            Require(pointDepthWorst<.008f&&shadowLeak<.003f,"NRD mixed unequal point-lit depth surfaces or blurred their thin shadow");
            Require(textureWorst<.008f,"NRD blurred demodulated two-pixel albedo");
            Debug.Log("[HoRTDIValidation] PASS boundary gates: analytic raw, 2px albedo/specular, metal classes, unequal point-lit depth and thin world shadow; compatibility="+compatibility);
        }
        private static Color ReferenceGGX(Vector3 L,float rough,Color f0)
            =>ReferenceGGX(Vector3.back,Vector3.back,L,rough,f0);
        private static Color ReferenceGGX(Vector3 N,Vector3 V,Vector3 L,float rough,Color f0)
        {
            var H=(V+L).normalized;
            double nl=Math.Max(0,Vector3.Dot(N,L)),nv=Math.Max(0,Vector3.Dot(N,V)),nh=Math.Max(0,Vector3.Dot(N,H)),vh=Math.Max(0,Vector3.Dot(V,H));
            if(nl<=0||nv<=0)return Color.black;
            double a=rough*rough,a2=a*a,d=a2/(Math.PI*Math.Pow(nh*nh*a2+1-nh*nh,2));
            // Height-correlated Smith G / NdotV, as used by the reference BRDF.
            // A separable G1 product agrees only when NdotV=1.
            double g=2*nl/(nv*Math.Sqrt(a2+(1-a2)*nl*nl)+nl*Math.Sqrt(a2+(1-a2)*nv*nv)),fc=Math.Pow(1-vh,5);
            return new Color((float)((f0.r+(1-f0.r)*fc)*d*g/4),(float)((f0.g+(1-f0.g)*fc)*d*g/4),(float)((f0.b+(1-f0.b)*fc)*d*g/4),1);
        }
        private static Vector3 PlanePosition(int x,int y,int size)
        {var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/size,(y+.5f)/size,0));return ray.origin-ray.direction*(ray.origin.z/ray.direction.z);}
        private static (Color diffuse,Color specular,float distance,float cone) ReferenceLocalLight(int x,int y,int size,float rough)
            =>ReferenceLocalLight(light,x,y,size,rough);
        private static (Color diffuse,Color specular,float distance,float cone) ReferenceLocalLight(Light source,int x,int y,int size,float rough)
        {
            var p=PlanePosition(x,y,size);var delta=source.transform.position-p;float distance=delta.magnitude;var L=delta/distance;
            float range=source.range;float attenuation=Mathf.Pow(Mathf.Clamp01(1-Mathf.Pow(distance/range,4)),2)/(distance*distance),cone=1;
            if(source.type==LightType.Spot)
            {
                float inner=Mathf.Cos(source.innerSpotAngle*Mathf.Deg2Rad*.5f),outer=Mathf.Cos(source.spotAngle*Mathf.Deg2Rad*.5f);
                cone=Mathf.Clamp01((Vector3.Dot(-L,source.transform.forward)-outer)/(inner-outer));attenuation*=cone*cone;
            }
            var radiance=LightColor(source)*(source.intensity*attenuation);var V=(camera.transform.position-p).normalized;
            var diffuse=radiance*(Mathf.Max(0,-L.z)/Mathf.PI);
            var spec=ReferenceGGX(Vector3.back,V,L,rough,new Color(10f/255,10f/255,10f/255,1))*radiance;
            return(diffuse,spec,distance,cone);
        }
        private static void SetLocalView(float angle)
        {camera.transform.position=Quaternion.Euler(0,angle,0)*new Vector3(0,0,-3);camera.transform.rotation=Quaternion.LookRotation(-camera.transform.position,Vector3.up);}
        private static void ValidateLocalLighting()
        {
            const int size=128;target.Release();target.width=target.height=size;target.Create();camera.orthographic=false;camera.fieldOfView=40;
            surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetColor("_Color",Color.white);
            light.color=new Color(.7f,.4f,.2f);light.intensity=4;light.range=20;light.innerSpotAngle=6;light.spotAngle=20;
            feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.initialSamples=1;
            var rawDiff=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var rawSpec=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var filteredDiff=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filteredSpec=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var hit=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));int inside=0,penumbra=0,outside=0;
            if(forceLocalPair)
            {
                SetLocalView(15);light.type=LightType.Point;surfaceMaterial.SetFloat("_Smoothness",.7f);light.transform.position=new Vector3(-camera.transform.position.x*1.5f/(-camera.transform.position.z),.15f,-1.5f);
                var distant=Point(new Vector3(-30,0,-50),light.color,25000);distant.range=100;
                if(forceLocalPairShadow){blocker.layer=1;camera.cullingMask=~(1<<1);blocker.transform.position=light.transform.position*.5f;blocker.transform.localScale=new Vector3(.05f,.7f,.08f);blocker.SetActive(true);}
                var modes=forceLocalSpatialAudit?new[]{(false,false),(true,true)}:forceLocalFilterAudit?new[]{(false,false),(false,false),(false,false),(false,false),(false,false)}:new[]{(false,false),(true,false),(false,true),(true,true)};
                bool shadowQuality=true;
                int variant=0;
                foreach(var mode in modes)
                {
                    SetLocalView(15);
                    feature.Settings.initialSamples=8;feature.Settings.temporal=mode.Item1;feature.Settings.spatial=mode.Item2;
                    feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;for(int frame=0;frame<192;frame++)RenderCamera();var c=feature.LastContext;
                    float r=Mathf.Round(Mathf.Abs(ReadCenter(c.nrdNormal.rt).b*2-1)*255)/255;
                    var applied=c.nrd.AppliedSettings;
                    if(forceLocalFilterAudit)
                    {
                        // Isolated Editor experiment: change the real SDK setting
                        // after timing budgets settle. Record won't overwrite it
                        // unless a budget changes; production DenseDI stays intact.
                        var probeSettings=applied;
                        if(variant==1)probeSettings.fastSigma=3; // Pinned supported range: 1..3.
                        if(variant==2)probeSettings.specFast=15;
                        if(variant==3){probeSettings.acceleration=0;probeSettings.reset=0;}
                        if(variant==4)probeSettings.specPrepass=5;
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        var instance=(IntPtr)c.nrd.GetType().GetField("instance",flags).GetValue(c.nrd);
                        lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.Check(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.SetDenoiserSettings(instance,0,ref probeSettings),"Local filter audit settings");
                        for(int frame=0;frame<128;frame++)RenderCamera();
                        Debug.Log($"[HoRTDIValidation] LOCAL_FILTER_VARIANT sigma={probeSettings.fastSigma} specFast={probeSettings.specFast} acceleration={probeSettings.acceleration} reset={probeSettings.reset} specPrepass={probeSettings.specPrepass}; real native setting, diagnostic only");
                    }
                    double expectedSum=0,rawSum=0,filteredSum=0,error=0,diffReference=0,diffRaw=0,historySum=0,historyReference=0,rawMse=0,filteredMse=0,referenceSquares=0;
                    var historyTexture=FindNrdHistory(c,"gHistory_Spec");
                    var shadowBounds=blocker.GetComponent<Renderer>().bounds;double shadowReference=0,shadowFiltered=0,shadowRaw=0,litReference=0,litFiltered=0;int shadowCount=0;
                    var reprojection=forceLocalSpatialAudit?FindNrdTemporalPool(c,"gOut_SpecReprojectionConfidence"):null;
                    var confidenceImage=forceLocalSpatialAudit?Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true)):null;
                    var bandReference=new double[4];var bandRaw=new double[4];var bandHistory=new double[4];var bandFinal=new double[4];
                    double confidenceSum=0,confidenceReference=0;float confidenceMin=1,confidenceMax=0;
                    for(int frame=0;frame<512;frame++)
                    {
                        RenderCamera();ReadImage(c.specular.rt,rawSpec);ReadImage(c.filteredSpecular.rt,filteredSpec);ReadImage(c.diffuse.rt,rawDiff);
                        if(frame%8==0){ReadImage(historyTexture,hit);ReadImage(c.nrdSpecular.rt,filteredDiff);}
                        if(forceLocalSpatialAudit&&frame%8==0)
                        {
                            ReadImage(reprojection,confidenceImage);
                            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                            {
                                var p=PlanePosition(x,y,size);if(Mathf.Abs(p.x)>=.999f||Mathf.Abs(p.y)>=.999f)continue;
                                float expected=ReferenceLocalLight(x,y,size,r).specular.r+ReferenceLocalLight(distant,x,y,size,r).specular.r;
                                float input=filteredDiff.GetPixel(x,y).r;Require(input>0,"Spatial audit cannot restore material factor");
                                float history=hit.GetPixel(x,y).r*rawSpec.GetPixel(x,y).r/input;
                                float confidence=confidenceImage.GetPixel(x,y).r;confidenceSum+=expected*confidence;confidenceReference+=expected;confidenceMin=Mathf.Min(confidenceMin,confidence);confidenceMax=Mathf.Max(confidenceMax,confidence);
                                for(int band=0;band<4;band++)if(x>=64-(band+1)*16&&x<64+(band+1)*16&&y>=64-(band+1)*16&&y<64+(band+1)*16)
                                {bandReference[band]+=expected;bandRaw[band]+=rawSpec.GetPixel(x,y).r;bandHistory[band]+=history;bandFinal[band]+=filteredSpec.GetPixel(x,y).r;}
                            }
                        }
                        for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                        {
                            var a=ReferenceLocalLight(x,y,size,r);var b=ReferenceLocalLight(distant,x,y,size,r);bool nearShadow=false;
                            if(forceLocalPairShadow)
                            {
                                var p=PlanePosition(x,y,size);nearShadow=SegmentHitsBox(p,light.transform.position,shadowBounds);
                                if(nearShadow){a.diffuse=Color.clear;a.specular=Color.clear;}
                                if(SegmentHitsBox(p,distant.transform.position,shadowBounds)){b.diffuse=Color.clear;b.specular=Color.clear;}
                            }
                            float expected=a.specular.r+b.specular.r;
                            expectedSum+=expected;rawSum+=rawSpec.GetPixel(x,y).r;filteredSum+=filteredSpec.GetPixel(x,y).r;error+=Math.Abs(filteredSpec.GetPixel(x,y).r-expected);diffReference+=a.diffuse.r+b.diffuse.r;diffRaw+=rawDiff.GetPixel(x,y).r;
                            double rawDelta=rawSpec.GetPixel(x,y).r-expected,filteredDelta=filteredSpec.GetPixel(x,y).r-expected;
                            rawMse+=rawDelta*rawDelta;filteredMse+=filteredDelta*filteredDelta;referenceSquares+=expected*expected;
                            if(forceLocalPairShadow){if(nearShadow){shadowReference+=expected;shadowFiltered+=filteredSpec.GetPixel(x,y).r;shadowRaw+=rawSpec.GetPixel(x,y).r;shadowCount++;}else{litReference+=expected;litFiltered+=filteredSpec.GetPixel(x,y).r;}}
                            if(frame%8==0)
                            {
                                // The raw/staged ratio recovers this pixel's
                                // material factor and current scale. Restore
                                // history physically without another GPU pass.
                                float input=filteredDiff.GetPixel(x,y).r;
                                if(input>0){historySum+=hit.GetPixel(x,y).r*rawSpec.GetPixel(x,y).r/input;historyReference+=expected;}
                                else Require(forceLocalPairShadow,"Cannot recover local material factor from zero input");
                            }
                        }
                    }
                    Debug.Log($"[HoRTDIValidation] LOCAL_PAIR_ABLATION temporal={mode.Item1} spatial={mode.Item2} rawRatio={rawSum/expectedSum:F6} historyRatio={historySum/historyReference:F6} filteredRatio={filteredSum/expectedSum:F6} filteredL1={error/expectedSum:F6} diffuseRawRatio={diffRaw/diffReference:F6}");
                    Debug.Log($"[HoRTDIValidation] LOCAL_SPECULAR_MSE temporal={mode.Item1} spatial={mode.Item2} rawNormalized={rawMse/referenceSquares:F6} filteredNormalized={filteredMse/referenceSquares:F6}; includes bias and temporal noise against analytic reference");
                    if(forceLocalPairShadow)
                    {
                        Debug.Log($"[HoRTDIValidation] LOCAL_PAIR_SHADOW samples={shadowCount} shadowRawRatio={shadowRaw/shadowReference:F6} shadowFilteredRatio={shadowFiltered/shadowReference:F6} litFilteredRatio={litFiltered/litReference:F6}");
                        shadowQuality&=shadowCount>0&&Math.Abs(shadowRaw/shadowReference-1)<.05;
                        shadowQuality&=Math.Abs(shadowFiltered/shadowReference-1)<.1&&Math.Abs(litFiltered/litReference-1)<.05;
                    }
                    if(forceLocalSpatialAudit)
                    {
                        for(int band=0;band<4;band++)Debug.Log($"[HoRTDIValidation] LOCAL_SPATIAL_BAND width={(band+1)*32} rawRatio={bandRaw[band]/bandReference[band]:F6} historyRatio={bandHistory[band]/bandReference[band]:F6} finalRatio={bandFinal[band]/bandReference[band]:F6}");
                        Debug.Log($"[HoRTDIValidation] LOCAL_REPROJECTION weightedMean={confidenceSum/confidenceReference:F6} min={confidenceMin:F6} max={confidenceMax:F6} lightingConfidenceCenter={ReadCenter(c.specConfidence.rt).r:F6}");
                    }
                    if(forceLocalFilterAudit)
                    {
                        expectedSum=rawSum=filteredSum=error=0;
                        for(int frame=0;frame<64;frame++)
                        {
                            SetLocalView(15+(frame+1)*.25f);RenderCamera();ReadImage(c.specular.rt,rawSpec);ReadImage(c.filteredSpecular.rt,filteredSpec);
                            for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                            {
                                float expected=ReferenceLocalLight(x,y,size,r).specular.r+ReferenceLocalLight(distant,x,y,size,r).specular.r;
                                expectedSum+=expected;rawSum+=rawSpec.GetPixel(x,y).r;filteredSum+=filteredSpec.GetPixel(x,y).r;error+=Math.Abs(filteredSpec.GetPixel(x,y).r-expected);
                            }
                        }
                        Debug.Log($"[HoRTDIValidation] LOCAL_FILTER_MOTION variant={variant} rawRatio={rawSum/expectedSum:F6} filteredRatio={filteredSum/expectedSum:F6} filteredL1={error/expectedSum:F6}");
                    }
                    Require(c.nrd.AppliedSettings.diffFrames==applied.diffFrames&&c.nrd.AppliedSettings.specFrames==applied.specFrames&&c.nrd.AppliedSettings.specFast==applied.specFast,"Filter audit timing budget changed and invalidated native override");variant++;
                }
                Require(!forceLocalPairShadow||shadowQuality,"Paired-light shadow raw visibility/energy or filtered highlight failed");
                Debug.Log("[HoRTDIValidation] LOCAL_PAIR observation completed; diagnostic ratios are not quality acceptance");return;
            }
            foreach(var type in new[]{LightType.Point,LightType.Spot})foreach(float rough in new[]{.3f,.7f})foreach(float angle in new[]{0f,30f,60f})
            {
                SetLocalView(angle);light.type=type;light.transform.position=new Vector3(-camera.transform.position.x*1.5f/(-camera.transform.position.z),.15f,-1.5f);
                light.transform.rotation=Quaternion.LookRotation(-light.transform.position,Vector3.up);surfaceMaterial.SetFloat("_Smoothness",1-rough);
                feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;for(int i=0;i<64;i++)RenderCamera();var c=feature.LastContext;
                float r=Mathf.Round(Mathf.Abs(ReadCenter(c.nrdNormal.rt).b*2-1)*255)/255;
                ReadImage(c.diffuse.rt,rawDiff);ReadImage(c.specular.rt,rawSpec);ReadImage(c.nrdSpecular.rt,hit);
                double diffReference=0,specReference=0,rawDiffError=0,rawSpecError=0,filteredDiffError=0,filteredSpecError=0;float maxDistanceError=0;
                for(int sample=0;sample<16;sample++)
                {
                    RenderCamera();ReadImage(c.filteredDiffuse.rt,filteredDiff);ReadImage(c.filteredSpecular.rt,filteredSpec);
                    for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                    {
                        var expected=ReferenceLocalLight(x,y,size,r);var a=rawDiff.GetPixel(x,y);var b=rawSpec.GetPixel(x,y);var f=filteredDiff.GetPixel(x,y);var g=filteredSpec.GetPixel(x,y);
                        Require(a.a>.5f&&b.a>.5f&&f.a>.5f&&g.a>.5f,"Local-light receiver missing");
                        diffReference+=expected.diffuse.r;specReference+=expected.specular.r;
                        rawDiffError+=Math.Abs(a.r-expected.diffuse.r);rawSpecError+=Math.Abs(b.r-expected.specular.r);
                        filteredDiffError+=Math.Abs(f.r-expected.diffuse.r);filteredSpecError+=Math.Abs(g.r-expected.specular.r);
                        if(sample==0)
                        {
                            // ReSTIR may have no valid reservoir outside the cone.
                            if(expected.cone>0)maxDistanceError=Mathf.Max(maxDistanceError,Mathf.Abs(hit.GetPixel(x,y).a-expected.distance));
                            if(type==LightType.Spot){if(expected.cone==0)outside++;else if(expected.cone==1)inside++;else penumbra++;}
                        }
                    }
                }
                Debug.Log($"[HoRTDIValidation] LOCAL type={type} rough={r:F4} view={angle} rawDiffL1={rawDiffError/diffReference:F6} rawSpecL1={rawSpecError/specReference:F6} filteredDiffL1={filteredDiffError/diffReference:F6} filteredSpecL1={filteredSpecError/specReference:F6} hitDistanceMax={maxDistanceError:F6}");
                Require(rawDiffError/diffReference<.02&&rawSpecError/specReference<.02,"Local-light raw differs from independent attenuation / correlated GGX");
                Require(maxDistanceError<.002f,"Local-light NRD hit distance differs from selected-light distance");
                Require(filteredDiffError/diffReference<.05&&filteredSpecError/specReference<.05,"Static local-light filtered L1 error exceeds 5%");
            }
            Debug.Log($"[HoRTDIValidation] LOCAL_CONE inner={inside} transition={penumbra} outside={outside}");
            Require(inside>0&&penumbra>0&&outside>0,"Spot test did not exercise inner/transition/outside cone");
            // An off-camera opaque strip is checked with an independent CPU
            // segment/AABB intersection, rather than accepting raw as truth.
            SetLocalView(30);surfaceMaterial.SetFloat("_Smoothness",.7f);light.transform.position=new Vector3(-camera.transform.position.x*1.5f/(-camera.transform.position.z),.15f,-1.5f);
            light.transform.rotation=Quaternion.LookRotation(-light.transform.position,Vector3.up);
            blocker.layer=1;camera.cullingMask=~(1<<1);blocker.transform.position=light.transform.position*.5f;blocker.transform.localScale=new Vector3(.05f,.7f,.08f);blocker.SetActive(true);
            foreach(var type in new[]{LightType.Point,LightType.Spot})
            {
                light.type=type;feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;for(int i=0;i<64;i++)RenderCamera();var c=feature.LastContext;
                float r=Mathf.Round(Mathf.Abs(ReadCenter(c.nrdNormal.rt).b*2-1)*255)/255;
                ReadImage(c.diffuse.rt,rawDiff);ReadImage(c.specular.rt,rawSpec);ReadImage(c.filteredDiffuse.rt,filteredDiff);ReadImage(c.filteredSpecular.rt,filteredSpec);
                var bounds=blocker.GetComponent<Renderer>().bounds;double refDiff=0,refSpec=0,rawErrorDiff=0,rawErrorSpec=0,errorDiff=0,errorSpec=0;float darkDiff=0,darkSpec=0,peakDiff=0,peakSpec=0;int shadowPixels=0,litPixels=0;
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    var p=PlanePosition(x,y,size);bool shadow=SegmentHitsBox(p,light.transform.position,bounds);var expected=ReferenceLocalLight(x,y,size,r);
                    float d=shadow?0:expected.diffuse.r,s=shadow?0:expected.specular.r;
                    var a=rawDiff.GetPixel(x,y);var b=rawSpec.GetPixel(x,y);var f=filteredDiff.GetPixel(x,y);var g=filteredSpec.GetPixel(x,y);
                    rawErrorDiff+=Math.Abs(a.r-d);rawErrorSpec+=Math.Abs(b.r-s);errorDiff+=Math.Abs(f.r-d);errorSpec+=Math.Abs(g.r-s);refDiff+=d;refSpec+=s;peakDiff=Mathf.Max(peakDiff,d);peakSpec=Mathf.Max(peakSpec,s);
                    if(shadow&&expected.diffuse.r>0){shadowPixels++;darkDiff=Mathf.Max(darkDiff,f.r);darkSpec=Mathf.Max(darkSpec,g.r);}else if(d>0)litPixels++;
                }
                Debug.Log($"[HoRTDIValidation] LOCAL_SHADOW type={type} shadowPixels={shadowPixels} litPixels={litPixels} rawDiffL1={rawErrorDiff/refDiff:F6} rawSpecL1={rawErrorSpec/refSpec:F6} filteredDiffL1={errorDiff/refDiff:F6} filteredSpecL1={errorSpec/refSpec:F6} shadowMaxFraction={darkDiff/peakDiff:F6}/{darkSpec/peakSpec:F6}");
                Require(shadowPixels>0&&litPixels>0,"Local shadow fixture missed lit or occluded pixels");
                Require(rawErrorDiff/refDiff<.02&&rawErrorSpec/refSpec<.02,"Local-light raw visibility differs from CPU segment/box");
                Require(errorDiff/refDiff<.05&&errorSpec/refSpec<.05&&darkDiff/peakDiff<.01f&&darkSpec/peakSpec<.01f,"Local-light filtered shadow edge or dark region failed");
            }
            blocker.SetActive(false);
            light.type=LightType.Point;surfaceMaterial.SetFloat("_Smoothness",.7f);SetLocalView(15);light.transform.position=new Vector3(-camera.transform.position.x*1.5f/(-camera.transform.position.z),.15f,-1.5f);
            for(int i=0;i<96;i++)RenderCamera();var context=feature.LastContext;float actualRough=Mathf.Round(Mathf.Abs(ReadCenter(context.nrdNormal.rt).b*2-1)*255)/255;
            double movingReference=0,movingRawError=0,movingFilteredError=0;
            for(int frame=0;frame<64;frame++)
            {
                SetLocalView(15+(frame+1)*.25f);RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {var expected=ReferenceLocalLight(x,y,size,actualRough).specular.r;movingReference+=expected;movingRawError+=Math.Abs(rawSpec.GetPixel(x,y).r-expected);movingFilteredError+=Math.Abs(filteredSpec.GetPixel(x,y).r-expected);}
            }
            Debug.Log($"[HoRTDIValidation] LOCAL_MOTION degreesPerSecond=15 rawL1={movingRawError/movingReference:F6} filteredL1={movingFilteredError/movingReference:F6}; moving finite-distance highlight, perspective orbit at 60Hz");
            Require(movingRawError/movingReference<.02,"Moving local-light raw GGX mismatch");
            Require(movingFilteredError/movingReference<.15,"Moving local-light highlight L1 exceeds 15%");
            if(!requireLocalPairQuality)
            {Debug.Log("[HoRTDIValidation] PASS local-light attenuation, oblique perspective GGX, selected distance, shadows and single-lamp orbit; compatibility="+compatibility);return;}
            var far=Point(new Vector3(-30,0,-50),light.color,25000);far.range=100;
            SetLocalView(15);feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=true;
            for(int frame=0;frame<192;frame++)RenderCamera();
            double pairedReference=0,pairedRaw=0,pairedError=0,pairedFiltered=0;
            for(int frame=0;frame<512;frame++)
            {
                RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    float expected=ReferenceLocalLight(x,y,size,actualRough).specular.r+ReferenceLocalLight(far,x,y,size,actualRough).specular.r;
                    pairedReference+=expected;pairedRaw+=rawSpec.GetPixel(x,y).r;pairedFiltered+=filteredSpec.GetPixel(x,y).r;pairedError+=Math.Abs(filteredSpec.GetPixel(x,y).r-expected);
                }
            }
            double staticRawRatio=pairedRaw/pairedReference,staticFilteredRatio=pairedFiltered/pairedReference;
            Debug.Log($"[HoRTDIValidation] LOCAL_PAIR_STATIC specRawRatio={staticRawRatio:F6} specFilteredRatio={staticFilteredRatio:F6} filteredL1={pairedError/pairedReference:F6}; near mirror lamp / far diffuse-dominant lamp, initial=8 temporal+spatial enabled, 192 warmup / 512 observations");
            pairedReference=pairedRaw=pairedError=pairedFiltered=0;
            for(int frame=0;frame<64;frame++)
            {
                SetLocalView(15+(frame+1)*.25f);RenderCamera();ReadImage(context.specular.rt,rawSpec);ReadImage(context.filteredSpecular.rt,filteredSpec);
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    float expected=ReferenceLocalLight(x,y,size,actualRough).specular.r+ReferenceLocalLight(far,x,y,size,actualRough).specular.r;
                    pairedReference+=expected;pairedRaw+=rawSpec.GetPixel(x,y).r;pairedFiltered+=filteredSpec.GetPixel(x,y).r;pairedError+=Math.Abs(filteredSpec.GetPixel(x,y).r-expected);
                }
            }
            Debug.Log($"[HoRTDIValidation] LOCAL_PAIR_MOTION specRawRatio={pairedRaw/pairedReference:F6} specFilteredRatio={pairedFiltered/pairedReference:F6} filteredL1={pairedError/pairedReference:F6}; near/far competing distances at 15deg/s, 60Hz");
            Require(Math.Abs(staticRawRatio-1)<.03,"Paired local-light raw ensemble energy mismatch");
            Require(Math.Abs(staticFilteredRatio-1)<.05,"Paired local-light NRD static energy bias exceeds 5%");
            Require(Math.Abs(pairedRaw/pairedReference-1)<.03,"Moving paired local-light raw ensemble energy mismatch");
            Require(pairedError/pairedReference<.15,"Moving paired local-light highlight L1 exceeds 15%");
            Debug.Log("[HoRTDIValidation] PASS local-light attenuation, oblique perspective GGX, selected distance and orbit; compatibility="+compatibility);
        }
        private static bool SegmentHitsBox(Vector3 start,Vector3 end,Bounds bounds)
        {
            float near=0,far=1;var delta=end-start;var min=bounds.min;var max=bounds.max;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(delta[axis])<1e-8f){if(start[axis]<min[axis]||start[axis]>max[axis])return false;continue;}
                float a=(min[axis]-start[axis])/delta[axis],b=(max[axis]-start[axis])/delta[axis];if(a>b){float swap=a;a=b;b=swap;}
                near=Mathf.Max(near,a);far=Mathf.Min(far,b);if(near>far)return false;
            }
            return far>0&&near<1;
        }
        private static RenderTexture FindNrdHistory(HoRTDIContext context,string name)
            =>FindNrdTemporalPool(context,name,true);
        private static RenderTexture FindNrdTemporalPool(HoRTDIContext context,string name,bool requirePermanent=false)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=context.nrd.GetType();
            var passes=(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.DispatchDesc[])type.GetField("passes",flags).GetValue(context.nrd);
            var resources=(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.ResourceDesc[,])type.GetField("resources",flags).GetValue(context.nrd);
            var permanent=(RTHandle[])type.GetField("permanent",flags).GetValue(context.nrd);var transient=(RTHandle[])type.GetField("transient",flags).GetValue(context.nrd);var manifest=lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdManifest.Resources[4];
            for(int i=0;i<context.nrd.LastDispatchCount;i++)if(passes[i].pipeline==4)
                for(int j=0;j<manifest.Length;j++)if(manifest[j]==name)
                {var resource=resources[i,j];string kind=lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.ResourceName(resource.type);Require(kind=="PERMANENT_POOL"||(!requirePermanent&&kind=="TRANSIENT_POOL"),"Temporal diagnostic is not the expected pool");return(kind=="PERMANENT_POOL"?permanent:transient)[resource.poolIndex].rt;}
            throw new InvalidOperationException("NRD history diagnostic input missing: "+name);
        }
        private static void ValidateSpecular()
        {
            surfaceMaterial.SetFloat("_Metallic",1);surfaceMaterial.SetColor("_Color",Color.white);
            light.type=LightType.Directional;
            var other=Keep(new GameObject("GGX colored directional")).AddComponent<Light>();other.type=LightType.Directional;
            var texture=Keep(new Texture2D(64,1,TextureFormat.RGBA32,false,true));texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            for(int x=0;x<64;x++)texture.SetPixel(x,0,(x/2)%2==0?Color.red:Color.blue);texture.Apply();surfaceMaterial.SetTexture("_MainTex",texture);
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            foreach(float rough in new[]{.1f,.3f,.7f})foreach(float angle in new[]{0f,20f,45f})
            {
                surfaceMaterial.SetFloat("_Smoothness",1-rough);
                light.transform.rotation=Quaternion.Euler(0,angle,0);other.transform.rotation=Quaternion.Euler(0,-angle*.7f,0);
                light.color=new Color(1,.2f,.1f);other.color=new Color(.1f,.3f,1);
                light.intensity=.02f;other.intensity=.01f;
                feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
                for(int i=0;i<96;i++)RenderCamera();var c=feature.LastContext;
                float decoded=Mathf.Abs(ReadCenter(c.nrdNormal.rt).b*2-1);float r=Mathf.Round(decoded*255)/255;
                Near(decoded,rough,"GGX material roughness guide",.004f);
                var L=-light.transform.forward;var L2=-other.transform.forward;var lc=LightColor(light)*light.intensity;var lc2=LightColor(other)*other.intensity;
                var red=ReferenceGGX(L,r,Color.red)*lc+ReferenceGGX(L2,r,Color.red)*lc2;
                var blueRef=ReferenceGGX(L,r,Color.blue)*lc+ReferenceGGX(L2,r,Color.blue)*lc2;
                Color rawSum=Color.clear,filteredSum=Color.clear;int count=0;
                for(int frame=0;frame<96;frame++)
                {
                    RenderCamera();ReadImage(c.specular.rt,raw);ReadImage(c.filteredSpecular.rt,filtered);
                    for(int y=16;y<48;y++)for(int x=16;x<48;x++){rawSum+=raw.GetPixel(x,y);filteredSum+=filtered.GetPixel(x,y);count++;}
                }
                var reference=(red+blueRef)*.5f;var a=rawSum/count;var b=filteredSum/count;
                Debug.Log($"[HoRTDIValidation] GGX rough={r:F4} angle={angle} raw={a.ToString("F6")} filtered={b.ToString("F6")} reference={reference.ToString("F6")} relative={Math.Abs((b.r+b.g+b.b)/(reference.r+reference.g+reference.b)-1):F6}");
                NearRGB(a,reference,"Independent GGX raw mean",Mathf.Max(.0000002f,(reference.r+reference.g+reference.b)*.04f));
                Require(Math.Abs((b.r+b.g+b.b)/(reference.r+reference.g+reference.b)-1)<.05,"GGX angular filtered energy exceeds 5% budget");
            }
            Debug.Log("[HoRTDIValidation] PASS nine independent low-intensity GGX angular/color/roughness gates; compatibility="+compatibility);
            if(!requireHDR)return;
            surfaceMaterial.SetFloat("_Smoothness",.9f);surfaceMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);
            light.transform.rotation=other.transform.rotation=Quaternion.identity;light.color=other.color=Color.white;light.intensity=.5f;other.intensity=.25f;
            feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;for(int i=0;i<96;i++)RenderCamera();
            var hdrContext=feature.LastContext;var hdrRaw=ReadCenter(hdrContext.specular.rt);var hdrFiltered=ReadCenter(hdrContext.filteredSpecular.rt);
            int nonFinite=0;float momentMax=0;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(var pool in new[]{"permanent","transient"})foreach(var handle in (RTHandle[])hdrContext.nrd.GetType().GetField(pool,flags).GetValue(hdrContext.nrd))
            {
                if(handle.rt.graphicsFormat!=UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat)continue;
                var image=Keep(new Texture2D(handle.rt.width,handle.rt.height,TextureFormat.RGBAFloat,false,true));ReadImage(handle.rt,image);
                foreach(var pixel in image.GetPixels()){if(!float.IsFinite(pixel.r)||!float.IsFinite(pixel.g)||!float.IsFinite(pixel.b)||!float.IsFinite(pixel.a))nonFinite++;else momentMax=Mathf.Max(momentMax,pixel.a);}
            }
            Debug.Log($"[HoRTDIValidation] GGX_HDR raw={hdrRaw} filtered={hdrFiltered} nonFinitePoolPixels={nonFinite} poolAlphaMax={momentMax}");
            Color hdrRawSum=Color.clear,hdrFilteredSum=Color.clear;int hdrCount=0;
            for(int i=0;i<96;i++){RenderCamera();ReadImage(hdrContext.specular.rt,raw);ReadImage(hdrContext.filteredSpecular.rt,filtered);for(int y=16;y<48;y++)for(int x=16;x<48;x++){hdrRawSum+=raw.GetPixel(x,y);hdrFilteredSum+=filtered.GetPixel(x,y);hdrCount++;}}
            float hdrRough=Mathf.Round(Mathf.Abs(ReadCenter(hdrContext.nrdNormal.rt).b*2-1)*255)/255;
            var hdrReference=ReferenceGGX(Vector3.back,hdrRough,Color.white)*.75f;
            Debug.Log($"[HoRTDIValidation] GGX_HDR_MEAN raw={hdrRawSum/hdrCount} filtered={hdrFilteredSum/hdrCount} reference={hdrReference}");
            Require(nonFinite==0,"Non-finite HDR pool pixel");
            NearRGB(hdrRawSum/hdrCount,hdrReference,"HDR raw energy",hdrReference.r*.04f);
            NearRGB(hdrFilteredSum/hdrCount,hdrReference,"HDR filtered energy",hdrReference.r*.05f);
            var nrdType=hdrContext.nrd.GetType();
            var passArray=(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.DispatchDesc[])nrdType.GetField("passes",flags).GetValue(hdrContext.nrd);
            var resourceArray=(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdNative.ResourceDesc[,])nrdType.GetField("resources",flags).GetValue(hdrContext.nrd);
            var permanent=(RTHandle[])nrdType.GetField("permanent",flags).GetValue(hdrContext.nrd);
            for(int i=0;i<hdrContext.nrd.LastDispatchCount;i++)if(passArray[i].pipeline==4)
            {
                var names=lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdManifest.Resources[4];
                for(int j=0;j<names.Length;j++)if(names[j]=="gHistory_Spec")
                {var sample=ReadCenter(permanent[resourceArray[i,j].poolIndex].rt);Debug.Log($"[HoRTDIValidation] GGX_HDR_MOMENT mean={sample.r:F3} second={sample.a:F3} variance={sample.a-sample.r*sample.r:F3}; second must exceed square of mean for the sampled HDR signal");Require(float.IsFinite(sample.a)&&sample.a>=sample.r*sample.r*.99f&&sample.a<20000,"HDR second moment saturated below mean squared or exhausted headroom");}
            }
            // This sequence keeps one NRD context/history alive. The 16+8 case
            // exceeds FP16 before demodulated input scaling, while raw stays in
            // its existing FP16 output range. A later dark frame may grow its
            // scale only when the existing history also fits the new domain.
            float oldScale=ReadNrdScale(hdrContext);
            foreach(float intensity in new[]{16f,.0005f,.5f})
            {
                light.intensity=intensity;other.intensity=intensity*.5f;
                RenderCamera();float scale=ReadNrdScale(hdrContext);
                Require(scale>0&&scale<=1,"NRD scale outside finite linear domain");
                if(intensity==16)Require(scale<oldScale,"HDR peak did not reduce scale");oldScale=scale;
                for(int frame=0;frame<96;frame++)RenderCamera();
                Color sum=Color.clear,rawTotal=Color.clear;int samples=0;
                for(int frame=0;frame<96;frame++)
                {
                    RenderCamera();ReadImage(hdrContext.filteredSpecular.rt,filtered);ReadImage(hdrContext.specular.rt,raw);
                    for(int y=16;y<48;y++)for(int x=16;x<48;x++){sum+=filtered.GetPixel(x,y);rawTotal+=raw.GetPixel(x,y);samples++;}
                }
                var reference=ReferenceGGX(Vector3.back,hdrRough,Color.white)*(intensity*1.5f);
                Debug.Log($"[HoRTDIValidation] HDR_SCALE intensity={intensity} initialScale={scale} settledScale={ReadNrdScale(hdrContext)} raw={rawTotal/samples} filtered={sum/samples} reference={reference}");
                if(intensity==.0005f)Require(ReadNrdScale(hdrContext)>scale,"NRD dark-domain precision did not recover");
                NearRGB(rawTotal/samples,reference,"HDR changing-light raw energy",reference.r*.04f);
                NearRGB(sum/samples,reference,"HDR changing-light filtered energy",reference.r*.05f);
                for(int i=0;i<hdrContext.nrd.LastDispatchCount;i++)if(passArray[i].pipeline==4)
                    for(int j=0;j<lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdManifest.Resources[4].Length;j++)if(lilToon.URP.Extensions.RTDI.NRD.HoRTDINrdManifest.Resources[4][j]=="gHistory_Spec")
                    {
                        var sample=ReadCenter(permanent[resourceArray[i,j].poolIndex].rt);
                        Debug.Log($"[HoRTDIValidation] HDR_SCALE_MOMENT intensity={intensity} mean={sample.r:E6} second={sample.a:E6} variance={sample.a-sample.r*sample.r:E6}");
                        // Several FP16 writes can round subnormal moments by a
                        // few 2^-24 ULPs. Retain the relative saturation gate at
                        // larger values, and an explicit absolute bound here.
                        Require(float.IsFinite(sample.a)&&sample.a<20000&&sample.a+4f/16777216>=sample.r*sample.r*.99f,"Changing-scale HDR moment overflow");
                    }
            }
            // Bright/dark F0 regions share the scale. Compare each against the
            // analytic reference so precision loss cannot hide in a frame mean.
            for(int x=0;x<64;x++)texture.SetPixel(x,0,x<32?Color.white:new Color(.04f,.04f,.04f,1));texture.Apply();surfaceMaterial.SetTexture("_MainTex",texture);
            light.intensity=16;other.intensity=8;
            for(int frame=0;frame<96;frame++)RenderCamera();
            Color bright=Color.clear,dark=Color.clear;int regionCount=0;
            for(int frame=0;frame<96;frame++)
            {
                RenderCamera();ReadImage(hdrContext.filteredSpecular.rt,filtered);
                for(int y=16;y<48;y++)for(int x=16;x<24;x++){bright+=filtered.GetPixel(x,y);dark+=filtered.GetPixel(x+24,y);regionCount++;}
            }
            var brightRef=ReferenceGGX(Vector3.back,hdrRough,Color.white)*24;
            var darkRef=brightRef*(10f/255); // RGBA32 F0 quantization of 0.04.
            Debug.Log($"[HoRTDIValidation] HDR_MIX bright={bright/regionCount} dark={dark/regionCount} references={brightRef}/{darkRef} scale={ReadNrdScale(hdrContext)}");
            NearRGB(bright/regionCount,brightRef,"Shared-scale bright region",brightRef.r*.05f);
            NearRGB(dark/regionCount,darkRef,"Shared-scale dark region",darkRef.r*.05f);
            // Denoiser disposal/rebuild owns a fresh scale and permits recovery.
            light.intensity=other.intensity=.0001f;feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;RenderCamera();
            Require(ReadNrdScale(feature.LastContext)==1,"NRD scale did not reset on resource recreation");
            Debug.Log("[HoRTDIValidation] PASS HDR moment range; compatibility="+compatibility);
        }
        private static float ReadNrdScale(HoRTDIContext context)
        {var words=new uint[4];context.nrdScale.GetData(words);return BitConverter.Int32BitsToSingle((int)words[1]);}
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
            if(context.nrdScale!=null)result+=(long)context.nrdScale.count*context.nrdScale.stride;
            foreach(var texture in new[]{context.diffuse,context.specular,context.nrdDiffuse,context.nrdSpecular,context.nrdNormal,context.nrdViewZ,context.nrdOutputDiffuse,context.nrdOutputSpecular,context.previousLuminance,context.gradientA,context.gradientB,context.diffConfidence,context.specConfidence})result+=Payload(texture);
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
            Require(ReferenceEquals(context.filteredDiffuse,context.nrdOutputDiffuse)&&ReferenceEquals(context.filteredSpecular,context.nrdOutputSpecular),"Filtered publication allocated duplicate external outputs");
            Require(!ReferenceEquals(context.diffuse,context.filteredDiffuse),"In-place NRD resolve overwrote raw DI");
            Debug.Log("[HoRTDIValidation] NRD single raw="+ReadMean(context.diffuse.rt)+" filtered="+ReadMean(context.filteredDiffuse.rt)+" dispatches="+context.nrd.LastDispatchCount);
            Near(ReadMean(context.filteredDiffuse.rt).r,ExpectedMean(light),"NRD single-light energy",.03f);
            Near(ReadCenter(context.diffConfidence.rt).r,1,"Stable lighting reported a confidence gradient",.02f);
            light.color=Color.red;RenderCamera();
            Require(!context.lastTemporal,"Confidence independence probe unexpectedly reused reservoirs");
            Require(ReadCenter(context.diffConfidence.rt).r<.5f,"NRD confidence ignored light changes while RTXDI temporal was disabled");
            light.color=Color.white;for(int i=0;i<8;i++)RenderCamera();
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
            Near(ReadCenter(context.diffConfidence.rt).r,1,"Static stochastic lighting caused false confidence loss",.03f);
            var confidenceImage=Keep(new Texture2D(context.diffConfidence.rt.width,context.diffConfidence.rt.height,TextureFormat.RGBAFloat,false,true));ReadImage(context.diffConfidence.rt,confidenceImage);
            float confidenceMinimum=1;double confidenceMean=0;
            for(int y=0;y<confidenceImage.height;y++)for(int x=0;x<confidenceImage.width;x++){float value=confidenceImage.GetPixel(x,y).r;confidenceMinimum=Mathf.Min(confidenceMinimum,value);confidenceMean+=value;}
            confidenceMean/=confidenceImage.width*confidenceImage.height;
            Debug.Log($"[HoRTDIValidation] static stochastic confidence min={confidenceMinimum:F6} mean={confidenceMean:F6}");
            Require(confidenceMinimum>.97f,"Static RNG changes were interpreted as lighting gradients");
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
        private static void RenderCamera(){if((forceBoundaries||forceSpecular||forceLocalLighting)&&feature.LastContext?.nrd!=null)feature.LastContext.nrd.ValidationTimeSeconds=++boundarySample/60.0;VolumeManager.instance.Update(camera.transform,-1);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
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
