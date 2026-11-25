#pragma warning disable CS0618, CS0672

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GTAO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoCapsuleAOIntegrationTests
    {
        public static void EnableCompatibility()
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            if (!symbols.Contains("URP_COMPATIBILITY_MODE"))
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, symbols + ";URP_COMPATIBILITY_MODE");
        }

        [UnityTest] public IEnumerator ActualPipelineOverlaysReceiverColor() => Validate(false);
        [UnityTest] public IEnumerator ActualPipelineMsaa4OverlaysReceiverColor() => Validate(false, 4);
        [UnityTest] public IEnumerator ActualPipelinePerspectiveOverlaysReceiverColor() => Validate(false, 1, true);
#if URP_COMPATIBILITY_MODE
        [UnityTest] public IEnumerator CompatibilityOverlaysReceiverColor() => Validate(true);
#endif

        private static IEnumerator Validate(bool compatibility, int msaa=1, bool perspective=false)
        {
            var created = new List<Object>();
            T Keep<T>(T x) where T : Object { created.Add(x); return x; }
            RenderPipelineAsset oldDefault = GraphicsSettings.defaultRenderPipeline, oldQuality = QualitySettings.renderPipeline;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            RenderTexture oldActive = RenderTexture.active;
            RTHandle probeOutput = null;
#if URP_COMPATIBILITY_MODE
            RenderGraphSettings graphSettings = null;
            bool oldCompatibility = false;
#endif
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var geometry = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
                var gtao = Keep(ScriptableObject.CreateInstance<HoGTAORendererFeature>());
                var character = Keep(ScriptableObject.CreateInstance<HoCharacterSpecializationRendererFeature>());
                gtao.Settings.publishAOTextureOnly = true;
                geometry.SetActive(false);gtao.SetActive(false);
                renderer.rendererFeatures.Add(geometry); renderer.rendererFeatures.Add(gtao); renderer.rendererFeatures.Add(character);
                var probe = Keep(ScriptableObject.CreateInstance<HoCapsuleAOProbeFeature>());
                probe.material = Keep(new Material(Shader.Find("Hidden/HoCapsuleAO/Fixture")));
                renderer.rendererFeatures.Add(probe);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                pipeline.msaaSampleCount = msaa; pipeline.supportsHDR = true; pipeline.shadowDistance = 0;
                pipeline.supportsCameraDepthTexture = true;
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
#if URP_COMPATIBILITY_MODE
                typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings",true)
                    .GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{true});
                graphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
                Assert.That(graphSettings,Is.Not.Null,"The isolated URP fixture must register its global graphics settings before selecting a backend.");
                oldCompatibility = graphSettings.enableRenderCompatibilityMode;
                graphSettings.enableRenderCompatibilityMode = compatibility;
#endif
                var volume = Keep(new GameObject("Capsule AO integration volume")).AddComponent<Volume>();
                volume.isGlobal = true; volume.priority = 10000;
                volume.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                var effects = volume.sharedProfile.Add<HoCharacterSpecializationVolume>(true).Effects.value;
                effects.eyeRevealEnabled = effects.hairDropShadowEnabled = effects.faceHairDiffuseEnabled
                    = effects.subjectOutlineEnabled = effects.enhancedOutlineEnabled = false;
                effects.capsuleAOEnabled = true; effects.capsuleAOStrength = .4f;
                var gtaoVolume = volume.sharedProfile.Add<HoGTAOVolume>();
                gtaoVolume.enable.Override(false); gtaoVolume.publishAOTextureOnly.Override(true);
                var camera = Keep(new GameObject("Capsule AO integration camera")).AddComponent<Camera>();
                camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor;
                Vector3 cameraPosition=perspective?new Vector3(0,2,-1.5f):new Vector3(0,2,0);
                camera.transform.SetPositionAndRotation(cameraPosition,Quaternion.LookRotation(-cameraPosition,perspective?Vector3.up:Vector3.forward));
                camera.orthographic = !perspective; camera.fieldOfView=50; camera.orthographicSize = 1; camera.aspect = 97f / 65;
                camera.nearClipPlane = .01f; camera.farClipPlane = 10;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                var target = Keep(new RenderTexture(97,65,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){antiAliasing=msaa});target.Create();
                var captured = Keep(new RenderTexture(97,65,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));captured.Create();
                probeOutput = RTHandles.Alloc(captured); probe.destination = probeOutput;
                var image = Keep(new Texture2D(97,65,TextureFormat.RGBAFloat,false,true));
                var ground = Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = new Vector3(.2f,1,.2f);
                var floorMaterial = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                ground.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var receiver = ground.AddComponent<HoCapsuleAOReceiver>();
                var emitter = Keep(new GameObject("Capsule AO integration foot")).AddComponent<HoCapsuleAOEmitter>();
                emitter.transform.position = new Vector3(0,.06f,0);
                string originalMaterial = EditorJsonUtility.ToJson(floorMaterial);
                yield return null;
                void Render()
                {
                    if (VolumeManager.instance.stack != null)
                        VolumeManager.instance.Update(VolumeManager.instance.stack,camera.transform,-1);
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                }
                float Read(float x=.5f,float y=.5f)
                {
                    RenderTexture.active=captured;image.ReadPixels(new Rect(0,0,97,65),0,0);image.Apply();
                    return image.GetPixel(Mathf.Clamp((int)(x*97),0,96),Mathf.Clamp((int)(y*65),0,64)).r;
                }
                float ReadColor(float x=.5f,float y=.5f)
                {
                    RenderTexture source=target;
                    if(target.antiAliasing>1)
                    {
                        var resolved=Keep(new RenderTexture(target.width,target.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));resolved.Create();
                        target.ResolveAntiAliasedSurface(resolved);source=resolved;
                    }
                    var pixels=Keep(new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true));
                    RenderTexture.active=source;pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();
                    return pixels.GetPixel(Mathf.Clamp((int)(x*source.width),0,source.width-1),Mathf.Clamp((int)(y*source.height),0,source.height-1)).r;
                }
                effects.capsuleAOEnabled=false;Render();yield return null;Render();float baseline=ReadColor();
                Assert.That(baseline,Is.GreaterThan(.9f));
                effects.capsuleAOEnabled=true;Render();
                Assert.That(character.CapsuleAOActiveCount,Is.EqualTo(1),character.CapsuleAOStatus);
                Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f),character.CapsuleAOStatus);
                Assert.That(probe.hasAO,Is.False,"Receiver overlay must work with no HoAO producer or GB.");
                Assert.That(ReadColor(.75f,.5f),Is.EqualTo(baseline).Within(.008f));
                effects.capsuleAOMode=HoCapsuleAOMode.Cross;Render();
                Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f),character.CapsuleAOStatus);
                emitter.transform.position=Vector3.zero;Render();
                Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f),"Cross disappeared when the bone pivot was on the floor and the capsule penetrated it.");
                ground.transform.rotation=Quaternion.Euler(15,0,0);Render();
                Assert.That(ReadColor(),Is.LessThan(baseline*.75f),"Cross disappeared on a sloped receiver.");
                ground.transform.rotation=Quaternion.identity;
                emitter.transform.position=new Vector3(0,.8f,0);Render();Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f));
                emitter.transform.position=new Vector3(0,.06f,0);
                receiver.enabled=false;Render();Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f));
                receiver.enabled=true;emitter.enabled=false;Render();Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f));
                emitter.enabled=true;
                var overlap=Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));
                overlap.transform.localScale=ground.transform.localScale;
                overlap.GetComponent<Renderer>().sharedMaterial=floorMaterial;overlap.AddComponent<HoCapsuleAOReceiver>();
                Render();Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f),"Overlapping receivers multiplied the same shadow twice.");
                overlap.SetActive(false);
                var front=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
                front.transform.position=new Vector3(0,.15f,0);front.transform.localScale=new Vector3(.2f,.1f,.2f);
                front.GetComponent<Renderer>().sharedMaterial=floorMaterial;Render();
                Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f),"Unregistered foreground was darkened.");front.SetActive(false);
                var firstCamera=camera;
                camera=Keep(new GameObject("Second receiver camera")).AddComponent<Camera>();camera.CopyFrom(firstCamera);camera.enabled=false;
                camera.transform.SetPositionAndRotation(new Vector3(.7f,2,0),Quaternion.LookRotation(Vector3.down,Vector3.forward));
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                Render();Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f),"Another camera reused the foot projection.");
                camera=firstCamera;
                var staticRoot=Keep(new GameObject("Static receiver root"));ground.transform.SetParent(staticRoot.transform,true);
                var distant=Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));distant.transform.position=new Vector3(20,0,0);
                distant.GetComponent<Renderer>().sharedMaterial=floorMaterial;StaticBatchingUtility.Combine(new[]{ground,distant},staticRoot);
                Assert.That(ground.GetComponent<MeshRenderer>().isPartOfStaticBatch,Is.True);
                Render();Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f),"Static batch redraw failed.");
                emitter.transform.position=new Vector3(.4f,.06f,0);Render();var moved=camera.WorldToViewportPoint(new Vector3(.4f,0,0));
                Assert.That(ReadColor(moved.x,moved.y),Is.LessThan(baseline*.8f));
                Assert.That(ReadColor(),Is.EqualTo(baseline).Within(.008f));emitter.transform.position=new Vector3(0,.06f,0);
                // HoAO stays unchanged, in white mode and in computed GTAO mode.
                geometry.SetActive(true);gtao.SetActive(true);gtaoVolume.enable.value=true;
                foreach(bool white in compatibility?new[]{true}:new[]{true,false})
                {
                    gtaoVolume.publishAOTextureOnly.value=white;
                    gtaoVolume.temporalFrameCount.Override(0);gtaoVolume.sliceCount.Override(1);gtaoVolume.stepCount.Override(8);gtaoVolume.boxPassCount.Override(1);
                    effects.capsuleAOEnabled=false;Render();float originalAO=Read();
                    Assert.That(probe.hasAO,Is.True);
                    effects.capsuleAOEnabled=true;Render();
                    Assert.That(Read(),Is.EqualTo(originalAO).Within(.001f),"Receiver overlay changed the published GTAO texture.");
                    Assert.That(ReadColor(),Is.EqualTo(baseline*.6f).Within(.008f));
                }
                geometry.SetActive(false);gtao.SetActive(false);
                // Material AO is explicitly off, yet the independent receiver overlay remains visible.
                var lil=Keep(new Material(Shader.Find("lilToon")));
                lil.SetColor("_Color",Color.white);lil.SetFloat("_LightMinLimit",1);lil.SetFloat("_LightMaxLimit",1);
                lil.SetFloat("_UseShadow",0);lil.SetFloat("_UseReflection",0);lil.SetFloat("_AOSource",0);lil.SetFloat("_UseAODarkening",0);
                ground.GetComponent<Renderer>().sharedMaterial=lil;string originalLil=EditorJsonUtility.ToJson(lil);
                effects.capsuleAOEnabled=false;Render();float lilBaseline=ReadColor();
                effects.capsuleAOEnabled=true;Render();
                Assert.That(ReadColor(),Is.EqualTo(lilBaseline*.6f).Within(.01f),"Receiver overlay still depended on lilToon material AO.");
                Assert.That(EditorJsonUtility.ToJson(lil),Is.EqualTo(originalLil));
                Assert.That(EditorJsonUtility.ToJson(floorMaterial),Is.EqualTo(originalMaterial));
                Assert.That(ShaderUtil.ShaderHasError(lil.shader),Is.False);
                ground.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                if(!compatibility && msaa==1 && !perspective)
                {
                    string previews=Environment.GetEnvironmentVariable("HO_CAPSULE_AO_OVERLAY_PREVIEW_DIR");
                    if(!string.IsNullOrEmpty(previews))
                    {
                        System.IO.Directory.CreateDirectory(previews);var previousTarget=target;float previousAspect=camera.aspect;
                        var preview=Keep(new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));preview.Create();
                        target=preview;camera.aspect=1;
                        foreach(var mode in new[]{HoCapsuleAOMode.Capsule,HoCapsuleAOMode.Cross})
                        {
                            effects.capsuleAOMode=mode;Render();
                            var png=Keep(new Texture2D(512,512,TextureFormat.RGB24,false,true));
                            RenderTexture.active=preview;png.ReadPixels(new Rect(0,0,512,512),0,0);png.Apply();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(previews,mode+"-"+SystemInfo.graphicsDeviceType+".png"),png.EncodeToPNG());
                        }
                        target=previousTarget;camera.aspect=previousAspect;
                    }
                }
                Debug.Log("HO_CAPSULE_RECEIVER_OVERLAY_PASS compatibility="+compatibility+" msaa="+msaa+" perspective="+perspective+" no-GB/no-GTAO/material-AO-off/unchanged-HoAO/cross-penetration/slope/overlap");
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
#if URP_COMPATIBILITY_MODE
                if(graphSettings!=null)graphSettings.enableRenderCompatibilityMode=oldCompatibility;
#endif
                probeOutput?.Release();RenderTexture.active=oldActive;ShaderUtil.allowAsyncCompilation=oldAsync;
                for(int i=created.Count-1;i>=0;i--)if(created[i]!=null)Object.DestroyImmediate(created[i]);
            }
            yield return null;
        }
    }

    public sealed class HoCapsuleAOProbeFeature : ScriptableRendererFeature
    {
        public Material material;
        public RTHandle destination;
        public bool hasAO;
        private ProbePass pass;
        public override void Create() => pass=new ProbePass(this);
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData) => renderer.EnqueuePass(pass);
        private sealed class ProbePass : ScriptableRenderPass
        {
            private sealed class Data {internal Material material;}
            private readonly HoCapsuleAOProbeFeature owner;
            internal ProbePass(HoCapsuleAOProbeFeature owner){this.owner=owner;renderPassEvent=RenderPassEvent.AfterRenderingTransparents;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                Type type=typeof(HoGTAOSettings).Assembly.GetType("lilToon.URP.Extensions.GTAO.HoGTAORenderGraphResources",true);
                object resources=typeof(ContextContainer).GetMethod("GetOrCreate").MakeGenericMethod(type).Invoke(frame,null);
                TextureHandle ao=(TextureHandle)type.GetField("aoTexture").GetValue(resources);
                owner.hasAO=ao.IsValid();if(!owner.hasAO)return;
                using(var builder=graph.AddRasterRenderPass<Data>("Capsule Validation Read Published AO",out var data))
                {
                    data.material=owner.material;
                    builder.UseTexture(ao,AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(graph.ImportTexture(owner.destination),0,AccessFlags.Write);
                    builder.SetRenderFunc(static(Data d,RasterGraphContext context)=>
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,2,MeshTopology.Triangles,3));
                }
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
            {
                Type type=typeof(HoGTAOSettings).Assembly.GetType("lilToon.URP.Extensions.GTAO.HoAOCompatibilityPublication",true);
                owner.hasAO=(Camera)type.GetField("Camera",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)==renderingData.cameraData.camera;
                if(!owner.hasAO)return;
                var cmd=CommandBufferPool.Get("Capsule Validation Read Published AO");
                CoreUtils.SetRenderTarget(cmd,owner.destination);
                cmd.DrawProcedural(Matrix4x4.identity,owner.material,2,MeshTopology.Triangles,3);
                context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
