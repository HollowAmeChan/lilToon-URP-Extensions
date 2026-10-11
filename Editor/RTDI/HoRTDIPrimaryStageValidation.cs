using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static string primaryStageMode;
        public static void RunPrimaryStages(){primaryStageMode="current";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesNoCutout(){primaryStageMode="noCutout";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesAtrousTwo(){primaryStageMode="atrous2";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesAtrousThree(){primaryStageMode="atrous3";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesNoClamp(){primaryStageMode="noClamp";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesNoSpatial(){primaryStageMode="noSpatial";RunPrimaryMotionAudit();}
        public static void RunPrimaryStagesHistoryEight(){primaryStageMode="history8";RunPrimaryMotionAudit();}
        private static void ValidatePrimaryStages(Light far)
        {
            if(primaryStageMode=="noCutout")blocker.SetActive(false);
            if(primaryStageMode=="noSpatial")feature.Settings.spatial=false;
            RenderCamera();var c=feature.LastContext;
            c.nrd.ValidationSettings=s=>{s.atrous=5;s.diffPhi=primaryStageMode=="noCutout"?2:1;s.fastSigma=primaryStageMode=="noCutout"?2:4;if(primaryStageMode=="atrous2")s.atrous=2;if(primaryStageMode=="atrous3")s.atrous=3;if(primaryStageMode=="noClamp")s.diffFast=s.diffFrames;if(primaryStageMode=="history8")s.diffFrames=8;return s;};
            var targets=new Dictionary<int,RenderTexture>();var images=new Dictionary<int,Texture2D>();var sums=new Dictionary<int,Vector2>();
            foreach(int stage in new[]{4,6,9,10})
            {var rt=Keep(new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear));rt.Create();targets[stage]=rt;images[stage]=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));sums[stage]=Vector2.zero;}
            c.nrd.ValidationDiffuseStage=(cmd,stage,texture)=>{if(targets.TryGetValue(stage,out var rt))cmd.CopyTexture(texture,new RenderTargetIdentifier(rt));};
            var raw=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));var filtered=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            try
            {
                for(int i=0;i<192;i++)RenderCamera();double referenceR=0,referenceB=0,rawR=0,rawB=0,filteredR=0,filteredB=0;
                for(int frame=0;frame<256;frame++)
                {
                    RenderCamera();ReadImage(c.diffuse.rt,raw);ReadImage(c.filteredDiffuse.rt,filtered);foreach(var pair in targets)ReadImage(pair.Value,images[pair.Key]);
                    var scale=new uint[c.nrdScale.count];c.nrdScale.GetData(scale);float inverse=1/BitConverter.Int32BitsToSingle((int)scale[1]);
                    for(int y=24;y<40;y++)for(int x=24;x<40;x++)
                    {
                        referenceR+=PrimaryLampReference(light,x,y,0,false);referenceB+=PrimaryLampReference(far,x,y,0,false);var a=raw.GetPixel(x,y);var f=filtered.GetPixel(x,y);rawR+=a.r;rawB+=a.b;filteredR+=f.r;filteredB+=f.b;
                        foreach(int stage in targets.Keys){var v=images[stage].GetPixel(x,y);sums[stage]+=new Vector2(v.r,v.b)*inverse;}
                    }
                }
                Debug.Log($"[HoRTDIValidation] PRIMARY_STAGE_CONTROL mode={primaryStageMode} rawR={rawR/referenceR:F6} rawB={rawB/referenceB:F6} filteredR={filteredR/referenceR:F6} filteredB={filteredB/referenceB:F6} phi={c.nrd.AppliedSettings.diffPhi} sigma={c.nrd.AppliedSettings.fastSigma} history={c.nrd.AppliedSettings.diffFrames}");
                foreach(var pair in sums)Debug.Log($"[HoRTDIValidation] PRIMARY_STAGE mode={primaryStageMode} pipeline={pair.Key} red={pair.Value.x/referenceR:F6} blue={pair.Value.y/referenceB:F6}");
                Require(Math.Abs(rawR/referenceR-1)<.03&&Math.Abs(rawB/referenceB-1)<.03,"primary stage raw energy mismatch");
            }
            finally{c.nrd.ValidationDiffuseStage=null;}
        }
    }
}
