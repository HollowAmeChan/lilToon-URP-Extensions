#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.SurfaceBuffer
{
    public static class HoInvisibleSurfaceValidation
    {
        [Serializable]private sealed class Case {public string name;public Color[] outputs;}
        [Serializable]private sealed class Report {public bool pass,compatibility;public int checks,actualObjectSamples;public List<Case> cases=new();}
        private static bool compatibility;private static readonly List<Object> owned=new();
        public static void RunCompatibility(){compatibility=true;Run();}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new InvalidOperationException("Use the isolated project");
            int exit=0;HoInvisibleSurfaceProbe probe=null;var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;
            var graphSettings=GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();bool oldCompatibility=graphSettings.enableRenderCompatibilityMode;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;graphSettings.enableRenderCompatibilityMode=compatibility;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());probe=Keep(ScriptableObject.CreateInstance<HoInvisibleSurfaceProbe>());
                probe.material=Mat("Hidden/lilToon/URP/Validation/InvisibleSurface");
                for(int i=0;i<6;i++)probe.outputs[i]=RTHandles.Alloc(64,64,colorFormat:GraphicsFormat.R32G32B32A32_SFloat,name:"Invisible surface probe "+i);
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());var ob=Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>());
                ob.Settings.sampleCount=HoObjectBufferSampleCount.Four;ob.Settings.selectionLayers=HoObjectBufferSelectionLayers.Off;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableCorrelatedSemantics=false;
                var ac=Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>());
                renderer.rendererFeatures.Add(probe);renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(ob);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(ac);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=QualitySettings.renderPipeline=pipeline;
                var camera=Keep(new GameObject("Infrastructure alpha camera")).AddComponent<Camera>();camera.enabled=false;camera.orthographic=false;camera.fieldOfView=40;camera.aspect=1;
                camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.transform.position=new Vector3(0,0,-3);camera.GetUniversalAdditionalCameraData().volumeLayerMask=-1;
                var target=Keep(new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf));target.Create();
                var back=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));back.transform.localScale=Vector3.one*3;
                var background=Mat("lilToon");background.SetColor("_Color",Color.red);background.SetFloat("_Cull",0);back.GetComponent<Renderer>().sharedMaterial=background;
                var front=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));front.transform.localScale=Vector3.one*2;front.transform.position=new Vector3(0,0,-1);
                var transparent=Mat("Hidden/lilToonTransparent");transparent.renderQueue=2460;transparent.SetFloat("_Cull",0);transparent.SetFloat("_Cutoff",.001f);
                transparent.SetTexture("_MainTex",Texture2D.whiteTexture);front.GetComponent<Renderer>().sharedMaterial=transparent;
                var group=Keep(new GameObject("Alpha test identity group")).AddComponent<HoObjectBufferGroup>();
                group.parts.Add(new HoObjectBufferPartEntry{name="back",renderers=new Object[]{back.GetComponent<Renderer>()}});
                group.parts.Add(new HoObjectBufferPartEntry{name="front",renderers=new Object[]{front.GetComponent<Renderer>()}});group.Apply();
                var pixels=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var report=new Report{compatibility=compatibility};
                Color[] Capture(string name)
                {
                    for(int frame=0;frame<3;frame++){VolumeManager.instance.Update(camera.transform,-1);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});}
                    var values=new Color[6];var previous=RenderTexture.active;
                    try{for(int i=0;i<6;i++){RenderTexture.active=probe.outputs[i].rt;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();values[i]=pixels.GetPixel(32,32);}}
                    finally{RenderTexture.active=previous;}
                    report.cases.Add(new Case{name=name,outputs=values});File.WriteAllText("invisible-surface-partial-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(report,true));Debug.Log("[HoInvisibleSurface] "+name+" "+string.Join(" | ",Array.ConvertAll(values,x=>x.ToString("F6"))));return values;
                }
                void Check(bool value,string label){if(!value)throw new Exception(label);report.checks++;}
                void Same(Color[] actual,Color[] expected,string label)
                {for(int i=0;i<6;i++){float error=0;for(int k=0;k<4;k++)error=Mathf.Max(error,Mathf.Abs(actual[i][k]-expected[i][k]));Check(error<.002f,label+" output="+i+" error="+error);}}
                front.SetActive(false);var baseline=Capture("back-only");Check(Mathf.Abs(baseline[0].r-3)<.01f&&Mathf.Abs(baseline[0].g-3)<.01f,"GB/AC background depth missing");Check(baseline[1].r>.9f&&baseline[1].g<.01f,"SB background colour missing");
                report.actualObjectSamples=compatibility?(HoObjectBufferPass.CompatibilityTargets?.MsaaSamples??0):HoObjectBufferPass.LastActualSamples;
                Check(report.actualObjectSamples>1,"Native transparent OB path requires actual MSAA, not opaque-only 1x fallback");
                Check(baseline[0].b>.5f&&baseline[0].a>.5f,"SB/AC producer not active");Check(baseline[3].r+baseline[3].g>0&&baseline[3].b+baseline[3].a>0,"Numeric/semantic owners not produced");
                front.SetActive(true);transparent.SetColor("_Color",new Color(0,1,0,0));Same(Capture("transparent-zero"),baseline,"Zero alpha writes infrastructure");
                transparent.SetFloat("_Cutoff",0);Same(Capture("transparent-zero-cutoff-zero"),baseline,"Zero alpha with zero cutoff writes infrastructure");
                transparent.SetColor("_Color",Color.green);var visible=Capture("transparent-visible");Check(Mathf.Abs(visible[0].r-2)<.01f&&Mathf.Abs(visible[0].g-2)<.01f,"Visible front surface lacks GB/AC depth");Check(visible[1].g>.9f&&visible[1].r<.01f,"Visible front surface lacks SB colour");
                Check((visible[2]-baseline[2]).maxColorComponent>.001f||(baseline[2]-visible[2]).maxColorComponent>.001f,"OB identity control does not distinguish front/back");
                Check((visible[3]-baseline[3]).maxColorComponent>.001f||(baseline[3]-visible[3]).maxColorComponent>.001f,"SB/semantic owner control does not distinguish front/back");
                var clear=Keep(new Texture2D(1,1,TextureFormat.RGBA32,false,true));clear.SetPixel(0,0,Color.clear);clear.Apply();transparent.SetTexture("_MainTex",clear);
                Same(Capture("texture-alpha-zero"),baseline,"Texture opacity0 writes infrastructure");transparent.SetTexture("_MainTex",Texture2D.whiteTexture);
                var opaque=Mat("lilToon");opaque.SetColor("_Color",new Color(0,1,0,0));opaque.SetFloat("_Cull",0);front.GetComponent<Renderer>().sharedMaterial=opaque;
                var opaqueZero=Capture("opaque-colour-alpha-zero");Check(Mathf.Abs(opaqueZero[0].r-2)<.01f&&opaqueZero[1].g>.9f,"Opaque colour alpha was incorrectly treated as transparency");
                report.pass=true;File.WriteAllText("invisible-surface-"+(compatibility?"compat":"rg")+".json",JsonUtility.ToJson(report,true));Debug.Log("[HoInvisibleSurface] PASS "+report.checks+" checks; compatibility="+compatibility);
            }
            catch(Exception error){Debug.LogException(error);exit=1;}
            finally{if(probe!=null)foreach(var output in probe.outputs)output?.Release();GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;graphSettings.enableRenderCompatibilityMode=oldCompatibility;ShaderUtil.allowAsyncCompilation=oldAsync;for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();}
            EditorApplication.Exit(exit);
        }
        private static T Keep<T>(T value)where T:Object{owned.Add(value);return value;}
        private static Material Mat(string name){var shader=Shader.Find(name);if(shader==null)throw new Exception("Missing shader "+name);return Keep(new Material(shader));}
    }
    internal sealed class HoInvisibleSurfaceProbe:ScriptableRendererFeature
    {
        internal Material material;internal readonly RTHandle[] outputs=new RTHandle[6];private ProbePass pass;
        public override void Create(){pass=new ProbePass{owner=this};}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {HoAttributeCompositeConsumerRegistry.DeclareForCamera(data.cameraData.camera,this,"Invisible surface validation",Array.Empty<HoACQueryDescriptor>(),null,HoACDemandResources.Geometry|HoACDemandResources.Identity|HoACDemandResources.Outline|HoACDemandResources.SurfaceAttributes|HoACDemandResources.SurfaceSemantic);renderer.EnqueuePass(pass);}
        protected override void Dispose(bool disposing){HoAttributeCompositeConsumerRegistry.Remove(this);}
        private sealed class ProbePass:ScriptableRenderPass
        {
            internal HoInvisibleSurfaceProbe owner;private readonly MaterialPropertyBlock properties=new();
            internal ProbePass(){renderPassEvent=RenderPassEvent.AfterRenderingOpaques;properties.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));}
            private sealed class Data{internal Material material;internal MaterialPropertyBlock properties;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var gb=frame.GetOrCreate<HoGeometryBufferRenderGraphResources>();var sb=frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>();var ob=frame.GetOrCreate<HoObjectBufferRenderGraphResources>();var ac=frame.GetOrCreate<HoAttributeCompositeRenderGraphResources>();
                using(var builder=graph.AddRasterRenderPass<Data>("Invisible surface validation",out var data))
                {
                    data.material=owner.material;data.properties=properties;
                    foreach(var input in new[]{gb.normalDepthTexture,gb.outlineNormalDepthTexture,sb.colorTexture,sb.ownerTexture,sb.semanticOwnerTexture,sb.semanticLaneTextures[0],ob.id0Texture,ac.geometryNormalDepthTexture,ac.outlineNormalDepthTexture,ac.identityId0Texture})if(input.IsValid())builder.UseTexture(input,AccessFlags.Read);
                    for(int i=0;i<6;i++)builder.SetRenderAttachment(graph.ImportTexture(owner.outputs[i]),i,AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);builder.SetRenderFunc(static(Data d,RasterGraphContext context)=>{context.cmd.SetViewport(new Rect(0,0,64,64));context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,d.properties);});
                }
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {
                var cmd=CommandBufferPool.Get();var targets=new RenderTargetIdentifier[6];for(int i=0;i<6;i++)targets[i]=owner.outputs[i].nameID;
                cmd.SetRenderTarget(targets,BuiltinRenderTextureType.None);cmd.SetViewport(new Rect(0,0,64,64));cmd.DrawProcedural(Matrix4x4.identity,owner.material,0,MeshTopology.Triangles,3,1,properties);
                CoreUtils.SetRenderTarget(cmd,data.cameraData.renderer.cameraColorTargetHandle,data.cameraData.renderer.cameraDepthTargetHandle);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
