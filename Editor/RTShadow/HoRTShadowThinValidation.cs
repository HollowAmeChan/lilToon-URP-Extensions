using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool thinAudit;
        private static Transform thinBone;private static SkinnedMeshRenderer thinSkin;
        [Serializable] private sealed class ThinReport
        {public bool pass;public int size=128,referenceSpp=1024,estimates=256,shadowPixels,retainedPixels,unboundedRetainedPixels;public float mse,rampMse,movedMse,blendMse,minimumVisibility,unboundedMse;public List<string> checks=new();}
        public static void RunThin(){thinAudit=true;nprIntegration=true;rawOnly=true;Run();}
        public static void RunThinCompatibility(){compatibility=true;RunThin();}
        private static void SetupThinRig()
        {
            var go=near.gameObject;Object.DestroyImmediate(near);go.transform.localScale=Vector3.one;
            var mesh=Keep(new Mesh{name="Twelve thin rigged cutout hair cards"});var positions=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            for(int card=0;card<12;card++)
            {
                float x=-1.1f+card*.2f,width=.12f,height=1.8f+(card%3)*.12f;int start=positions.Count;
                positions.Add(new Vector3(x-width*.5f,-1,0));positions.Add(new Vector3(x+width*.5f,-1,0));positions.Add(new Vector3(x-width*.5f,-1+height,0));positions.Add(new Vector3(x+width*.5f,-1+height,0));
                uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.up);uv.Add(Vector2.one);indices.AddRange(new[]{start,start+2,start+1,start+2,start+3,start+1});
            }
            mesh.SetVertices(positions);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var weights=new BoneWeight[positions.Count];for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;mesh.bindposes=new[]{Matrix4x4.identity};
            var delta=new Vector3[positions.Count];for(int i=0;i<delta.Length;i++)delta[i]=new Vector3(positions[i].y>.5f?.03f:0,0,0);mesh.AddBlendShapeFrame("Hair tip micro bend",100,delta,new Vector3[delta.Length],new Vector3[delta.Length]);
            thinBone=Keep(new GameObject("Hair bone")).transform;thinBone.SetParent(go.transform,false);
            thinSkin=go.AddComponent<SkinnedMeshRenderer>();thinSkin.sharedMesh=mesh;thinSkin.bones=new[]{thinBone};thinSkin.rootBone=thinBone;thinSkin.localBounds=mesh.bounds;thinSkin.sharedMaterial=alpha;thinSkin.shadowCastingMode=ShadowCastingMode.ShadowsOnly;thinSkin.updateWhenOffscreen=false;near=thinSkin;
            var cutout=Keep(new Texture2D(4,1,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp});cutout.SetPixels(new[]{Color.clear,Color.white,Color.white,Color.clear});cutout.Apply(false,true);alpha.SetTexture("_MainTex",cutout);alpha.SetFloat("_Cutoff",.5f);
            far.shadowCastingMode=ShadowCastingMode.Off;target=Target(128);shadow.Settings.angularDiameter=.5f;shadow.Settings.singleFrameEstimates=256;
            toon.SetFloat("_ShadowBlur",.01f);toon.SetFloat("_ShadowBorder",.5f);
        }
        private static void ValidateThinRig()
        {
            var evaluation=new ThinReport();
            shadow.Settings.reconstruction=HoRTShadowReconstruction.Raw;shadow.Settings.samplesPerPixel=1024;var reference=Render();var refBeauty=Read(target);
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;Render();var actual=Read(shadow.LastOutput.Visibility.rt);var beauty=Read(target);
            Measure(reference,actual,refBeauty,beauty,out evaluation.mse,out evaluation.rampMse,out evaluation.shadowPixels,out evaluation.retainedPixels,out evaluation.minimumVisibility);
            var sums=Read(shadow.LastOutput.sigma.visibilitySum.rt);var unbounded=(Color[])actual.Clone();
            for(int i=0;i<unbounded.Length;i++)unbounded[i].r=sums[i].r/evaluation.estimates;
            Measure(reference,unbounded,refBeauty,beauty,out evaluation.unboundedMse,out _,out _,out evaluation.unboundedRetainedPixels,out _);
            ThinCheck(evaluation.mse<evaluation.unboundedMse&&evaluation.retainedPixels>evaluation.unboundedRetainedPixels,"Raw-statistics bounds improve thin-detail retention at the exact same 256-sample budget: MSE="+evaluation.unboundedMse+" -> "+evaluation.mse+" retained="+evaluation.unboundedRetainedPixels+" -> "+evaluation.retainedPixels);
            ThinCheck(evaluation.shadowPixels>100,"Thin alpha-clipped cards produce measurable narrow shadows");
            ThinCheck(evaluation.retainedPixels>evaluation.shadowPixels*.85f&&evaluation.minimumVisibility<.3f,"Reconstruction retains thin-card shadow support instead of erasing it: "+evaluation.retainedPixels+" / "+evaluation.shadowPixels+" min="+evaluation.minimumVisibility);
            ThinCheck(evaluation.mse<.02f&&evaluation.rampMse<.08f,"Thin-cutout export matches high-budget current-pose reference: V="+evaluation.mse+" ramp="+evaluation.rampMse);
            SaveThin("rest-reference",reference,refBeauty);SaveThin("rest-sigma",actual,beauty);File.WriteAllBytes(Path.Combine(Folder(),"thin-rest-unbounded-visibility.rgba32"),Bytes(unbounded));
            var previous=actual;thinBone.localPosition=Vector3.right*.011f;Render();actual=Read(shadow.LastOutput.Visibility.rt);ThinCheck(!Equal(previous,actual),"One-third-pixel bone movement changes the current shadow");
            ThinCheck(shadow.LastOutput.sigma.LastReset,"Skinned motion rejects old stabilized geometry");
            shadow.Settings.reconstruction=HoRTShadowReconstruction.Raw;reference=Render();refBeauty=Read(target);shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;Render();actual=Read(shadow.LastOutput.Visibility.rt);beauty=Read(target);
            Measure(reference,actual,refBeauty,beauty,out evaluation.movedMse,out float movedRamp,out _,out _,out _);
            ThinCheck(evaluation.movedMse<.02f&&movedRamp<.08f,"Bone-moved output matches independently traced pose without settling frames");
            SaveThin("bone-reference",reference,refBeauty);SaveThin("bone-sigma",actual,beauty);
            thinSkin.SetBlendShapeWeight(0,100);Render();actual=Read(shadow.LastOutput.Visibility.rt);shadow.Settings.reconstruction=HoRTShadowReconstruction.Raw;reference=Render();refBeauty=Read(target);shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;Render();actual=Read(shadow.LastOutput.Visibility.rt);beauty=Read(target);
            Measure(reference,actual,refBeauty,beauty,out evaluation.blendMse,out float blendRamp,out _,out _,out _);
            ThinCheck(evaluation.blendMse<.02f&&blendRamp<.08f,"BlendShape micro-bend export follows current pose without history");
            SaveThin("blend-reference",reference,refBeauty);SaveThin("blend-sigma",actual,beauty);
            alpha.SetColor("_Color",new Color(1,1,1,0));Render();var holes=Read(shadow.LastOutput.Visibility.rt);int valid=0;bool lit=true;
            foreach(var p in holes)if(p.b==1&&p.a==bodyId){valid++;lit&=p.r>.99f;}ThinCheck(valid>100&&lit,"Cutout alpha change opens all thin-card rays immediately");alpha.SetColor("_Color",Color.white);
            thinBone.localPosition=Vector3.zero;thinSkin.SetBlendShapeWeight(0,0);Render();ThinCheck(Equal(previous,Read(shadow.LastOutput.Visibility.rt)),"Returning to exact pose reproduces export independent of prior motion");
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaPreview;Render();Render();ThinCheck(!shadow.LastOutput.sigma.LastReset,"Static skinned pose can retain preview history");thinBone.localPosition=Vector3.right*.011f;Render();ThinCheck(shadow.LastOutput.sigma.LastReset,"Actual skinned pose revision resets preview history");
            ThinCheck(host.BakeCount>1,"Production RT host actually refreshed skinned snapshots");
            evaluation.pass=true;File.WriteAllText(Path.Combine(Folder(),"thin-report.json"),JsonUtility.ToJson(evaluation,true));
            Debug.Log("[HoRTShadowThin] PASS real skinned Cutout thin cards / bone and BlendShape micro-motion / independent current-pose references; MSE="+evaluation.mse+" ramp="+evaluation.rampMse);
            void ThinCheck(bool pass,string message){Check(pass,message);evaluation.checks.Add(message);}
        }
        private static void Measure(Color[] reference,Color[] actual,Color[] refBeauty,Color[] beauty,out float mse,out float ramp,out int shadows,out int retained,out float minimum)
        {
            double error=0,colorError=0;int count=0;shadows=retained=0;minimum=1;
            for(int i=0;i<reference.Length;i++)if(reference[i].b==1&&reference[i].a==bodyId)
            {error+=Math.Pow(reference[i].r-actual[i].r,2);colorError+=Math.Pow(refBeauty[i].r-beauty[i].r,2);count++;minimum=Mathf.Min(minimum,actual[i].r);if(reference[i].r<.5f){shadows++;if(actual[i].r<.5f)retained++;}}
            mse=(float)(error/count);ramp=(float)(colorError/count);
        }
        private static void SaveThin(string name,Color[] visibility,Color[] beauty)
        {
            File.WriteAllBytes(Path.Combine(Folder(),"thin-"+name+"-visibility.rgba32"),Bytes(visibility));File.WriteAllBytes(Path.Combine(Folder(),"thin-"+name+"-beauty.rgba32"),Bytes(beauty));
            var image=new Texture2D(128,128,TextureFormat.RGBA32,false,true);try{image.SetPixels(beauty);image.Apply();File.WriteAllBytes(Path.Combine(Folder(),"thin-"+name+".png"),image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
        }
    }
}
