using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine;
using lilToon.URP.Extensions.RTGI;
namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGICastleValidation
    {
        static Transform endpointSecondary,endpointBlocker,endpointAlpha;static Material endpointSurface,endpointCutout;
        static void BuildEndpointScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RenderSettings.skybox=null;RenderSettings.fog=false;
            var camera=Keep(new GameObject("Endpoint source camera")).AddComponent<Camera>();camera.tag="MainCamera";camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=20;camera.cullingMask=1;camera.transform.position=new Vector3(0,0,-3);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            Material Make(string name,bool alpha=false){var m=Keep(new Material(Shader.Find(alpha?"Hidden/lilToonCutout":"lilToon")){name=name});m.SetFloat("_Cull",0);m.SetFloat("_AsUnlit",1);m.SetFloat("_Metallic",0);m.SetFloat("_Reflectance",0);m.SetFloat("_Smoothness",0);m.SetFloat("_UseEmission",0);m.SetFloat("_UseEmission2nd",0);m.SetTexture("_MainTex",Texture2D.whiteTexture);m.SetColor("_Color",alpha?new Color(0,0,0,0):Color.white);if(alpha){m.SetOverrideTag("RenderType","TransparentCutout");m.renderQueue=2450;m.SetFloat("_Cutoff",.5f);m.SetFloat("_AlphaMaskMode",0);}return m;}
            Transform Plane(string name,Vector3 pos,Vector3 scale,Material mat,int layer,bool flipped=false){var go=Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));go.name=name;go.layer=layer;go.transform.position=pos;go.transform.localScale=scale;if(flipped)go.transform.rotation=Quaternion.Euler(0,180,0);go.GetComponent<Renderer>().sharedMaterial=mat;return go.transform;}
            Plane("Primary receiver",Vector3.zero,new Vector3(2,2,1),Make("EndpointPrimary"),0);
            endpointSurface=Make("EndpointSecondary");endpointSecondary=Plane("Secondary target",new Vector3(0,0,-1),new Vector3(4,4,1),endpointSurface,1,true);
            var black=Make("EndpointBlocker");black.SetColor("_Color",Color.black);endpointBlocker=Plane("Opaque moving blocker",new Vector3(10,0,-.6f),new Vector3(.4f,4,1),black,1);
            endpointCutout=Make("EndpointAlpha",true);endpointAlpha=Plane("Alpha shutter",new Vector3(10,0,-.5f),new Vector3(4,4,1),endpointCutout,1);
            Plane("Excluded shell",new Vector3(1000,0,0),Vector3.one,Make("天空"),1);
            foreach(var pair in new[]{(new Vector3(-.7f,.4f,-.2f),Color.red),(new Vector3(.7f,-.4f,-.2f),Color.blue)}){var go=Keep(new GameObject("Endpoint point light"));go.transform.position=pair.Item1;var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=pair.Item2;light.intensity=4;light.range=6;}
        }
        public static void RunEndpointReplayQualification()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:16,secondaryReplay:true,endpointAudit:true,validationTag:"endpoint-replay-qualification");
        public static void RunEndpointReplay()=>Run(false,64,denseScheduled:true,lineageAudit:true,lineageFrames:32,secondaryReplay:true,endpointAudit:true,validationTag:"endpoint-replay");
        public static void RunEndpointReplayCompatibility()=>Run(true,64,denseScheduled:true,lineageAudit:true,lineageFrames:32,secondaryReplay:true,endpointAudit:true,validationTag:"endpoint-replay");
        static async Task<HoRTGISelectionLineageAudit.Report> AuditEndpointReplay(HoRTGIRendererFeature a,HoRTGIRendererFeature b,HoRTGISelectionLineageAudit audit,Action render,Func<RenderTexture,Color[]> read,string output,int frames)
        {
            bool t=a.Settings.temporal,s=a.Settings.spatial;int old=a.LastContext.frame;audit.Begin(output,frames);audit.ResetMode(3);foreach(var f in new[]{a,b}){f.Settings.temporal=true;f.Settings.spatial=true;f.LastContext.frame=1000;f.LastContext.historyValid=false;f.LastContext.nrd.historyValid=false;}
            try{for(int i=0;i<frames;i++){
                int phase=i/(frames/8);endpointSecondary.position=new Vector3(0,0,phase==1?-1.25f:-1);endpointBlocker.position=new Vector3(phase==3?-.35f:10,0,-.6f);endpointAlpha.position=new Vector3(phase==5||phase==6?0:10,0,-.5f);endpointCutout.SetColor("_Color",new Color(0,0,0,phase==5?1:0));endpointSurface.SetColor("_Color",phase==7?Color.green:Color.white);
                audit.EndpointPhase=phase;await NextEditorTick();render();audit.Observe(1000,read);
            }var r=audit.Finish(output);Debug.Log("[HoGIEndpointReplay] COMPLETE frames="+r.frames.Length);return r;}
            finally{endpointSecondary.position=new Vector3(0,0,-1);endpointBlocker.position=new Vector3(10,0,-.6f);endpointAlpha.position=new Vector3(10,0,-.5f);endpointCutout.SetColor("_Color",new Color(0,0,0,0));endpointSurface.SetColor("_Color",Color.white);foreach(var f in new[]{a,b}){f.Settings.temporal=t;f.Settings.spatial=s;f.LastContext.frame=old;f.LastContext.historyValid=false;f.LastContext.nrd.historyValid=false;}}
        }
    }
}
