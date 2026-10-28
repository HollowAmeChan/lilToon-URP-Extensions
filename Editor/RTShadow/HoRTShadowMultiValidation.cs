#pragma warning disable CS0618,CS0672
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool multiAudit;private static Light point,spot,fill;private static HoRTShadowLight mainDeclaration,fillDeclaration;
        private static HoRTShadowMultiProbe consumer;private static RTHandle consumed;
        [Serializable]private sealed class MultiReport{public bool pass,compatibility;public int checks,requests;public List<string> evidence=new();}
        public static void RunMulti(){multiAudit=true;nprIntegration=true;rawOnly=true;Run();}
        public static void RunMultiCompatibility(){compatibility=true;RunMulti();}
        private static void SetupMultiLight()
        {
            shadow.Settings.lightSelection=HoRTShadowLightSelection.Declared;
            mainDeclaration=light.gameObject.AddComponent<HoRTShadowLight>();mainDeclaration.angularDiameter=0;
            point=Keep(new GameObject("Point product")).AddComponent<Light>();point.type=LightType.Point;point.transform.position=new Vector3(5,0,-8);point.range=30;point.intensity=1;point.gameObject.AddComponent<HoRTShadowLight>();
            spot=Keep(new GameObject("Spot product")).AddComponent<Light>();spot.type=LightType.Spot;spot.transform.position=point.transform.position;spot.transform.rotation=Quaternion.LookRotation(-spot.transform.position);spot.spotAngle=30;spot.range=30;spot.gameObject.AddComponent<HoRTShadowLight>();
            fill=Keep(new GameObject("Opposite directional product")).AddComponent<Light>();fill.type=LightType.Directional;fill.transform.rotation=Quaternion.LookRotation(-new Vector3(-2.5f,0,-4).normalized);fillDeclaration=fill.gameObject.AddComponent<HoRTShadowLight>();fillDeclaration.angularDiameter=0;
            // These requests measure pure visibility, independent of radiance.
            // Keep additional light energy out of the main-only beauty oracle.
            point.intensity=spot.intensity=fill.intensity=0;
            var serializedPipeline=new SerializedObject(GraphicsSettings.defaultRenderPipeline);
            var data=(UniversalRendererData)serializedPipeline.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            // Add to the existing renderer data before its runtime renderer is
            // rebuilt, keeping host -> inputs -> shadow -> consumer ordering.
            consumer=Keep(ScriptableObject.CreateInstance<HoRTShadowMultiProbe>());consumer.feature=shadow;consumer.camera=camera;
            consumer.shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/RTShadow/HoRTShadowMultiValidation.compute");
            var output=Keep(new RenderTexture(16,2,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){enableRandomWrite=true});output.Create();consumed=RTHandles.Alloc(output);consumer.output=consumed;
            data.rendererFeatures.Add(consumer);data.SetDirty();
        }
        private static void ValidateMultiLight()
        {
            var result=new MultiReport{compatibility=compatibility};
            MultiRender();RequireCount(4);var main=shadow.OutputForLight(light);var pointOutput=shadow.OutputForLight(point);var spotOutput=shadow.OutputForLight(spot);var fillOutput=shadow.OutputForLight(fill);
            MultiCheck(main!=null&&pointOutput!=null&&spotOutput!=null&&fillOutput!=null,"Four independent light products scheduled");
            MultiCheck(CentreRaw(point).r==0&&CentreRaw(spot).r==0&&CentreRaw(fill).r==1,"Directional/point/spot products preserve independent light visibility");
            float expected=4/Mathf.Abs(Direction.z);MultiCheck(Mathf.Abs(CentreRaw(point).g-expected)<.01f,"Point trace returns nearest blocker before the finite light endpoint");
            MultiCheck(shadow.LastOutput==main,"Main Cast alias stays attached to URP main light");
            MultiCheck(Read(target)[32*64+32].r<.1f,"Last additional product cannot overwrite main NPR Cast");
            long mainId=HoRTLightIdentity.Get(light),pointId=HoRTLightIdentity.Get(point),spotId=HoRTLightIdentity.Get(spot),fillId=HoRTLightIdentity.Get(fill);
            MultiCheck(mainId!=pointId&&pointId!=spotId&&spotId!=fillId,"Lifetime light identity is independent of product slot");
            fill.enabled=false;MultiRender();RequireCount(3);MultiCheck(ReferenceEquals(main,shadow.OutputForLight(light))&&ReferenceEquals(pointOutput,shadow.OutputForLight(point)),"Removing another light preserves surviving resources and identities");
            fill.enabled=true;MultiRender();RequireCount(4);MultiCheck(HoRTLightIdentity.Get(fill)==fillId,"Disable/re-enable retains actual light identity");
            HoRTShadowLight.Active.Reverse();MultiRender();MultiCheck(HoRTLightIdentity.Get(point)==pointId&&ReferenceEquals(pointOutput,shadow.OutputForLight(point)),"Declaration reordering does not reassign light history");
            near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.Off;MultiRender();MultiCheck(CentreRaw(point).r==1&&CentreRaw(spot).r==1,"Finite local lights produce valid miss visibility");near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            point.transform.position=new Vector3(1.25f,0,-2);MultiRender();MultiCheck(CentreRaw(point).r==1,"Caster beyond the point source is excluded by endpoint");point.transform.position=new Vector3(5,0,-8);
            point.range=1;MultiRender();MultiCheck(CentreRaw(point).b==3,"Out-of-range surface is invalid-domain, never false unoccluded");point.range=30;
            shadow.Settings.maxDistance=2;MultiRender();MultiCheck(CentreRaw(point).b==4,"Insufficient ray budget is explicit instead of pretending the whole segment is visible");shadow.Settings.maxDistance=10000;
            point.type=LightType.Spot;point.spotAngle=30;point.transform.rotation=Quaternion.LookRotation(-point.transform.position);MultiRender();MultiCheck(HoRTLightIdentity.Get(point)==pointId&&CentreRaw(point).r==0,"Changing the same Light type preserves identity while updating its sampling domain");point.type=LightType.Point;
            var beforeColor=CentreRaw(point);point.intensity=17;point.color=Color.red;MultiRender();MultiCheck(CentreRaw(point)==beforeColor,"Core geometric V remains separate from light intensity and color");point.intensity=0;point.color=Color.white;
            spot.transform.rotation=Quaternion.LookRotation(Vector3.right);MultiRender();MultiCheck(CentreRaw(spot).b==3,"Outside spot cone has explicit invalid-domain status");spot.transform.rotation=Quaternion.LookRotation(-spot.transform.position);
            receiver.receiveAllLights=false;receiver.lights.Add(point);MultiRender();RequireCount(1);MultiCheck(shadow.LastOutput==null&&Shader.GetGlobalFloat("_HoRTShadowActive")==0,"Local-only request cannot activate main Cast");receiver.receiveAllLights=true;receiver.lights.Clear();
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaPreview;mainDeclaration.angularDiameter=8;fillDeclaration.angularDiameter=8;MultiRender();MultiRender();
            var a=shadow.OutputForLight(light);var b=shadow.OutputForLight(fill);MultiCheck(a.sigma!=null&&b.sigma!=null&&!ReferenceEquals(a.sigma.nrd,b.sigma.nrd),"Directional products own separate SIGMA history");
            MultiCheck(shadow.OutputForLight(point).sigma==null&&shadow.OutputForLight(spot).sigma==null,"Delta local products expose raw exact Cast without invented penumbra");
            mainDeclaration.angularDiameter=4;MultiRender();MultiCheck(a.sigma.LastReset&&!b.sigma.LastReset,"One light revision resets its history without resetting another");
            shadow.Settings.lightSelection=HoRTShadowLightSelection.Main;MultiRender();RequireCount(1);MultiCheck(shadow.ScheduledLightCount==1,"Main-only does not allocate or trace all-light products");
            shadow.Settings.lightSelection=HoRTShadowLightSelection.AllSupported;mainDeclaration.enabled=false;fillDeclaration.enabled=false;MultiRender();RequireCount(4);MultiCheck(shadow.OutputForLight(fill)!=null,"All-supported request discovers undeclared enabled lights");
            spot.cookie=Texture2D.whiteTexture;MultiRender();MultiCheck(shadow.OutputForLight(spot)==null&&consumer.rejectedIds.Contains(spotId),"Unsupported cookie has metadata reason and no texture product");spot.cookie=null;
            long removedId=HoRTLightIdentity.Get(fill);Object.DestroyImmediate(fill.gameObject);fill=Keep(new GameObject("Recreated directional product")).AddComponent<Light>();fill.type=LightType.Directional;MultiRender();MultiCheck(HoRTLightIdentity.Get(fill)!=removedId,"Recreated Light never inherits removed lifetime identity");
            MultiCheck(consumer.successfulReads>0,"Downstream graph/compatibility consumer actually reads each published texture");
            result.pass=true;result.requests=report.requests;result.checks=result.evidence.Count;File.WriteAllText(Path.Combine(Folder(),"multi-report.json"),JsonUtility.ToJson(result,true));
            consumed.Release();consumed=null;Debug.Log("[HoRTShadowMulti] PASS stable light products / point/spot endpoints and domains / per-light receivers / independent history / actual downstream reads; checks="+result.checks+" requests="+result.requests);
            void MultiCheck(bool success,string message){Check(success,message);result.evidence.Add(message);}
            void RequireCount(int count){MultiCheck(shadow.ScheduledLightCount==count,"Scheduled product count "+count);MultiCheck(consumer.availableCount==count,"Downstream metadata count "+count);var values=Read(consumed.rt);foreach(var item in consumer.available){var raw=CentreRaw(item);MultiCheck(values[consumer.slots[item]].Equals(raw),"Consumer reads correct current LightId slot");}}
        }
        private static void MultiRender(){Render(false);}
        private static Color CentreRaw(Light light)=>Read(shadow.OutputForLight(light).raw.rt)[32*64+32];
    }
    [SupportedOnRenderer()]
    internal sealed class HoRTShadowMultiProbe:ScriptableRendererFeature
    {
        internal HoRTShadowRendererFeature feature;internal Camera camera;internal ComputeShader shader;internal RTHandle output;
        internal int availableCount,successfulReads;internal readonly List<Light> available=new();internal readonly Dictionary<Light,int> slots=new();internal readonly HashSet<long> rejectedIds=new();
        private Consumer pass;
        public override void Create(){pass=new Consumer();}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){pass.owner=this;renderer.EnqueuePass(pass);}
        private sealed class Consumer:ScriptableRenderPass
        {
            internal HoRTShadowMultiProbe owner;internal Consumer(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
            private sealed class Data{internal HoRTShadowMultiProbe owner;internal TextureHandle raw,visibility,output;internal int slot;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var products=frame.GetOrCreate<HoRTShadowRenderGraphResources>();owner.available.Clear();owner.slots.Clear();owner.rejectedIds.Clear();owner.availableCount=0;
                foreach(var item in products.Products)
                {
                    if(item.Status!=HoRTShadowProductStatus.Available){owner.rejectedIds.Add(item.LightId);continue;}
                    owner.available.Add(item.Light);owner.slots[item.Light]=item.Slot;owner.availableCount++;
                    using(var builder=graph.AddUnsafePass<Data>("RTShadow downstream per-light consumer",out var d))
                    {d.owner=owner;d.raw=item.Raw;d.visibility=item.Visibility;d.output=graph.ImportTexture(owner.output);d.slot=item.Slot;builder.UseTexture(d.raw);builder.UseTexture(d.visibility);builder.UseTexture(d.output,AccessFlags.ReadWrite);builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static(Data d,UnsafeGraphContext context)=>{var cmd=CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);cmd.SetComputeTextureParam(d.owner.shader,0,"_Raw",d.raw);cmd.SetComputeTextureParam(d.owner.shader,0,"_Visibility",d.visibility);cmd.SetComputeTextureParam(d.owner.shader,0,"_Output",d.output);cmd.SetComputeIntParam(d.owner.shader,"_Slot",d.slot);cmd.DispatchCompute(d.owner.shader,0,1,1,1);d.owner.successfulReads++;});}
                }
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {
                owner.available.Clear();owner.slots.Clear();owner.rejectedIds.Clear();owner.availableCount=0;int slot=0;
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
                {if(!owner.feature.TryGetCompatibilityProduct(data.cameraData.camera,light,out var item)){if(light.enabled&&light.cookie!=null)owner.rejectedIds.Add(HoRTLightIdentity.Get(light));continue;}
                    owner.available.Add(light);owner.slots[light]=slot;owner.availableCount++;
                    var cmd=CommandBufferPool.Get("RTShadow compatibility per-light consumer");cmd.SetComputeTextureParam(owner.shader,0,"_Raw",item.Raw.nameID);cmd.SetComputeTextureParam(owner.shader,0,"_Visibility",item.Visibility.nameID);cmd.SetComputeTextureParam(owner.shader,0,"_Output",owner.output.nameID);cmd.SetComputeIntParam(owner.shader,"_Slot",slot++);cmd.DispatchCompute(owner.shader,0,1,1,1);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);owner.successfulReads++;
                }
            }
        }
    }
}
