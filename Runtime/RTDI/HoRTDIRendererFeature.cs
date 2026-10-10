#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
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
        [SerializeField] private ComputeShader[] nrdShaders;
        [SerializeField] private Shader debugShader;
        [SerializeField] private Shader debugOverlayShader;
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
            EnsureEditorShaderReferences();
            #endif
            if(!registered){RenderPipelineManager.beginCameraRendering+=ResetCamera;registered=true;}
        }
        #if UNITY_EDITOR
        // Repair missing references without resetting the runtime context or
        // replacing assigned shader overrides. SaveAssets persists the subasset.
        internal bool EnsureEditorShaderReferences()
        {
            const string root="Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/";
            bool changed=LoadMissing(ref shader,root+"HoRTDI.compute");
            changed|=LoadMissing(ref debugShader,root+"HoRTDIDebug.shader");
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
            if(!settings.enabled||(volume!=null&&volume.enable.overrideState&&!volume.enable.value)||data.cameraData.renderType!=CameraRenderType.Base
                ||(camera.cameraType!=CameraType.Game&&(camera.cameraType!=CameraType.SceneView||!settings.inSceneView)))
            {if(contexts.TryGetValue(camera,out var skipped)){skipped.historyValid=false;skipped.nrdHistoryValid=false;}return;}
            if(!scene.IsAvailable||shader==null){Status="需要 D3D12 inline RT 与 Compute Shader";return;}
            var format=GraphicsFormat.R16G16B16A16_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore))format=GraphicsFormat.R32G32B32A32_SFloat;
            if(!SystemInfo.IsFormatSupported(format,GraphicsFormatUsage.LoadStore)){Status="无 HDR UAV 格式";return;}
            dead.Clear();foreach(var pair in contexts)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var key in dead){contexts[key].Dispose();contexts.Remove(key);}
            var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);var geometry=new List<Renderer>();
            foreach(var item in renderers)if((settings.geometryLayers.value&(1<<item.gameObject.layer))!=0)geometry.Add(item);
            scene.Synchronize(geometry);
            var lightList=new List<AnalyticLight>();var lightKeys=new List<Light>();int signature=settings.initialSamples.GetHashCode()^settings.rayBias.GetHashCode()^((int)settings.localSampling*397);int excludedLights=0;
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
            context.PreparePower(localCount,settings.localSampling==HoRTDILocalSampling.Power);
            context.PrepareDenoiser(settings.denoise,nrdShaders);
            var lightData=lightList.Count>0?lightList.ToArray():new AnalyticLight[1];
            pass.Setup(context,scene,shader,settings,lightData,lightKeys.ToArray(),lightList.Count,localCount,signature);
            renderer.EnqueuePass(pass);Status=$"RTXDI DI：{(context.powerSampling?"功率":"均匀")} / {localCount} 局部 / {infiniteLights.Count} 方向灯 / {scene.InstanceCount} RT submesh / 排除 {excludedLights} 灯、{scene.Rejections.Count} 几何项";
            if(settings.denoise)Status+=" / "+context.nrdStatus;
            if(volume!=null&&volume.debugMode.value!=HoRTDIDebugMode.Off&&(camera.cameraType==CameraType.SceneView?volume.debugInSceneView.value:volume.debugInGameView.value))
            {
                if(debugMaterial==null&&debugShader!=null)debugMaterial=CoreUtils.CreateEngineMaterial(debugShader);
                HoDebugGrid.EnsureOverlay(debugOverlayShader);
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
            private AnalyticLight[] lights;private Light[] lightKeys;private int lightCount,localLightCount,signature;
            internal HoRTDIPass(){renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;}
            internal void Setup(HoRTDIContext context,HoRTScene scene,ComputeShader shader,HoRTDISettings settings,AnalyticLight[] lights,Light[] keys,int count,int localCount,int signature)
            {this.context=context;this.scene=scene;this.shader=shader;this.settings=settings;this.lights=lights;lightKeys=keys;lightCount=count;localLightCount=localCount;this.signature=signature;}
            private sealed class Data
            {
                internal HoRTDIContext context;internal HoRTScene.Inputs scene;internal HoRTPrimarySurfaceInputs primary;
                internal ComputeShader shader;internal HoRTDISettings settings;internal HoRTDIContext.LightFrame lightFrame;
                internal int lightCount,localLightCount,signature;internal uint revision;internal Matrix4x4 view,projection;internal Camera camera;
                internal double renderTime;
                internal TextureHandle diffuse,specular,powerPdf;
            }
            private Data Snapshot(Camera camera,Matrix4x4 view,Matrix4x4 projection) => new Data {context=context,scene=scene.ReadCompatibility(),shader=shader,settings=settings,lightFrame=context.CaptureLights(lights,lightKeys),
                lightCount=lightCount,localLightCount=localLightCount,signature=signature,revision=scene.Revision,view=view,projection=projection,camera=camera,renderTime=Time.unscaledTimeAsDouble};
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddUnsafePass<Data>("Ho-RTDI reference DI passes",out var data))
                {
                    var source=Snapshot(camera.camera,camera.GetViewMatrix(),camera.GetProjectionMatrix());
                    data.context=source.context;data.shader=source.shader;data.settings=source.settings;data.lightFrame=source.lightFrame;data.lightCount=source.lightCount;
                    data.localLightCount=source.localLightCount;
                    data.signature=source.signature;data.revision=source.revision;data.view=source.view;data.projection=source.projection;data.camera=source.camera;
                    data.renderTime=source.renderTime;
                    data.scene=scene.Read(graph,builder);data.primary=HoRTPrimarySurfaceInputs.Read(graph,frame,builder);
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
                var c=d.context;bool temporal=d.settings.temporal&&c.historyValid&&c.sceneRevision==d.revision&&c.settingsSignature==d.signature;
                c.lastTemporal=temporal;
                cmd.SetBufferData(c.lights,d.lightFrame.current);cmd.SetBufferData(c.previousLights,d.lightFrame.previous);
                cmd.SetBufferData(c.currentToPrevious,d.lightFrame.currentToPrevious);cmd.SetBufferData(c.previousToCurrent,d.lightFrame.previousToCurrent);
                string shade=c.nrd!=null?"ShadeNRD":"Shade";
                var names=c.powerSampling?new[]{"BuildPowerPdf","Presample","Initial","Temporal","Spatial",shade}:new[]{"Initial","Temporal","Spatial",shade};
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
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIRISBuffer",c.ris);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIRISLayout",new Vector4(HoRTDIContext.RISTileSize,HoRTDIContext.RISTileCount,c.powerSampling?1:0,0));
                    cmd.SetComputeIntParam(d.shader,"_HoRTDIPdfSize",c.pdfSize);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIDiffuse",c.diffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDISpecular",c.specular.nameID);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFrame",new Vector4(c.width,c.height,d.lightCount,d.localLightCount));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDISettings",new Vector4(Mathf.Clamp(d.settings.initialSamples,1,32),Mathf.Clamp(d.settings.spatialSamples,1,16),Mathf.Clamp(d.settings.spatialRadius,1,32),Mathf.Max(.00001f,d.settings.rayBias)));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFlags",new Vector4(temporal?1:0,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),0));
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIReservoirLayout",new Vector4(c.rowPitch,c.arrayPitch,0,0));cmd.SetComputeIntParam(d.shader,"_HoRTDIFrameIndex",c.frame);
                    cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousVP",c.previousVP);cmd.SetComputeMatrixParam(d.shader,"_HoRTDIPreviousInverseVP",c.previousInverseVP);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousCameraPosition",c.previousCamera);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousTowardsEye",c.previousTowardsEye);
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousViewZ",c.previousViewZ);cmd.SetComputeVectorParam(d.shader,"_HoRTDIPreviousClipParameters",c.previousClip);
                    if(name=="ShadeNRD")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdDiffuse",c.nrdDiffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdSpecular",c.nrdSpecular.nameID);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdNormal",c.nrdNormal.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdViewZ",c.nrdViewZ.nameID);
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPreviousLuminance",c.previousLuminance.nameID);
                    }
                    if(name=="BuildPowerPdf")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPdfOutput",c.powerPdf.nameID,0);
                        cmd.DispatchCompute(d.shader,kernel,(c.pdfSize+7)/8,(c.pdfSize+7)/8,1);cmd.GenerateMips(c.powerPdf.nameID);
                    }
                    else if(name=="Presample")
                    {
                        cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDIPdf",c.powerPdf.nameID);
                        cmd.DispatchCompute(d.shader,kernel,HoRTDIContext.RISTileSize/256,HoRTDIContext.RISTileCount,1);
                    }
                    else cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                if(c.nrd!=null)
                {
                    bool reset=!c.nrdHistoryValid||!c.historyValid||c.sceneRevision!=d.revision||c.settingsSignature!=d.signature;
                    var common=HoRTDINrdNative.CommonSettings.Create(c.width,c.height,d.view,d.projection,c.previousView,c.previousProjection,c.frame,reset);
                    common.timeDelta=c.nrd.SetRenderTime(d.renderTime,out bool timeReset);if(timeReset)common.accumulationMode=2;
                    cmd.SetComputeIntParam(d.shader,"_HoRTDINrdHistoryReady",reset||timeReset?0:1);
                    // Confidence needs previous primary guides even when the
                    // independent RTXDI temporal-reservoir stage is disabled.
                    cmd.SetComputeVectorParam(d.shader,"_HoRTDIFlags",new Vector4(reset||timeReset?0:1,d.settings.spatial?1:0,Mathf.Clamp(d.settings.maxHistory,1,32),0));
                    foreach(string stage in new[]{"Gradients","FilterGradients","Confidence"})
                    {
                        int k=d.shader.FindKernel(stage);d.scene.BuildAndBind(cmd,d.shader,k);
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
                    c.nrd.BindPrimaryGuide(cmd,d.primary,compatibility,cameraData,c.history,!(reset||timeReset));
                    cmd.BeginSample("HoRTDI NRD RELAX");c.nrd.Record(cmd,common,external);cmd.EndSample("HoRTDI NRD RELAX");
                    int kernel=d.shader.FindKernel("ResolveNRD");
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdOutputDiffuse",c.nrdOutputDiffuse.nameID);cmd.SetComputeTextureParam(d.shader,kernel,"_HoRTDINrdOutputSpecular",c.nrdOutputSpecular.nameID);
                    cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);c.nrdHistoryValid=true;
                    kernel=d.shader.FindKernel("SaveHistory");
                    if(compatibility)HoRTPrimarySurfaceInputs.BindCompatibilityCompute(cmd,d.shader,kernel,cameraData);
                    else d.primary.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd),d.shader,kernel);
                    cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIReservoirs",c.reservoirs);cmd.SetComputeBufferParam(d.shader,kernel,"_HoRTDIHistory",c.history);
                    cmd.DispatchCompute(d.shader,kernel,(c.width+7)/8,(c.height+7)/8,1);
                }
                cmd.SetGlobalFloat("_HoRTDIActive",1);cmd.SetGlobalTexture("_HoRTDIDiffuseTexture",c.PublishedDiffuse.nameID);cmd.SetGlobalTexture("_HoRTDISpecularTexture",c.PublishedSpecular.nameID);
                cmd.SetGlobalTexture("_HoRTDIRawDiffuseTexture",c.diffuse.nameID);cmd.SetGlobalTexture("_HoRTDIRawSpecularTexture",c.specular.nameID);
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
