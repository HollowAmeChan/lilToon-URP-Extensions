using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGISecondaryValidation
    {
        static bool skinnedAudit;
        static readonly List<string> skinnedChecks=new();
        public static void RunSkinned(){skinnedAudit=true;Run();}
        static void ValidateSkinnedHit()
        {
            var holder=renderer.gameObject;var mesh=holder.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(renderer);UnityEngine.Object.DestroyImmediate(holder.GetComponent<MeshFilter>());
            var bone0=Keep(new GameObject("GI material bone 0")).transform;bone0.SetParent(holder.transform,false);
            var bone1=Keep(new GameObject("GI material bone 1")).transform;bone1.SetParent(holder.transform,false);
            mesh.bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity};var weights=new BoneWeight[4];for(int i=0;i<4;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            var uv1=new Vector2[4];for(int i=0;i<4;i++)uv1[i]=Vector2.one-mesh.uv[i];mesh.uv2=uv1;
            var skin=holder.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=source;skin.bones=new[]{bone0,bone1};skin.rootBone=bone0;skin.updateWhenOffscreen=false;renderer=skin;
            var map=Keep(new Texture2D(2,2,TextureFormat.RGBAFloat,false,true));map.filterMode=FilterMode.Point;map.wrapMode=TextureWrapMode.Repeat;map.SetPixels(new[]{Color.red,Color.green,Color.blue,new Color(.25f,.5f,.75f,1)});map.Apply();
            source.SetColor("_Color",Color.white);source.SetTexture("_MainTex",map);source.SetFloat("_UseEmission",1);source.SetFloat("_UseEmission2nd",0);source.SetFloat("_EmissionBlend",1);source.SetColor("_EmissionColor",Color.white);source.SetFloat("_EmissionMainStrength",0);source.SetTexture("_EmissionMap",map);source.SetFloat("_EmissionMap_UVMode",1);
            Vector3 target=new Vector3(.2f,.3f,0),normal=Vector3.back;
            bool mapped=false;
            Check(Matrix4x4.identity,target,normal,"Initial skin");Require(scene.SkinnedMeshCount==1&&scene.SkinnedBakeCount==1,"GI hit uses one owned current-pose snapshot");
            uint bakes=scene.SkinnedBakeCount,builds=scene.BuildCount,revision=scene.Revision;Check(Matrix4x4.identity,target,normal,"Stable skin");Require(scene.SkinnedBakeCount==bakes&&scene.BuildCount==builds&&scene.Revision==revision,"Stable offscreen skin reuses pose and BLAS for material hits");
            bone0.localPosition=new Vector3(.15f,-.1f,.4f);bone0.localRotation=Quaternion.Euler(0,25,0);
            Check(holder.transform.worldToLocalMatrix*bone0.localToWorldMatrix,target,normal,"Rotated translated bone");
            // The expected deformation is in renderer-local space, independent
            // of BakeMesh and the AS-owned mesh.
            var deformation=Matrix4x4.TRS(bone0.localPosition,bone0.localRotation,Vector3.one);
            holder.transform.localScale=new Vector3(2,.5f,3);Check(deformation,target,normal,"Nonuniform renderer");
            holder.transform.localScale=new Vector3(-2,.5f,3);Check(deformation,target,normal,"Mirrored renderer");
            var normalMap=Keep(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));normalMap.SetPixel(0,0,new Color(.75f,.5f,(1+Mathf.Sqrt(.75f))*.5f,1));normalMap.Apply();source.SetTexture("_BumpMap",normalMap);source.SetFloat("_BumpScale",1);source.SetFloat("_UseBumpMap",1);mapped=true;
            Check(deformation,target,normal,"Mirrored skin tangent normal map");source.SetFloat("_UseBumpMap",0);mapped=false;
            holder.transform.localScale=Vector3.one;bone0.localRotation=Quaternion.Euler(0,15,0);bone0.localPosition=new Vector3(.2f,0,.4f);bone1.localRotation=Quaternion.Euler(0,-20,0);bone1.localPosition=new Vector3(-.2f,0,-.1f);
            var replacement=Keep(UnityEngine.Object.Instantiate(mesh));for(int i=0;i<4;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=.25f,boneIndex1=1,weight1=.75f};replacement.boneWeights=weights;skin.sharedMesh=replacement;
            var m0=Matrix4x4.TRS(bone0.localPosition,bone0.localRotation,Vector3.one);var m1=Matrix4x4.TRS(bone1.localPosition,bone1.localRotation,Vector3.one);var mixed=Matrix4x4.zero;for(int i=0;i<16;i++)mixed[i]=m0[i]*.25f+m1[i]*.75f;
            Check(mixed,target,normal,"Independent mixed bone weights");
            var deltas=new Vector3[4];var deltaNormals=new Vector3[4];var vertices=replacement.vertices;var fullNormal=new Vector3(.1f,0,-1).normalized;
            for(int i=0;i<4;i++){deltas[i]=new Vector3(.1f,-.2f,.2f+.1f*vertices[i].x);deltaNormals[i]=fullNormal-Vector3.back;}
            replacement.AddBlendShapeFrame("GI material morph",100,deltas,deltaNormals,new Vector3[4]);scene.MarkGeometryDirty();skin.SetBlendShapeWeight(0,50);
            var morphed=target+new Vector3(.1f,-.2f,.2f+.1f*target.x)*.5f;var morphedNormal=(normal+(fullNormal-normal)*.5f).normalized;
            Check(mixed,morphed,morphedNormal,"BlendShape positions/normals and stable UVs");
            Require(mesh.blendShapeCount==0&&mesh.vertices[0]==new Vector3(-1,-1,0)&&skin.sharedMesh==replacement,"GI snapshot never mutates original source geometry");
            bakes=scene.SkinnedBakeCount;builds=scene.BuildCount;Check(mixed,morphed,morphedNormal,"Stable morphed skin");Require(scene.SkinnedBakeCount==bakes&&scene.BuildCount==builds,"Stable morph does not rebake/rebuild");
            Debug.Log("[HoGISkinnedHit] PASS real GI attributes, bones, mixed weights, morphs, transforms, UV0/UV1 and cache");

            void Check(Matrix4x4 localSkin,Vector3 point,Vector3 objectNormal,string label)
            {
                var world=holder.transform.localToWorldMatrix*localSkin;var position=world.MultiplyPoint3x4(point);
                var expectedNormal=world.inverse.transpose.MultiplyVector(objectNormal).normalized;
                rays.SetData(new[]{new Ray{originMin=new Vector4(position.x+expectedNormal.x,position.y+expectedNormal.y,position.z+expectedNormal.z,.001f),directionMax=new Vector4(-expectedNormal.x,-expectedNormal.y,-expectedNormal.z,10)},new Ray{originMin=new Vector4(-100,0,-1,.001f),directionMax=new Vector4(0,0,1,10)}});
                var hit=Trace();Require(hit.status==1,label+" evaluated");Near(hit.distance,1,label+" independent ray distance");Near3(hit.geometricNormal,expectedNormal,label+" geometric normal");
                var expectedShading=mapped?(world.MultiplyVector(Vector3.right).normalized*.5f+expectedNormal*Mathf.Sqrt(.75f)).normalized:expectedNormal;
                Near3(hit.normal,expectedShading,label+" shading normal");
                Near3(hit.baseColor,new Vector3(.25f,.5f,.75f),label+" UV0 main texture");Near3(hit.emission,Vector3.right,label+" UV1 emission texture");
            }
        }
        static void SaveSkinnedHitReport(Exception error)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-skinned-hit"));Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(new SkinnedHitReport{pass=error==null,error=error?.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),bakes=scene?.SkinnedBakeCount??0,checks=skinnedChecks.ToArray()},true));
        }
        [Serializable]sealed class SkinnedHitReport{public bool pass;public string error,colorSpace;public uint bakes;public string[] checks;}
    }
}
