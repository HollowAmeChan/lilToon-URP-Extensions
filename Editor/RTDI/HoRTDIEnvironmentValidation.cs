using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.Atmosphere;
namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceEnvironment;
        private static HoAtmosphereRendererFeature atmosphere;
        public static void RunEnvironment(){Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceEnvironment=true;Run();}
        public static void RunEnvironmentCompatibility(){compatibility=true;RunEnvironment();}
        private static void SetupEnvironment(UniversalRendererData renderer)
        {
            atmosphere=Keep(ScriptableObject.CreateInstance<HoAtmosphereRendererFeature>());atmosphere.Settings.source=HoAtmosphereSource.Constant;atmosphere.Settings.constantColor=new Color(1,.5f,.25f,1);
            renderer.rendererFeatures.Insert(0,atmosphere);
        }
        private static void ValidateEnvironment()
        {
            light.enabled=false;blocker.SetActive(false);feature.Settings.useEnvironment=true;feature.Settings.environmentSamples=4;feature.Settings.initialSamples=1;
            feature.Settings.temporal=feature.Settings.spatial=false;feature.Settings.denoise=false;
            var image=Keep(new Texture2D(64,64,TextureFormat.RGBAFloat,false,true));Color sum=Color.clear;
            for(int i=0;i<8;i++)RenderCamera();var context=feature.LastContext;
            Require(context.environmentSampling&&context.environmentPdf!=null,"Environment PDF/RIS resources missing");
            for(int frame=0;frame<256;frame++){RenderCamera();ReadImage(context.diffuse.rt,image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)sum+=image.GetPixel(x,y);}
            sum/=256*256;Debug.Log($"[HoRTDIValidation] ENVIRONMENT constant rawMean={sum} expected=(1,0.5,0.25); equal-area PDF={context.environmentPdf.rt.width}, RIS={context.ris.count}");
            NearRGB(sum,new Color(1,.5f,.25f,1),"Uniform environment Lambert integral",.03f);
            foreach(var mode in new[]{(true,false),(false,true),(true,true)})
            {feature.Settings.temporal=mode.Item1;feature.Settings.spatial=mode.Item2;NearRGB(EnvironmentMean(image,32,256,false),new Color(1,.5f,.25f,1),"Reused environment Lambert integral",.03f);}
            feature.Settings.temporal=feature.Settings.spatial=false;
            light.enabled=true;var mixed=EnvironmentMean(image,8,256,false);NearRGB(mixed,new Color(1,.5f,.25f,1)+Color.white*ExpectedMean(light),"Mixed analytic/environment MIS",.03f);light.enabled=false;
            var room=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));room.layer=1;room.transform.localScale=Vector3.one*10;room.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;camera.cullingMask=~(1<<1);
            NearRGB(EnvironmentMean(image,8,32,false),Color.black,"Enclosed world blocks sky",.001f);room.SetActive(false);
            var faces=new[]{new Color(10,0,0,1),new Color(0,4,0,1),new Color(0,0,2,1),Color.white,new Color(9,0,9,1),new Color(2,1,.5f,1)};
            var cube=Keep(new Cubemap(16,TextureFormat.RGBAHalf,false));cube.filterMode=FilterMode.Point;
            for(int face=0;face<6;face++){var colors=new Color[256];Array.Fill(colors,faces[face]);cube.SetPixels(colors,(CubemapFace)face);}cube.Apply(false,false);
            atmosphere.Settings.source=HoAtmosphereSource.RadianceCubemap;atmosphere.Settings.radianceCubemap=cube;
            foreach(float yaw in new[]{0f,90f})
            {
                atmosphere.Settings.rotationY=yaw;atmosphere.Settings.intensity=2;atmosphere.Settings.tint=new Color(.8f,.5f,1,1);
                var actual=EnvironmentMean(image,8,512,false);var expected=EnvironmentCubeReference(faces,yaw)*atmosphere.Settings.tint*2;
                Debug.Log($"[HoRTDIValidation] ENVIRONMENT cube yaw={yaw} raw={actual} reference={expected}; CPU hemispherical quadrature, HDR face colors");
                Require(Mathf.Abs(actual.r/expected.r-1)<.03f&&Mathf.Abs(actual.g/expected.g-1)<.03f&&Mathf.Abs(actual.b/expected.b-1)<.03f,"HDR environment cube/PDF/direction mismatch");
            }
            atmosphere.Settings.source=HoAtmosphereSource.Constant;atmosphere.Settings.rotationY=0;atmosphere.Settings.intensity=1;atmosphere.Settings.tint=Color.white;
            surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetFloat("_Smoothness",.3f);feature.Settings.denoise=true;
            var specRaw=EnvironmentMean(image,192,256,false,true);var specFiltered=EnvironmentMean(image,0,256,true,true);double referenceSpec=0;
            for(int i=0;i<4096;i++){float mu=(i+.5f)/4096;referenceSpec+=ReferenceGGX(new Vector3(Mathf.Sqrt(1-mu*mu),0,-mu),178f/255,new Color(10f/255,10f/255,10f/255,1)).r*2*Math.PI/4096;}
            Debug.Log($"[HoRTDIValidation] ENVIRONMENT NRD specRaw={specRaw.r:F6} filtered={specFiltered.r:F6} reference={referenceSpec:F6}; broad GGX hemisphere integral");
            Require(Math.Abs(specRaw.r/referenceSpec-1)<.05&&Math.Abs(specFiltered.r/referenceSpec-1)<.05,"Environment GGX/NRD energy mismatch");
            feature.Settings.denoise=false;surfaceMaterial.SetFloat("_Reflectance",0);surfaceMaterial.SetFloat("_Smoothness",.5f);
            feature.Settings.temporal=feature.Settings.spatial=true;for(int frame=0;frame<32;frame++)RenderCamera();
            atmosphere.Settings.constantColor=Color.black;RenderCamera();Require(!context.lastTemporal,"Sky revision failed to reset history");
            Near(Read(context.diffuse.rt).r,0,"Black environment retains old radiance",.001f);
            atmosphere.Settings.constantColor=new Color(1,.5f,.25f,1);for(int frame=0;frame<32;frame++)RenderCamera();
            feature.Settings.useEnvironment=false;RenderCamera();Near(Read(context.diffuse.rt).r,0,"Disabled environment still shades",.001f);Require(context.environmentPdf==null,"Disabled environment retains PDF");
            feature.Settings.useEnvironment=true;atmosphere.SetActive(false);RenderCamera();Near(Read(context.diffuse.rt).r,0,"Absent environment still shades",.001f);Require(context.environmentPdf==null,"Absent environment retains PDF");
            Debug.Log("[HoRTDIValidation] PASS constant environment SDK sampling, physical Lambert integral, revision/disable/absent lifecycle; compatibility="+compatibility);
        }
        private static Color EnvironmentMean(Texture2D image,int warmup,int observations,bool filtered,bool specular=false)
        {
            for(int i=0;i<warmup;i++)RenderCamera();var context=feature.LastContext;Color sum=Color.clear;
            for(int i=0;i<observations;i++)
            {RenderCamera();ReadImage(specular?(filtered?context.filteredSpecular.rt:context.specular.rt):(filtered?context.filteredDiffuse.rt:context.diffuse.rt),image);for(int y=24;y<40;y++)for(int x=24;x<40;x++)sum+=image.GetPixel(x,y);}
            return sum/(observations*256);
        }
        private static Color EnvironmentCubeReference(Color[] faces,float yaw)
        {
            Color result=Color.clear;var rotation=Quaternion.Euler(0,-yaw,0);const int steps=256;
            for(int y=0;y<steps;y++)for(int x=0;x<steps;x++)
            {
                float mu=(y+.5f)/steps,phi=2*Mathf.PI*(x+.5f)/steps,r=Mathf.Sqrt(1-mu*mu);var L=rotation*new Vector3(r*Mathf.Cos(phi),r*Mathf.Sin(phi),-mu);var a=new Vector3(Mathf.Abs(L.x),Mathf.Abs(L.y),Mathf.Abs(L.z));
                int face=a.x>=a.y&&a.x>=a.z?(L.x>0?0:1):a.y>=a.z?(L.y>0?2:3):(L.z>0?4:5);result+=faces[face]*(2*mu/(steps*steps));
            }
            return result;
        }
    }
}
