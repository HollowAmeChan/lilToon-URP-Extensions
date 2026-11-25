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

        [UnityTest] public IEnumerator ActualPipelineReadsAppendedHoAO() => Validate(false);
        [UnityTest] public IEnumerator ActualPipelineMsaa4ReadsAppendedHoAO() => Validate(false, 4);
        [UnityTest] public IEnumerator ActualPipelinePerspectiveReadsAppendedHoAO() => Validate(false, 1, true);
#if URP_COMPATIBILITY_MODE
        [UnityTest] public IEnumerator CompatibilityReadsAppendedHoAO() => Validate(true);
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
                renderer.rendererFeatures.Add(geometry); renderer.rendererFeatures.Add(gtao); renderer.rendererFeatures.Add(character);
                var probe = Keep(ScriptableObject.CreateInstance<HoCapsuleAOProbeFeature>());
                probe.material = Keep(new Material(Shader.Find("Hidden/HoCapsuleAO/Fixture")));
                renderer.rendererFeatures.Add(probe);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer));
                pipeline.msaaSampleCount = msaa; pipeline.supportsHDR = true; pipeline.shadowDistance = 0;
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
                gtaoVolume.enable.Override(true); gtaoVolume.publishAOTextureOnly.Override(true);
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
                Render(); yield return null; Render();
                Assert.That(character.CapsuleAOActiveCount,Is.EqualTo(1),character.CapsuleAOStatus);
                Assert.That(Read(),Is.EqualTo(.6f).Within(.006f),character.CapsuleAOStatus);
                Assert.That(Read(.95f,.95f),Is.EqualTo(1).Within(.001f));
                var firstCamera=camera;
                camera=Keep(new GameObject("Second capsule camera")).AddComponent<Camera>();
                camera.CopyFrom(firstCamera);camera.enabled=false;
                camera.transform.SetPositionAndRotation(new Vector3(2,2,0),Quaternion.LookRotation(Vector3.down,Vector3.forward));
                camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                Render();Assert.That(Read(),Is.EqualTo(1).Within(.001f),"Another camera reused the first foot projection.");
                camera=firstCamera;Render();Assert.That(Read(),Is.EqualTo(.6f).Within(.006f));
                effects.capsuleAOMode=HoCapsuleAOMode.Cross; Render();
                Assert.That(Read(),Is.EqualTo(.6f).Within(.006f),character.CapsuleAOStatus);
                if(!compatibility && msaa==1 && !perspective)
                {
                    string previews=Environment.GetEnvironmentVariable("HO_CAPSULE_AO_PREVIEW_DIR");
                    if(!string.IsNullOrEmpty(previews))
                    {
                        System.IO.Directory.CreateDirectory(previews);
                        var previousTarget=target;var previousProbe=probe.destination;float previousAspect=camera.aspect;
                        var previewColor=Keep(new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));previewColor.Create();
                        var previewAO=Keep(new RenderTexture(512,512,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));previewAO.Create();
                        var previewHandle=RTHandles.Alloc(previewAO);
                        target=previewColor;probe.destination=previewHandle;camera.aspect=1;
                        foreach(var mode in new[]{HoCapsuleAOMode.Capsule,HoCapsuleAOMode.Cross})
                        {
                            effects.capsuleAOMode=mode;Render();
                            var png=Keep(new Texture2D(512,512,TextureFormat.RGB24,false,true));
                            RenderTexture.active=previewAO;png.ReadPixels(new Rect(0,0,512,512),0,0);png.Apply();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(previews,mode+"-"+SystemInfo.graphicsDeviceType+".png"),png.EncodeToPNG());
                        }
                        target=previousTarget;probe.destination=previousProbe;camera.aspect=previousAspect;
                        previewHandle.Release();effects.capsuleAOMode=HoCapsuleAOMode.Cross;
                    }
                }
                receiver.enabled=false;Render();Assert.That(Read(),Is.EqualTo(1).Within(.001f));
                receiver.enabled=true;emitter.enabled=false;Render();Assert.That(Read(),Is.EqualTo(1).Within(.001f));
                emitter.enabled=true;
                var front = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
                front.transform.position=new Vector3(0,.15f,0);front.transform.localScale=new Vector3(.2f,.1f,.2f);
                front.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                Render();Assert.That(Read(),Is.EqualTo(1).Within(.001f),"Unregistered foreground received ground AO.");
                front.SetActive(false);
                gtaoVolume.enable.value=false;Render();
                Assert.That(probe.hasAO,Is.False,"Capsule AO published a replacement after the owner was disabled.");
                gtaoVolume.enable.value=true;Render();Assert.That(Read(),Is.EqualTo(.6f).Within(.006f));
                effects.capsuleAOEnabled=false;Render();Assert.That(Read(),Is.EqualTo(1).Within(.001f));
                Assert.That(EditorJsonUtility.ToJson(floorMaterial),Is.EqualTo(originalMaterial));
                var staticRoot=Keep(new GameObject("Static receiver root"));
                ground.transform.SetParent(staticRoot.transform,true);
                var distant=Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));
                distant.transform.position=new Vector3(20,0,0);
                distant.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                StaticBatchingUtility.Combine(new[]{ground,distant},staticRoot);
                Assert.That(ground.GetComponent<MeshRenderer>().isPartOfStaticBatch,Is.True);
                effects.capsuleAOEnabled=true;Render();Assert.That(Read(),Is.EqualTo(.6f).Within(.006f),"Static-batched receiver did not match GB.");
                emitter.transform.position=new Vector3(.4f,.06f,0);Render();
                Vector3 movedPoint=camera.WorldToViewportPoint(new Vector3(.4f,0,0));
                Assert.That(Read(movedPoint.x,movedPoint.y),Is.EqualTo(.6f).Within(.008f),"Static batch or world-space foot movement was not followed.");
                Assert.That(Read(),Is.EqualTo(1).Within(.001f),"Old foot position retained an AO stamp.");
                emitter.transform.position=new Vector3(0,.06f,0);Render();
                if(!compatibility)
                {
                    effects.capsuleAOEnabled=false;
                    gtaoVolume.publishAOTextureOnly.value=false;
                    gtaoVolume.temporalFrameCount.Override(0);gtaoVolume.sliceCount.Override(1);gtaoVolume.stepCount.Override(8);
                    gtaoVolume.boxPassCount.Override(1);Render();
                    float baseAO=Read();effects.capsuleAOEnabled=true;Render();
                    Assert.That(gtao.Settings.publishAOTextureOnly,Is.True,"Volume mode polluted the serialized Feature fallback.");
                    Assert.That(gtao.Settings.temporalFrameCount,Is.EqualTo(12),"Volume sampling polluted the serialized Feature fallback.");
                    Assert.That(Read(),Is.EqualTo(Mathf.Min(baseAO,.6f)).Within(.008f),"Computed GTAO and capsule Min were not combined.");
                    effects.capsuleAOBlend=HoCapsuleAOBlendMode.Multiply;Render();
                    Assert.That(Read(),Is.EqualTo(baseAO*.6f).Within(.008f));
                    gtaoVolume.publishAOTextureOnly.value=true;
                    effects.capsuleAOBlend=HoCapsuleAOBlendMode.Min;
                }
                // Existing lilToon AO properties, on a disposable fixture material only.
                var lil=Keep(new Material(Shader.Find("lilToon")));
                lil.SetColor("_Color",Color.white);lil.SetFloat("_LightMinLimit",1);lil.SetFloat("_LightMaxLimit",1);
                lil.SetFloat("_UseShadow",0);lil.SetFloat("_UseReflection",0);lil.SetFloat("_AOSource",2);
                lil.SetFloat("_UseAODarkening",1);lil.SetFloat("_AODarkStrength",1);lil.SetColor("_AOColor",Color.black);
                ground.GetComponent<Renderer>().sharedMaterial=lil;
                float ReadColor()
                {
                    RenderTexture source=target;
                    if(target.antiAliasing>1)
                    {
                        var resolved=Keep(new RenderTexture(97,65,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));resolved.Create();
                        target.ResolveAntiAliasedSurface(resolved);source=resolved;
                    }
                    RenderTexture.active=source;image.ReadPixels(new Rect(0,0,97,65),0,0);image.Apply();
                    return image.GetPixel(48,32).r;
                }
                effects.capsuleAOEnabled=false;Render();float baselineColor=ReadColor();
                effects.capsuleAOEnabled=true;Render();float contactColor=ReadColor();
                Assert.That(baselineColor,Is.GreaterThan(.01f));
                Assert.That(contactColor,Is.LessThan(baselineColor*.85f),"lilToon did not consume appended HoAO.");
                lil.SetFloat("_AOSource",0);Render();
                Assert.That(ReadColor(),Is.EqualTo(baselineColor).Within(.01f),"Capsule AO bypassed the material's AO Off setting.");
                Assert.That(ShaderUtil.ShaderHasError(lil.shader),Is.False);
                Assert.That(character.Settings.renderScale,Is.EqualTo(HoCharacterRenderScale.Full));
                Debug.Log("HO_CAPSULE_AO_PIPELINE_PASS compatibility="+compatibility+" msaa="+msaa+" perspective="+perspective+" size=97x65 receiver/foreground/disable/re-enable/material-controls");
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
            internal ProbePass(HoCapsuleAOProbeFeature owner){this.owner=owner;renderPassEvent=RenderPassEvent.AfterRenderingOpaques;}
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
