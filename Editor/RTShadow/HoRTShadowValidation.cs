#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        [Serializable] private sealed class Report
        {public bool pass,compatibility,rawOnly,sigma;public string failure;public int requests,fractionalPixels,distinctLevels;public float toonShadow,toonReceiveOff,toonLit,rawMse,filteredMse;public List<string> checks=new();}
        private static readonly List<Object> objects=new();
        private static Report report; private static bool compatibility,previousAsync,rawOnly; private static int tick;
        private static RenderPipelineAsset previousDefault,previousQuality;
        private static HoRTRendererFeature host;private static HoRTShadowRendererFeature shadow;
        private static HoGeometryBufferRendererFeature gb;private static HoSurfaceBufferRendererFeature sb;
        private static HoRTShadowLifetimeProbeFeature lifetime;
        private static HoRTShadowReceiver receiver;private static HoObjectBufferGroup group;
        private static Renderer near,far;private static Material alpha,toon;private static Camera camera;private static Light light;
        private static RenderTexture target;private static uint bodyId,detailId;
        private static readonly Vector3 Direction=new Vector3(2.5f,0,-4).normalized;
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void RunRaw(){rawOnly=true;Run();}
        public static void RunRawCompatibility(){compatibility=true;RunRaw();}
        public static void RunRasterFallback()
        {try{CharacterShadow.HoCharacterShadowValidation.ValidateRendering();Debug.Log("[HoRTShadowRasterFallback] PASS");EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            try
            {
                if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Independent batch project required");
                if(!HoRTScene.IsSupported||!SystemInfo.supportsRayTracing)throw new Exception("D3D12 native RT required");
                previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                report=new Report{compatibility=compatibility,rawOnly=rawOnly,sigma=sigmaAudit};EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                host=Keep(ScriptableObject.CreateInstance<HoRTRendererFeature>());gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
                sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                shadow=Keep(ScriptableObject.CreateInstance<HoRTShadowRendererFeature>());shadow.Settings.geometryLayers=1<<29;
                lifetime=Keep(ScriptableObject.CreateInstance<HoRTShadowLifetimeProbeFeature>());lifetime.shadow=shadow;
                renderer.rendererFeatures.Add(host);renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(shadow);renderer.rendererFeatures.Add(lifetime);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.msaaSampleCount=1;pipeline.supportsHDR=true;
                if(nprIntegration){var serializedPipeline=new SerializedObject(pipeline);serializedPipeline.FindProperty("m_MainLightShadowsSupported").boolValue=false;serializedPipeline.ApplyModifiedPropertiesWithoutUndo();}
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                camera=Keep(new GameObject("RTShadow camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2;camera.aspect=1;
                camera.transform.position=new Vector3(0,0,-3);camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                target=Target(64);camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                var holder=Keep(new GameObject("RTShadow volumes")).AddComponent<Volume>();holder.isGlobal=true;holder.sharedProfile=Keep(ScriptableObject.CreateInstance<VolumeProfile>());holder.sharedProfile.Add<HoSurfaceBufferVolume>(true);
                light=Keep(new GameObject("RTShadow main light")).AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.LookRotation(-Direction);light.shadows=LightShadows.Soft;RenderSettings.sun=light;
                var body=Quad(new Vector3(0,0,0),new Vector3(3,3,1),false);var detail=Quad(new Vector3(1,0,-.05f),new Vector3(.5f,1,1),false);
                toon=Keep(new Material(Shader.Find("lilToon")));body.sharedMaterial=toon;toon.SetColor("_Color",Color.white);toon.SetTexture("_MainTex",Texture2D.whiteTexture);toon.SetFloat("_Cull",0);
                toon.SetFloat("_UseShadow",1);toon.SetFloat("_ShadowReceive",1);toon.SetFloat("_ShadowStrength",1);toon.SetFloat("_LightMinLimit",0);toon.SetColor("_ShadowColor",Color.black);
                // Cast/ramp oracle has no AO producer. The material's default
                // realtime AO otherwise samples a fallback grey and masks Cast.
                if(!rawOnly||nprIntegration)toon.SetFloat("_UseRealtimeAO",0);
                group=Keep(new GameObject("RTShadow generic receiver group")).AddComponent<HoObjectBufferGroup>();
                group.parts=new List<HoObjectBufferPartEntry>{new(){name="body",renderers=new Object[]{body}},new(){name="detail",renderers=new Object[]{detail}}};group.Apply();
                bodyId=HoObjectBufferRegistry.GetPartId(group.groupId,"body");detailId=HoObjectBufferRegistry.GetPartId(group.groupId,"detail");Check(bodyId!=0&&detailId!=0&&bodyId!=detailId,"Stable distinct receiver identities");
                receiver=group.gameObject.AddComponent<HoRTShadowReceiver>();receiver.objectGroup=group;
                near=Quad(new Vector3(2.5f,0,-4),new Vector3(1.2f,3,1),true);far=Quad(new Vector3(3.75f,0,-6),new Vector3(1.2f,3,1),true);
                alpha=near.sharedMaterial;near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                if(thinAudit)SetupThinRig();
                if(multiAudit)SetupMultiLight();
                EditorApplication.update+=Tick;Debug.Log("[HoRTShadowValidation] START compatibility="+compatibility);
            }
            catch(Exception error){Finish(error);}
        }
        private static Renderer Quad(Vector3 position,Vector3 scale,bool caster)
        {
            var go=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));go.layer=29;go.transform.position=position;go.transform.localScale=scale;
            var material=Keep(new Material(Shader.Find("Hidden/lilToonCutout")));material.SetFloat("_Cull",0);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetColor("_Color",Color.white);material.SetFloat("_Cutoff",.5f);
            foreach(var name in new[]{"_UseMain2ndTex","_UseMain3rdTex","_UseParallax","_UseDither","_AlphaMaskMode"})material.SetFloat(name,0);
            material.SetVector("_MainTex_ScrollRotate",Vector4.zero);material.SetVector("_DissolveParams",Vector4.zero);material.SetVector("_DistanceFade",Vector4.zero);
            material.renderQueue=2450;material.SetOverrideTag("RenderType","TransparentCutout");
            var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=caster?ShadowCastingMode.TwoSided:ShadowCastingMode.Off;return renderer;
        }
        private static void Tick()
        {
            try
            {
                if(++tick<3){Render();return;}
                if(safePolicyAudit){ValidateSafePolicy();Finish(null);return;}
                if(curvedAudit){ValidateCurvedNormals();Finish(null);return;}
                if(selfAudit){ValidateSelfVisibility();Finish(null);return;}
                if(inputOrderAudit){ValidateInputOrder();Finish(null);return;}
                if(spatialAudit){ValidateSpatialSampling();Finish(null);return;}
                if(uiAudit){ValidateControls();Finish(null);return;}
                if(multiAudit){ValidateMultiLight();Finish(null);return;}
                if(thinAudit){ValidateThinRig();Finish(null);return;}
                if(singleFrameAudit){ValidateSingleFrameQuality();Finish(null);return;}
                if(nprIntegration){ValidateNprIntegration();Finish(null);return;}
                if(sigmaAudit){ValidateProductionSigma();Finish(null);return;}
                var image=Render();Valid(image,32,32,0,bodyId,"Opaque offscreen shadow");Near(image[32*64+32].g,(-4)/Direction.z,"Nearest blocker distance",.005f);
                Valid(image,16,32,1,bodyId,"Unblocked receiver");Valid(image,48,32,1,detailId,"Second receiver identity");
                Check(shadow.LastOutput.light==light,"Output keeps exact main light identity");Check(shadow.LastOutput.raw.rt.width==64&&shadow.LastOutput.raw.rt.height==64,"Full-resolution 32F output");
                Check(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),near.bounds),"Caster is outside camera frustum");
                if(!rawOnly)
                {
                    report.toonShadow=Read(target)[32*64+32].r;
                    toon.SetFloat("_ShadowReceive",0);Render();report.toonReceiveOff=Read(target)[32*64+32].r;toon.SetFloat("_ShadowReceive",1);
                    near.shadowCastingMode=ShadowCastingMode.Off;far.shadowCastingMode=ShadowCastingMode.Off;Render();report.toonLit=Read(target)[32*64+32].r;
                    Check(report.toonLit>report.toonShadow+.05f&&report.toonReceiveOff>report.toonShadow+.05f,"Actual lilToon ramp responds to RT Cast and respects material receive switch: "+report.toonShadow+" / "+report.toonReceiveOff+" / "+report.toonLit);
                    near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                }
                alpha.SetColor("_Color",new Color(1,1,1,0));image=Render();Valid(image,32,32,0,bodyId,"Alpha hole reveals far blocker");Near(image[32*64+32].g,(-6)/Direction.z,"Far actual blocker distance",.005f);
                far.shadowCastingMode=ShadowCastingMode.Off;image=Render();Valid(image,32,32,1,bodyId,"Both blockers absent");Near(image[32*64+32].g,65504,"Miss sentinel",.1f);
                alpha.SetColor("_Color",Color.white);image=Render();Valid(image,32,32,0,bodyId,"Alpha restored");
                receiver.receiveWholeGroup=false;receiver.receiverParts.Add("body");image=Render();Valid(image,32,32,0,bodyId,"Selected part receives");Check(image[32*64+48]==Color.clear,"Unrequested part stays invalid instead of visible");
                receiver.receiverParts.Clear();Render(false);Check(Shader.GetGlobalFloat("_HoRTShadowActive")==0&&shadow.LastOutput==null,"Empty selection disables publication");receiver.receiveWholeGroup=true;
                gb.Settings.renderScale=HoGeometryBufferRenderScale.Half;image=Render();Check(image[32*64+32].b==2,"Mismatched surface sample domain rejected");gb.Settings.renderScale=HoGeometryBufferRenderScale.Full;
                sb.SetActive(false);image=Render();Check(image[32*64+32].b==2,"Missing SB rejected");sb.SetActive(true);
                host.SetActive(false);Render(false);Check(Shader.GetGlobalFloat("_HoRTShadowActive")==0,"Missing host explicitly disables output");host.SetActive(true);
                shadow.SetActive(false);Render(false);Check(Shader.GetGlobalFloat("_HoRTShadowActive")==0,"Disabled feature never leaks previous camera output");shadow.SetActive(true);
                light.cullingMask=1<<29;Render(false);Check(Shader.GetGlobalFloat("_HoRTShadowActive")==0,"Unsupported restricted light does not publish all-world shadow");light.cullingMask=-1;
                target=Target(96);image=Render();Check(image.Length==96*96&&shadow.LastOutput.raw.rt.width==96,"Resize follows full camera domain");target=Target(64);
                shadow.Settings.angularDiameter=8;shadow.Settings.samplesPerPixel=256;image=Render();var levels=new HashSet<float>();
                bool finite=true;foreach(var pixel in image)if(pixel.b==1){finite&=pixel.r>=0&&pixel.r<=1;if(pixel.r>0&&pixel.r<1){report.fractionalPixels++;levels.Add(pixel.r);}}
                Check(finite,"All valid samples stay within physical visibility range");
                report.distinctLevels=levels.Count;Check(report.fractionalPixels>20&&levels.Count>8,"Finite emitter produces actual continuous visibility samples");
                var repeated=Render();bool same=true;for(int i=0;i<image.Length;i++)same&=image[i]==repeated[i];Check(same,"Fixed-seed same-frame estimator has no hidden temporal history");
                shadow.Settings.angularDiameter=0;shadow.Settings.samplesPerPixel=1;image=Render();Valid(image,32,32,0,bodyId,"Hard reference restored");
                lifetime.retire=true;Render(false);Check(lifetime.heldBeforeFence,"Consumer resources retained through mid-frame Create");
                HoRTRendererFeature.CollectRetired();image=Render();Valid(image,32,32,0,bodyId,"Recreated feature resumes production");HoRTRendererFeature.CollectRetired();Check(HoRTRendererFeature.RetiredResourceCount==0,"Deferred consumer resources released after completion");
                var after=Read(shadow.LastOutput.raw.rt);File.WriteAllBytes(Path.Combine(Folder(),"raw.rgba32"),Bytes(after));
                Debug.Log("[HoRTShadowValidation] PASS scope="+(rawOnly?"raw-only; NPR acceptance pending":"NPR")+" raw/identity/nearest/alpha/continuous/offscreen/domain/lifetime; checks="+report.checks.Count+" requests="+report.requests+" fractional="+report.fractionalPixels+" levels="+report.distinctLevels);
                Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private static byte[] Bytes(Color[] pixels){var f=new float[pixels.Length*4];for(int i=0;i<pixels.Length;i++){f[i*4]=pixels[i].r;f[i*4+1]=pixels[i].g;f[i*4+2]=pixels[i].b;f[i*4+3]=pixels[i].a;}var bytes=new byte[f.Length*4];Buffer.BlockCopy(f,0,bytes,0,bytes.Length);return bytes;}
        private static RenderTexture Target(int size){var texture=Keep(new RenderTexture(size,size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));texture.Create();return texture;}
        private static Color[] Render(bool read=true)
        {report.requests++;VolumeManager.instance.Update(camera.transform,camera.GetUniversalAdditionalCameraData().volumeLayerMask);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});if(!read)return null;Check(shadow.LastOutput!=null,"Shadow request scheduled: "+shadow.Status);return Read(shadow.LastOutput.raw.rt);}
        private static Color[] Read(RenderTexture source)
        {var old=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);try{RenderTexture.active=source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();return image.GetPixels();}finally{RenderTexture.active=old;Object.DestroyImmediate(image);}}
        private static void Valid(Color[] image,int x,int y,float v,uint owner,string label){var p=image[y*64+x];Check(p.b==1&&p.a==owner,label+" status/identity: "+p);Near(p.r,v,label+" visibility",.0001f);}
        private static void Near(float a,float b,string label,float tolerance){Check(Mathf.Abs(a-b)<tolerance,label+": "+a+" / "+b);}
        private static void Check(bool condition,string text){if(!condition)throw new Exception(text);report.checks.Add(text);}
        private static T Keep<T>(T value)where T:Object{objects.Add(value);return value;}
        private static string Folder(){var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-shadow"));Directory.CreateDirectory(folder);return folder;}
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;
            if(report!=null){report.pass=error==null;report.failure=error?.ToString();File.WriteAllText(Path.Combine(Folder(),"report.json"),JsonUtility.ToJson(report,true));}
            GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;ShaderUtil.allowAsyncCompilation=previousAsync;
            shadow?.Dispose();host?.Dispose();HoRTRendererFeature.CollectRetired();
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();
            if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
        }
    }
    [SupportedOnRenderer()]
    internal sealed class HoRTShadowLifetimeProbeFeature:ScriptableRendererFeature
    {
        internal HoRTShadowRendererFeature shadow;internal bool retire,heldBeforeFence;private ProbePass pass;
        public override void Create(){pass=new ProbePass();}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(retire){pass.owner=this;renderer.EnqueuePass(pass);}}
        private void Recreate(){retire=false;var old=shadow.LastOutput;shadow.Create();heldBeforeFence=old!=null&&old.raw.rt.IsCreated()&&old.receivers.IsValid()&&HoRTRendererFeature.RetiredResourceCount>0;}
        private sealed class ProbePass:ScriptableRenderPass
        {
            internal HoRTShadowLifetimeProbeFeature owner;internal ProbePass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            private sealed class Data{internal HoRTShadowLifetimeProbeFeature owner;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame){using(var b=graph.AddUnsafePass<Data>("RTShadow resource retirement probe",out var d)){d.owner=owner;b.AllowGlobalStateModification(true);b.AllowPassCulling(false);b.SetRenderFunc(static(Data d,UnsafeGraphContext context)=>d.owner.Recreate());}}
            public override void Execute(ScriptableRenderContext context,ref RenderingData data){owner.Recreate();}
        }
    }
}
