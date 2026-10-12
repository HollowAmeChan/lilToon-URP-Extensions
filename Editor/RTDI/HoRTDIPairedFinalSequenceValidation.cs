using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool pairedFinalSequence;
        public static void RunCutoutPairedFinalSequence(){pairedFinalSequence=true;RunCutoutResolutionPairedFinal();}
        public static void RunCutoutPairedFinalSequenceCompatibility(){compatibility=true;RunCutoutPairedFinalSequence();}
        [StructLayout(LayoutKind.Sequential)] private struct PairedPackedReservoir{public uint lightData,uvData,mVisibility,distanceAge;public float targetPdf,weight;}
        [Serializable] private sealed class PairedSequenceReport{public bool pass;public string path;public int frames,probes,zeroSelections,sampleChanges,openChecks,shadowChecks,visibilityChecks;}
        private static void ValidatePairedFinalSequence(Light blue)
        {
            const int frames=256,size=64;var xs=new[]{18,26,34,42};var previous=new uint[4];var zeros=new int[4];var changes=new int[4];
            var image=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var packed=new PairedPackedReservoir[1];
            int open=0,shadow=0,checks=0;RenderCamera();var c=feature.LastContext;c.historyValid=c.nrdHistoryValid=false;c.frame=0;
            Require(c.finalVisibility!=null&&!c.environmentSampling&&Marshal.SizeOf<PairedPackedReservoir>()==24,"Paired sequence fixture/packing invalid");
            for(int frame=0;frame<frames;frame++)
            {
                skinBlockerBone.localPosition=Vector3.right*((frame/16&1)==0?0:.5f);c.nrd.ValidationTimeSeconds=(frame+1)/60.0;RenderCamera();ReadImage(c.finalVisibility.rt,image);
                Require(c.previousLightKeys.Length==2,"Paired sequence must contain exactly two analytic lights");
                for(int i=0;i<xs.Length;i++)
                {
                    int x=xs[i],y=32,offset=(y/16)*c.rowPitch+(x/16)*256+(y%16)*16+x%16;
                    uint bits=(uint)Mathf.RoundToInt(image.GetPixel(x,y).r*3);
                    c.reservoirs.GetData(packed,0,2*c.arrayPitch+offset,1);var extra=packed[0];uint index=extra.lightData&0x7fffffff;
                    Require((extra.lightData&0x80000000)!=0&&index<2&&Math.Abs(extra.weight-2)<1e-6,"Paired auxiliary sample/PDF invalid");
                    if(index==0)zeros[i]++;if(frame>0&&index!=previous[i])changes[i]++;previous[i]=index;
                    var source=c.previousLightKeys[index];Require(source==light||source==blue,"Paired light source mapping invalid");
                    bool blocked=CutoutPlaneShadow(source,x,y,size);Require(((bits>>1)&1)==(blocked?0u:1u),"Paired auxiliary native alpha visibility mismatch");
                    if(blocked)shadow++;else open++;checks++;
                    c.reservoirs.GetData(packed,0,c.arrayPitch+offset,1);var primary=packed[0];
                    if((primary.lightData&0x80000000)!=0)
                    {
                        var primarySource=c.previousLightKeys[primary.lightData&0x7fffffff];bool primaryBlocked=CutoutPlaneShadow(primarySource,x,y,size);
                        Require((bits&1)==(primaryBlocked?0u:1u)&&((primary.mVisibility&63)!=0)==!primaryBlocked,"Paired primary visibility/cache mismatch");checks++;
                    }
                }
            }
            c.nrd.ValidationTimeSeconds=null;int totalZero=0,totalChanges=0;
            for(int i=0;i<xs.Length;i++){Require(zeros[i]>=64&&zeros[i]<=192&&changes[i]>=32,"Paired auxiliary RNG frozen or unexpectedly imbalanced");totalZero+=zeros[i];totalChanges+=changes[i];}
            Require(open>0&&shadow>0,"Paired sequence did not exercise both alpha visibility states");
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-finalpair50-sequence-"+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(new PairedSequenceReport{pass=true,path=compatibility?"compatibility":"RenderGraph",frames=frames,probes=xs.Length,zeroSelections=totalZero,sampleChanges=totalChanges,openChecks=open,shadowChecks=shadow,visibilityChecks=checks},true));
            Debug.Log($"[HoRTDIValidation] PAIRED_SEQUENCE zeros={totalZero} changes={totalChanges} open={open} shadow={shadow} checks={checks} pass=true");
        }
    }
}
