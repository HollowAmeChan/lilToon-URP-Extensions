#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI
{
    [DisallowMultipleRendererFeature("Ho-RTDI")]
    public sealed class HoRTDIRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoRTDISettings settings=new HoRTDISettings();
        [SerializeField] private ComputeShader shader;
        private HoRTScene scene;
        private readonly Dictionary<Camera,HoRTDIContext> contexts=new();
        private readonly List<Camera> dead=new();
        private HoRTDIPass pass;
        private HoRTDIDebugPass debugPass;
        private Material debugMaterial;
        private bool registered;
        public HoRTDISettings Settings=>settings;
        public string Status {get;private set;}="未生产";
        internal HoRTDIContext LastContext {get;private set;}
        public override void Create()
        {
            DisposeResources();scene=new HoRTScene();pass=new HoRTDIPass();debugPass=new HoRTDIDebugPass();
            #if UNITY_EDITOR
            if(shader==null)shader=UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDI.compute");
            #endif
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        private void ResetCamera(ScriptableRenderContext context,Camera camera)
        {
            Shader.SetGlobalFloat("_HoRTDIActive",0);Shader.SetGlobalTexture("_HoRTDIDiffuseTexture",Texture2D.blackTexture);Shader.SetGlobalTexture("_HoRTDISpecularTexture",Texture2D.blackTexture);
            if(!isActive||!settings.enabled)foreach(var item in contexts.Values)item.historyValid=false;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct AnalyticLight {public Vector4 positionRange,radianceType,directionOuter,innerShadow;}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            var camera=data.cameraData.camera;var volume=VolumeManager.instance.stack.GetComponent<HoRTDIVolume>();
            if(!settings.enabled||(volume!=null&&volume.enable.overrideState&&!volume.enable.value)||data.cameraData.renderType!=CameraRenderType.Base
                ||(camera.cameraType!=CameraType.Game&&(camera.cameraType!=CameraType.SceneView||!settings.inSceneView)))
            {if(contexts.TryGetValue(camera,out var skipped))skipped.historyValid=false;return;}
            if(!scene.IsAvailable||shader==null){Status="需要 D3D12 inline RT 与 Compute Shader";return;}
            var format=GraphicsFormat.R16G16B16A16_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))format=GraphicsFormat.R32G32B32A32_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore)){Status="无 HDR UAV 格式";return;}
            dead.Clear();foreach(var pair in contexts)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var key in dead){contexts[key].Dispose();contexts.Remove(key);}
            var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);var geometry=new List<Renderer>();
            foreach(var item in renderers)if((settings.geometryLayers.value&(1<<item.gameObject.layer))!=0)geometry.Add(item);
            scene.Synchronize(geometry);
            var lightList=new List<AnalyticLight>();var lightKeys=new List<Light>();int signature=settings.initialSamples.GetHashCode()^settings.rayBias.GetHashCode();int excludedLights=0;
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
            {
                if(!light.enabled||!light.gameObject.activeInHierarchy||(settings.lightLayers.value&(1<<light.gameObject.layer))==0)continue;
                if(light.type!=LightType.Point&&light.type!=LightType.Spot&&light.type!=LightType.Directional){excludedLights++;continue;}
                // Cookies / light-layer material routing are deliberately not silently approximated.
                if(light.cookie!=null||light.cullingMask!=-1||light.lightmapBakeType==LightmapBakeType.Baked){excludedLights++;continue;}
                var color=QualitySettings.activeColorSpace==ColorSpace.Linear?light.color.linear:light.color;
                if(light.useColorTemperature)color*=Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                color*=Mathf.Max(0,light.intensity);
                var direction=light.transform.forward;
                var item=new AnalyticLight{positionRange=new Vector4(light.transform.position.x,light.transform.position.y,light.transform.position.z,Mathf.Max(.001f,light.range)),
                    radianceType=new Vector4(color.r,color.g,color.b,light.type==LightType.Point?0:light.type==LightType.Spot?1:2),
                    directionOuter=new Vector4(direction.x,direction.y,direction.z,Mathf.Cos(light.spotAngle*Mathf.Deg2Rad*.5f)),
                    innerShadow=new Vector4(Mathf.Cos(light.innerSpotAngle*Mathf.Deg2Rad*.5f),0,0,0)};
                lightList.Add(item);lightKeys.Add(light);
            }
            if(!contexts.TryGetValue(camera,out var context)){context=new HoRTDIContext();contexts.Add(camera,context);}
            var descriptor=data.cameraData.cameraTargetDescriptor;
            context.Prepare(descriptor.width,descriptor.height,lightList.Count,format);LastContext=context;
            var lightData=lightList.Count>0?lightList.ToArray():new AnalyticLight[1];
            pass.Setup(context,scene,shader,settings,lightData,lightKeys.ToArray(),lightList.Count,signature);
            renderer.EnqueuePass(pass);Status=$"RTXDI DI：{lightList.Count} 灯 / {scene.InstanceCount} RT submesh / 排除 {excludedLights} 灯、{scene.Rejections.Count} 几何项";
            if(volume!=null&&volume.debugMode.value!=HoRTDIDebugMode.Off&&(camera.cameraType==CameraType.SceneView?volume.debugInSceneView.value:volume.debugInGameView.value))
            {
                if(debugMaterial==null){var found=Shader.Find("Hidden/lilToon/URP/RTDI/Debug");if(found!=null)debugMaterial=CoreUtils.CreateEngineMaterial(found);}
                if(debugMaterial!=null){debugPass.Setup(volume.debugMode.value,debugMaterial,context);debugPass.Enqueue(renderer);}
            }
        }
        private void DisposeResources()
        {foreach(var context in contexts.Values)context.Dispose();contexts.Clear();scene?.Dispose();scene=null;debugPass?.ReleaseDebugResources();CoreUtils.Destroy(debugMaterial);debugMaterial=null;LastContext=null;}
        protected override void Dispose(bool disposing)
        {if(registered)RenderPipelineManager.beginCameraRendering-=ResetCamera;registered=false;ResetCamera(default,null);DisposeResources();}

        private sealed class HoRTDIPass : ScriptableRenderPass
        {
            private HoRTDIContext context;private HoRTScene scene;private ComputeShader shader;private HoRTDISettings settings;
            private AnalyticLight[] lights;private Light[] lightKeys;private int lightCount,signature;
            internal HoRTDIPass(){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;}
            internal void Setup(HoRTDIContext context,HoRTScene scene,ComputeShader shader,HoRTDISettings settings,AnalyticLight[] lights,Light[] keys,int count,int signature)
            {this.context=context;this.scene=scene;this.shader=shader;this.settings=settings;this.lights=lights;lightKeys=keys;lightCount=count;this.signature=signature;}
            private sealed class Data
            {
                internal HoRTDIContext context;internal HoRTScene.Inputs scene;internal HoRTPrimarySurfaceInputs primary;
                internal ComputeShader shader;internal HoRTDISettings settings;internal HoRTDIContext.LightFrame lightFrame;
                internal int lightCount,signature;internal uint revision;internal Matrix4x4 view,projection;internal Camera camera;
                internal TextureHandle diffuse,specular;
            }
            private Data Snapshot(Camera camera,Matrix4x4 view,Matrix4x4 projection) => new Data {context=context,scene=scene.ReadCompatibility(),shader=shader,settings=settings,lightFrame=context.CaptureLights(lights,lightKeys),
                lightCount=lightCount,signature=signature,revision=scene.Revision,view=view,projection=projection,camera=camera};
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddUnsafePass<Data>("Ho-RTDI reference DI passes",out var data))
                {
                    var source=Snapshot(camera.camera,camera.GetViewMatrix(),camera.GetProjectionMatrix());
                    data.context=source.context;data.shader=source.shader;data.settings=source.settings;data.lightFrame=source.lightFrame;data.lightCount=source.lightCount;
                    data.signature=source.signature;data.revision=source.revision;data.view=source.view;data.projection=source.projection;data.camera=source.camera;
                    data.scene=scene.Read(graph,builder);data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                    foreach(var buffer in new[]{context.reservoirs,context.history,context.lights,context.previousLights,context.currentToPrevious,context.previousToCurrent})builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
                    builder.UseBuffer(graph.ImportBuffer(context.neighbors));
                    data.diffuse=graph.ImportTexture(context.diffuse);data.specular=graph.ImportTexture(context.specular);
                    builder.UseTexture(data.diffuse,AccessFlags.WriteAll);builder.UseTexture(data.specular,AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(data.diffuse,Shader.PropertyToID("_HoRTDIDiffuseTexture"));builder.SetGlobalTextureAfterPass(data.specular,Shader.PropertyToID("_HoRTDISpecularTexture"));
                    var output=frame.GetOrCreate<HoRTDIRenderGraphResources>();output.diffuse=data.diffuse;output.specular=data.specular;
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext ctx)=>Dispatch(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd),d,false,default));
                }
            }
            private static void Dispatch(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData)
            {
                var c=d.context;bool temporal=d.settings.temporal&&c.historyValid&&c.sceneRevision==d.revision&&c.settingsSignature==d.signature;
                c.lastTemporal=temporal;
                cmd.SetBufferData(c.lights,d.lightFrame.current);cmd.SetBufferData(c.previousLights,d.lightFrame.previous);
                cmd.SetBufferData(c.currentToPrevious,d.lightFrame.currentToPrevious);cmd.SetBufferData(c.previousToCurrent,d.lightFrame.previousToCurrent);
                var names=new[]{"Initial","Temporal","Spatial","Shade"};
                foreach(var name in names)
                {
                    int kernel=d.shader.FindKernel(name);d.scene.BuildAndBind(cmd,d.shader,kernel);
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDILights",c.lights);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIReservoirs",c.reservoirs);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIPreviousLights",c.previousLights);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDICurrentToPrevious",c.currentToPrevious);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIPreviousToCurrent",c.previousToCurrent);
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIPreviousLightCount",d.lightFrame.previousCount);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIHistory",c.history);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDINeighbors",c.neighbors);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIDiffuse",c.diffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDISpecular",c.specular.nameID);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFrame",new Vector4(c.width,c.height,d.lightCount,0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDISettings",new Vector4(Mathf.Clamp(d.settings.initialSamples,1,32),Mathf.Clamp(d.settings.spatialSamples,1,16),Mathf.Clamp(d.settings.spatialRadius,1,32),Mathf.Max(.00001f,d.settings.rayBias)));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));cmd.SetComputeIntParam(d.shader,"_HoRTDIFrameIndex",c.frame);
                    cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousVP",c.previousVP);cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousCameraPosition",c.previousCamera);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousViewZ",c.previousViewZ);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousClipParameters",c.previousClip);
                    cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                cmd.SetGlobalFloat("_HoRTDIActive",1);cmd.SetGlobalTexture("_HoRTDIDiffuseTexture",c.diffuse.nameID);cmd.SetGlobalTexture("_HoRTDISpecularTexture",c.specular.nameID);
                c.Complete(d.view,d.projection,d.camera,d.revision,d.signature,d.lightFrame);
            }
            public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
            {
                var d=Snapshot(data.cameraData.camera,data.cameraData.GetViewMatrix(),data.cameraData.GetProjectionMatrix());
                var cmd=CommandBufferPool.Get("Ho-RTDI reference DI passes");Dispatch(cmd,d,true,data.cameraData);rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
