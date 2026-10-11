using System;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutoutQuality;
        public static void RunCutoutQuality(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use isolated project");forceCutoutQuality=true;RunCutout();}
        public static void RunCutoutQualityCompatibility(){compatibility=true;RunCutoutQuality();}
        private static void SetupCutoutQuality()
        {
            var mesh=Keep(new Mesh{name="Independent cutout plane"});
            mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh.bindposes=new[]{Matrix4x4.identity};var weights=new BoneWeight[4];for(int i=0;i<4;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            blocker.GetComponent<SkinnedMeshRenderer>().sharedMesh=mesh;blocker.transform.localScale=new Vector3(.35f,1.4f,1);
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)alphaTexture.SetPixel(x,y,new Color(1,1,1,x>=5&&x<11?0:1));alphaTexture.Apply();
            feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.initialSamples=1;feature.Settings.denoise=true;
        }
        private static void ValidateCutoutQuality()
        {
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            for(int i=0;i<192;i++)RenderCamera();
            var rows=new System.Text.StringBuilder("mode,frame,x,y,reference,unshadowed,raw,filtered\n");
            foreach(string mode in new[]{"static","camera","caster","staticT","staticS","staticTS"})
            {
                feature.Settings.temporal=mode=="staticT"||mode=="staticTS";feature.Settings.spatial=mode=="staticS"||mode=="staticTS";
                camera.transform.position=new Vector3(0,0,-3);skinBlockerBone.localPosition=Vector3.zero;
                feature.LastContext.historyValid=false;feature.LastContext.nrdHistoryValid=false;
                for(int i=0;i<192;i++)RenderCamera();
                double referenceSum=0,rawSum=0,filteredSum=0,rawError=0,filteredError=0;float shadowLeak=0,coreLeak=0;int shadowCount=0,coreCount=0;
                for(int frame=0;frame<64;frame++)
                {
                    if(mode=="camera")camera.transform.position=new Vector3(.18f*Mathf.Sin(frame*.04f),0,-3);
                    if(mode=="caster")skinBlockerBone.localPosition=Vector3.right*(.12f*Mathf.Sin(frame*.07f));
                    RenderCamera();var context=feature.LastContext;ReadImage(context.diffuse.rt,raw);ReadImage(context.filteredDiffuse.rt,filtered);
                    for(int y=16;y<48;y++)for(int x=16;x<44;x++)
                    {
                        float unshadowed=Expected(light,x,y);bool shadow=CutoutPlaneShadow(x,y);
                        float reference=shadow?0:unshadowed,a=raw.GetPixel(x,y).r,b=filtered.GetPixel(x,y).r;
                        referenceSum+=reference;rawSum+=a;filteredSum+=b;rawError+=Math.Abs(a-reference);filteredError+=Math.Abs(b-reference);
                        if(shadow&&CutoutPlaneShadow(x-1,y)&&CutoutPlaneShadow(x+1,y)){shadowLeak=Mathf.Max(shadowLeak,b/Mathf.Max(unshadowed,1e-8f));shadowCount++;}
                        if(shadow&&CutoutPlaneShadow(x-2,y)&&CutoutPlaneShadow(x+2,y)){coreLeak=Mathf.Max(coreLeak,b/Mathf.Max(unshadowed,1e-8f));coreCount++;}
                        if(frame==63)rows.Append(FormattableString.Invariant($"{mode},{frame},{x},{y},{reference:R},{unshadowed:R},{a:R},{b:R}\n"));
                    }
                }
                double rawRatio=rawSum/referenceSum,filterRatio=filteredSum/referenceSum,rawL1=rawError/referenceSum,filterL1=filteredError/referenceSum;
                Debug.Log($"[HoRTDIValidation] CUTOUT_QUALITY mode={mode} rawRatio={rawRatio:F6} filteredRatio={filterRatio:F6} rawL1={rawL1:F6} filteredL1={filterL1:F6} margin1Leak={shadowLeak:F6} margin2Leak={coreLeak:F6} coreCount={coreCount}");
                Require((feature.Settings.temporal||feature.Settings.spatial||rawL1<.01)&&Math.Abs(rawRatio-1)<.03,"single-light cutout raw energy/reference mismatch");
                Require(Math.Abs(filterRatio-1)<.05&&filterL1<.15,"cutout NRD local energy/shape gate");
                Require(coreCount>0&&coreLeak<.02,"cutout shadow core leak gate");
                Require(!feature.LastContext.lastTemporal,"single-delta fixture reused a stochastic reservoir");
                // Report all cases before judging filtered quality; these are physical-shape gates, not brightness compensation.
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-cutout28-quality-"+(compatibility?"compat":"rg")+".csv"),rows.ToString());
            Debug.Log("[HoRTDIValidation] PASS cutout raw edge/camera/caster oracle; filtered metrics recorded for review; compatibility="+compatibility);
        }
        private static bool CutoutPlaneShadow(int x,int y)
        {
            var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=ray.origin+ray.direction*(-ray.origin.z/ray.direction.z);
            var direction=light.transform.position-p;float t=(blocker.transform.position.z-p.z)/direction.z;if(t<=0||t>=1)return false;
            var point=blocker.transform.InverseTransformPoint(p+direction*t)-skinBlockerBone.localPosition;
            if(Mathf.Abs(point.x)>.5f||Mathf.Abs(point.y)>.5f)return false;
            return CutoutCpuSample(alphaTexture,new Vector2(point.x+.5f,point.y+.5f))>=.5f;
        }
    }
}
