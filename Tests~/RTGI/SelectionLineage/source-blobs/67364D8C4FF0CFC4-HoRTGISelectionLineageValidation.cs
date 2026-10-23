using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using lilToon.URP.Extensions.RTGI;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICastleValidation
    {
        public static void RunSelectionLineageQualification()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:8,validationTag:"lineage-qualification");
        public static void RunSelectionLineage()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:32,validationTag:"lineage");
        public static void RunSelectionLineageCompatibility()=>Run(true,64,denseScheduled:true,lineageAudit:true,lineageFrames:32,validationTag:"lineage");
        static async Task<HoRTGISelectionLineageAudit.Report> AuditSelectionLineage(HoRTGIRendererFeature a,HoRTGIRendererFeature b,HoRTGISelectionLineageAudit audit,Action render,Func<RenderTexture,Color[]> read,string output,int frames)
        {
            bool oldT=a.Settings.temporal,oldS=a.Settings.spatial;int oldFrame=a.LastContext.frame;audit.Begin(output,frames);
            try{
                for(int mode=0;mode<4;mode++){
                    a.Settings.temporal=b.Settings.temporal=(mode&1)!=0;a.Settings.spatial=b.Settings.spatial=(mode&2)!=0;
                    foreach(var c in new[]{a.LastContext,b.LastContext}){c.historyValid=false;c.nrd.historyValid=false;c.frame=(mode+1)*1000;}
                    audit.ResetMode(mode);
                    for(int i=0;i<frames;i++){await NextEditorTick();render();audit.Observe((mode+1)*1000,read);}
                    Debug.Log("[HoGISelectionLineage] mode="+mode+" complete");
                }
                var r=audit.Finish(output);Debug.Log("[HoGISelectionLineage] COMPLETE frames="+r.frames.Length);return r;
            }finally{a.Settings.temporal=b.Settings.temporal=oldT;a.Settings.spatial=b.Settings.spatial=oldS;foreach(var c in new[]{a.LastContext,b.LastContext}){c.historyValid=false;c.nrd.historyValid=false;c.frame=oldFrame;}}
        }
    }
}
