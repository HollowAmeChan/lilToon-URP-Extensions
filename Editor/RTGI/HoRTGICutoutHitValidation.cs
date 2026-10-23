using lilToon.URP.Extensions.RayTracing;
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    public static partial class HoRTGISecondaryValidation
    {
        static bool cutoutAudit;
        static readonly List<string> cutoutChecks=new();
        public static void RunCutout(){cutoutAudit=true;Run();}
        static void ValidateCutout()
        {
            source.shader=Shader.Find("Hidden/lilToonCutout");source.renderQueue=2450;source.SetOverrideTag("RenderType","TransparentCutout");
            source.SetFloat("_Cutoff",.5f);source.SetFloat("_AlphaMaskMode",0);source.SetFloat("_UseMain2ndTex",0);source.SetFloat("_UseMain3rdTex",0);source.SetFloat("_UseParallax",0);source.SetFloat("_UseDither",0);
            source.SetVector("_MainTex_ScrollRotate",Vector4.zero);source.SetVector("_DissolveParams",Vector4.zero);source.SetVector("_DistanceFade",Vector4.zero);
            Debug.Log("[HoGICutoutHit] INPUT shader="+source.shader.name+" queue="+source.renderQueue+" tag="+source.GetTag("RenderType",false)+" mask="+source.GetFloat("_AlphaMaskMode"));
            Require(HoRTCutoutSupport.Supports(source),"Basic static Cutout classifier accepted");
            source.SetColor("_Color",new Color(.2f,.4f,.6f,1));source.SetTexture("_MainTex",Texture2D.whiteTexture);
            var hit=Trace();Require(hit.status==1&&scene.CutoutCount==1,"Accepted Cutout is registered non-opaque and evaluated");Near(hit.distance,1,"Cutout closest distance");
            source.SetColor("_Color",new Color(.2f,.4f,.6f,0));Require(Trace().status==0,"Color alpha hole ignores intersection");
            source.SetColor("_Color",new Color(.2f,.4f,.6f,1));
            var texture=Keep(new Texture2D(2,1,TextureFormat.RGBAFloat,false,true));texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Repeat;
            texture.SetPixels(new[]{new Color(1,1,1,0),Color.white});texture.Apply();source.SetTexture("_MainTex",texture);
            Require(Trace().status==1,"UV .6 selects solid main alpha texel");
            source.SetTextureOffset("_MainTex",new Vector2(.5f,0));Require(Trace().status==0,"Main alpha offset selects hole");source.SetTextureOffset("_MainTex",Vector2.zero);
            source.SetTextureScale("_MainTex",new Vector2(2,1));Require(Trace().status==0,"Main alpha scale selects repeated hole");source.SetTextureScale("_MainTex",Vector2.one);
            source.SetVector("_MainTex_ScrollRotate",new Vector4(0,0,Mathf.PI,0));Require(Trace().status==0,"Static main rotation selects hole");source.SetVector("_MainTex_ScrollRotate",Vector4.zero);
            rays.SetData(new[]{new Ray{originMin=new Vector4(2.2f,.3f,1,.001f),directionMax=new Vector4(0,0,-1,10)},new Ray{originMin=new Vector4(-2,0,1,.001f),directionMax=new Vector4(0,0,-1,10)}});
            source.SetTextureScale("_MainTex",new Vector2(.5f,1));source.SetFloat("_ShiftBackfaceUV",0);Require(Trace().status==0,"Unshifted backface alpha selects hole");source.SetFloat("_ShiftBackfaceUV",1);
            hit=Trace();Require(hit.status==1,"Backface UV shift precedes main ST");Near3(hit.geometricNormal,Vector3.forward,"Backface geometric normal");
            source.SetFloat("_ShiftBackfaceUV",0);source.SetTextureScale("_MainTex",Vector2.one);
            rays.SetData(new[]{new Ray{originMin=new Vector4(2.2f,.3f,-1,.001f),directionMax=new Vector4(0,0,1,10)},new Ray{originMin=new Vector4(-2,0,-1,.001f),directionMax=new Vector4(0,0,1,10)}});
            texture.SetPixels(new[]{Color.white,Color.white});texture.Apply();
            var mask=Keep(new Texture2D(2,1,TextureFormat.RGBAFloat,false,true));mask.filterMode=FilterMode.Point;mask.wrapMode=TextureWrapMode.Repeat;source.SetTexture("_AlphaMask",mask);
            Mask(.5f);source.SetFloat("_AlphaMaskScale",1);source.SetFloat("_AlphaMaskValue",0);source.SetColor("_Color",new Color(.2f,.4f,.6f,.25f));
            source.SetFloat("_AlphaMaskMode",1);Require(Trace().status==1,"Mask replace accepts equality at cutoff");
            source.SetFloat("_AlphaMaskMode",2);Require(Trace().status==0,"Mask multiply rejects quarter alpha");
            source.SetFloat("_AlphaMaskMode",3);Require(Trace().status==1,"Mask add restores alpha");
            source.SetColor("_Color",new Color(.2f,.4f,.6f,1));Mask(.75f);source.SetFloat("_AlphaMaskMode",4);Require(Trace().status==0,"Mask subtract rejects alpha");
            source.SetFloat("_AlphaMaskMode",1);Mask(.25f);source.SetFloat("_AlphaMaskScale",2);source.SetFloat("_AlphaMaskValue",.1f);Require(Trace().status==1,"Mask scale and positive bias accept");source.SetFloat("_AlphaMaskValue",-.2f);Require(Trace().status==0,"Mask negative bias rejects");
            source.SetFloat("_AlphaMaskScale",1);source.SetFloat("_AlphaMaskValue",0);mask.SetPixels(new[]{Color.black,Color.white});mask.Apply();source.SetTextureOffset("_AlphaMask",Vector2.zero);Require(Trace().status==1,"Independent mask UV solid texel");source.SetTextureOffset("_AlphaMask",new Vector2(.5f,0));Require(Trace().status==0,"Independent mask ST selects hole");
            source.SetFloat("_AlphaMaskMode",0);hit=Trace(true);Require(hit.status==2,"Visibility-only native payload returns blocker status");Near(hit.distance,1,"Visibility-only actual distance");Near3(hit.emission,Vector3.zero,"Visibility query skips expensive emission evaluation");
            source.SetColor("_Color",new Color(.2f,.4f,.6f,0));Require(Trace(true).status==0,"Visibility-only rays execute the same alpha any-hit");
            source.SetColor("_Color",Color.white);source.SetFloat("_UseMain2ndTex",1);Require(!HoRTCutoutSupport.Supports(source),"Unsupported multilayer Cutout remains outside bounded domain");source.SetFloat("_UseMain2ndTex",0);source.SetVector("_MainTex_ScrollRotate",new Vector4(1,0,0,0));Require(!HoRTCutoutSupport.Supports(source),"Animated alpha UV remains outside bounded domain");
            foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/lilToon/URP/RTGI/SecondaryMaterial")))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error||message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)throw new Exception(message.message);
            Debug.Log("[HoGICutoutHit] PASS native alpha/mask/UV/backface/visibility payload");
            void Mask(float value){mask.SetPixels(new[]{new Color(value,value,value,1),new Color(value,value,value,1)});mask.Apply();}
        }
        static void SaveCutoutReport(Exception error)
        {
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-cutout-hit"));Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(new CutoutHitReport{pass=error==null,error=error?.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),checks=cutoutChecks.ToArray()},true));
        }
        [Serializable]sealed class CutoutHitReport{public bool pass;public string error,colorSpace;public string[] checks;}
    }
}
