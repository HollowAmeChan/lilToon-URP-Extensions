#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTGI;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static class HoRTGIGridValidation
    {
        [StructLayout(LayoutKind.Sequential)]internal struct Packet
        {
            internal Vector4 worldDepth,normalRawDepth,primaryUVGridUV,nrdWorldValid,previousWorldValid,previousNrdWorldValid,currentToPrevious;
        }
        [Serializable]sealed class Case
        {
            public string name;public int width,height,scale,pixels,historyPixels,foregroundPixels;
            public bool halfRequested,historyAllowed,orthographic;
            public double positionError,normalError,depthError,primaryUvError,gridUvError,nrdPositionError,previousPositionError,previousNrdPositionError,temporalUvError;
        }
        [Serializable]sealed class Report{public bool pass,compatibility;public string error,device,api;public int renderRequests;public Case[] cases;}
        sealed class Trial
        {
            internal string name;internal int w,h,expectedScale;internal bool half,ortho,offset;internal Vector3 position,euler;
            internal Trial(string name,int w,int h,bool half,int scale,bool ortho=false,bool offset=false,bool moved=false)
            {this.name=name;this.w=w;this.h=h;this.half=half;expectedScale=scale;this.ortho=ortho;this.offset=offset;position=moved?new Vector3(.19f,-.12f,-4):new Vector3(0,0,-4);euler=moved?new Vector3(-1.5f,2,0):Vector3.zero;}
        }
        public static void Run()=>Run(false);
        public static void RunCompatibility()=>Run(true);
        static void Run(bool compatibility)
        {
            var owned=new List<UnityEngine.Object>();var buffers=new List<GraphicsBuffer>();var cases=new List<Case>();
            var report=new Report{compatibility=compatibility,device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};int exit=1;
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;string output=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-grid-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(output);
                ShaderUtil.allowAsyncCompilation=false;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.fog=false;
                var renderer=Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                var gb=Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());gb.Settings.maxRenderQueue=(int)RenderQueue.GeometryLast;
                var sb=Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());sb.Settings.enableSemanticLanes=sb.Settings.enableCorrelatedSemantics=false;
                var probe=Keep(ScriptableObject.CreateInstance<HoRTGIGridProbeFeature>());probe.shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTGI/Shaders/HoRTGIGridAudit.compute");
                if(probe.shader==null)throw new Exception("Grid audit shader missing");
                renderer.rendererFeatures.Add(gb);renderer.rendererFeatures.Add(sb);renderer.rendererFeatures.Add(probe);
                var pipeline=Keep(UniversalRenderPipelineAsset.Create(renderer));pipeline.supportsHDR=true;pipeline.msaaSampleCount=1;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compatibility;
                #endif
                var camera=Keep(new GameObject("GI lattice camera")).AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.fieldOfView=55;camera.orthographicSize=2;camera.GetUniversalAdditionalCameraData().volumeLayerMask=0;
                var planes=new[]{Quad("Back plane",new Vector3(0,0,1),new Vector3(9,17,0),new Vector3(12,12,1)),Quad("Foreground",new Vector3(-.25f,.1f,0),new Vector3(-17,-24,0),new Vector3(1.2f,1.7f,1))};
                var trials=new[]{new Trial("full-perspective",64,48,false,1),new Trial("half-perspective",64,48,true,2),new Trial("half-moving-camera",64,48,true,2,moved:true),new Trial("half-offcenter-resize",66,50,true,2,offset:true),new Trial("half-offcenter-moving",66,50,true,2,offset:true,moved:true),new Trial("half-orthographic",96,54,true,2,ortho:true),new Trial("half-orthographic-offcenter-moving",96,54,true,2,ortho:true,offset:true,moved:true),new Trial("odd-full-fallback",65,49,true,1),new Trial("single-column-full-fallback",1,8,true,1),new Trial("single-row-full-fallback",8,1,true,1),new Trial("full-restored",64,48,false,1),new Trial("full-moving-camera",64,48,false,1,moved:true)};
                var dummy=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,Marshal.SizeOf<Packet>());buffers.Add(dummy);
                Packet[] previous=null;GraphicsBuffer previousBuffer=dummy;HoRTGIGrid previousGrid=default;Matrix4x4 previousView=Matrix4x4.identity,previousProjection=Matrix4x4.identity;
                foreach(var trial in trials)
                {
                    camera.orthographic=trial.ortho;camera.aspect=(float)trial.w/trial.h;camera.transform.SetPositionAndRotation(trial.position,Quaternion.Euler(trial.euler));camera.ResetProjectionMatrix();
                    var projection=camera.projectionMatrix;if(trial.offset){projection.m02+=trial.ortho?0:.13f;projection.m12+=trial.ortho?0:-.09f;projection.m03+=trial.ortho?.07f:0;projection.m13+=trial.ortho?-.05f:0;}camera.projectionMatrix=projection;
                    var grid=new HoRTGIGrid(trial.w,trial.h,trial.half);bool reuse=previous!=null&&grid.CanReuseHistory(previousGrid);
                    if(grid.scale!=trial.expectedScale)throw new Exception("Unexpected sample lattice: "+trial.name);
                    var target=Keep(new RenderTexture(trial.w,trial.h,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));target.Create();
                    var result=new GraphicsBuffer(GraphicsBuffer.Target.Structured,grid.width*grid.height,Marshal.SizeOf<Packet>());buffers.Add(result);
                    probe.grid=grid;probe.previousGrid=previous==null?grid:previousGrid;probe.previousView=previous==null?camera.worldToCameraMatrix:previousView;probe.previousProjection=previous==null?projection:previousProjection;probe.hasHistory=reuse;probe.output=result;probe.history=previousBuffer;
                    VolumeManager.instance.Update(camera.transform,0);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});report.renderRequests++;
                    var packets=new Packet[result.count];result.GetData(packets); // actual GPU completion before inspecting or releasing inputs
                    var row=new Case{name=trial.name,width=trial.w,height=trial.h,scale=grid.scale,halfRequested=trial.half,historyAllowed=reuse,orthographic=trial.ortho};cases.Add(row);
                    for(int y=0;y<grid.height;y++)for(int x=0;x<grid.width;x++)
                    {
                        int at=y*grid.width+x;var p=packets[at];int fullX=trial.expectedScale*x+(trial.expectedScale==2?1:0),fullY=trial.expectedScale*y+(trial.expectedScale==2?1:0);
                        var uv=new Vector2((fullX+.5f)/trial.w,(fullY+.5f)/trial.h);var ray=camera.ViewportPointToRay(uv);
                        float nearest=float.PositiveInfinity;Transform hit=null;Vector3 expected=default;
                        foreach(var plane in planes)
                        {
                            var normal=plane.rotation*Vector3.back;float denominator=Vector3.Dot(ray.direction,normal);if(Mathf.Abs(denominator)<1e-7f)continue;
                            float t=Vector3.Dot(plane.position-ray.origin,normal)/denominator;if(t<=0||t>=nearest)continue;
                            var world=ray.origin+ray.direction*t;var local=plane.InverseTransformPoint(world);if(Mathf.Abs(local.x)>.5f||Mathf.Abs(local.y)>.5f)continue;
                            nearest=t;hit=plane;expected=world;
                        }
                        if(hit==null){if(p.nrdWorldValid.w>.5)throw new Exception("GPU sample outside independent geometry");continue;}
                        if(p.nrdWorldValid.w<.5)throw new Exception("Missing GPU primary: "+trial.name+" at "+x+","+y);
                        row.pixels++;if(hit==planes[1])row.foregroundPixels++;
                        Max(ref row.positionError,Vector3.Distance(p.worldDepth,expected));Max(ref row.normalError,Vector3.Distance(p.normalRawDepth,hit.rotation*Vector3.back));
                        Max(ref row.depthError,Math.Abs(p.worldDepth.w+camera.worldToCameraMatrix.MultiplyPoint3x4(expected).z));
                        Max(ref row.primaryUvError,Vector2.Distance(new Vector2(p.primaryUVGridUV.x,p.primaryUVGridUV.y),uv));
                        Max(ref row.gridUvError,Vector2.Distance(new Vector2(p.primaryUVGridUV.z,p.primaryUVGridUV.w),new Vector2((x+.5f)/grid.width,(y+.5f)/grid.height)));
                        Max(ref row.nrdPositionError,Vector3.Distance(p.nrdWorldValid,expected));
                        var prevGrid=previous==null?grid:previousGrid;var prevLogicalVP=(previous==null?projection:previousProjection)*(previous==null?camera.worldToCameraMatrix:previousView);
                        Vector4 clip=prevLogicalVP*new Vector4(expected.x,expected.y,expected.z,1);float offset=prevGrid.scale==2?.5f:0;
                        var expectedPreviousUV=new Vector2(clip.x/clip.w*.5f+.5f-offset/prevGrid.inputWidth,clip.y/clip.w*.5f+.5f-offset/prevGrid.inputHeight);
                        Max(ref row.temporalUvError,Vector2.Distance(p.currentToPrevious,expectedPreviousUV));
                        if(reuse&&previous[at].nrdWorldValid.w>.5)
                        {
                            if(p.previousWorldValid.w<.5||p.previousNrdWorldValid.w<.5)throw new Exception("Missing previous GPU depth reconstruction");row.historyPixels++;
                            Max(ref row.previousPositionError,Vector3.Distance(p.previousWorldValid,previous[at].worldDepth));Max(ref row.previousNrdPositionError,Vector3.Distance(p.previousNrdWorldValid,previous[at].worldDepth));
                        }
                        else if(p.previousWorldValid.w>.5||p.previousNrdWorldValid.w>.5)throw new Exception("Mismatched lattice reused history");
                    }
                    if(row.pixels==0||row.positionError>.002||row.normalError>.012||row.depthError>.002||row.primaryUvError>1e-6||row.gridUvError>2e-5||row.nrdPositionError>.002||row.previousPositionError>.002||row.previousNrdPositionError>.002||row.temporalUvError>2e-5)throw new Exception("Grid contract failed: "+JsonUtility.ToJson(row));
                    Debug.Log("[HoGIGrid] CHECK "+JsonUtility.ToJson(row));previous=packets;previousBuffer=result;previousGrid=grid;previousView=camera.worldToCameraMatrix;previousProjection=projection;
                }
                if(cases.Sum(x=>x.foregroundPixels)<100||cases.Sum(x=>x.historyPixels)<1000)throw new Exception("Missing foreground/history coverage");report.pass=true;exit=0;Debug.Log("[HoGIGrid] PASS cases="+cases.Count);

                T Keep<T>(T obj)where T:UnityEngine.Object{owned.Add(obj);return obj;}
                Transform Quad(string name,Vector3 position,Vector3 rotation,Vector3 scale)
                {
                    var obj=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));obj.name=name;obj.transform.SetPositionAndRotation(position,Quaternion.Euler(rotation));obj.transform.localScale=scale;
                    var material=Keep(new Material(Shader.Find("lilToon")));material.EnableKeyword("UNITY_UI_ALPHACLIP");material.SetFloat("_Cull",0);material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_UseEmission",0);material.SetFloat("_UseEmission2nd",0);obj.GetComponent<Renderer>().sharedMaterial=material;return obj.transform;
                }
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally
            {
                report.cases=cases.ToArray();if(output!=null)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
                GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;
                foreach(var buffer in buffers)buffer.Dispose();foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            }
            EditorApplication.Exit(exit);
        }
        static void Max(ref double maximum,double value){if(!double.IsFinite(value))throw new Exception("Nonfinite coordinate audit");maximum=Math.Max(maximum,value);}
    }

    internal sealed class HoRTGIGridProbeFeature:ScriptableRendererFeature
    {
        internal ComputeShader shader;internal HoRTGIGrid grid,previousGrid;internal Matrix4x4 previousView,previousProjection;
        internal GraphicsBuffer output,history;internal bool hasHistory;ProbePass pass;
        public override void Create()=>pass=new ProbePass();
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){pass.owner=this;renderer.EnqueuePass(pass);}
        sealed class ProbePass:ScriptableRenderPass
        {
            internal HoRTGIGridProbeFeature owner;
            internal ProbePass(){renderPassEvent=RenderPassEvent.AfterRenderingOpaques;ConfigureInput(ScriptableRenderPassInput.Depth);}
            sealed class Data
            {
                internal ComputeShader shader;internal HoRTPrimarySurfaceInputs primary;internal HoRTGIGrid grid,previousGrid;
                internal GraphicsBuffer output,history;internal bool hasHistory;internal Matrix4x4 nrdVP,previousVP,previousNrdVP;
            }
            Data Snapshot(Matrix4x4 view,Matrix4x4 projection)=>new Data{shader=owner.shader,grid=owner.grid,previousGrid=owner.previousGrid,output=owner.output,history=owner.history,hasHistory=owner.hasHistory,nrdVP=GL.GetGPUProjectionMatrix(owner.grid.ProjectionForGrid(projection),true)*view,previousVP=GL.GetGPUProjectionMatrix(owner.previousProjection,true)*owner.previousView,previousNrdVP=GL.GetGPUProjectionMatrix(owner.previousGrid.ProjectionForGrid(owner.previousProjection),true)*owner.previousView};
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();var data=Snapshot(camera.GetViewMatrix(),camera.GetProjectionMatrix());
                using(var builder=graph.AddUnsafePass<Data>("GI grid coordinate audit",out var d))
                {
                    d.shader=data.shader;d.grid=data.grid;d.previousGrid=data.previousGrid;d.output=data.output;d.history=data.history;d.hasHistory=data.hasHistory;d.nrdVP=data.nrdVP;d.previousVP=data.previousVP;d.previousNrdVP=data.previousNrdVP;
                    d.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);builder.UseBuffer(graph.ImportBuffer(d.output),AccessFlags.WriteAll);builder.UseBuffer(graph.ImportBuffer(d.history));builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static(Data value,UnsafeGraphContext context)=>{var cmd=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);value.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),value.shader,0);Record(cmd,value);});
                }
            }
            public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
            {
                var d=Snapshot(data.cameraData.GetViewMatrix(),data.cameraData.GetProjectionMatrix());var cmd=CommandBufferPool.Get("GI grid coordinate audit");
                HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,0,data.cameraData);Record(cmd,d);rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
            static void Record(CommandBuffer cmd,Data d)
            {
                cmd.SetComputeVectorParam(d.shader,"_GridAuditGrid",d.grid.Parameters);cmd.SetComputeVectorParam(d.shader,"_GridAuditPreviousGrid",d.previousGrid.Parameters);cmd.SetComputeVectorParam(d.shader,"_GridAuditSize",new Vector4(d.grid.width,d.grid.height,0,0));cmd.SetComputeIntParam(d.shader,"_GridAuditHasHistory",d.hasHistory?1:0);
                cmd.SetComputeMatrixParam(d.shader,"_GridAuditNrdVP",d.nrdVP);cmd.SetComputeMatrixParam(d.shader,"_GridAuditNrdInverseVP",d.nrdVP.inverse);cmd.SetComputeMatrixParam(d.shader,"_GridAuditPreviousInverseVP",d.previousVP.inverse);cmd.SetComputeMatrixParam(d.shader,"_GridAuditPreviousVP",d.previousVP);cmd.SetComputeMatrixParam(d.shader,"_GridAuditPreviousNrdInverseVP",d.previousNrdVP.inverse);
                cmd.SetComputeBufferParam(d.shader,0,"_GridAuditOutput",d.output);cmd.SetComputeBufferParam(d.shader,0,"_GridAuditHistory",d.history);cmd.DispatchCompute(d.shader,0,(d.grid.width+7)/8,(d.grid.height+7)/8,1);
            }
        }
    }
}
