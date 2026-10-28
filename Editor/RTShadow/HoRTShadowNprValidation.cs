using System;
using System.IO;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEditor;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool nprIntegration;
        public static void RunNpr(){nprIntegration=true;rawOnly=true;Run();}
        public static void RunNprCompatibility(){compatibility=true;RunNpr();}
        public static void RunRasterRegression(){RunRaster(false);}
        public static void RunRasterRegressionCompatibility(){RunRaster(true);}
        private static void RunRaster(bool compat)
        {
            try
            {
                if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/.codex-research/"))throw new Exception("Independent batch project required");
                // This legacy fixture creates its own renderer later. Initialize
                // the per-pipeline settings before choosing the path explicitly.
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();var pipeline=UniversalRenderPipelineAsset.Create(renderer);
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var camera=new GameObject("Raster regression bootstrap").AddComponent<Camera>();camera.enabled=false;
                var target=new RenderTexture(16,16,0);target.Create();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(target);
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode=compat;
                #endif
                CharacterShadow.HoCharacterShadowValidation.ValidateReceiverSelectionRendering();
                Debug.Log("[HoRTShadowNprRasterRegression] PASS existing CS reception; API="+SystemInfo.graphicsDeviceType+" compatibility="+compat);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static Color[] MainAfterRender(){Render();return Read(target);}
        private static float Centre(Color[] values)=>values[32*64+32].r;
        private static void ValidateNprIntegration()
        {
            Check(!Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS")&&!Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_CASCADE")&&!Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_SCREEN"),"URP main shadow maps disabled; independent RT Cast must supply reception");
            report.toonShadow=Centre(MainAfterRender());
            toon.SetFloat("_ShadowReceive",0);report.toonReceiveOff=Centre(MainAfterRender());toon.SetFloat("_ShadowReceive",1);
            near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.Off;report.toonLit=Centre(MainAfterRender());
            Check(report.toonReceiveOff>report.toonShadow+.2f&&report.toonLit>report.toonShadow+.2f,"Actual lilToon receives RT Cast before its original ramp without URP maps: "+report.toonShadow+" / "+report.toonReceiveOff+" / "+report.toonLit);
            near.shadowCastingMode=far.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            toon.SetTexture("_ShadowReceiveMask",Texture2D.blackTexture);Check(Centre(MainAfterRender())>report.toonShadow+.2f,"Original material receive mask bypasses RT Cast");toon.SetTexture("_ShadowReceiveMask",Texture2D.whiteTexture);
            light.shadowStrength=.25f;Check(Centre(MainAfterRender())>report.toonShadow+.2f,"Light shadow strength is applied once before ramp");light.shadowStrength=1;
            shadow.SetActive(false);Render(false);Check(Centre(Read(target))>report.toonShadow+.2f,"Disabled RT uses the normal no-map fallback");shadow.SetActive(true);
            receiver.receiveWholeGroup=false;receiver.receiverParts.Add("detail");Check(Centre(MainAfterRender())>report.toonShadow+.2f,"Unrequested part keeps the normal Cast source");receiver.receiverParts.Clear();receiver.receiveWholeGroup=true;
            sb.SetActive(false);MainAfterRender();Check(Centre(Read(target))>report.toonShadow+.2f,"Unavailable primary domain falls back instead of darkening");sb.SetActive(true);
            // lilToon's second layer lives inside its primary shadow. Keep
            // that primary region active, then isolate red versus blue layers.
            toon.SetFloat("_ShadowReceive",1);toon.SetColor("_ShadowColor",Color.red);toon.SetFloat("_Shadow2ndReceive",1);toon.SetColor("_Shadow2ndColor",Color.blue);
            var second=MainAfterRender()[32*64+32];Check(second.b>second.r+.2f,"Second shadow layer consumes RT visibility within the original nested ramp: "+second);
            toon.SetFloat("_Shadow2ndReceive",0);var primary=MainAfterRender()[32*64+32];Check(primary.r>primary.b+.2f,"Second-layer receive switch preserves the primary shadow: "+primary);
            toon.SetFloat("_Shadow2ndReceive",1);toon.SetFloat("_Shadow3rdReceive",1);toon.SetColor("_Shadow3rdColor",Color.green);
            var third=MainAfterRender()[32*64+32];Check(third.g>third.r+.2f&&third.g>third.b+.2f,"Third nested shadow layer consumes RT Cast: "+third);
            toon.SetFloat("_Shadow3rdReceive",0);var thirdOff=MainAfterRender()[32*64+32];Check(thirdOff.b>thirdOff.g+.2f,"Third-layer receive switch preserves the second layer: "+thirdOff);
            toon.SetFloat("_Shadow2ndReceive",0);toon.SetColor("_Shadow3rdColor",Color.clear);toon.SetColor("_ShadowColor",Color.black);toon.SetColor("_Shadow2ndColor",new Color(.68f,.66f,.79f,1));
            far.shadowCastingMode=ShadowCastingMode.Off;near.transform.position+=Vector3.up*.7f;near.transform.localScale=new Vector3(1.2f,.5f,1);
            camera.orthographic=false;camera.fieldOfView=60;CheckAlignment(MainAfterRender());
            var shifted=camera.projectionMatrix;shifted.m02+=.09f;shifted.m12-=.07f;camera.projectionMatrix=shifted;CheckAlignment(MainAfterRender());camera.ResetProjectionMatrix();camera.orthographic=true;
            near.transform.position-=Vector3.up*.7f;near.transform.localScale=new Vector3(1.2f,3,1);
            far.shadowCastingMode=ShadowCastingMode.Off;shadow.Settings.angularDiameter=8;shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;
            toon.SetFloat("_ShadowBlur",.01f);toon.SetFloat("_ShadowBorder",.2f);var narrow=MainAfterRender();var raw=Read(shadow.LastOutput.raw.rt);var v=Read(shadow.LastOutput.Visibility.rt);
            toon.SetFloat("_ShadowBorder",.8f);var wide=MainAfterRender();int small=0,large=0,continuous=0;
            for(int i=0;i<raw.Length;i++)if(raw[i].b==1&&raw[i].a==bodyId)
            {if(narrow[i].r<.1f)small++;if(wide[i].r<.1f)large++;if(v[i].r>0&&v[i].r<1)continuous++;}
            Check(continuous>20&&large>small+8,"Real narrow ramp thresholds reshape continuous SIGMA Cast: "+small+" -> "+large+"; continuous="+continuous);
            SaveNpr("narrow",narrow);SaveNpr("wide",wide);
            Debug.Log("[HoRTShadowNprValidation] PASS actual lilToon / independent Cast / receive masks / per-layer switches / light strength / fallback / continuous SIGMA ramp; shadow="+report.toonShadow+" off="+report.toonReceiveOff+" lit="+report.toonLit+" thresholds="+small+"/"+large);
        }
        private static void CheckAlignment(Color[] beauty)
        {
            var cast=Read(shadow.LastOutput.raw.rt);int lit=0,occluded=0;float error=0;
            for(int y=1;y<63;y++)for(int x=1;x<63;x++)
            {
                int i=y*64+x;var c=cast[i];if(c.b!=1||c.a!=bodyId)continue;
                if(cast[i-1].r!=c.r||cast[i+1].r!=c.r||cast[i-64].r!=c.r||cast[i+64].r!=c.r)continue;
                if(cast[i-1].a!=bodyId||cast[i+1].a!=bodyId||cast[i-64].a!=bodyId||cast[i+64].a!=bodyId)continue;
                error=Mathf.Max(error,Mathf.Abs(beauty[i].r-c.r));if(c.r==0)occluded++;else lit++;
            }
            Check(lit>10&&occluded>4&&error<.05f,"Asymmetric perspective/jitter Cast aligns with its actual surface pixels: lit="+lit+" shadow="+occluded+" maxError="+error);
        }
        private static void SaveNpr(string name,Color[] pixels)
        {var image=new Texture2D(64,64,TextureFormat.RGBA32,false,true);try{image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Path.Combine(Folder(),"npr-"+name+".png"),image.EncodeToPNG());File.WriteAllBytes(Path.Combine(Folder(),"npr-"+name+".rgba32"),Bytes(pixels));}finally{UnityEngine.Object.DestroyImmediate(image);}}
    }
}
