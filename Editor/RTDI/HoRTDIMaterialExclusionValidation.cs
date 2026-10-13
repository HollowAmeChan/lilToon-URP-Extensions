using System;
using System.Reflection;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceMaterialExclusion;
        public static void RunMaterialExclusion(){forceMaterialExclusion=true;Run();}
        public static void RunMaterialExclusionCompatibility(){compatibility=true;RunMaterialExclusion();}
        private static void ValidateMaterialExclusion()
        {
            feature.Settings.useEnvironment=false;feature.Settings.temporal=feature.Settings.spatial=feature.Settings.denoise=false;blocker.SetActive(false);
            var sky=Keep(new Material(surfaceMaterial){name="Independent sky submesh"});var wall=Keep(new Material(surfaceMaterial){name="Independent wall submesh"});
            var mesh=Keep(new Mesh{name="Merged sky and wall"});
            mesh.vertices=new[]{new Vector3(.5f,-.2f,-.5f),new Vector3(1,-.2f,-.5f),new Vector3(.5f,.2f,-.5f),new Vector3(1,.2f,-.5f),new Vector3(-1,-.2f,-.5f),new Vector3(-.5f,-.2f,-.5f),new Vector3(-1,.2f,-.5f),new Vector3(-.5f,.2f,-.5f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one,Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,2,1,2,3,1},0);mesh.SetTriangles(new[]{4,6,5,6,7,5},1);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var holder=Keep(new GameObject("Merged exclusion fixture"));holder.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=holder.AddComponent<MeshRenderer>();renderer.sharedMaterials=new[]{sky,wall};
            var left=Point(new Vector3(-1.5f,0,-1),Color.white,4);left.enabled=false;
            RenderCamera();var registry=(HoRTScene)typeof(lilToon.URP.Extensions.RTDI.HoRTDIRendererFeature).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(feature);
            Require(registry.InstanceCount==3,"Merged fixture must register receiver and two distinct submeshes");Near(Read(feature.LastContext.diffuse.rt).r,0,"sky submesh initially blocks right lamp",.002f);
            uint wallId=Identity(1),skyId=Identity(0),revision=registry.StructureRevision;
            feature.Settings.excludedRayMaterials=new Material[]{null,sky,sky};RenderCamera();
            Require(registry.InstanceCount==2&&Identity(1)==wallId&&!registry.TryResolve(skyId,out _)&&registry.StructureRevision!=revision,"Exclusion did not remove only sky submesh with stable retained identity");
            Require(renderer.sharedMaterials[0]==sky&&renderer.sharedMaterials[1]==wall&&renderer.GetComponent<MeshFilter>().sharedMesh==mesh,"Exclusion mutated source renderer or mesh");
            Near(Read(feature.LastContext.diffuse.rt).r,Expected(light),"excluded sky transmits right lamp",.015f);
            uint stable=registry.StructureRevision;feature.Settings.excludedRayMaterials=new[]{sky};RenderCamera();Require(registry.StructureRevision==stable,"Equivalent exclusion set rebuilt scene");
            light.enabled=false;left.enabled=true;RenderCamera();Near(Read(feature.LastContext.diffuse.rt).r,0,"retained wall still blocks left lamp",.002f);
            feature.Settings.excludedRayMaterials=Array.Empty<Material>();RenderCamera();Require(registry.InstanceCount==3&&Identity(1)==wallId&&Identity(0)!=skyId,"Restore recycled excluded identity or changed retained wall");
            light.enabled=true;left.enabled=false;RenderCamera();Near(Read(feature.LastContext.diffuse.rt).r,0,"restored sky blocks again",.002f);
            // Accepted Cutout count, and thus actual native visibility resource,
            // follows the remaining submeshes rather than the whole Renderer.
            var cutout=Mat("Hidden/lilToonCutout");cutout.SetFloat("_Cull",0);cutout.SetFloat("_Cutoff",.5f);cutout.SetTexture("_MainTex",Texture2D.whiteTexture);renderer.sharedMaterials=new[]{cutout,wall};RenderCamera();
            Require(registry.CutoutCount==1&&feature.LastContext.finalVisibility!=null,"Cutout submesh did not enable native visibility");
            feature.Settings.excludedRayMaterials=new[]{cutout};RenderCamera();Require(registry.CutoutCount==0&&registry.InstanceCount==2&&feature.LastContext.finalVisibility==null,"Last Cutout exclusion retained native visibility resource");
            Near(Read(feature.LastContext.diffuse.rt).r,Expected(light),"excluded Cutout sky transmits",.015f);
            feature.Settings.excludedRayMaterials=Array.Empty<Material>();RenderCamera();Require(registry.CutoutCount==1&&feature.LastContext.finalVisibility!=null,"Cutout restore failed");
            var skinHolder=Keep(new GameObject("Excluded skin"));var skin=skinHolder.AddComponent<SkinnedMeshRenderer>();var bone=Keep(new GameObject("Excluded skin bone"));skin.bones=new[]{bone.transform};skin.rootBone=bone.transform;
            var skinMesh=Keep(UnityEngine.Object.Instantiate(mesh));skinMesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[skinMesh.vertexCount];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};skinMesh.boneWeights=weights;skin.sharedMesh=skinMesh;skin.sharedMaterials=new[]{sky,wall};
            feature.Settings.excludedRayMaterials=new[]{sky,wall,cutout};RenderCamera();Require(registry.SkinnedMeshCount==0,"Fully excluded skin still allocated a pose snapshot");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath,"../rtdi-exclusion80-"+(compatibility?"compat":"rg")+".json"),"{\"pass\":true,\"mergedSubmeshFilter\":true,\"stableRetainedIdentity\":true,\"nativeVisibilityLifecycle\":true,\"fullyExcludedSkinNoBake\":true}");
            Debug.Log("[HoRTDIValidation] MATERIAL_EXCLUSION pass=true merged mesh / actual shadow / identity / native resources / excluded skin");
            uint Identity(int submesh){for(uint id=1;id<128;id++)if(registry.TryResolve(id,out var instance)&&instance.renderer==renderer&&instance.submesh==submesh)return id;throw new Exception("Expected merged submesh missing");}
        }
    }
}
