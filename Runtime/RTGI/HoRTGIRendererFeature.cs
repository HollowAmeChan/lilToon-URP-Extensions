#pragma warning disable CS0618,CS0672
using System.Collections.Generic;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTDI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RTGI
{
    [DisallowMultipleRendererFeature("Ho-RTGI")]
    public sealed class HoRTGIRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoRTGISettings settings = new();
        [SerializeField] private ComputeShader shader;
        [SerializeField] private RayTracingShader rayShader;
        [SerializeField] private Shader secondaryMaterialShader;
        private HoRTScene scene;
        private HoRTGISecondaryMaterials materials;
        private readonly Dictionary<Camera,HoRTGIContext> contexts = new();
        private readonly List<Camera> dead = new();
        private HoRTGIPass pass;
        private HoRTGIDebugPass debugPass;
        private Material debugMaterial;
        private bool registered;
        public HoRTGISettings Settings => settings;
        public string Status { get; private set; } = "未生产";
        internal HoRTGIContext LastContext { get; private set; }

        public override void Create()
        {
            DisposeResources();
            #if UNITY_EDITOR
            const string root="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/";
            if(shader==null)shader=UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root+"HoRTGI.compute");
            if(rayShader==null)rayShader=UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(root+"HoRTGI.raytrace");
            if(secondaryMaterialShader==null)secondaryMaterialShader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root+"HoRTGISecondaryMaterial.shader");
            #endif
            if(secondaryMaterialShader!=null)materials=new HoRTGISecondaryMaterials(secondaryMaterialShader);
            scene=materials!=null?new HoRTScene(materials.Resolve):new HoRTScene();
            pass=new HoRTGIPass();debugPass=new HoRTGIDebugPass();
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        private void ResetCamera(ScriptableRenderContext context,Camera camera)
        {
            Shader.SetGlobalFloat("_HoRTGIActive",0);Shader.SetGlobalTexture("_HoRTGIDiffuseTexture",Texture2D.blackTexture);
            if(!isActive||!settings.enabled)foreach(var item in contexts.Values)item.historyValid=false;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            var camera=data.cameraData.camera;var volume=VolumeManager.instance.stack.GetComponent<HoRTGIVolume>();
            if(!settings.enabled||(volume!=null&&volume.enable.overrideState&&!volume.enable.value)||data.cameraData.renderType!=CameraRenderType.Base
                ||(camera.cameraType!=CameraType.Game&&(camera.cameraType!=CameraType.SceneView||!settings.inSceneView)))
            {if(contexts.TryGetValue(camera,out var skipped))skipped.historyValid=false;return;}
            if(!scene.IsAvailable||!SystemInfo.supportsRayTracing||shader==null||rayShader==null||materials==null)
            {Status="需要 D3D12 DXR / inline RT 与完整 GI Shader";return;}
            var format=GraphicsFormat.R16G16B16A16_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))format=GraphicsFormat.R32G32B32A32_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore)){Status="无 HDR UAV 格式";return;}
            dead.Clear();foreach(var pair in contexts)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var key in dead){contexts[key].Dispose();contexts.Remove(key);}
            var geometry=new List<Renderer>();
            foreach(var item in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if((settings.geometryLayers.value&(1<<item.gameObject.layer))!=0)geometry.Add(item);
            materials.BeginFrame();scene.Synchronize(geometry);materials.EndFrame();
            var lights=CollectLights(settings.lightLayers.value,out int excluded,out int signature);
            unchecked
            {
                signature=signature*397^materials.Signature;signature=signature*397^settings.secondaryLightSamples;
                signature=signature*397^settings.rayBias.GetHashCode();signature=signature*397^settings.maxDistance.GetHashCode();
                signature=signature*397^settings.useEnvironment.GetHashCode();
            }
            if(!contexts.TryGetValue(camera,out var context)){context=new HoRTGIContext();contexts.Add(camera,context);}
            var descriptor=data.cameraData.cameraTargetDescriptor;
            context.Prepare(descriptor.width,descriptor.height,lights.Count,format);LastContext=context;
            if(materials.TimeVarying)context.historyValid=false;
            pass.Setup(context,scene,shader,rayShader,settings,lights.Count>0?lights.ToArray():new HoRTDIRendererFeature.AnalyticLight[1],lights.Count,signature);
            renderer.EnqueuePass(pass);
            Status=$"一跳 diffuse GI：{lights.Count} 灯 / {scene.InstanceCount} RT submesh / 排除 {excluded} 灯、{materials.Rejections.Count} 命中材质、{scene.Rejections.Count} 几何项";
            if(volume!=null&&volume.debugMode.value!=HoRTGIDebugMode.Off&&(camera.cameraType==CameraType.SceneView?volume.debugInSceneView.value:volume.debugInGameView.value))
            {
                if(debugMaterial==null){var found=Shader.Find("Hidden/lilToon/URP/RTGI/Debug");if(found!=null)debugMaterial=CoreUtils.CreateEngineMaterial(found);}
                if(debugMaterial!=null){debugPass.Setup(volume.debugMode.value,debugMaterial,context);debugPass.Enqueue(renderer);}
            }
        }
        // Identical analytic-light adaptation to Ho-RTDI; no dependency on DI being enabled.
        private static List<HoRTDIRendererFeature.AnalyticLight> CollectLights(int layerMask,out int excluded,out int signature)
        {
            var lights=new List<HoRTDIRendererFeature.AnalyticLight>();excluded=0;signature=0;
            foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
            {
                if(!light.enabled||!light.gameObject.activeInHierarchy||(layerMask&(1<<light.gameObject.layer))==0)continue;
                bool bakedOnly=light.bakingOutput.lightmapBakeType==LightmapBakeType.Baked;
                #if UNITY_EDITOR
                bakedOnly=light.lightmapBakeType==LightmapBakeType.Baked;
                #endif
                if((light.type!=LightType.Point&&light.type!=LightType.Spot&&light.type!=LightType.Directional)||light.cookie!=null||light.cullingMask!=-1||bakedOnly){excluded++;continue;}
                var color=QualitySettings.activeColorSpace==ColorSpace.Linear?light.color.linear:light.color;
                if(light.useColorTemperature)color*=Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                color*=Mathf.Max(0,light.intensity);var direction=light.transform.forward;
                var item=new HoRTDIRendererFeature.AnalyticLight
                {
                    positionRange=new Vector4(light.transform.position.x,light.transform.position.y,light.transform.position.z,Mathf.Max(.001f,light.range)),
                    radianceType=new Vector4(color.r,color.g,color.b,light.type==LightType.Point?0:light.type==LightType.Spot?1:2),
                    directionOuter=new Vector4(direction.x,direction.y,direction.z,Mathf.Cos(light.spotAngle*Mathf.Deg2Rad*.5f)),
                    innerShadow=new Vector4(Mathf.Cos(light.innerSpotAngle*Mathf.Deg2Rad*.5f),0,0,0)
                };
                lights.Add(item);unchecked{signature=signature*397^light.GetInstanceID();signature=signature*397^item.positionRange.GetHashCode();signature=signature*397^item.radianceType.GetHashCode();signature=signature*397^item.directionOuter.GetHashCode();signature=signature*397^item.innerShadow.GetHashCode();}
            }
            return lights;
        }
        private void DisposeResources()
        {
            foreach(var context in contexts.Values)context.Dispose();contexts.Clear();scene?.Dispose();scene=null;
            materials?.Dispose();materials=null;debugPass?.ReleaseDebugResources();CoreUtils.Destroy(debugMaterial);debugMaterial=null;LastContext=null;
        }
        protected override void Dispose(bool disposing)
        {if(registered)RenderPipelineManager.beginCameraRendering-=ResetCamera;registered=false;ResetCamera(default,null);DisposeResources();}

        private sealed class HoRTGIPass : ScriptableRenderPass
        {
            private HoRTGIContext context;private HoRTScene scene;private ComputeShader shader;private RayTracingShader rayShader;
            private HoRTGISettings settings;private HoRTDIRendererFeature.AnalyticLight[] lights;private int lightCount,signature;
            internal HoRTGIPass(){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;}
            internal void Setup(HoRTGIContext context,HoRTScene scene,ComputeShader shader,RayTracingShader rayShader,HoRTGISettings settings,HoRTDIRendererFeature.AnalyticLight[] lights,int lightCount,int signature)
            {this.context=context;this.scene=scene;this.shader=shader;this.rayShader=rayShader;this.settings=settings;this.lights=lights;this.lightCount=lightCount;this.signature=signature;}
            private sealed class Data
            {
                internal HoRTGIContext context;internal HoRTScene.Inputs scene;internal HoRTPrimarySurfaceInputs primary;
                internal ComputeShader shader;internal RayTracingShader rayShader;internal HoRTGISettings settings;
                internal HoRTDIRendererFeature.AnalyticLight[] lights;internal int lightCount,signature;internal uint revision,environmentRevision;
                internal Matrix4x4 view,projection;internal Camera camera;internal TextureHandle diffuse,environment;
                internal Vector4 environmentParameters,environmentTint,environmentConstant;
            }
            private Data Snapshot(Camera camera,Matrix4x4 view,Matrix4x4 projection) => new Data
            {context=context,scene=scene.ReadCompatibility(),shader=shader,rayShader=rayShader,settings=settings,lights=lights,lightCount=lightCount,signature=signature,revision=scene.Revision,view=view,projection=projection,camera=camera};
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddUnsafePass<Data>("Ho-RTGI reference GI passes",out var data))
                {
                    var source=Snapshot(camera.camera,camera.GetViewMatrix(),camera.GetProjectionMatrix());
                    data.context=source.context;data.shader=source.shader;data.rayShader=source.rayShader;data.settings=source.settings;data.lights=source.lights;data.lightCount=source.lightCount;
                    data.signature=source.signature;data.revision=source.revision;data.view=source.view;data.projection=source.projection;data.camera=source.camera;
                    data.scene=scene.Read(graph,builder);data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                    foreach(var buffer in new[]{context.reservoirs,context.history,context.secondary,context.lights})builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
                    builder.UseBuffer(graph.ImportBuffer(context.neighbors));
                    data.diffuse=graph.ImportTexture(context.diffuse);builder.UseTexture(data.diffuse,AccessFlags.WriteAll);
                    // RenderGraph pass data is pooled; absent/disabled sky must not
                    // inherit the previous pass's handle, parameters or revision.
                    data.environment=TextureHandle.nullHandle;data.environmentRevision=0;
                    data.environmentParameters=data.environmentTint=data.environmentConstant=Vector4.zero;
                    var sky=frame.GetOrCreate<HoAtmosphereRenderGraphResources>();
                    if(settings.useEnvironment&&sky.IsValid)
                    {
                        data.environment=sky.radianceTexture;data.environmentParameters=sky.parameters;data.environmentTint=sky.tint;data.environmentConstant=sky.constantColor;data.environmentRevision=sky.revision;
                        builder.UseTexture(data.environment);
                    }
                    builder.SetGlobalTextureAfterPass(data.diffuse,Shader.PropertyToID("_HoRTGIDiffuseTexture"));
                    frame.GetOrCreate<HoRTGIRenderGraphResources>().diffuse=data.diffuse;
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext ctx)=>
                    {
                        var cmd=CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);var raster=CommandBufferHelpers.GetRasterCommandBuffer(cmd);
                        d.primary.Bind(raster);
                        if(d.environment.IsValid())cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,d.environment);
                        else cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                        Dispatch(cmd,d,false,default);
                    });
                }
            }
            private static void Dispatch(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData)
            {
                var c=d.context;int signature;unchecked{signature=d.signature*397^(int)d.environmentRevision;}
                bool temporal=d.settings.temporal&&c.historyValid&&c.sceneRevision==d.revision&&c.lightingSignature==signature;
                c.lastTemporal=temporal;
                var frame=new Vector4(c.width,c.height,d.lightCount,0);
                var settings=new Vector4(Mathf.Clamp(d.settings.secondaryLightSamples,1,32),Mathf.Clamp(d.settings.spatialSamples,1,16),Mathf.Clamp(d.settings.spatialRadius,1,32),Mathf.Max(.00001f,d.settings.rayBias));
                cmd.SetBufferData(c.lights,d.lights);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ParametersId,d.environmentParameters);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.TintId,d.environmentTint);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ConstantId,d.environmentConstant);
                d.scene.BuildAndBind(cmd,d.rayShader);cmd.SetRayTracingShaderPass(d.rayShader,"HoRTGISecondarySurface");
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGISecondary",c.secondary);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIFrame",frame);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIRaySettings",new Vector4(settings.w,Mathf.Max(settings.w*2,d.settings.maxDistance),d.settings.useEnvironment?1:0,100000));
                cmd.SetRayTracingIntParam(d.rayShader,"_HoRTGIFrameIndex",c.frame);
                cmd.DispatchRays(d.rayShader,"GenerateSecondary",(uint)c.width,(uint)c.height,1);
                foreach(var name in new[]{"ShadeSecondary","Temporal","Spatial","Final"})
                {
                    int kernel=d.shader.FindKernel(name);d.scene.BuildAndBind(cmd,d.shader,kernel);
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGILights",c.lights);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIReservoirs",c.reservoirs);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIHistory",c.history);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGINeighbors",c.neighbors);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGISecondary",c.secondary);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTGIDiffuse",c.diffuse.nameID);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIFrame",frame);cmd.SetComputeVectorParam(d.shader,"_HoRTGISettings",settings);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));cmd.SetComputeIntParam(d.shader,"_HoRTGIFrameIndex",c.frame);
                    cmd.SetComputeMatrixParam(d.shader,"_HoRTGIPreviousVP",c.previousVP);cmd.SetComputeMatrixParam(d.shader,"_HoRTGIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousCameraPosition",c.previousCamera);cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousViewZ",c.previousViewZ);cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousClipParameters",c.previousClip);
                    cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                cmd.SetGlobalFloat("_HoRTGIActive",1);cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",c.diffuse.nameID);
                c.Complete(d.view,d.projection,d.camera,d.revision,signature);
            }
            public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
            {
                var d=Snapshot(data.cameraData.camera,data.cameraData.GetViewMatrix(),data.cameraData.GetProjectionMatrix());
                var cmd=CommandBufferPool.Get("Ho-RTGI reference GI passes");
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd,data.cameraData);
                if(settings.useEnvironment&&HoAtmosphereBindings.compatibilityTexture!=null)
                {
                    cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,HoAtmosphereBindings.compatibilityTexture.nameID);
                    d.environmentParameters=HoAtmosphereBindings.compatibilityParameters;d.environmentTint=HoAtmosphereBindings.compatibilityTint;
                    d.environmentConstant=HoAtmosphereBindings.compatibilityConstant;d.environmentRevision=HoAtmosphereBindings.compatibilityRevision;
                }
                else cmd.SetRayTracingTextureParam(d.rayShader,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                Dispatch(cmd,d,true,data.cameraData);rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
