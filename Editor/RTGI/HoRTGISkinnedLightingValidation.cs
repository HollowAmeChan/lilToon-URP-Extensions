using System;
using System.IO;
using lilToon.URP.Extensions.RayTracing;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGIEnvironmentValidation
    {
        static bool skinnedLighting;
        static Color skinEmission,skinSky;
        public static void RunSkinned(){skinnedLighting=true;Run();}
        public static void RunSkinnedCompatibility(){compatibility=true;RunSkinned();}
        static void ValidateSkinnedLighting()
        {
            gi.Settings.useEnvironment=false;gi.Settings.temporal=gi.Settings.denoise=true;gi.Settings.spatial=false;
            var wallMaterial=wall.GetComponent<Renderer>().sharedMaterial;wallMaterial.SetColor("_Color",Color.black);wallMaterial.SetFloat("_UseEmission",1);wallMaterial.SetFloat("_EmissionBlend",1);wallMaterial.SetFloat("_EmissionMainStrength",0);wallMaterial.SetColor("_EmissionColor",Color.red);
            var skin=Skin(wall,true,out var bone);skinEmission=Mean(4);var context=gi.LastContext;var registry=(HoRTScene)Field(gi,"scene");
            Require(registry.SkinnedMeshCount==1&&context.nrd!=null,"GI emission is produced by a real owned skin snapshot and NRD");Near(skinEmission,Color.red,"Offscreen skinned emission reaches primary",.025f);
            Near(ReadRoi(context.PublishedDiffuse.rt),skinEmission,"Filtered skinned emission keeps energy",.025f);
            uint bakes=registry.SkinnedBakeCount,builds=registry.BuildCount;Mean(3);Require(registry.SkinnedBakeCount==bakes&&registry.BuildCount==builds&&context.lastTemporal&&!context.nrd.lastReset,"Stable skin reuses pose/BLAS/GI and NRD histories");
            bone.localPosition=Vector3.back*2;Near(Render(),Color.black,"Bone moves emitter behind primary, raw vanishes first frame",.0001f);Near(ReadRoi(context.PublishedDiffuse.rt),Color.black,"Bone move clears old filtered emission first frame",.0001f);Require(!context.lastTemporal&&context.nrd.lastReset&&registry.SkinnedBakeCount==bakes+1,"Bone pose change rebakes once and resets both histories");
            bone.localPosition=Vector3.zero;Near(Render(),Color.red,"Restored bone pose restores indirect emission",.025f);Require(context.nrd.lastReset,"Restored bone pose rejects stale dark history");
            skin.SetBlendShapeWeight(0,100);Near(Render(),Color.black,"BlendShape moves emitter behind primary",.0001f);Require(context.nrd.lastReset,"BlendShape updates reject old GI/NRD history");skin.SetBlendShapeWeight(0,0);Near(Render(),Color.red,"BlendShape restoration returns emission",.025f);
            var source=skin.sharedMesh;Require(source.blendShapeCount==1&&!source.isReadable,"GI uses a non-CPU-readable source without modifying it");
            wallMaterial.SetFloat("_UseEmission",0);wallMaterial.SetColor("_Color",Color.white);gi.Settings.useEnvironment=true;gi.Settings.temporal=false;skinSky=Mean(32);Require(skinSky.r>.03f,"Skinned white wall receives secondary sky");
            var roofSkin=Skin(roof,false,out var roofBone);roof.SetActive(true);Near(Mean(4),Color.black,"Skinned offscreen roof blocks secondary sky",.001f);roofBone.localPosition=Vector3.back*2;Relative(Mean(32),skinSky,"Bone opens secondary sky without changing primary raster");
            var cutout=Keep(new Material(Shader.Find("Hidden/lilToonCutout")));cutout.SetOverrideTag("RenderType","TransparentCutout");cutout.renderQueue=2450;cutout.SetFloat("_Cull",0);cutout.SetFloat("_Cutoff",.5f);cutout.SetFloat("_AlphaMaskMode",0);cutout.SetTexture("_MainTex",Texture2D.whiteTexture);cutout.SetColor("_Color",Color.black);roofSkin.sharedMaterial=cutout;roofBone.localPosition=Vector3.zero;
            Near(Mean(4),Color.black,"Solid skinned Cutout roof uses native alpha shadow",.001f);Require(context.alphaVisibility,"Skinned Cutout selects native GI backend");
            cutout.SetColor("_Color",new Color(0,0,0,0));Relative(Mean(32),skinSky,"Skinned Cutout holes preserve secondary sky");cutout.SetColor("_Color",Color.black);roofBone.localPosition=Vector3.back*2;Relative(Mean(32),skinSky,"Moved solid skinned Cutout opens light through updated geometry");
            uint stable=registry.SkinnedBakeCount;Mean(3);Require(registry.SkinnedBakeCount==stable,"Stable deformed emitter/caster does not rebake per request");
            roofSkin.enabled=false;Render();Require(!context.alphaVisibility&&registry.SkinnedMeshCount==1,"Disabling last skinned Cutout releases native mode and its snapshot");
            skin.bones=new Transform[]{null};Near(Render(),Color.black,"Invalid skin removes old bounce geometry",.0001f);Require(registry.SkinnedMeshCount==0,"Invalid pose does not retain stale GI snapshot");skin.bones=new[]{bone};Relative(Mean(32),skinSky,"Restoring valid skin returns real sky bounce");
            Debug.Log("[HoGISkinnedLighting] PASS bones/morph/non-readable emission, sky/Cutout occlusion, cache and history");
        }
        static SkinnedMeshRenderer Skin(GameObject obj,bool morph,out Transform bone)
        {
            var old=obj.GetComponent<MeshRenderer>();var material=old.sharedMaterial;var mesh=Keep(UnityEngine.Object.Instantiate(obj.GetComponent<MeshFilter>().sharedMesh));UnityEngine.Object.DestroyImmediate(old);UnityEngine.Object.DestroyImmediate(obj.GetComponent<MeshFilter>());
            bone=Keep(new GameObject("GI owned fixture bone")).transform;bone.SetParent(obj.transform,false);mesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[mesh.vertexCount];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            if(morph){var delta=new Vector3[mesh.vertexCount];for(int i=0;i<delta.Length;i++)delta[i]=Vector3.back*2;mesh.AddBlendShapeFrame("GI emitter displacement",100,delta,new Vector3[delta.Length],new Vector3[delta.Length]);}
            var skin=obj.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=material;skin.bones=new[]{bone};skin.rootBone=bone;skin.updateWhenOffscreen=false;mesh.UploadMeshData(true);return skin;
        }
        static void SaveSkinnedLightingReport(Exception error)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-skinned-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(new SkinnedLightingReport{pass=error==null,compatibility=compatibility,colorSpace=QualitySettings.activeColorSpace.ToString(),error=error?.ToString(),frames=frames,emission=skinEmission,sky=skinSky,checks=checks.ToArray()},true));
        }
        [Serializable]sealed class SkinnedLightingReport{public bool pass,compatibility;public string colorSpace,error;public int frames;public Color emission,sky;public string[] checks;}
    }
}
