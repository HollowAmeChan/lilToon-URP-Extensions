using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutCube;
        private static bool cubeSigmaFour;
        private static bool cubeOriginal;
        public static void RunCutoutCubeStages(){forceCutoutCube=true;RunCutoutQuality();}
        public static void RunCutoutCubeSigmaFour(){cubeSigmaFour=true;RunCutoutCubeStages();}
        public static void RunCutoutCubeOriginalStages(){cubeOriginal=true;RunCutoutCubeStages();}
        private static void SetupCutoutCube()
        {
            var primitive=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));primitive.SetActive(false);
            var mesh=Keep(UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh));
            mesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[mesh.vertexCount];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            blocker.GetComponent<SkinnedMeshRenderer>().sharedMesh=mesh;blocker.transform.position=new Vector3(.75f,0,-.5f);blocker.transform.localScale=new Vector3(.4f,.4f,.2f);
            AlphaFill(alphaTexture,1);feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=feature.Settings.denoise=true;
        }
        private static bool CutoutCubeShadow(Light source,int x,int y)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);
            var origin=blocker.transform.InverseTransformPoint(p)-skinBlockerBone.localPosition;var direction=blocker.transform.InverseTransformVector(source.transform.position-p);
            float lo=0,hi=1;for(int axis=0;axis<3;axis++)
            {if(Mathf.Abs(direction[axis])<1e-7f){if(Mathf.Abs(origin[axis])>.5f)return false;continue;}float a=(-.5f-origin[axis])/direction[axis],b=(.5f-origin[axis])/direction[axis];lo=Mathf.Max(lo,Mathf.Min(a,b));hi=Mathf.Min(hi,Mathf.Max(a,b));if(lo>hi)return false;}
            return hi>0&&lo<1;
        }
        private static void ValidateCutoutCube()
        {
            light.color=Color.red;var far=Point(new Vector3(-1.5f,0,-1),Color.blue,4);light.range=far.range=10;
            RenderCamera();var c=feature.LastContext;
            if(cubeSigmaFour)c.nrd.ValidationSettings=s=>{s.diffPhi=.5f;s.fastSigma=4;return s;};
            if(cubeOriginal)c.nrd.ValidationSettings=s=>{s.diffPhi=1;s.fastSigma=2;return s;};
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();
            foreach(int stage in new[]{4,6,9,10})
            {var rt=Keep(new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[stage]=rt;images[stage]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));}
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>{if(targets.TryGetValue(stage,out var rt))cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));};
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            try
            {
                foreach(bool step in new[]{false,true})
                {
                    skinBlockerBone.localPosition=Vector3.zero;for(int i=0;i<192;i++)RenderCamera();double reference=0,rawBlue=0,filteredBlue=0;
                    var sums=new Dictionary<int,double>();foreach(int key in targets.Keys)sums[key]=0;
                    for(int frame=0;frame<256;frame++)
                    {
                        skinBlockerBone.localPosition=Vector3.right*(step?((frame/16&1)==0?0:2):(.6f+.6f*Mathf.Sin(frame*.07f)));
                        RenderCamera();ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);foreach(var pair in targets)ReadImage(pair.Value,images[pair.Key]);
                        var scale=new uint[c.nrdScale.count];c.nrdScale.GetData(scale);float inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);
                        for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                        {Require(!CutoutCubeShadow(far,x,y),"cube stage Far reference occluded");reference+=Expected(far,x,y);rawBlue+=raw.GetPixel(x,y).b;filteredBlue+=filtered.GetPixel(x,y).b;foreach(int key in targets.Keys)sums[key]+=images[key].GetPixel(x,y).b*inverse;}
                    }
                    Debug.Log($"[HoRTDIValidation] CUTOUT_CUBE step={step} original={cubeOriginal} rawBlue={rawBlue/reference:F6} filteredBlue={filteredBlue/reference:F6} phi={c.nrd.AppliedSettings.diffPhi} sigma={c.nrd.AppliedSettings.fastSigma} frames={c.nrd.AppliedSettings.diffFrames}");
                    foreach(var pair in sums)Debug.Log($"[HoRTDIValidation] CUTOUT_CUBE_STAGE step={step} sigma4={cubeSigmaFour} pipeline={pair.Key} blueRatio={pair.Value/reference:F6}");
                    Require(Math.Abs(rawBlue/reference-1)<.03,"cube raw blue energy");
                }
            }
            finally{c.nrd.ValidationDiffuseStage=null;}
        }
    }
}
