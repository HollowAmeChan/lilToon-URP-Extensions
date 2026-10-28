using lilToon.URP.Extensions.RT;
using System;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceVisibilityConfidenceOracle;
        public static void RunVisibilityConfidence(){forceVisibilityConfidenceOracle=true;RunCutoutQuality();}
        public static void RunVisibilityConfidenceCompatibility(){compatibility=true;RunVisibilityConfidence();}
        private static void ValidateVisibilityConfidence()
        {
            light.color=Color.red;var far=Point(new Vector3(-1.5f,0,-1),Color.blue,4);
            feature.Settings.initialSamples=8;feature.Settings.temporal=feature.Settings.spatial=true;
            for(int i=0;i<96;i++)RenderCamera();var c=feature.LastContext;
            var luminance=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var gradients=Keep(new Texture2D(22,22,TextureFormat.RGBAFloat,false,true));
            var confidence=Keep(new Texture2D(22,22,TextureFormat.RGBAFloat,false,true));int checkedCells=0,changedCells=0;
            foreach(float pose in new[]{.5f,0,.4f})
            {
                var previous=new Reservoir[c.reservoirs.count];c.reservoirs.GetData(previous);
                var lamps=new HoRTAnalyticLight[c.lights.count];c.lights.GetData(lamps);
                skinBlockerBone.localPosition=Vector3.right*pose;RenderCamera();
                Require(c.lastVisibilityGradients&&!c.lastNrdReset&&!c.lastTemporal,"pose needs true visibility confidence and retained NRD, with reservoir history rejected");
                ReadImage(c.previousLuminance.rt,luminance);ReadImage(c.gradientA.rt,gradients);ReadImage(c.diffConfidence.rt,confidence);
                for(int cy=6;cy<15;cy++)for(int cx=6;cx<14;cx++)
                {
                    double largest=0;
                    for(int y=cy*3;y<cy*3+3;y++)for(int x=cx*3;x<cx*3+3;x++)
                    {
                        int pointer=(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;var r=previous[pointer];
                        double before=luminance.GetPixel(x,y).r,after=0;
                        if(r.light!=0)
                        {
                            var lamp=lamps[r.light&0x7fffffffu];bool near=lamp.radianceType.x>0;
                            if(!(near&&CutoutPlaneShadow(x,y)))
                            {
                                float rgb=Mathf.HalfToFloat(Mathf.FloatToHalf(Expected(near?light:far,x,y)*r.weight));
                                after=Mathf.HalfToFloat(Mathf.FloatToHalf(rgb*(near?.2126f:.0722f)));
                            }
                        }
                        double d=Math.Abs(before-after),m=Math.Max(before,after),ratio=d/(m+1.0/4096);
                        largest=Math.Max(largest,ratio);
                    }
                    var g=gradients.GetPixel(cx,cy);double actualRatio=g.r/(g.b+1.0/4096);
                    Require(Math.Abs(actualRatio-largest)<.003,"native same-sample visibility gradient disagrees with CPU plane/alpha oracle");
                    // The transfer's derivative is at most 8. Propagate the
                    // .003 analytic/FP16 gradient tolerance plus R8 quantization.
                    Near(confidence.GetPixel(cx,cy).r,(float)Math.Pow(Math.Max(0,1-largest),8),"native visibility confidence oracle",.03f);
                    checkedCells++;if(largest>.9)changedCells++;
                }
                RenderCamera();Require(!c.lastVisibilityGradients&&!c.lastNrdReset,"stable world/light must skip native gradient rays");
            }
            Require(changedCells>0,"oracle never exercised a visibility transition");
            uint structure=c.sceneStructureRevision;blocker.SetActive(false);RenderCamera();
            Require(c.lastNrdReset&&c.sceneStructureRevision!=structure,"instance removal must reject NRD structure history");
            blocker.SetActive(true);RenderCamera();Require(c.lastNrdReset,"instance addition must reject NRD structure history");
            skinBlockerBone.localPosition=Vector3.zero;RenderCamera();
            var replacement=Keep(UnityEngine.Object.Instantiate(blocker.GetComponent<SkinnedMeshRenderer>().sharedMesh));
            structure=c.sceneStructureRevision;blocker.GetComponent<SkinnedMeshRenderer>().sharedMesh=replacement;RenderCamera();
            Require(c.lastNrdReset&&c.sceneStructureRevision!=structure,"same-topology skinned source replacement must reject NRD structure history");
            Debug.Log($"[HoRTDIValidation] PASS native visibility confidence CPU oracle cells={checkedCells} changed={changedCells}, stable ray skip and structure resets; compatibility={compatibility}");
        }
    }
}
