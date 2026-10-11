using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCutout;
        private static Material cutoutMaterial;
        private static Texture2D alphaTexture,alphaMask;
        public static void RunCutout() { forceCutout=forceSkinBlocker=forceEnvironment=true;Run(); }
        public static void RunCutoutCompatibility() { compatibility=true;RunCutout(); }
        private static void SetupCutout()
        {
            cutoutMaterial=Mat("Hidden/lilToonCutout");cutoutMaterial.SetFloat("_Cull",0);
            alphaTexture=Keep(new Texture2D(16,16,TextureFormat.RGBA32,false,true));alphaTexture.filterMode=FilterMode.Point;
            alphaMask=Keep(new Texture2D(16,16,TextureFormat.RGBA32,false,true));alphaMask.filterMode=FilterMode.Point;
            cutoutMaterial.SetTexture("_MainTex",alphaTexture);cutoutMaterial.SetTexture("_AlphaMask",alphaMask);
            cutoutMaterial.SetFloat("_Cutoff",.5f);cutoutMaterial.SetFloat("_AlphaMaskScale",1);cutoutMaterial.SetFloat("_AlphaMaskValue",0);
            blocker.GetComponent<Renderer>().sharedMaterial=cutoutMaterial;blocker.SetActive(true);
            AlphaFill(alphaTexture,1);AlphaFill(alphaMask,1);
            atmosphere.Settings.enabled=false;feature.Settings.useEnvironment=false;
            if(forceCutoutQuality)SetupCutoutQuality();
            if(forceCutoutGpuTiming)SetupCutoutGpuTiming();
        }
        private static void AlphaFill(Texture2D texture,float alpha)
        {var data=new Color[texture.width*texture.height];for(int i=0;i<data.Length;i++)data[i]=new Color(alpha,alpha,alpha,alpha);texture.SetPixels(data);texture.Apply();}
        private static void ValidateCutout()
        {
            if(forceCutoutQuality){ValidateCutoutQuality();return;}
            float reference=Expected(light);var context=feature.LastContext;
            Require(context.finalVisibility!=null,"final alpha pass not allocated: "+feature.Status);
            Near(Render().r,0,"cutout solid alpha shadow",.002f);
            int previous=context.settingsSignature;AlphaFill(alphaTexture,0);Near(Render().r,reference,"cutout empty alpha transmits",.015f);
            Require(context.settingsSignature!=previous,"alpha texture update must reject stale visibility history");
            AlphaFill(alphaTexture,1);cutoutMaterial.SetColor("_Color",new Color(1,1,1,.4f));Near(Render().r,reference,"base alpha below cutoff",.015f);
            cutoutMaterial.SetColor("_Color",new Color(1,1,1,.6f));Near(Render().r,0,"base alpha above cutoff",.002f);
            cutoutMaterial.SetColor("_Color",Color.white);AlphaFill(alphaTexture,0);cutoutMaterial.SetFloat("_Cutoff",0);Near(Render().r,0,"clip equality survives",.002f);
            cutoutMaterial.SetFloat("_Cutoff",.5f);
            cutoutMaterial.SetFloat("_AlphaMaskMode",1);AlphaFill(alphaMask,1);Near(Render().r,0,"alpha mask replacement",.002f);
            cutoutMaterial.SetFloat("_AlphaMaskMode",2);Near(Render().r,reference,"alpha mask multiplication",.015f);
            cutoutMaterial.SetFloat("_AlphaMaskMode",3);Near(Render().r,0,"alpha mask addition",.002f);
            AlphaFill(alphaTexture,1);cutoutMaterial.SetFloat("_AlphaMaskMode",4);Near(Render().r,reference,"alpha mask subtraction",.015f);
            AlphaFill(alphaMask,.25f);cutoutMaterial.SetFloat("_AlphaMaskMode",1);cutoutMaterial.SetFloat("_AlphaMaskScale",2);cutoutMaterial.SetFloat("_AlphaMaskValue",.1f);
            Near(Render().r,0,"alpha mask scale and value",.002f);cutoutMaterial.SetFloat("_AlphaMaskScale",1);Near(Render().r,reference,"scaled mask below cutoff",.015f);
            cutoutMaterial.SetFloat("_AlphaMaskValue",0);
            cutoutMaterial.SetFloat("_AlphaMaskMode",0);Near(Render().r,0,"mask disabled",.002f);
            skinBlockerBone.localPosition=Vector3.right*100;Near(Render().r,reference,"cutout bone moved away",.015f);
            skinBlockerBone.localPosition=Vector3.zero;Near(Render().r,0,"cutout bone returned",.002f);
            ValidateCutoutPattern();
            AlphaFill(alphaTexture,1);cutoutMaterial.SetTextureScale("_MainTex",Vector2.one);cutoutMaterial.SetTextureOffset("_MainTex",Vector2.zero);cutoutMaterial.SetVector("_MainTex_ScrollRotate",Vector4.zero);
            feature.Settings.denoise=true;for(int i=0;i<96;i++)RenderCamera();Near(Read(feature.LastContext.filteredDiffuse.rt).r,0,"NRD solid cutout shadow",.004f);
            AlphaFill(alphaTexture,0);for(int i=0;i<96;i++)RenderCamera();Near(Read(feature.LastContext.filteredDiffuse.rt).r,reference,"NRD transparent cutout",reference*.05f);
            feature.Settings.denoise=false;
            ValidateCutoutReuse();
            ValidateCutoutLightDomains();
            blocker.SetActive(false);RenderCamera();Require(feature.LastContext.finalVisibility==null,"alpha visibility texture released without cutouts");
            Debug.Log("[HoRTDIValidation] PASS native cutout: original alpha textures, base alpha/cutoff equality, all mask modes, texture revision, skinned shadow, conditional lifetime; compatibility="+compatibility);
        }
        private static void ValidateCutoutReuse()
        {
            AlphaFill(alphaTexture,1);
            var far=Point(new Vector3(-1.5f,0,-1),Color.white,4);
            var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));float expected=ExpectedMean(far);
            feature.Settings.initialSamples=8;int sequence=0;
            foreach(var mode in new[]{(false,false),(true,false),(false,true),(true,true)})
            {
                feature.Settings.temporal=mode.Item1;feature.Settings.spatial=mode.Item2;feature.LastContext.historyValid=false;feature.LastContext.frame=++sequence*1024;
                for(int i=0;i<64;i++)RenderCamera();double sum=0;
                for(int frame=0;frame<512;frame++)
                {RenderCamera();ReadImage(feature.LastContext.diffuse.rt,image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)sum+=image.GetPixel(x,y).r;}
                float ratio=(float)(sum/(512*256*expected));Near(ratio,1,$"cutout conservative/final reuse energy T={mode.Item1} S={mode.Item2}",.03f);
                Debug.Log($"[HoRTDIValidation] CUTOUT_REUSE T={mode.Item1} S={mode.Item2} ratio={ratio:F6}");
            }
            feature.Settings.denoise=true;feature.Settings.temporal=feature.Settings.spatial=false;
            foreach(bool cutoutActive in new[]{true,false,true})
            {
                blocker.SetActive(cutoutActive);for(int i=0;i<96;i++)RenderCamera();
                Require(feature.LastContext.nrd.AppliedSettings.diffPhi==(cutoutActive?1f:2f)&&feature.LastContext.nrd.AppliedSettings.fastSigma==(cutoutActive?4f:2f),"NRD cutout/opaque luminance and clamping profile must follow scene support");
                double sum=0;for(int frame=0;frame<256;frame++)
                {RenderCamera();ReadImage(feature.LastContext.filteredDiffuse.rt,image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)sum+=image.GetPixel(x,y).r;}
                float ratio=(float)(sum/(256*256*(expected+(cutoutActive?0:ExpectedMean(light)))));
                Near(ratio,1,"filtered energy after cutout/opaque profile switch",.05f);
                Debug.Log($"[HoRTDIValidation] CUTOUT_NRD_PROFILE active={cutoutActive} phi={feature.LastContext.nrd.AppliedSettings.diffPhi} energy={ratio:F6}");
            }
            feature.Settings.denoise=false;far.enabled=false;feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.initialSamples=1;
        }
        private static void ValidateCutoutLightDomains()
        {
            var rotation=light.transform.rotation;
            light.transform.LookAt(Vector3.zero);light.type=LightType.Spot;light.spotAngle=90;light.innerSpotAngle=60;
            AlphaFill(alphaTexture,1);Near(Render().r,0,"spot cutout shadow",.002f);
            AlphaFill(alphaTexture,0);Near(Render().r,Expected(light),"spot cutout transmission",.015f);
            light.type=LightType.Directional;float reference=light.intensity*Mathf.Max(0,light.transform.forward.z)/Mathf.PI;
            AlphaFill(alphaTexture,1);Near(Render().r,0,"directional cutout shadow",.002f);
            AlphaFill(alphaTexture,0);Near(Render().r,reference,"directional cutout transmission",.015f);
            light.type=LightType.Point;light.transform.rotation=rotation;light.enabled=false;blocker.SetActive(false);
            var shell=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));shell.name="Independent cutout environment shell";shell.transform.localScale=Vector3.one*10;
            shell.GetComponent<Renderer>().sharedMaterial=cutoutMaterial;
            atmosphere.Settings.enabled=true;feature.Settings.useEnvironment=true;
            var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            AlphaFill(alphaTexture,1);var shadow=EnvironmentMean(image,8,64,false);Near(shadow.r,0,"closed cutout environment shell",.002f);
            AlphaFill(alphaTexture,0);var open=EnvironmentMean(image,16,256,false);NearRGB(open,new Color(1,.5f,.25f,1),"transparent cutout environment shell",.03f);
            Debug.Log($"[HoRTDIValidation] CUTOUT_LIGHT_DOMAIN directionalReference={reference:F6} skyShadow={shadow.r:F6} skyOpen={open}");
            shell.SetActive(false);atmosphere.Settings.enabled=false;feature.Settings.useEnvironment=false;light.enabled=true;
        }
        private static void ValidateCutoutPattern()
        {
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)alphaTexture.SetPixel(x,y,new Color(1,1,1,((x/4+y/4)&1)==0?0:1));alphaTexture.Apply();
            var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));
            var mesh=blocker.GetComponent<SkinnedMeshRenderer>().sharedMesh;var vertices=mesh.vertices;var indices=mesh.triangles;var uvs=mesh.uv;
            for(int i=0;i<vertices.Length;i++)vertices[i]=blocker.transform.TransformPoint(vertices[i]);
            var wraps=new[]{TextureWrapMode.Repeat,TextureWrapMode.Clamp,TextureWrapMode.Mirror,TextureWrapMode.MirrorOnce};
            int blocked=0,open=0,skipped=0;
            foreach(var filter in new[]{FilterMode.Point,FilterMode.Bilinear})for(int mode=0;mode<wraps.Length;mode++)
            {
                alphaTexture.filterMode=filter;alphaTexture.wrapModeU=wraps[mode];alphaTexture.wrapModeV=wraps[(mode+1)%4];
                cutoutMaterial.SetTextureScale("_MainTex",new Vector2(2,-1));cutoutMaterial.SetTextureOffset("_MainTex",new Vector2(.17f,-.22f));
                cutoutMaterial.SetVector("_MainTex_ScrollRotate",new Vector4(0,0,.7f,0));RenderCamera();ReadImage(feature.LastContext.diffuse.rt,image);
                for(int y=18;y<46;y++)for(int x=18;x<46;x++)
                {
                    var view=camera.ViewportPointToRay(new Vector3((x+.5f)/64,(y+.5f)/64,0));var p=view.origin+view.direction*(-view.origin.z/view.direction.z);
                    var delta=light.transform.position-p;float max=delta.magnitude;var direction=delta/max;bool occluded=false,boundary=false;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=vertices[indices[i]];var e1=vertices[indices[i+1]]-a;var e2=vertices[indices[i+2]]-a;var cross=Vector3.Cross(direction,e2);
                        float determinant=Vector3.Dot(e1,cross);if(Mathf.Abs(determinant)<1e-8f)continue;
                        var t=p-a;float u=Vector3.Dot(t,cross)/determinant;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)/determinant;
                        // Include shared edges robustly: float world-space arithmetic can
                        // otherwise reject both triangles at an exactly diagonal-edge hit.
                        float distance=Vector3.Dot(e2,q)/determinant;if(u< -1e-6f||v< -1e-6f||u+v>1+1e-6f||distance<=.001f||distance>=max-.001f)continue;
                        var uv=uvs[indices[i]]*(1-u-v)+uvs[indices[i+1]]*u+uvs[indices[i+2]]*v;
                        if(determinant<0&&cutoutMaterial.GetFloat("_ShiftBackfaceUV")>0)uv.x+=1;
                        uv=Vector2.Scale(uv,new Vector2(2,-1))+new Vector2(.17f,-.22f)-Vector2.one*.5f;
                        uv=new Vector2(uv.x*Mathf.Cos(.7f)-uv.y*Mathf.Sin(.7f),uv.x*Mathf.Sin(.7f)+uv.y*Mathf.Cos(.7f))+Vector2.one*.5f;
                        float alpha=CutoutCpuSample(alphaTexture,uv);
                        boundary|=Mathf.Abs(alpha-.5f)<.003f;
                        if(filter==FilterMode.Point)boundary|=Mathf.Abs(uv.x*16-Mathf.Round(uv.x*16))<.003f||Mathf.Abs(uv.y*16-Mathf.Round(uv.y*16))<.003f;
                        occluded|=alpha>=.5f;
                    }
                    if(boundary){skipped++;continue;}
                    float expected=occluded?0:Expected(light,x,y);Near(image.GetPixel(x,y).r,expected,$"native alpha oracle filter={filter} wraps={alphaTexture.wrapModeU}/{alphaTexture.wrapModeV} pixel={x},{y} backUV={cutoutMaterial.GetFloat("_ShiftBackfaceUV")}",.003f);
                    if(occluded)blocked++;else open++;
                }
            }
            Require(blocked>50&&open>50,"pattern must exercise both outcomes");
            Debug.Log($"[HoRTDIValidation] CUTOUT_PATTERN blocked={blocked} open={open} boundarySkipped={skipped} transforms/rotation/point+bilinear/mixed U/V wraps");
        }
        private static float CutoutCpuSample(Texture2D texture,Vector2 uv)
        {
            if(texture.wrapModeU==TextureWrapMode.MirrorOnce)uv.x=Mathf.Clamp01(Mathf.Abs(uv.x));
            if(texture.wrapModeV==TextureWrapMode.MirrorOnce)uv.y=Mathf.Clamp01(Mathf.Abs(uv.y));
            int Wrap(int index,int count,TextureWrapMode mode)
            {
                if(mode==TextureWrapMode.Clamp||mode==TextureWrapMode.MirrorOnce)return Mathf.Clamp(index,0,count-1);
                int period=mode==TextureWrapMode.Mirror?count*2:count;index=(index%period+period)%period;
                return index<count?index:period-1-index;
            }
            float Pixel(int x,int y)=>texture.GetPixel(Wrap(x,texture.width,texture.wrapModeU),Wrap(y,texture.height,texture.wrapModeV)).a;
            if(texture.filterMode==FilterMode.Point)return Pixel(Mathf.FloorToInt(uv.x*texture.width),Mathf.FloorToInt(uv.y*texture.height));
            float px=uv.x*texture.width-.5f,py=uv.y*texture.height-.5f;int ix=Mathf.FloorToInt(px),iy=Mathf.FloorToInt(py);
            return Mathf.Lerp(Mathf.Lerp(Pixel(ix,iy),Pixel(ix+1,iy),px-ix),Mathf.Lerp(Pixel(ix,iy+1),Pixel(ix+1,iy+1),px-ix),py-iy);
        }
    }
}
