#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Atmosphere;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTDI.NRD;
using lilToon.URP.Extensions.Debugging;
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
        [SerializeField] private RayTracingShader finalVisibilityShader;
        [SerializeField] private Shader visibilityMaterialShader;
        [SerializeField] private ComputeShader[] nrdShaders;
        [SerializeField] private Shader debugShader;
        [SerializeField] private Shader debugOverlayShader;
        [SerializeField] private Shader compositeShader;
        private HoRTScene scene;
        private HoRTDIVisibilityMaterials visibilityMaterials;
        private readonly Dictionary<Camera,HoRTDIContext> contexts=new();
        private readonly List<Camera> dead=new();
        private HoRTDIPass pass;
        private HoRTDIDebugPass debugPass;
        private Material debugMaterial;
        private HoRTDICompositePass compositePass;
        private Material compositeMaterial;
        private bool registered;
        public HoRTDISettings Settings=>settings;
        public string Status {get;private set;}="未生产";
        internal HoRTDIContext LastContext {get;private set;}
        internal HoRTDIContext ContextForCamera(Camera camera)=>camera!=null&&contexts.TryGetValue(camera,out var context)?context:null;
        internal static readonly ProfilingSampler FinalVisibilityProfiling=new("HoRTDI Final Visibility");
        internal static readonly ProfilingSampler VisibilityGradientsProfiling=new("HoRTDI Visibility Gradients");
        public override void Create()
        {
            DisposeResources();pass=new HoRTDIPass();debugPass=new HoRTDIDebugPass();compositePass=new HoRTDICompositePass();
            #if UNITY_EDITOR
            EnsureEditorShaderReferences();
            #endif
            bool alpha=SystemInfo.supportsRayTracing&&finalVisibilityShader!=null&&visibilityMaterialShader!=null;
            if(alpha)visibilityMaterials=new HoRTDIVisibilityMaterials(visibilityMaterialShader);
            scene=alpha?new HoRTScene(visibilityMaterials.Resolve,HoRTDIVisibilityMaterials.SupportsCutout):new HoRTScene();
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        #if UNITY_EDITOR
        // Repair missing references without resetting the runtime context or
        // replacing assigned shader overrides. SaveAssets persists the subasset.
        internal bool EnsureEditorShaderReferences()
        {
            const string root="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/";
            bool changed=LoadMissing(ref shader,root+"HoRTDI.compute");
            changed|=LoadMissing(ref finalVisibilityShader,root+"HoRTDIFinalVisibility.raytrace");
            changed|=LoadMissing(ref visibilityMaterialShader,root+"HoRTDIVisibilityMaterial.shader");
            changed|=LoadMissing(ref debugShader,root+"HoRTDIDebug.shader");
            changed|=LoadMissing(ref compositeShader,root+"HoRTDIComposite.shader");
            changed|=LoadMissing(ref debugOverlayShader,"Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Debug/Shaders/HoDebugGridOverlay.shader");
            if(nrdShaders==null||nrdShaders.Length!=HoRTDINrdManifest.Identifiers.Length)
            {Array.Resize(ref nrdShaders,HoRTDINrdManifest.Identifiers.Length);changed=true;}
            for(int i=0;i<nrdShaders.Length;i++)changed|=LoadMissing(ref nrdShaders[i],$"{root}NRD/HoRTDINrd{i:00}.compute");
            if(changed)UnityEditor.EditorUtility.SetDirty(this);
            return changed;
        }
        private static bool LoadMissing<T>(ref T asset,string path) where T:UnityEngine.Object
        {
            if(asset!=null)return false;
            asset=UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);return asset!=null;
        }
        #endif
        private void ResetCamera(ScriptableRenderContext context,Camera camera)
        {
            Shader.SetGlobalFloat("_HoRTDIActive",0);Shader.SetGlobalTexture("_HoRTDIDiffuseTexture",Texture2D.blackTexture);Shader.SetGlobalTexture("_HoRTDISpecularTexture",Texture2D.blackTexture);
            Shader.SetGlobalTexture("_HoRTDIRawDiffuseTexture",Texture2D.blackTexture);Shader.SetGlobalTexture("_HoRTDIRawSpecularTexture",Texture2D.blackTexture);
            if(!isActive||!settings.enabled)foreach(var item in contexts.Values){item.historyValid=false;item.nrdHistoryValid=false;}
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct AnalyticLight {public Vector4 positionRange,radianceType,directionOuter,innerShadow;}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            var camera=data.cameraData.camera;var volume=VolumeManager.instance.stack.GetComponent<HoRTDIVolume>();
            var viewSettings=new HoRTDIViewSettings(settings,volume);
            if(!viewSettings.enabled||data.cameraData.renderType!=CameraRenderType.Base
                ||(camera.cameraType!=CameraType.Game&&(camera.cameraType!=CameraType.SceneView||!settings.inSceneView)))
            {if(contexts.TryGetValue(camera,out var skipped)){skipped.historyValid=false;skipped.nrdHistoryValid=false;}Status=viewSettings.enabled?"当前相机未生产":"未生产：需要 Feature 总开关及 Volume 启用覆盖";return;}
            if(!scene.IsAvailable||shader==null){Status="需要 D3D12 inline RT 与 Compute Shader";return;}
            var format=GraphicsFormat.R16G16B16A16_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))format=GraphicsFormat.R32G32B32A32_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore)){Status="无 HDR UAV 格式";return;}
            dead.Clear();foreach(var pair in contexts)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var key in dead){contexts[key].Dispose();contexts.Remove(key);}
            var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);var geometry=new List<Renderer>();
            foreach(var item in renderers)if((settings.geometryLayers.value&(1<<item.gameObject.layer))!=0)geometry.Add(item);
            visibilityMaterials?.BeginFrame();scene.Synchronize(geometry,0xff,settings.excludedRayMaterials);visibilityMaterials?.EndFrame();
            var lightList=new List<AnalyticLight>();var lightKeys=new List<Light>();int signature=viewSettings.initialSamples.GetHashCode()^settings.rayBias.GetHashCode()^((int)viewSettings.localSampling*397)^((int)viewSettings.finalSampling*457)^(visibilityMaterials?.Signature??0);int excludedLights=0;
            var infiniteLights=new List<AnalyticLight>();var infiniteKeys=new List<Light>();
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
            {
                if(!light.enabled||!light.gameObject.activeInHierarchy||(settings.lightLayers.value&(1<<light.gameObject.layer))==0)continue;
                if(light.type!=LightType.Point&&light.type!=LightType.Spot&&light.type!=LightType.Directional){excludedLights++;continue;}
                // Cookies / light-layer material routing are deliberately not silently approximated.
                bool bakedOnly=light.bakingOutput.lightmapBakeType==LightmapBakeType.Baked;
                #if UNITY_EDITOR
                // In an unbaked Editor scene the requested mode is authoritative.
                bakedOnly=light.lightmapBakeType==LightmapBakeType.Baked;
                #endif
                if(light.cookie!=null||light.cullingMask!=-1||bakedOnly){excludedLights++;continue;}
                var color=QualitySettings.activeColorSpace==ColorSpace.Linear?light.color.linear:light.color;
                if(light.useColorTemperature)color*=Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                color*=Mathf.Max(0,light.intensity);
                var direction=light.transform.forward;
                var item=new AnalyticLight{positionRange=new Vector4(light.transform.position.x,light.transform.position.y,light.transform.position.z,Mathf.Max(.001f,light.range)),
                    radianceType=new Vector4(color.r,color.g,color.b,light.type==LightType.Point?0:light.type==LightType.Spot?1:2),
                    directionOuter=new Vector4(direction.x,direction.y,direction.z,Mathf.Cos(light.spotAngle*Mathf.Deg2Rad*.5f)),
                    innerShadow=new Vector4(Mathf.Cos(light.innerSpotAngle*Mathf.Deg2Rad*.5f),0,0,0)};
                if(light.type==LightType.Directional){infiniteLights.Add(item);infiniteKeys.Add(light);}
                else {lightList.Add(item);lightKeys.Add(light);}
            }
            // RTXDI's uniform local and infinite samplers operate on separate
            // contiguous ranges. Stable ordering within each range keeps the
            // identity-based temporal maps valid when a light changes type.
            int localCount=lightList.Count;lightList.AddRange(infiniteLights);lightKeys.AddRange(infiniteKeys);
            if(!contexts.TryGetValue(camera,out var context)){context=new HoRTDIContext();contexts.Add(camera,context);}
            var descriptor=data.cameraData.cameraTargetDescriptor;
            context.Prepare(descriptor.width,descriptor.height,lightList.Count,format);LastContext=context;
            context.PrepareFinalVisibility(scene.CutoutCount>0);
            context.PreparePower(localCount,viewSettings.localSampling==HoRTDILocalSampling.Power);
            context.PrepareDenoiser(viewSettings.denoise,nrdShaders);
            HoAttributeCompositeConsumerRegistry.DeclareForCamera(camera,this,"RTDI primary normals",System.Array.Empty<HoACQueryDescriptor>(),null,HoACDemandResources.HighPrecisionSurfaceNormal);
            var lightData=lightList.Count>0?lightList.ToArray():new AnalyticLight[1];
            pass.Setup(context,scene,shader,finalVisibilityShader,settings,viewSettings,lightData,lightKeys.ToArray(),lightList.Count,localCount,signature);
            float compositeAmount=viewSettings.compositeAmount;
            bool composite=viewSettings.composite&&compositeAmount>0&&descriptor.msaaSamples==1&&descriptor.dimension==TextureDimension.Tex2D
                &&!data.cameraData.xr.enabled&&camera.rect==new Rect(0,0,1,1);
            if(composite&&compositeMaterial==null&&compositeShader!=null)compositeMaterial=CoreUtils.CreateEngineMaterial(compositeShader);
            composite&=compositeMaterial!=null;
            // Keep the legacy producer timing when compositing is disabled.
            // In composite mode both passes share the named pre-transparent event,
            // ordered by enqueue order: DI -> replacement -> transparent rendering.
            pass.renderPassEvent=composite?RenderPassEvent.BeforeRenderingTransparents:RenderPassEvent.BeforeRenderingPostProcessing;
            renderer.EnqueuePass(pass);Status=$"RTXDI DI：{(context.powerSampling?"功率":"均匀")} / {localCount} 局部 / {infiniteLights.Count} 方向灯 / {scene.InstanceCount} RT submesh / 排除 {excludedLights} 灯、{scene.Rejections.Count} 几何项";
            if(composite){compositePass.Setup(compositeMaterial,context,viewSettings.compositeMode,compositeAmount);renderer.EnqueuePass(compositePass);Status+=viewSettings.compositeMode==HoRTDICompositeMode.Additive?" / 加法叠加":" / 参考 PBR 合成";}
            else if(viewSettings.composite)Status+=" / 合成未启用（需要 Shader、全视口 Mono 单采样输入及非零权重）";
            if(viewSettings.finalSampling==HoRTDIFinalSampling.TripleAnalytic)Status+=" / 三最终样本";
            if(viewSettings.denoise)Status+=" / "+context.nrdStatus;
            if(volume!=null&&volume.debugMode.value!=HoRTDIDebugMode.Off&&(camera.cameraType==CameraType.SceneView?volume.debugInSceneView.value:volume.debugInGameView.value))
            {
                if(debugMaterial==null&&debugShader!=null)debugMaterial=CoreUtils.CreateEngineMaterial(debugShader);
                HoDebugGrid.EnsureOverlay(debugOverlayShader);
                if(debugMaterial!=null){debugPass.Setup(volume.debugMode.value,debugMaterial,context);debugPass.Enqueue(renderer);}
            }
        }
        private void DisposeResources()
        {HoAttributeCompositeConsumerRegistry.Remove(this);foreach(var context in contexts.Values)context.Dispose();contexts.Clear();scene?.Dispose();scene=null;visibilityMaterials?.Dispose();visibilityMaterials=null;debugPass?.ReleaseDebugResources();CoreUtils.Destroy(debugMaterial);debugMaterial=null;compositePass?.Dispose();compositePass=null;CoreUtils.Destroy(compositeMaterial);compositeMaterial=null;LastContext=null;}
        protected override void Dispose(bool disposing)
        {if(registered)RenderPipelineManager.beginCameraRendering-=ResetCamera;registered=false;ResetCamera(default,null);DisposeResources();}

        private sealed class HoRTDIPass : ScriptableRenderPass
        {
            // Numeric values used after recording must not refer to the live
            // Inspector object. Geometry/material filtering is already applied
            // by the collector before this snapshot is constructed.
            private readonly struct PassSettings
            {
                internal readonly bool useEnvironment,temporal,spatial;
                internal readonly int environmentSamples,environmentBrdfSamples,initialSamples,maxHistory,spatialSamples;
                internal readonly float spatialRadius,rayBias,environmentIntensity;
                internal PassSettings(HoRTDISettings source,HoRTDIViewSettings view)
                {
                    useEnvironment=view.useEnvironment;temporal=view.temporal;spatial=view.spatial;
                    environmentSamples=view.environmentSamples;environmentBrdfSamples=view.environmentBrdfSamples;initialSamples=view.initialSamples;maxHistory=view.maxHistory;spatialSamples=view.spatialSamples;
                    spatialRadius=view.spatialRadius;rayBias=source.rayBias;environmentIntensity=view.environmentIntensity;
                }
            }
            private static readonly string[] ReferenceStages={"BuildPowerPdf","Presample","BuildEnvironmentPdf","PresampleEnvironment","Initial","Temporal","Spatial","Shade"};
            private HoRTDIContext context;private HoRTScene scene;private ComputeShader shader;private PassSettings settings;
            private RayTracingShader finalVisibilityShader;
            private AnalyticLight[] lights;private Light[] lightKeys;private int lightCount,localLightCount,signature;
            private int requestedFinalSamples;
            internal HoRTDIPass(){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;}
            internal void Setup(HoRTDIContext context,HoRTScene scene,ComputeShader shader,RayTracingShader finalVisibilityShader,HoRTDISettings settings,HoRTDIViewSettings view,AnalyticLight[] lights,Light[] keys,int count,int localCount,int signature)
            {
                this.context=context;this.scene=scene;this.shader=shader;this.finalVisibilityShader=finalVisibilityShader;this.settings=new PassSettings(settings,view);this.lights=lights;lightKeys=keys;lightCount=count;localLightCount=localCount;this.signature=signature;
                requestedFinalSamples=view.finalSampling==HoRTDIFinalSampling.TripleAnalytic?3:1;
                #if UNITY_EDITOR
                ConfigureInput(context.nrd?.ValidationMotionCapture!=null?ScriptableRenderPassInput.Motion:ScriptableRenderPassInput.None);
                #endif
            }
            private sealed class Data
            {
                internal HoRTDIContext context;internal HoRTScene.Inputs scene;internal HoRTPrimarySurfaceInputs primary;
                internal ComputeShader shader;internal PassSettings settings;internal HoRTDIContext.LightFrame lightFrame;
                internal RayTracingShader finalVisibilityShader;
                internal int lightCount,localLightCount,signature;internal uint revision,structureRevision;internal Matrix4x4 view,projection;internal Camera camera;
                internal HoRTDIProjectionSample projectionSample;
                internal double renderTime;
                internal int finalSamples;
                internal float[] auxiliaryCDF;
                internal GraphicsBuffer auxiliaryCDFBuffer,auxiliarySecondaryBuffer;
                internal TextureHandle diffuse,specular,powerPdf;
                internal TextureHandle environment,environmentPdf;
                #if UNITY_EDITOR
                internal TextureHandle motion;
                #endif
                internal Vector4 environmentParameters,environmentTint,environmentConstant;
                internal uint environmentRevision;
            }
            private Data Snapshot(Camera camera,Matrix4x4 view,Matrix4x4 projection) => new Data {context=context,scene=scene.ReadCompatibility(),shader=shader,finalVisibilityShader=finalVisibilityShader,settings=settings,lightFrame=context.CaptureLights(lights,lightKeys),
                lightCount=lightCount,localLightCount=localLightCount,signature=signature,revision=scene.Revision,structureRevision=scene.StructureRevision,view=view,projection=projection,
                projectionSample=new HoRTDIProjectionSample(projection,camera.projectionMatrix,context.width,context.height),camera=camera,renderTime=RenderTime(camera),finalSamples=requestedFinalSamples};
            private static double RenderTime(Camera camera)
            {
                #if UNITY_EDITOR
                // Scene View repaints can occur while the player clock is
                // frozen, notably during editor startup/pause. Use editor time.
                if(!Application.isPlaying&&camera.cameraType==CameraType.SceneView)return UnityEditor.EditorApplication.timeSinceStartup;
                #endif
                return Time.unscaledTimeAsDouble;
            }
            private static void PrepareAuxiliary(Data d)
            {
                var c=d.context;
                if(d.lightCount==0||(d.lightCount==1&&!c.environmentSampling))d.finalSamples=1;
                c.PrepareAuxiliary(d.finalSamples>1,d.lightCount,c.finalVisibility!=null);
                d.auxiliaryCDFBuffer=c.auxiliaryCDF??c.currentToPrevious;
                // Structured UAV validation still needs a binding in the single
                // branch. The existing eight-byte neighbor buffer is never
                // accessed by that branch or the opaque local estimator.
                d.auxiliarySecondaryBuffer=c.auxiliarySecondary??c.neighbors;
                if(d.finalSamples>1)d.auxiliaryCDF=d.lightFrame.AuxiliaryCDF(d.lightCount);
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddUnsafePass<Data>("Ho-RTDI reference DI passes",out var data))
                {
                    var source=Snapshot(camera.camera,camera.GetViewMatrix(),camera.GetProjectionMatrix());
                    data.context=source.context;data.shader=source.shader;data.settings=source.settings;data.lightFrame=source.lightFrame;data.lightCount=source.lightCount;
                    data.finalVisibilityShader=source.finalVisibilityShader;
                    data.localLightCount=source.localLightCount;
                    data.signature=source.signature;data.revision=source.revision;data.structureRevision=source.structureRevision;data.view=source.view;data.projection=source.projection;data.camera=source.camera;
                    data.projectionSample=source.projectionSample;
                    data.renderTime=source.renderTime;
                    data.finalSamples=source.finalSamples;
                    data.scene=scene.Read(graph,builder);data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
                    #if UNITY_EDITOR
                    if(context.nrd?.ValidationMotionCapture!=null)
                    {data.motion=frame.Get<UniversalResourceData>().motionVectorColor;if(data.motion.IsValid())builder.UseTexture(data.motion,AccessFlags.Read);}
                    #endif
                    if(context.finalVisibility!=null)builder.UseTexture(graph.ImportTexture(context.finalVisibility),AccessFlags.ReadWrite);
                    data.environment=TextureHandle.nullHandle;data.environmentPdf=TextureHandle.nullHandle;data.environmentRevision=0;
                    data.environmentParameters=data.environmentTint=data.environmentConstant=Vector4.zero;
                    var sky=frame.GetOrCreate<HoAtmosphereRenderGraphResources>();context.PrepareEnvironment(settings.useEnvironment&&settings.environmentIntensity>0&&sky.IsValid);
                    if(context.environmentSampling)
                    {
                        data.environment=sky.radianceTexture;data.environmentParameters=sky.parameters;data.environmentTint=sky.tint;data.environmentConstant=sky.constantColor;data.environmentRevision=sky.revision;
                        // Copy the producer's snapshot. Scale only this DI
                        // consumer; never change the borrowed sky or globals.
                        data.environmentParameters.z*=settings.environmentIntensity;
                        builder.UseTexture(data.environment);data.environmentPdf=graph.ImportTexture(context.environmentPdf);builder.UseTexture(data.environmentPdf,AccessFlags.ReadWrite);
                    }
                    PrepareAuxiliary(data);
                    if(context.auxiliaryCDF!=null)builder.UseBuffer(graph.ImportBuffer(context.auxiliaryCDF),AccessFlags.Read);
                    if(context.auxiliarySecondary!=null)builder.UseBuffer(graph.ImportBuffer(context.auxiliarySecondary),AccessFlags.ReadWrite);
                    foreach(var buffer in new[]{context.reservoirs,context.history,context.lights,context.previousLights,context.currentToPrevious,context.previousToCurrent,context.ris})builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.ReadWrite);
                    data.powerPdf=TextureHandle.nullHandle;
                    if(context.powerSampling){data.powerPdf=graph.ImportTexture(context.powerPdf);builder.UseTexture(data.powerPdf,AccessFlags.ReadWrite);}
                    builder.UseBuffer(graph.ImportBuffer(context.neighbors));
                    data.diffuse=graph.ImportTexture(context.diffuse);data.specular=graph.ImportTexture(context.specular);
                    builder.UseTexture(data.diffuse,context.nrd!=null?AccessFlags.ReadWrite:AccessFlags.WriteAll);builder.UseTexture(data.specular,context.nrd!=null?AccessFlags.ReadWrite:AccessFlags.WriteAll);
                    var output=frame.GetOrCreate<HoRTDIRenderGraphResources>();output.rawDiffuse=data.diffuse;output.rawSpecular=data.specular;
                    builder.SetGlobalTextureAfterPass(data.diffuse,Shader.PropertyToID("_HoRTDIRawDiffuseTexture"));builder.SetGlobalTextureAfterPass(data.specular,Shader.PropertyToID("_HoRTDIRawSpecularTexture"));
                    if(context.nrd!=null)
                    {
                        builder.UseBuffer(graph.ImportBuffer(context.nrdScale),AccessFlags.ReadWrite);
                        foreach(var texture in new[]{context.nrdDiffuse,context.nrdSpecular,context.nrdNormal,context.nrdViewZ,context.nrdOutputDiffuse,context.nrdOutputSpecular,context.previousLuminance,context.gradientA,context.gradientB,context.diffConfidence,context.specConfidence})
                            builder.UseTexture(graph.ImportTexture(texture),AccessFlags.ReadWrite);
                        context.nrd.Declare(graph,builder);data.diffuse=graph.ImportTexture(context.filteredDiffuse);data.specular=graph.ImportTexture(context.filteredSpecular);
                    }
                    builder.SetGlobalTextureAfterPass(data.diffuse,Shader.PropertyToID("_HoRTDIDiffuseTexture"));builder.SetGlobalTextureAfterPass(data.specular,Shader.PropertyToID("_HoRTDISpecularTexture"));
                    output.diffuse=data.diffuse;output.specular=data.specular;
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext ctx)=>Dispatch(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd),d,false,default));
                }
            }
            private static void Dispatch(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData)
            {
                var c=d.context;unchecked{d.signature=d.signature*397^(int)d.environmentRevision^d.settings.useEnvironment.GetHashCode()^(d.settings.environmentSamples*7919)^(d.settings.environmentBrdfSamples*104729)^d.settings.environmentIntensity.GetHashCode();}
                #if UNITY_EDITOR
                if(c.nrd?.ValidationMotionCapture!=null)
                {
                    // Compatibility diagnostics must sample the shader global;
                    // this name is not a temporary RT that CopyTexture resolves.
                    if(compatibility)c.nrd.ValidationMotionCapture(cmd,new RenderTargetIdentifier("_MotionVectorTexture"));
                    else if(d.motion.IsValid())c.nrd.ValidationMotionCapture(cmd,d.motion);
                }
                #endif
                // One delta light and no environment has no light/UV uncertainty.
                // Keep its exact initial estimate instead of injecting resampling variance.
                bool singleDelta=d.lightCount==1&&!c.environmentSampling;
                bool temporal=!singleDelta&&d.settings.temporal&&c.historyValid&&c.sceneRevision==d.revision&&c.settingsSignature==d.signature;
                c.lastTemporal=temporal;
                bool reset=false,timeReset=false,traceConfidence=false;
                c.lastVisibilityGradients=false;c.lastNrdReset=false;
                var common=default(HoRTDINrdNative.CommonSettings);
                if(c.nrd!=null)
                {
                    c.nrd.HasCutoutVisibility=c.finalVisibility!=null;
                    bool worldChanged=c.sceneRevision!=d.revision;
                    bool dynamicVisibility=c.finalVisibility!=null&&d.finalVisibilityShader!=null;
                    bool worldReset=dynamicVisibility?c.sceneStructureRevision!=d.structureRevision:worldChanged;
                    #if UNITY_EDITOR
                    // Isolated motion audit only: keeping history here is unsafe
                    // without dynamic final-visibility confidence.
                    worldReset&=!c.nrd.ValidationPreserveWorldHistory;
                    worldReset|=worldChanged&&c.nrd.ValidationForceWorldReset;
                    #endif
                    // A removed emitter or changed analytic domain invalidates
                    // NRD's accumulated colours. Keep ReSTIR's identity maps
                    // and reservoir history; ordinary lamp motion/power changes
                    // continue through the existing visibility-confidence path.
                    reset=!c.nrdHistoryValid||!c.historyValid||worldReset||c.settingsSignature!=d.signature||d.lightFrame.domainChanged;
                    var nrdProjection=d.projectionSample.nonJittered;var previousNrdProjection=c.previousNonJitteredProjection;
                    var nrdJitter=d.projectionSample.jitterPixels;var previousNrdJitter=c.previousJitterPixels;
                    #if UNITY_EDITOR
                    if(c.nrd.ValidationUseJitteredProjection){nrdProjection=d.projection;previousNrdProjection=c.previousProjection;nrdJitter=previousNrdJitter=Vector2.zero;}
                    #endif
                    common=HoRTDINrdNative.CommonSettings.Create(c.width,c.height,d.view,nrdProjection,c.previousView,previousNrdProjection,c.frame,reset);
                    common.jitter=nrdJitter;common.previousJitter=previousNrdJitter;
                    #if UNITY_EDITOR
                    c.nrd.SceneViewPreview=!Application.isPlaying&&(d.camera.cameraType==CameraType.SceneView||c.nrd.ValidationForceSceneViewPreview)&&!c.nrd.ValidationDisableSceneViewPreview;
                    #endif
                    common.timeDelta=c.nrd.SetRenderTime(d.renderTime,out timeReset);if(timeReset)common.accumulationMode=2;
                    bool lightsChanged=LightsChanged(d.lightFrame);
                    reset|=c.nrd.ObserveLightingStability(reset||timeReset||worldChanged||lightsChanged||c.previousView!=d.view||previousNrdProjection!=nrdProjection);
                    if(reset)common.accumulationMode=2;
                    traceConfidence=dynamicVisibility&&!(reset||timeReset)&&(worldChanged||lightsChanged);
                    #if UNITY_EDITOR
                    traceConfidence|=c.nrd.ValidationTraceVisibilityConfidence;
                    traceConfidence&=!c.nrd.ValidationSkipVisibilityConfidence;
                    #endif
                    c.lastNrdReset=reset||timeReset;
                    int begin=d.shader.FindKernel("BeginNRDScale");
                    cmd.SetComputeBufferParam(d.shader,begin,"_HoRTDINrdScale",c.nrdScale);
                    cmd.SetComputeIntParam(d.shader,"_HoRTDINrdScaleReset",reset||timeReset?1:0);
                    cmd.DispatchCompute(d.shader,begin,1,1,1);
                }
                cmd.SetBufferData(c.lights,d.lightFrame.current);cmd.SetBufferData(c.previousLights,d.lightFrame.previous);
                cmd.SetBufferData(c.currentToPrevious,d.lightFrame.currentToPrevious);cmd.SetBufferData(c.previousToCurrent,d.lightFrame.previousToCurrent);
                if(d.finalSamples>1)cmd.SetBufferData(d.auxiliaryCDFBuffer,d.auxiliaryCDF);
                foreach(var stage in ReferenceStages)
                {
                    if(singleDelta&&(stage=="Temporal"||stage=="Spatial"))continue;
                    if((stage=="BuildPowerPdf"||stage=="Presample")&&!c.powerSampling)continue;
                    if((stage=="BuildEnvironmentPdf"||stage=="PresampleEnvironment")&&!c.environmentSampling)continue;
                    if(stage=="BuildEnvironmentPdf"&&c.environmentPdfReady&&c.environmentPdfRevision==d.environmentRevision)continue;
                    string name=stage=="Shade"&&c.nrd!=null?"ShadeNRD":stage;
                    if(stage=="Shade"&&c.finalVisibility!=null)
                    {using(new ProfilingScope(cmd,FinalVisibilityProfiling))DispatchFinalVisibility(cmd,d,compatibility,cameraData);}
                    int kernel=d.shader.FindKernel(name);d.scene.BuildAndBind(cmd,d.shader,kernel);
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIFinalSamples",d.finalSamples);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIRuntimeAuxiliaryCDF",d.auxiliaryCDFBuffer);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIRuntimeAuxiliarySecondary",d.auxiliarySecondaryBuffer);
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDILights",c.lights);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIReservoirs",c.reservoirs);
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIFinalVisibilityEnabled",c.finalVisibility!=null?1:0);
                    if(c.finalVisibility!=null)cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIFinalVisibility",c.finalVisibility.nameID);
                    else cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIFinalVisibility",Texture2D.whiteTexture);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIPreviousLights",c.previousLights);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDICurrentToPrevious",c.currentToPrevious);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIPreviousToCurrent",c.previousToCurrent);
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIPreviousLightCount",d.lightFrame.previousCount);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIHistory",c.history);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDINeighbors",c.neighbors);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIRISBuffer",c.ris);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIRISLayout",new Vector4(HoRTDIContext.RISTileSize,HoRTDIContext.RISTileCount,c.powerSampling?1:0,0));
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIPdfSize",c.pdfSize);
                    BindEnvironment(cmd,d,compatibility,kernel);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIDiffuse",c.diffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDISpecular",c.specular.nameID);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFrame",new Vector4(c.width,c.height,d.lightCount,d.localLightCount));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDISettings",new Vector4(Mathf.Clamp(d.settings.initialSamples,1,32),Mathf.Clamp(d.settings.spatialSamples,1,16),Mathf.Clamp(d.settings.spatialRadius,1,32),Mathf.Max(.00001f,d.settings.rayBias)));
                    int historyLimit=Mathf.Clamp(d.settings.maxHistory,1,32);
                    #if UNITY_EDITOR
                    historyLimit=Mathf.Clamp(c.ValidationReservoirHistory??historyLimit,1,32);
                    #endif
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,historyLimit,0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));cmd.SetComputeIntParam(d.shader,"_HoRTDIFrameIndex",c.frame);
                    cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousVP",c.previousVP);cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousCameraPosition",c.previousCamera);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousViewZ",c.previousViewZ);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousClipParameters",c.previousClip);
                    if(name=="ShadeNRD")
                    {
                        cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDINrdScale",c.nrdScale);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdDiffuse",c.nrdDiffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdSpecular",c.nrdSpecular.nameID);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdNormal",c.nrdNormal.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdViewZ",c.nrdViewZ.nameID);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPreviousLuminance",c.previousLuminance.nameID);
                    }
                    if(name=="BuildPowerPdf")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPdfOutput",c.powerPdf.nameID,0);
                        cmd.DispatchCompute(d.shader,kernel,(c.pdfSize+7)/8,(c.pdfSize+7)/8,1);cmd.GenerateMips(c.powerPdf.nameID);
                    }
                    else if(name=="BuildEnvironmentPdf")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIEnvironmentPdfOutput",c.environmentPdf.nameID,0);
                        cmd.DispatchCompute(d.shader,kernel,(HoRTDIContext.EnvironmentPdfSize+7)/8,(HoRTDIContext.EnvironmentPdfSize+7)/8,1);cmd.GenerateMips(c.environmentPdf.nameID);
                        c.environmentPdfReady=true;c.environmentPdfRevision=d.environmentRevision;
                    }
                    else if(name=="PresampleEnvironment")cmd.DispatchCompute(d.shader,kernel,HoRTDIContext.RISTileSize/256,HoRTDIContext.RISTileCount,1);
                    else if(name=="Presample")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPdf",c.powerPdf.nameID);
                        cmd.DispatchCompute(d.shader,kernel,HoRTDIContext.RISTileSize/256,HoRTDIContext.RISTileCount,1);
                    }
                    else cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                if(c.nrd!=null)
                {
                    int measure=d.shader.FindKernel("MeasureNRDHistory");
                    cmd.SetComputeBufferParam(d.shader,measure,"_HoRTDINrdScale",c.nrdScale);
                    c.nrd.BindSignalHistory(cmd,d.shader,measure,!(reset||timeReset));
                    cmd.DispatchCompute(d.shader,measure,(c.width+7)/8,(c.height+7)/8,1);
                    int select=d.shader.FindKernel("SelectNRDScale");
                    cmd.SetComputeBufferParam(d.shader,select,"_HoRTDINrdScale",c.nrdScale);cmd.DispatchCompute(d.shader,select,1,1,1);
                    int normalize=d.shader.FindKernel("NormalizeNRD");
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,normalize,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,normalize);
                    cmd.SetComputeBufferParam(d.shader,normalize,"_HoRTDINrdScale",c.nrdScale);
                    cmd.SetComputeTextureParam(d.shader,normalize,"_HoRTDINrdDiffuse",c.nrdDiffuse.nameID);cmd.SetComputeTextureParam(d.shader,normalize,"_HoRTDINrdSpecular",c.nrdSpecular.nameID);
                    cmd.SetComputeTextureParam(d.shader,normalize,"_HoRTDIDiffuse",c.diffuse.nameID);cmd.SetComputeTextureParam(d.shader,normalize,"_HoRTDISpecular",c.specular.nameID);
                    cmd.SetComputeTextureParam(d.shader,normalize,"_HoRTDIPreviousLuminance",c.previousLuminance.nameID);
                    cmd.DispatchCompute(d.shader,normalize,(c.width+7)/8,(c.height+7)/8,1);
                    if(singleDelta)
                    {
                        // The per-pixel delta estimate is already noise-free. Preserve
                        // hard visibility edges instead of temporally filtering them.
                        cmd.CopyTexture(c.diffuse.nameID,c.nrdOutputDiffuse.nameID);
                        cmd.CopyTexture(c.specular.nameID,c.nrdOutputSpecular.nameID);
                        c.nrdHistoryValid=false;
                    }
                    else
                    {
                        cmd.SetComputeIntParam(d.shader,"_HoRTDINrdHistoryReady",reset||timeReset?0:1);
                        // Confidence needs previous primary guides even when the
                        // independent RTXDI temporal-reservoir stage is disabled.
                        cmd.SetComputeVectorParam(d.shader,"_HoRTDIFlags",new Vector4(reset||timeReset?0:1,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),0));
                        foreach(string stage in new[]{"Gradients","FilterGradients","Confidence"})
                        {
                            if(stage=="Gradients"&&traceConfidence)
                            {using(new ProfilingScope(cmd,VisibilityGradientsProfiling))DispatchVisibilityGradients(cmd,d,compatibility,cameraData,!(reset||timeReset));c.lastVisibilityGradients=true;continue;}
                            int k=d.shader.FindKernel(stage);d.scene.BuildAndBind(cmd,d.shader,k);
                            BindEnvironment(cmd,d,compatibility,k);
                            if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,k,cameraData);
                            else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,k);
                            cmd.SetComputeBufferParam(d.shader,k,"_HoRTDIReservoirs",c.reservoirs);cmd.SetComputeBufferParam(d.shader,k,"_HoRTDIHistory",c.history);
                            cmd.SetComputeBufferParam(d.shader,k,"_HoRTDILights",c.lights);cmd.SetComputeBufferParam(d.shader,k,"_HoRTDIPreviousLights",c.previousLights);
                            cmd.SetComputeBufferParam(d.shader,k,"_HoRTDICurrentToPrevious",c.currentToPrevious);cmd.SetComputeBufferParam(d.shader,k,"_HoRTDIPreviousToCurrent",c.previousToCurrent);
                            cmd.SetComputeTextureParam(d.shader,k,"_HoRTDIDiffuse",c.diffuse.nameID);cmd.SetComputeTextureParam(d.shader,k,"_HoRTDISpecular",c.specular.nameID);
                            cmd.SetComputeTextureParam(d.shader,k,"_HoRTDIPreviousLuminance",c.previousLuminance.nameID);
                            cmd.SetComputeTextureParam(d.shader,k,"_HoRTDIGradientA",c.gradientA.nameID);cmd.SetComputeTextureParam(d.shader,k,"_HoRTDIGradientB",c.gradientB.nameID);
                            cmd.SetComputeTextureParam(d.shader,k,"_HoRTDIDiffConfidence",c.diffConfidence.nameID);cmd.SetComputeTextureParam(d.shader,k,"_HoRTDISpecConfidence",c.specConfidence.nameID);
                            int iterations=stage=="FilterGradients"?4:1;
                            if(stage=="FilterGradients"&&traceConfidence)iterations=0;
                            #if UNITY_EDITOR
                            if(stage=="FilterGradients"&&c.nrd.ValidationTraceVisibilityConfidence)iterations=c.nrd.ValidationVisibilityFilterPasses;
                            #endif
                            for(int step=0;step<iterations;step++){cmd.SetComputeIntParam(d.shader,"_HoRTDIGradientPass",step);cmd.DispatchCompute(d.shader,k,((c.width+2)/3+7)/8,((c.height+2)/3+7)/8,1);}
                        }
                        common.confidence=1;
                        var external=new Dictionary<string,RenderTargetIdentifier>
                        {
                            ["IN_MV"]=new RenderTargetIdentifier(Texture2D.blackTexture),["IN_NORMAL_ROUGHNESS"]=c.nrdNormal.nameID,["IN_VIEWZ"]=c.nrdViewZ.nameID,
                            ["IN_DIFF_RADIANCE_HITDIST"]=c.nrdDiffuse.nameID,["IN_SPEC_RADIANCE_HITDIST"]=c.nrdSpecular.nameID,
                            ["OUT_DIFF_RADIANCE_HITDIST"]=c.nrdOutputDiffuse.nameID,["OUT_SPEC_RADIANCE_HITDIST"]=c.nrdOutputSpecular.nameID,
                            ["IN_DIFF_CONFIDENCE"]=c.diffConfidence.nameID,["IN_SPEC_CONFIDENCE"]=c.specConfidence.nameID,
                            ["IN_DISOCCLUSION_THRESHOLD_MIX"]=new RenderTargetIdentifier(Texture2D.blackTexture)
                        };
                        c.nrd.BindPrimaryGuide(cmd,d.primary,compatibility,cameraData,c.history,!(reset||timeReset),c.nrdScale);
                        cmd.BeginSample("HoRTDI NRD RELAX");c.nrd.Record(cmd,common,external);cmd.EndSample("HoRTDI NRD RELAX");
                        int kernel=d.shader.FindKernel("ResolveNRD");
                        cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDINrdScale",c.nrdScale);
                        if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                        else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdOutputDiffuse",c.nrdOutputDiffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdOutputSpecular",c.nrdOutputSpecular.nameID);
                        cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);c.nrdHistoryValid=true;
                    }
                    int historyKernel=d.shader.FindKernel("SaveHistory");
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,historyKernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,historyKernel);
                    cmd.SetComputeBufferParam(d.shader,historyKernel,"_HoRTDIReservoirs",c.reservoirs);cmd.SetComputeBufferParam(d.shader,historyKernel,"_HoRTDIHistory",c.history);
                    cmd.DispatchCompute(d.shader,historyKernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                cmd.SetGlobalFloat("_HoRTDIActive",1);cmd.SetGlobalTexture("_HoRTDIDiffuseTexture",c.PublishedDiffuse.nameID);cmd.SetGlobalTexture("_HoRTDISpecularTexture",c.PublishedSpecular.nameID);
                cmd.SetGlobalTexture("_HoRTDIRawDiffuseTexture",c.diffuse.nameID);cmd.SetGlobalTexture("_HoRTDIRawSpecularTexture",c.specular.nameID);
                c.Complete(d.view,d.projection,d.projectionSample,d.camera,d.revision,d.signature,d.lightFrame);
                c.RecordAuxiliaryFence(cmd);
                c.sceneStructureRevision=d.structureRevision;
            }
            private static bool LightsChanged(HoRTDIContext.LightFrame frame)
            {
                if(frame.keys.Length!=frame.previousCount)return true;
                for(int i=0;i<frame.keys.Length;i++)
                {
                    int old=frame.currentToPrevious[i];if(old<0)return true;
                    var a=frame.current[i];var b=frame.previous[old];
                    if(!a.positionRange.Equals(b.positionRange)||!a.radianceType.Equals(b.radianceType)||!a.directionOuter.Equals(b.directionOuter)||!a.innerShadow.Equals(b.innerShadow))return true;
                }
                return false;
            }
            private static void DispatchFinalVisibility(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData)
                =>DispatchVisibilityRays(cmd,d,compatibility,cameraData,false,false);
            private static void DispatchVisibilityGradients(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData,bool historyReady)
                =>DispatchVisibilityRays(cmd,d,compatibility,cameraData,true,historyReady);
            private static void DispatchVisibilityRays(CommandBuffer cmd,Data d,bool compatibility,CameraData cameraData,bool gradients,bool historyReady)
            {
                var c=d.context;var rayShader=d.finalVisibilityShader;
                if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibility(cmd,cameraData);
                else d.primary.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd));
                d.scene.BuildAndBind(cmd,rayShader);cmd.SetRayTracingShaderPass(rayShader,"HoRTDIVisibility");
                cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIReservoirs",c.reservoirs);cmd.SetRayTracingBufferParam(rayShader,"_HoRTDILights",c.lights);
                cmd.SetRayTracingIntParam(rayShader,"_HoRTDIFinalSamples",d.finalSamples);cmd.SetRayTracingIntParam(rayShader,"_HoRTDIFrameIndex",c.frame);
                cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIRuntimeAuxiliaryCDF",d.auxiliaryCDFBuffer);cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIRuntimeAuxiliarySecondary",d.auxiliarySecondaryBuffer);
                cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIFrame",new Vector4(c.width,c.height,d.lightCount,d.localLightCount));
                cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));
                cmd.SetRayTracingVectorParam(rayShader,"_HoRTDISettings",new Vector4(0,0,0,Mathf.Max(.00001f,d.settings.rayBias)));
                if(gradients)
                {
                    cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIHistory",c.history);
                    cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIPreviousLights",c.previousLights);
                    cmd.SetRayTracingBufferParam(rayShader,"_HoRTDIPreviousToCurrent",c.previousToCurrent);
                    cmd.SetRayTracingIntParam(rayShader,"_HoRTDIPreviousLightCount",d.lightFrame.previousCount);
                    cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIFlags",new Vector4(historyReady?1:0,0,0,0));
                    cmd.SetRayTracingMatrixParam(rayShader,"_HoRTDIPreviousVP",c.previousVP);
                    cmd.SetRayTracingMatrixParam(rayShader,"_HoRTDIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIPreviousCameraPosition",c.previousCamera);
                    cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIPreviousViewZ",c.previousViewZ);
                    cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIPreviousClipParameters",c.previousClip);
                    cmd.SetRayTracingTextureParam(rayShader,"_HoRTDIPreviousLuminance",c.previousLuminance.nameID);
                    cmd.SetRayTracingTextureParam(rayShader,"_HoRTDIGradientA",c.gradientA.nameID);
                }
                else cmd.SetRayTracingTextureParam(rayShader,"_HoRTDIFinalVisibility",c.finalVisibility.nameID);
                cmd.SetRayTracingVectorParam(rayShader,"_HoRTDIEnvironment",new Vector4(HoRTDIContext.EnvironmentPdfSize,0,c.environmentSampling?1:0,0));
                cmd.SetRayTracingVectorParam(rayShader,HoAtmosphereBindings.ParametersId,d.environmentParameters);
                cmd.SetRayTracingVectorParam(rayShader,HoAtmosphereBindings.TintId,d.environmentTint);cmd.SetRayTracingVectorParam(rayShader,HoAtmosphereBindings.ConstantId,d.environmentConstant);
                if(!compatibility&&d.environment.IsValid())cmd.SetRayTracingTextureParam(rayShader,HoAtmosphereBindings.RadianceId,d.environment);
                else if(compatibility&&c.environmentSampling)cmd.SetRayTracingTextureParam(rayShader,HoAtmosphereBindings.RadianceId,HoAtmosphereBindings.compatibilityTexture.nameID);
                else cmd.SetRayTracingTextureParam(rayShader,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                if(c.environmentSampling)cmd.SetRayTracingTextureParam(rayShader,"_HoRTDIEnvironmentPdf",c.environmentPdf.nameID);
                else cmd.SetRayTracingTextureParam(rayShader,"_HoRTDIEnvironmentPdf",Texture2D.blackTexture);
                cmd.DispatchRays(rayShader,gradients?"VisibilityGradients":"FinalVisibility",(uint)(gradients?(c.width+2)/3:c.width),(uint)(gradients?(c.height+2)/3:c.height),1);
            }
            private static void BindEnvironment(CommandBuffer cmd,Data d,bool compatibility,int kernel)
            {
                var c=d.context;
                cmd.SetComputeVectorParam(d.shader,"_HoRTDIEnvironment",new Vector4(HoRTDIContext.EnvironmentPdfSize,Mathf.Clamp(d.settings.environmentSamples,1,32),c.environmentSampling?1:0,c.powerSampling?HoRTDIContext.RISTileSize*HoRTDIContext.RISTileCount:0));
                cmd.SetComputeIntParam(d.shader,"_HoRTDIEnvironmentBrdfSamples",Mathf.Clamp(d.settings.environmentBrdfSamples,0,32));
                cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.ParametersId,d.environmentParameters);cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.TintId,d.environmentTint);cmd.SetComputeVectorParam(d.shader,HoAtmosphereBindings.ConstantId,d.environmentConstant);
                if(!compatibility&&d.environment.IsValid())cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,d.environment);
                else if(compatibility&&c.environmentSampling)cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,HoAtmosphereBindings.compatibilityTexture.nameID);
                else cmd.SetComputeTextureParam(d.shader,kernel,HoAtmosphereBindings.RadianceId,CoreUtils.blackCubeTexture);
                if(c.environmentSampling)cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIEnvironmentPdf",c.environmentPdf.nameID);
                else cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIEnvironmentPdf",Texture2D.blackTexture);
            }
            public override void Execute(ScriptableRenderContext rendering,ref RenderingData data)
            {
                var d=Snapshot(data.cameraData.camera,data.cameraData.GetViewMatrix(),data.cameraData.GetProjectionMatrix());
                context.PrepareEnvironment(settings.useEnvironment&&settings.environmentIntensity>0&&HoAtmosphereBindings.compatibilityTexture!=null&&HoAtmosphereBindings.compatibilityParameters.w>.5f);
                if(context.environmentSampling)
                {
                    d.environmentParameters=HoAtmosphereBindings.compatibilityParameters;d.environmentTint=HoAtmosphereBindings.compatibilityTint;d.environmentConstant=HoAtmosphereBindings.compatibilityConstant;d.environmentRevision=HoAtmosphereBindings.compatibilityRevision;
                    d.environmentParameters.z*=settings.environmentIntensity;
                }
                PrepareAuxiliary(d);
                var cmd=CommandBufferPool.Get("Ho-RTDI reference DI passes");Dispatch(cmd,d,true,data.cameraData);rendering.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
        }
    }
}
