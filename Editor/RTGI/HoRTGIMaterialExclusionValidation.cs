using System;
using System.Reflection;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICompositeValidation
    {
        static bool materialExclusion;
        public static void RunMaterialExclusion(){materialExclusion=true;Run();}
        public static void RunMaterialExclusionCompatibility(){compatibility=true;RunMaterialExclusion();}
        static void ValidateMaterialExclusion()
        {
            Require(gi.Settings.excludedRayMaterials.Length==0,"GI material filter defaults empty");
            camera.cullingMask=1;gi.Settings.useEnvironment=false;gi.Settings.includeDirectEnvironment=false;
            // Isolate the analytic uniform-emitter sample from stochastic
            // spatial reconnection weights; temporal/NRD resets remain tested.
            gi.Settings.temporal=gi.Settings.denoise=true;gi.Settings.spatial=false;gi.Settings.composite=false;gi.Settings.intensity=1;
            receiverMaterial.SetColor("_Color",Color.white);
            var shell=Keep(new Material(receiverMaterial){name="Same material name"});shell.SetColor("_Color",Color.black);shell.SetFloat("_UseEmission",0);
            var emitter=Keep(new Material(shell){name=shell.name});emitter.SetFloat("_UseEmission",1);emitter.SetColor("_EmissionColor",Color.red);emitter.SetFloat("_EmissionBlend",1);emitter.SetFloat("_EmissionMainStrength",0);
            var mesh=Keep(new Mesh{name="Merged shell and bounce emitter"});const float half=2000;
            mesh.vertices=new[]{new Vector3(-half,-half,-.5f),new Vector3(half,-half,-.5f),new Vector3(-half,half,-.5f),new Vector3(half,half,-.5f),new Vector3(-half,-half,-1),new Vector3(half,-half,-1),new Vector3(-half,half,-1),new Vector3(half,half,-1)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one,Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,2,1,2,3,1},0);mesh.SetTriangles(new[]{4,6,5,6,7,5},1);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            var holder=Keep(new GameObject("Merged GI shell fixture"));holder.layer=1;holder.AddComponent<MeshFilter>().sharedMesh=mesh;var merged=holder.AddComponent<MeshRenderer>();merged.sharedMaterials=new[]{shell,emitter};
            var basis=Render();Save("filter-before");var context=gi.LastContext;
            var scene=(HoRTScene)typeof(HoRTGIRendererFeature).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gi);
            Require(scene.InstanceCount==3&&context.nrd!=null,"GI registers primary and both merged submeshes with real NRD");
            Near(GI(),Color.black,"Unexcluded front shell blocks secondary emitter",.0001f);Near(Read(context.PublishedDiffuse.rt),Color.black,"Filtered shell-blocked GI is black",.0001f);
            uint emitterId=Identity(1),shellId=Identity(0),revision=scene.StructureRevision;
            gi.Settings.excludedRayMaterials=new Material[]{null,shell,shell};Render();
            Require(scene.InstanceCount==2&&Identity(1)==emitterId&&!scene.TryResolve(shellId,out _)&&scene.StructureRevision!=revision,"Only excluded shell removed; retained emitter identity stays stable");
            Require(context.nrd.lastReset&&!context.lastTemporal,"Changed exclusion resets GI reservoirs and independent NRD");
            Near(GI(),Color.red,"Excluded shell exposes actual secondary emission",.03f);
            Require(Read(context.PublishedDiffuse.rt).r>.8f,"Filtered publication contains the newly exposed red contribution");
            Require(merged.sharedMaterials[0]==shell&&merged.sharedMaterials[1]==emitter&&merged.GetComponent<MeshFilter>().sharedMesh==mesh&&shell.GetFloat("_UseEmission")==0&&emitter.GetFloat("_UseEmission")==1,"Material filter does not mutate source renderer, mesh or material");
            gi.Settings.composite=true;Near(Render(),Expected(basis,Read(context.PublishedDiffuse.rt),1),"Normal camera consumes GI exposed behind excluded merged shell",.008f);Save("filter-added");
            revision=scene.StructureRevision;gi.Settings.excludedRayMaterials=new[]{shell};Render();
            Require(scene.StructureRevision==revision&&!context.nrd.lastReset&&context.lastTemporal,"Equivalent null/duplicate-free filter preserves scene and both histories");
            Require(Identity(1)==emitterId&&GI().r>.8f,"Same-named other material remains eligible by reference identity");
            gi.Settings.excludedRayMaterials=Array.Empty<Material>();Render();
            Require(scene.InstanceCount==3&&Identity(1)==emitterId&&Identity(0)!=shellId&&context.nrd.lastReset,"Restored shell gets a new identity and resets histories");
            Near(GI(),Color.black,"Restored shell blocks raw contribution again",.0001f);Near(Read(context.PublishedDiffuse.rt),Color.black,"Restored shell rejects old filtered red immediately",.0001f);
            gi.Settings.excludedRayMaterials=new[]{emitter};Render();Near(GI(),Color.black,"Excluding only rear emitter does not remove front shell",.0001f);
            gi.Settings.excludedRayMaterials=new[]{shell,emitter};Render();Require(scene.InstanceCount==1,"Excluding both submeshes retains independent primary");Near(GI(),Color.black,"Missing secondary geometry stays black with direct sky excluded",.0001f);
            gi.Settings.excludedRayMaterials=null;Render();Require(scene.InstanceCount==3,"Null filter restores default registration");Near(GI(),Color.black,"Null filter preserves shell blocking",.0001f);
            // Registry coverage only: no claim of GI bone/material shading here.
            var skinObject=Keep(new GameObject("Fully excluded GI skin"));skinObject.layer=1;var skin=skinObject.AddComponent<SkinnedMeshRenderer>();var bone=Keep(new GameObject("Excluded GI skin bone"));skin.bones=new[]{bone.transform};skin.rootBone=bone.transform;
            var skinMesh=Keep(UnityEngine.Object.Instantiate(mesh));skinMesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[skinMesh.vertexCount];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};skinMesh.boneWeights=weights;skin.sharedMesh=skinMesh;skin.sharedMaterials=new[]{shell,emitter};
            uint bakes=scene.SkinnedBakeCount;gi.Settings.excludedRayMaterials=new[]{shell,emitter};Render();Require(scene.SkinnedMeshCount==0&&scene.SkinnedBakeCount==bakes,"Fully excluded skin allocates no GI pose snapshot or bake");
            uint Identity(int submesh){for(uint id=1;id<128;id++)if(scene.TryResolve(id,out var item)&&item.renderer==merged&&item.submesh==submesh)return id;throw new Exception("Expected merged GI submesh missing");}
        }
    }
}
