using System;
using System.Threading.Tasks;
using UnityEngine;
using lilToon.URP.Extensions.RTGI;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICastleValidation
    {
        public static void RunLineageBranchesQualification()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:8,lineageBranches:true,validationTag:"lineage-branches-qualification");
        public static void RunLineageBranches()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,lineageBranches:true,validationTag:"lineage-branches");
        public static void RunLineageBranchesCompatibility()=>Run(true,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,lineageBranches:true,validationTag:"lineage-branches");
        static async Task<HoRTGISelectionLineageAudit.Report> AuditLineageBranches(HoRTGIRendererFeature a,HoRTGIRendererFeature b,HoRTGISelectionLineageAudit audit,Camera camera,Action render,Func<RenderTexture,Color[]> read,string output,int frames)
        {
            Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;int mask=camera.cullingMask;float distance=a.Settings.maxDistance;bool sky=a.Settings.includeDirectEnvironment;int history=a.Settings.maxHistory;int oldFrame=a.LastContext.frame;bool t=a.Settings.temporal,s=a.Settings.spatial;audit.Begin(output,frames);
            try{
                for(int fixture=0;fixture<5;fixture++){
                    int mode=fixture*4+7,seed=(fixture+1)*1000;audit.ResetMode(mode);camera.transform.SetPositionAndRotation(position,rotation);camera.cullingMask=mask;
                    a.Settings.temporal=b.Settings.temporal=true;a.Settings.spatial=b.Settings.spatial=true;a.Settings.maxHistory=b.Settings.maxHistory=fixture==0?2:history;a.Settings.maxDistance=b.Settings.maxDistance=fixture==3?.01f:distance;a.Settings.includeDirectEnvironment=b.Settings.includeDirectEnvironment=fixture==3||sky;
                    foreach(var c in new[]{a.LastContext,b.LastContext}){c.frame=seed;c.historyValid=false;c.nrd.historyValid=false;}
                    for(int i=0;i<frames;i++){
                        if(fixture==1){camera.transform.position=position+rotation*new Vector3(.5f*Mathf.Sin(i*.7f),0,.2f*Mathf.Sin(i*.5f));camera.transform.rotation=(i>=frames/2&&i<frames/2+2)?rotation*Quaternion.Euler(0,180,0):rotation;}
                        audit.ExpectEmptyCurrent=fixture==2&&i>=frames/4&&i<frames/2;camera.cullingMask=audit.ExpectEmptyCurrent?0:mask;
                        if(fixture==4&&i==frames/2){audit.PrepareRebuild(seed+i);a.Create();b.Create();}
                        await NextEditorTick();render();audit.Observe(seed,read);
                    }
                    Debug.Log("[HoGILineageBranches] fixture="+fixture+" complete");
                }
                var r=audit.Finish(output);Debug.Log("[HoGILineageBranches] COMPLETE frames="+r.frames.Length);return r;
            }finally{camera.transform.SetPositionAndRotation(position,rotation);camera.cullingMask=mask;audit.ExpectEmptyCurrent=false;foreach(var f in new[]{a,b}){f.Settings.temporal=t;f.Settings.spatial=s;f.Settings.maxHistory=history;f.Settings.maxDistance=distance;f.Settings.includeDirectEnvironment=sky;if(f.LastContext!=null){f.LastContext.historyValid=false;f.LastContext.nrd.historyValid=false;f.LastContext.frame=oldFrame;}}}
        }
    }
}
