using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool sigmaAudit;
        public static void RunSigma(){sigmaAudit=true;rawOnly=true;Run();}
        public static void RunSigmaCompatibility(){compatibility=true;RunSigma();}
        private static void ValidateProductionSigma()
        {
            far.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            shadow.Settings.angularDiameter=8;shadow.Settings.samplesPerPixel=4096;
            var reference=Render();
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaSingleFrame;
            var raw=Render();var output=shadow.LastOutput;var filtered=Read(output.Visibility.rt);
            Check(output.sigma!=null&&output.sigma.DispatchCount>0,"Real Cast dispatches actual SIGMA");
            Check(output.sigma.nrd.AppliedSigmaSettings.maxStabilizedFrameNum==0&&output.sigma.LastReset,"No-history production mode explicitly resets stabilization");
            var levels=new HashSet<float>();double rawError=0,filteredError=0;int count=0;bool preserved=true,single=true,finite=true;
            for(int i=0;i<raw.Length;i++)
            {
                preserved&=raw[i].b==filtered[i].b&&raw[i].a==filtered[i].a&&raw[i].g==filtered[i].g;
                if(raw[i].b!=1)continue;
                single&=raw[i].r==0||raw[i].r==1;
                finite&=!float.IsNaN(filtered[i].r)&&filtered[i].r>=0&&filtered[i].r<=1;
                if(filtered[i].r>0&&filtered[i].r<1)levels.Add(filtered[i].r);
                if(raw[i].a!=bodyId)continue;
                float r=raw[i].r-reference[i].r,f=filtered[i].r-reference[i].r;rawError+=r*r;filteredError+=f*f;count++;
            }
            Check(preserved,"Decoder preserves raw status, owner and distance");Check(single,"SIGMA frontend receives a single actual binary light sample");Check(finite,"Finite physical reconstructed visibility");
            report.rawMse=(float)(rawError/count);report.filteredMse=(float)(filteredError/count);report.distinctLevels=levels.Count;
            Check(levels.Count>8,"Real geometry reconstructs continuous half-shadow");
            Check(report.filteredMse<report.rawMse,"SIGMA improves real Cast against independent 4096-spp reference: "+report.rawMse+" -> "+report.filteredMse);
            var penumbra=Read(output.sigma.penumbra.rt);var viewZ=Read(output.sigma.viewZ.rt);
            float tan=Mathf.Tan(4*Mathf.Deg2Rad);
            bool encodedCorrectly=true;for(int i=0;i<raw.Length;i++)if(raw[i].b==1&&raw[i].a==bodyId)
            {float expected=raw[i].g>=65504?65504:raw[i].g*tan*.5f;encodedCorrectly&=Mathf.Abs(penumbra[i].r-expected)<Mathf.Max(.001f,expected*.001f);}
            Check(encodedCorrectly,"Official single-sample directional penumbra encoding");
            Near(viewZ[32*64+32].r,3,"Current primary eye-depth guide",.01f);
            SaveSignal("reference",reference);SaveSignal("raw",raw);SaveSignal("sigma",filtered);
            var repeated=ReadAfterRender();Check(Equal(filtered,repeated),"No-history production output repeats without waiting for temporal convergence");
            shadow.Settings.samplesPerPixel=1;Check(Equal(filtered,ReadAfterRender()),"Raw reference budget does not change SIGMA's input statistics");
            shadow.Settings.samplingSeed=29;ReadAfterRender();shadow.Settings.samplingSeed=0;Check(Equal(filtered,ReadAfterRender()),"No-history mode is independent of earlier request order");
            shadow.Settings.reconstruction=HoRTShadowReconstruction.SigmaPreview;ReadAfterRender();
            Check(shadow.LastOutput.sigma.LastReset&&shadow.LastOutput.sigma.LastFrame==0,"Preview starts with fresh per-stream history");
            for(int i=0;i<6;i++)ReadAfterRender();Check(!shadow.LastOutput.sigma.LastReset&&shadow.LastOutput.sigma.LastFrame>=6,"Static preview actually retains temporal frames");
            near.transform.position+=Vector3.right*.1f;ReadAfterRender();Check(shadow.LastOutput.sigma.LastReset,"Moving caster resets unmatched geometry history");near.transform.position-=Vector3.right*.1f;ReadAfterRender();
            alpha.SetColor("_Color",new Color(1,1,1,0));ReadAfterRender();Check(shadow.LastOutput.sigma.LastReset,"Alpha revision resets visibility history");alpha.SetColor("_Color",Color.white);ReadAfterRender();
            camera.transform.position+=Vector3.right*.1f;ReadAfterRender();Check(shadow.LastOutput.sigma.LastReset,"Camera change resets until matching surface motion is available");camera.transform.position-=Vector3.right*.1f;ReadAfterRender();
            var cameraAOutput=shadow.LastOutput;var second=Keep(new GameObject("Second SIGMA view")).AddComponent<Camera>();second.CopyFrom(camera);second.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);second.enabled=false;
            var first=camera;camera=second;ReadAfterRender();Check(!ReferenceEquals(cameraAOutput,shadow.LastOutput)&&!ReferenceEquals(cameraAOutput.sigma.nrd,shadow.LastOutput.sigma.nrd),"Different cameras own separate SIGMA resources/history");camera=first;ReadAfterRender();
            host.SetActive(false);Render(false);host.SetActive(true);ReadAfterRender();Check(shadow.LastOutput.sigma.LastReset,"Missing host interval rejects previous history");
            host.Create();ReadAfterRender();Check(shadow.LastOutput.sigma.LastReset,"Host lifetime change rejects previous history");
            receiver.receiveWholeGroup=false;receiver.receiverParts.Add("detail");var subset=ReadAfterRender();Check(subset[32*64+32]==Color.clear,"Unrequested receiver cannot gain filtered shadow data");receiver.receiverParts.Clear();receiver.receiveWholeGroup=true;
            light.transform.rotation=Quaternion.LookRotation(Direction);var outside=ReadAfterRender();var outsideRaw=Read(shadow.LastOutput.raw.rt);var outsideZ=Read(shadow.LastOutput.sigma.viewZ.rt);
            Check(outside[32*64+32]==outsideRaw[32*64+32]&&outsideZ[32*64+32].r>=500000,"NPR back-lit domain preserves actual raw visibility outside SIGMA support");light.transform.rotation=Quaternion.LookRotation(-Direction);
            sb.SetActive(false);var missing=ReadAfterRender();Check(missing[32*64+32].b==2,"Missing primary guides remain invalid after decoding");sb.SetActive(true);ReadAfterRender();
            target=Target(96);ReadAfterRender();Check(shadow.LastOutput.Visibility.rt.width==96&&shadow.LastOutput.sigma.LastReset,"Resize creates matching full-resolution SIGMA domain");target=Target(64);ReadAfterRender();
            lifetime.retire=true;Render(false);Check(lifetime.heldBeforeFence,"Mid-frame Create retains complete SIGMA output until host fence");HoRTRendererFeature.CollectRetired();ReadAfterRender();HoRTRendererFeature.CollectRetired();Check(HoRTRendererFeature.RetiredResourceCount==0,"SIGMA consumer resources retire after actual completion");
            Debug.Log("[HoRTShadowProductionSigma] PASS real Cast / official frontend / continuous V / no-history / reset / separate views; MSE="+report.rawMse+" -> "+report.filteredMse+" levels="+report.distinctLevels);
        }
        private static Color[] ReadAfterRender(){Render();return Read(shadow.LastOutput.Visibility.rt);}
        private static bool Equal(Color[] a,Color[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        private static void SaveSignal(string name,Color[] pixels)
        {
            File.WriteAllBytes(Path.Combine(Folder(),name+".rgba32"),Bytes(pixels));var image=new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            try{var colors=new Color[pixels.Length];for(int i=0;i<colors.Length;i++)colors[i]=pixels[i].b==1?new Color(pixels[i].r,pixels[i].r,pixels[i].r,1):new Color(.2f,0,0,1);image.SetPixels(colors);image.Apply();File.WriteAllBytes(Path.Combine(Folder(),name+".png"),image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
        }
    }
}
