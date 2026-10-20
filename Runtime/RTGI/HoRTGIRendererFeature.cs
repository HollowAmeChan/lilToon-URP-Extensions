#pragma warning disable CS0618,CS0672
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTDI;
using lilToon.URP.Extensions.RTGI.NRD;
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
        [SerializeField] private Shader compositeShader;
        [SerializeField] private ComputeShader[] nrdShaders;
        private HoRTScene scene;
        private HoRTGISecondaryMaterials materials;
        private readonly Dictionary<Camera,HoRTGIContext> contexts = new();
        private readonly List<Camera> dead = new();
        private HoRTGIPass pass;
        private HoRTGIDebugPass debugPass;
        private Material debugMaterial;
        private HoRTGICompositePass compositePass;
        private Material compositeMaterial;
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
            if(compositeShader==null)compositeShader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(root+"HoRTGIComposite.shader");
            if(nrdShaders==null||nrdShaders.Length!=HoRTGINrdManifest.Identifiers.Length)nrdShaders=new ComputeShader[HoRTGINrdManifest.Identifiers.Length];
            for(int i=0;i<nrdShaders.Length;i++)if(nrdShaders[i]==null)nrdShaders[i]=UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(root+"NRD/HoRTGINrd"+i.ToString("D2")+".compute");
            #endif
            if(secondaryMaterialShader!=null)materials=new HoRTGISecondaryMaterials(secondaryMaterialShader);
            scene=materials!=null?new HoRTScene(materials.Resolve,HoRTGISecondaryMaterials.SupportsCutout):new HoRTScene();
            pass=new HoRTGIPass();debugPass=new HoRTGIDebugPass();compositePass=new HoRTGICompositePass();
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        private void ResetCamera(ScriptableRenderContext context,Camera camera)
        {
            Shader.SetGlobalFloat("_HoRTGIActive",0);Shader.SetGlobalTexture("_HoRTGIDiffuseTexture",Texture2D.blackTexture);
            Shader.SetGlobalTexture("_HoRTGIRawDiffuseTexture",Texture2D.blackTexture);
            if(!isActive||!settings.enabled)foreach(var item in contexts.Values){item.historyValid=false;if(item.nrd!=null)item.nrd.historyValid=false;}
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            var camera=data.cameraData.camera;var volume=VolumeManager.instance.stack.GetComponent<HoRTGIVolume>();
            if(!settings.enabled||(volume!=null&&volume.enable.overrideState&&!volume.enable.value)||data.cameraData.renderType!=CameraRenderType.Base
                ||(camera.cameraType!=CameraType.Game&&(camera.cameraType!=CameraType.SceneView||!settings.inSceneView)))
            {if(contexts.TryGetValue(camera,out var skipped)){skipped.historyValid=false;if(skipped.nrd!=null)skipped.nrd.historyValid=false;}return;}
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
            materials.BeginFrame();scene.Synchronize(geometry,0xff,settings.excludedRayMaterials);materials.EndFrame();
            var lights=CollectLights(settings.lightLayers.value,out int excluded,out int signature);
            var captured=new PassSettings(settings);
            unchecked {signature=(signature*397^materials.Signature)*397^captured.signature;}
            if(!contexts.TryGetValue(camera,out var context)){context=new HoRTGIContext();contexts.Add(camera,context);}
            var descriptor=data.cameraData.cameraTargetDescriptor;
            context.Prepare(descriptor.width,descriptor.height,lights.Count,format);LastContext=context;
            context.PrepareEnvironment(captured.useEnvironment&&captured.secondaryEnvironmentSamples>0);
            bool denoise=volume!=null&&volume.denoise.overrideState?volume.denoise.value:settings.denoise;
            context.PrepareDenoiser(denoise,nrdShaders);
            context.PrepareVisibility(scene.CutoutCount>0);
            HoAttributeCompositeConsumerRegistry.DeclareForCamera(camera,this,"RTGI primary normals",System.Array.Empty<HoACQueryDescriptor>(),null,HoACDemandResources.HighPrecisionSurfaceNormal);
            if(materials.TimeVarying)context.historyValid=false;
            pass.Setup(context,scene,shader,rayShader,captured,lights.Count>0?lights.ToArray():new HoRTDIRendererFeature.AnalyticLight[1],lights.Count,signature);
            bool compositeRequested=volume!=null&&volume.composite.overrideState?volume.composite.value:settings.composite;
            float amount=volume!=null&&volume.intensity.overrideState?volume.intensity.value:settings.intensity;
            amount=float.IsFinite(amount)?Mathf.Max(0,amount):0;
            bool composite=compositeRequested&&amount>0&&descriptor.msaaSamples==1&&descriptor.dimension==TextureDimension.Tex2D
                &&!data.cameraData.xr.enabled&&camera.rect==new Rect(0,0,1,1);
            if(composite&&compositeMaterial==null&&compositeShader!=null)compositeMaterial=CoreUtils.CreateEngineMaterial(compositeShader);
            composite&=compositeMaterial!=null;
            pass.renderPassEvent=composite?RenderPassEvent.BeforeRenderingTransparents:RenderPassEvent.BeforeRenderingPostProcessing;
            renderer.EnqueuePass(pass);
            Status=$"一跳 diffuse GI：{lights.Count} 灯 / {scene.InstanceCount} RT submesh / 排除 {excluded} 灯、{materials.Rejections.Count} 命中材质、{scene.Rejections.Count} 几何项";
            if(denoise)Status+=" / "+context.nrdStatus;
            if(scene.CutoutCount>0)Status+=" / 基础裁切 "+scene.CutoutCount;
            if(composite){compositePass.Setup(compositeMaterial,context,amount);renderer.EnqueuePass(compositePass);Status+=" / GI 加法合成";}
            else if(compositeRequested&&amount>0)Status+=" / 合成需要完整Shader、全视口Mono单采样输入";
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
            HoAttributeCompositeConsumerRegistry.Remove(this);
            foreach(var context in contexts.Values)context.Dispose();contexts.Clear();scene?.Dispose();scene=null;
            materials?.Dispose();materials=null;debugPass?.ReleaseDebugResources();CoreUtils.Destroy(debugMaterial);debugMaterial=null;LastContext=null;
            compositePass?.Dispose();compositePass=null;CoreUtils.Destroy(compositeMaterial);compositeMaterial=null;
        }
        protected override void Dispose(bool disposing)
        {if(registered)RenderPipelineManager.beginCameraRendering-=ResetCamera;registered=false;ResetCamera(default,null);DisposeResources();}

        // Numeric frame values are captured before resources/signatures are prepared.
        // Recording a graph must not retain the mutable inspector settings object.
        internal readonly struct PassSettings
        {
            internal readonly bool temporal,spatial,useEnvironment,includeDirectEnvironment;
            internal readonly int secondaryLightSamples,secondaryEnvironmentSamples,spatialSamples,maxHistory,signature;
            internal readonly float spatialRadius,rayBias,maxDistance;
            internal PassSettings(HoRTGISettings source)
            {
                temporal=source.temporal;spatial=source.spatial;useEnvironment=source.useEnvironment;includeDirectEnvironment=source.includeDirectEnvironment;
                secondaryLightSamples=Mathf.Clamp(source.secondaryLightSamples,1,32);secondaryEnvironmentSamples=Mathf.Clamp(source.secondaryEnvironmentSamples,0,32);
                spatialSamples=Mathf.Clamp(source.spatialSamples,1,16);maxHistory=Mathf.Clamp(source.maxHistory,1,32);
                spatialRadius=Mathf.Clamp(Finite(source.spatialRadius,16),1,32);rayBias=Mathf.Max(.00001f,Finite(source.rayBias,.001f));
                maxDistance=Mathf.Max(rayBias*2,Finite(source.maxDistance,1000));
                unchecked
                {
                    int hash=secondaryLightSamples;hash=hash*397^secondaryEnvironmentSamples;hash=hash*397^spatialSamples;hash=hash*397^maxHistory;
                    hash=hash*397^spatialRadius.GetHashCode();hash=hash*397^rayBias.GetHashCode();hash=hash*397^maxDistance.GetHashCode();
                    hash=hash*397^temporal.GetHashCode();hash=hash*397^spatial.GetHashCode();hash=hash*397^useEnvironment.GetHashCode();signature=hash*397^includeDirectEnvironment.GetHashCode();
                }
            }
            private static float Finite(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:value;
        }
        private sealed class HoRTGIPass : ScriptableRenderPass
        {
            private HoRTGIContext context;private HoRTScene scene;private ComputeShader shader;private RayTracingShader rayShader;
            private PassSettings settings;private HoRTDIRendererFeature.AnalyticLight[] lights;private int lightCount,signature;
            private double renderTime;
            private static readonly string[] Stages={"BuildEnvironmentPdf","PresampleEnvironment","ShadeSecondary","Temporal","Spatial","Final"};
            internal HoRTGIPass(){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;}
            internal void Setup(HoRTGIContext context,HoRTScene scene,ComputeShader shader,RayTracingShader rayShader,PassSettings settings,HoRTDIRendererFeature.AnalyticLight[] lights,int lightCount,int signature)
            {
                this.context=context;this.scene=scene;this.shader=shader;this.rayShader=rayShader;this.settings=settings;this.lights=lights;this.lightCount=lightCount;this.signature=signature;
                renderTime=Time.unscaledTimeAsDouble;
                #if UNITY_EDITOR
                if(!Application.isPlaying)renderTime=UnityEditor.EditorApplication.timeSinceStartup;
                #endif
            }
            private sealed class Data
            {
                internal HoRTGIContext context;internal HoRTScene.Inputs scene;internal HoRTPrimarySurfaceInputs primary;
                internal ComputeShader shader;internal RayTracingShader rayShader;internal PassSettings settings;
                internal HoRTDIRendererFeature.AnalyticLight[] lights;internal int lightCount,signature;internal uint revision,environmentRevision;
                internal Matrix4x4 view,projection;internal Camera camera;internal TextureHandle diffuse,rawDiffuse,environment;
                internal double renderTime;
                internal Vector4 environmentParameters,environmentTint,environmentConstant;
                internal RTHandle compatibilityEnvironment;
            }
            private Data Snapshot(Camera camera,Matrix4x4 view,Matrix4x4 projection) => new Data
            {context=context,scene=scene.ReadCompatibility(),shader=shader,rayShader=rayShader,settings=settings,lights=lights,lightCount=lightCount,signature=signature,revision=scene.Revision,view=view,projection=projection,camera=camera,renderTime=renderTime};
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddUnsafePass<Data>("Ho-RTGI reference GI passes",out var data))
                {
                    var source=Snapshot(camera.camera,camera.GetViewMatrix(),camera.GetProjectionMatrix());
                    data.context=source.context;data.shader=source.shader;data.rayShader=source.rayShader;data.settings=source.settings;data.lights=source.lights;data.lightCount=source.lightCount;
                    data.signature=source.signature;data.revision=source.revision;data.view=source.view;data.projection=source.projection;data.camera=source.camera;
                    data.renderTime=source.renderTime;
                    data.scene=scene.Read(graph,builder);data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                    foreach(var buffer in new[]{context.reservoirs,context.history,context.secondary,context.lights,context.finalVisibility})builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
                    builder.UseBuffer(graph.ImportBuffer(context.environmentRis),AccessFlags.ReadWrite);
                    if(context.environmentPdf!=null)builder.UseTexture(graph.ImportTexture(context.environmentPdf),AccessFlags.ReadWrite);
                    builder.UseBuffer(graph.ImportBuffer(context.neighbors));
                    data.rawDiffuse=graph.ImportTexture(context.diffuse);builder.UseTexture(data.rawDiffuse,AccessFlags.WriteAll);
                    if(context.nrd!=null){context.nrd.Declare(graph,builder);data.diffuse=graph.ImportTexture(context.PublishedDiffuse);}
                    else data.diffuse=data.rawDiffuse;
                    // RenderGraph pass data is pooled; absent/disabled sky must not
                    // inherit the previous pass's handle, parameters or revision.
                    data.environment=TextureHandle.nullHandle;data.environmentRevision=0;
                    data.compatibilityEnvironment=null;
                    data.environmentParameters=data.environmentTint=data.environmentConstant=Vector4.zero;
                    var sky=frame.GetOrCreate<HoAtmosphereRenderGraphResources>();
                    if(settings.useEnvironment&&sky.IsValid)
                    {
                        data.environment=sky.radianceTexture;data.environmentParameters=sky.parameters;data.environmentTint=sky.tint;data.environmentConstant=sky.constantColor;data.environmentRevision=sky.revision;
                        builder.UseTexture(data.environment);
                    }
                    builder.SetGlobalTextureAfterPass(data.diffuse,Shader.PropertyToID("_HoRTGIDiffuseTexture"));
                    builder.SetGlobalTextureAfterPass(data.rawDiffuse,Shader.PropertyToID("_HoRTGIRawDiffuseTexture"));
                    var output=frame.GetOrCreate<HoRTGIRenderGraphResources>();output.diffuse=data.diffuse;output.rawDiffuse=data.rawDiffuse;
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
                c.MarkGpu(cmd,"GI.Total",true);
                bool temporal=d.settings.temporal&&c.historyValid&&c.sceneRevision==d.revision&&c.lightingSignature==signature;
                c.lastTemporal=temporal;
                var frame=new Vector4(c.width,c.height,d.lightCount,0);
                var settings=new Vector4(d.settings.secondaryLightSamples,d.settings.spatialSamples,d.settings.spatialRadius,d.settings.rayBias);
                bool environment=c.environmentSampling&&d.settings.useEnvironment&&d.environmentParameters.w>.5f;
                cmd.SetBufferData(c.lights,d.lights);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ParametersId,d.environmentParameters);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.TintId,d.environmentTint);
                cmd.SetRayTracingVectorParam(d.rayShader,HoAtmosphereBindings.ConstantId,d.environmentConstant);
                c.MarkGpu(cmd,"GI.AS",true);d.scene.BuildAndBind(cmd,d.rayShader);c.MarkGpu(cmd,"GI.AS",false);cmd.SetRayTracingShaderPass(d.rayShader,"HoRTGISecondarySurface");
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGISecondary",c.secondary);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIFrame",frame);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIRaySettings",new Vector4(settings.w,d.settings.maxDistance,d.settings.useEnvironment&&d.settings.includeDirectEnvironment?1:0,100000));
                cmd.SetRayTracingIntParam(d.rayShader,"_HoRTGIFrameIndex",c.frame);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGISettings",settings);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIEnvironmentRISLayout",new Vector4(HoRTGIContext.RisTileSize,HoRTGIContext.RisTileCount,0,0));
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIEnvironment",new Vector4(HoRTGIContext.EnvironmentPdfSize,d.settings.secondaryEnvironmentSamples,environment?1:0,0));
                cmd.SetRayTracingIntParam(d.rayShader,"_HoRTGIEnvironmentBrdfSamples",0);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGILights",c.lights);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIReservoirs",c.reservoirs);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIEnvironmentRIS",c.environmentRis);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIFinalVisibility",c.finalVisibility);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGIHistory",c.history);
                cmd.SetRayTracingBufferParam(d.rayShader,"_HoRTGINeighbors",c.neighbors);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),1));
                cmd.SetRayTracingMatrixParam(d.rayShader,"_HoRTGIPreviousVP",c.previousVP);cmd.SetRayTracingMatrixParam(d.rayShader,"_HoRTGIPreviousInverseVP",c.previousInverseVP);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIPreviousCameraPosition",c.previousCamera);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIPreviousTowardsEye",c.previousTowardsEye);
                cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIPreviousViewZ",c.previousViewZ);cmd.SetRayTracingVectorParam(d.rayShader,"_HoRTGIPreviousClipParameters",c.previousClip);
                cmd.SetRayTracingTextureParam(d.rayShader,"_HoRTGIEnvironmentPdf",c.environmentPdf!=null?c.environmentPdf.nameID:new RenderTargetIdentifier(Texture2D.blackTexture));
                c.MarkGpu(cmd,"GI.GenerateSecondary",true);cmd.DispatchRays(d.rayShader,"GenerateSecondary",(uint)c.width,(uint)c.height,1);c.MarkGpu(cmd,"GI.GenerateSecondary",false);
                foreach(var name in Stages)
                {
                    if((name=="BuildEnvironmentPdf"||name=="PresampleEnvironment")&&!environment)continue;
                    if(name=="BuildEnvironmentPdf"&&c.environmentPdfReady&&c.environmentPdfRevision==d.environmentRevision)continue;
                    if(name!="Final")c.MarkGpu(cmd,"GI."+name,true);
                    if(name=="ShadeSecondary"&&c.alphaVisibility)
                    {cmd.DispatchRays(d.rayShader,"ShadeSecondaryNative",(uint)c.width,(uint)c.height,1);c.MarkGpu(cmd,"GI."+name,false);continue;}
                    if((name=="Temporal"||name=="Spatial")&&c.alphaVisibility)
                    {cmd.DispatchRays(d.rayShader,name+"Native",(uint)c.width,(uint)c.height,1);c.MarkGpu(cmd,"GI."+name,false);continue;}
                    if(name=="Final")
                    {
                        #if UNITY_EDITOR
                        c.ValidationBeforeFinal?.Invoke(cmd,c);
                        #endif
                        if(c.alphaVisibility){c.MarkGpu(cmd,"GI.FinalVisibility",true);cmd.DispatchRays(d.rayShader,"TraceFinalVisibility",(uint)c.width,(uint)c.height,1);c.MarkGpu(cmd,"GI.FinalVisibility",false);}
                        c.MarkGpu(cmd,"GI.Final",true);
                    }
                    int kernel=d.shader.FindKernel(name=="Final"&&c.nrd!=null?"FinalNRD":name);d.scene.BuildAndBind(cmd,d.shader,kernel);
                    if(name=="Final"&&c.nrd!=null)c.nrd.BindFinal(cmd,d.shader,kernel);
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGILights",c.lights);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIReservoirs",c.reservoirs);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIHistory",c.history);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGINeighbors",c.neighbors);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGISecondary",c.secondary);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIEnvironmentRIS",c.environmentRis);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTGIFinalVisibility",c.finalVisibility);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIEnvironmentRISLayout",new Vector4(HoRTGIContext.RisTileSize,HoRTGIContext.RisTileCount,0,0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIEnvironment",new Vector4(HoRTGIContext.EnvironmentPdfSize,d.settings.secondaryEnvironmentSamples,environment?1:0,0));
                    cmd.SetComputeIntParam(d.shader,"_HoRTGIEnvironmentBrdfSamples",0);
                    cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.ParametersId,d.environmentParameters);
                    cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.TintId,d.environmentTint);
                    cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.ConstantId,d.environmentConstant);
                    if(d.environment.IsValid())cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,d.environment);
                    else if(d.compatibilityEnvironment!=null)cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,d.compatibilityEnvironment.nameID);
                    else cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                    if(c.environmentPdf!=null)cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTGIEnvironmentPdf",c.environmentPdf.nameID);
                    else cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTGIEnvironmentPdf",Texture2D.blackTexture);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTGIDiffuse",c.diffuse.nameID);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIFrame",frame);cmd.SetComputeVectorParam(d.shader,"_HoRTGISettings",settings);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),c.alphaVisibility?1:0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));cmd.SetComputeIntParam(d.shader,"_HoRTGIFrameIndex",c.frame);
                    cmd.SetComputeMatrixParam(d.shader,"_HoRTGIPreviousVP",c.previousVP);cmd.SetComputeMatrixParam(d.shader,"_HoRTGIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousCameraPosition",c.previousCamera);cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousViewZ",c.previousViewZ);cmd.SetComputeVectorParam(d.shader,"_HoRTGIPreviousClipParameters",c.previousClip);
                    if(name=="BuildEnvironmentPdf")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTGIEnvironmentPdfOutput",c.environmentPdf.nameID,0);
                        cmd.DispatchCompute(d.shader,kernel,(HoRTGIContext.EnvironmentPdfSize+7)/8,(HoRTGIContext.EnvironmentPdfSize+7)/8,1);
                        cmd.GenerateMips(c.environmentPdf.nameID);c.environmentPdfReady=true;c.environmentPdfRevision=d.environmentRevision;
                    }
                    else if(name=="PresampleEnvironment")cmd.DispatchCompute(d.shader,kernel,HoRTGIContext.RisTileSize/256,HoRTGIContext.RisTileCount,1);
                    else cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                    c.MarkGpu(cmd,"GI."+name,false);
                }
                if(c.nrd!=null)
                {
                    bool reset=!c.historyValid||c.sceneRevision!=d.revision||c.lightingSignature!=signature;
                    c.MarkGpu(cmd,"GI.NRD",true);
                    c.nrd.Record(cmd,d.view,d.projection,c.frame,reset,d.renderTime,!Application.isPlaying&&d.camera.cameraType==CameraType.SceneView);
                    c.MarkGpu(cmd,"GI.NRD",false);c.MarkGpu(cmd,"GI.Resolve",true);
                    int resolve=d.shader.FindKernel("ResolveNRD");
                    cmd.SetComputeFloatParam(d.shader,"_HoRTGINrdSignalScale",HoRTGINrdContext.SignalScale);
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,resolve,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,resolve);
                    cmd.SetComputeTextureParam(d.shader,resolve,"_HoRTGINrdOutput",c.nrd.output.nameID);
                    cmd.SetComputeTextureParam(d.shader,resolve,"_HoRTGIFiltered",c.nrd.filtered.nameID);
                    cmd.DispatchCompute(d.shader,resolve,(c.width+7)/8,(c.height+7)/8,1);
                    c.MarkGpu(cmd,"GI.Resolve",false);
                }
                cmd.SetGlobalFloat("_HoRTGIActive",1);cmd.SetGlobalTexture("_HoRTGIDiffuseTexture",c.PublishedDiffuse.nameID);cmd.SetGlobalTexture("_HoRTGIRawDiffuseTexture",c.diffuse.nameID);
                c.MarkGpu(cmd,"GI.Total",false);
                c.Complete(d.view,d.projection,d.camera,d.revision,signature);
            }
            public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
            {
                var d=Snapshot(data.cameraData.camera,data.cameraData.GetViewMatrix(),data.cameraData.GetProjectionMatrix());
                var cmd=CommandBufferPool.Get("Ho-RTGI reference GI passes");
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd,data.cameraData);
                if(settings.useEnvironment&&HoAtmosphereBindings.compatibilityTexture!=null)
                {
                    d.compatibilityEnvironment=HoAtmosphereBindings.compatibilityTexture;
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
