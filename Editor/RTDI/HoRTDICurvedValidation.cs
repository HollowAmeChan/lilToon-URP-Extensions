using System;
using UnityEngine;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.SurfaceBuffer;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool forceCurved;
        private static bool forceCurvedMotion;
        private static bool forceCurvedHistoryAudit;
        public static void RunCurved()
        {Require(Application.dataPath.Replace('\\','/').Contains("/.codex-research/"),"Use the isolated project");forceCurved=forceDenoise=true;Run();}
        public static void RunCurvedCompatibility(){compatibility=true;RunCurved();}
        public static void RunCurvedMotion(){forceCurvedMotion=true;RunCurved();}
        public static void RunCurvedMotionCompatibility(){compatibility=true;RunCurvedMotion();}
        public static void RunCurvedHistoryAudit(){compatibility=true;forceCurvedHistoryAudit=true;RunCurved();}

        private struct CurvedHit {internal Vector3 position,normal;internal bool valid;}
        private static CurvedHit[] CurvedReference(Mesh mesh,Transform transform,int size)
        {
            var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;
            var normalMatrix=transform.localToWorldMatrix.inverse.transpose;
            for(int i=0;i<vertices.Length;i++){vertices[i]=transform.TransformPoint(vertices[i]);normals[i]=normalMatrix.MultiplyVector(normals[i]).normalized;}
            var result=new CurvedHit[size*size];
            for(int y=48;y<80;y++)for(int x=48;x<80;x++)
            {
                var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/size,(y+.5f)/size,0));float closest=float.PositiveInfinity;
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];var e1=vertices[b]-vertices[a];var e2=vertices[c]-vertices[a];
                    var cross=Vector3.Cross(ray.direction,e2);float determinant=Vector3.Dot(e1,cross);if(Mathf.Abs(determinant)<1e-8f)continue;
                    float inverse=1/determinant;var offset=ray.origin-vertices[a];float u=Vector3.Dot(offset,cross)*inverse;if(u<0||u>1)continue;
                    var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(ray.direction,q)*inverse;if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(e2,q)*inverse;if(distance<=0||distance>=closest)continue;
                    closest=distance;result[y*size+x]=new CurvedHit{valid=true,position=ray.GetPoint(distance),normal=(normals[a]*(1-u-v)+normals[b]*u+normals[c]*v).normalized};
                }
                Require(result[y*size+x].valid,"Curved reference ROI missed the mesh");
            }
            return result;
        }
        private static Vector3 CurvedDecodeNormal(Color packed)
        {
            // NRD's pinned signed-oct 10/10/10 contract; this guide was written
            // from the actual SB normal consumed by DI, not a post-frame global.
            var n=new Vector3(packed.r-packed.g,packed.r+packed.g-1,0);
            n.z=(1-Mathf.Abs(n.x)-Mathf.Abs(n.y))*(packed.b<.5f?-1:1);
            return n.normalized;
        }
        private static (float diffuse,float specular) CurvedLightReference(CurvedHit hit,Light source,float roughness)
        {
            if(!source.enabled)return(0,0);var delta=source.transform.position-hit.position;float distance=delta.magnitude;var L=delta/distance;
            float fade=Mathf.Clamp01(1-Mathf.Pow(distance/source.range,4));var radiance=LightColor(source)*(source.intensity/delta.sqrMagnitude*fade*fade);
            var spec=ReferenceGGX(hit.normal,(camera.transform.position-hit.position).normalized,L,roughness,new Color(10f/255,10f/255,10f/255,1))*radiance;
            return(radiance.r*Mathf.Max(0,Vector3.Dot(hit.normal,L))/Mathf.PI,spec.r);
        }
        private static void ValidateCurved()
        {
            using var distanceOverride=ApplySpecularDistanceControl();
            const int size=128;primaryReceiver.SetActive(false);blocker.SetActive(false);
            target.Release();target.width=target.height=size;target.Create();camera.orthographic=false;camera.fieldOfView=40;
            var receiver=Keep(GameObject.CreatePrimitive(PrimitiveType.Sphere));receiver.transform.localScale=Vector3.one*1.5f;receiver.GetComponent<Renderer>().sharedMaterial=surfaceMaterial;
            surfaceMaterial.SetFloat("_Metallic",0);surfaceMaterial.SetFloat("_Reflectance",.04f);surfaceMaterial.SetColor("_Color",Color.white);
            light.type=LightType.Point;light.transform.position=new Vector3(-.3f,.35f,-1.7f);light.range=20;light.intensity=4;light.color=new Color(.7f,.4f,.2f);
            var far=Point(new Vector3(-30,0,-50),light.color,25000);far.range=100;far.enabled=false;
            var reference=CurvedReference(receiver.GetComponent<MeshFilter>().sharedMesh,receiver.transform,size);
            var rawDiffuse=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var rawSpecular=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var filteredDiffuse=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var filteredSpecular=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var normals=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));var depth=Keep(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            if(forceCurvedMotion){ValidateCurvedMotion(receiver,far,rawDiffuse,rawSpecular,filteredDiffuse,filteredSpecular);return;}
            bool rawQuality=true,filteredQuality=true;
            foreach(float roughness in new[]{.3f,.7f})foreach(int mode in new[]{0,1,2,3,4})
            {
                far.enabled=mode>0;feature.Settings.initialSamples=mode==0?1:8;feature.Settings.temporal=mode==2||mode==4;feature.Settings.spatial=mode==3||mode==4;
                surfaceMaterial.SetFloat("_Smoothness",1-roughness);feature.Settings.denoise=false;RenderCamera();feature.Settings.denoise=true;
                for(int frame=0;frame<192;frame++)RenderCamera();var context=feature.LastContext;
                var demand=HoAttributeCompositeConsumerRegistry.LastSnapshot;
                Require(demand!=null&&demand.CameraId==camera.GetInstanceID(),"Curved demand snapshot belongs to another camera");
                Require(demand.NeedsHighPrecisionSurfaceNormal&&!demand.NeedsSurfaceSemantic,"RT normal precision request added semantic resources or was lost");
                if(compatibility)Require(HoSurfaceBufferPass.CompatibilitySource.NormalTexture.rt.graphicsFormat==HoSurfaceBufferFormatUtility.GetNormalGraphicsFormat(true),"Compatibility SB normal precision was not upgraded");
                float rough=Mathf.Round(Mathf.Abs(ReadCenter(context.nrdNormal.rt).b*2-1)*255)/255;
                if(forceCurvedHistoryAudit){AuditCurvedHistory(context,reference,rough,normals,size);return;}
                ReadImage(context.nrdNormal.rt,normals);ReadImage(context.nrdViewZ.rt,depth);
                float normalWorst=0,depthWorst=0;
                double diffReference=0,specReference=0,diffRaw=0,specRaw=0,diffFiltered=0,specFiltered=0,diffRawError=0,specRawError=0,diffFilteredError=0,specFilteredError=0,specRawMse=0,specFilteredMse=0,specReferenceSquares=0;
                var expectedDiffuse=new float[size*size];var expectedSpecular=new float[size*size];
                for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                {
                    int p=y*size+x;var hit=reference[p];normalWorst=Mathf.Max(normalWorst,Vector3.Angle(hit.normal,CurvedDecodeNormal(normals.GetPixel(x,y))));
                    depthWorst=Mathf.Max(depthWorst,Mathf.Abs(depth.GetPixel(x,y).r+camera.worldToCameraMatrix.MultiplyPoint(hit.position).z));
                    var a=CurvedLightReference(hit,light,rough);var b=CurvedLightReference(hit,far,rough);expectedDiffuse[p]=a.diffuse+b.diffuse;expectedSpecular[p]=a.specular+b.specular;
                }
                for(int frame=0;frame<256;frame++)
                {
                    RenderCamera();ReadImage(context.diffuse.rt,rawDiffuse);ReadImage(context.specular.rt,rawSpecular);ReadImage(context.filteredDiffuse.rt,filteredDiffuse);ReadImage(context.filteredSpecular.rt,filteredSpecular);
                    for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                    {
                        int p=y*size+x;float d=expectedDiffuse[p],s=expectedSpecular[p],dr=rawDiffuse.GetPixel(x,y).r,sr=rawSpecular.GetPixel(x,y).r,df=filteredDiffuse.GetPixel(x,y).r,sf=filteredSpecular.GetPixel(x,y).r;
                        diffReference+=d;specReference+=s;diffRaw+=dr;specRaw+=sr;diffFiltered+=df;specFiltered+=sf;
                        diffRawError+=Math.Abs(dr-d);specRawError+=Math.Abs(sr-s);diffFilteredError+=Math.Abs(df-d);specFilteredError+=Math.Abs(sf-s);
                        specRawMse+=(sr-s)*(sr-s);specFilteredMse+=(sf-s)*(sf-s);specReferenceSquares+=s*s;
                    }
                }
                Debug.Log($"[HoRTDIValidation] CURVED rough={rough:F4} mode={mode} normalMaxDegrees={normalWorst:F6} depthMax={depthWorst:F6} rawDiffuseRatio={diffRaw/diffReference:F6} rawSpecularRatio={specRaw/specReference:F6} filteredDiffuseRatio={diffFiltered/diffReference:F6} filteredSpecularRatio={specFiltered/specReference:F6} rawDiffuseL1={diffRawError/diffReference:F6} rawSpecularL1={specRawError/specReference:F6} filteredDiffuseL1={diffFilteredError/diffReference:F6} filteredSpecularL1={specFilteredError/specReference:F6} specRawNormalizedMSE={specRawMse/specReferenceSquares:F6} specFilteredNormalizedMSE={specFilteredMse/specReferenceSquares:F6}");
                rawQuality&=normalWorst<.3f&&depthWorst<.002f&&Math.Abs(diffRaw/diffReference-1)<.03&&Math.Abs(specRaw/specReference-1)<.03;
                if(mode==0)rawQuality&=diffRawError/diffReference<.02&&specRawError/specReference<.02;
                filteredQuality&=Math.Abs(diffFiltered/diffReference-1)<.05&&Math.Abs(specFiltered/specReference-1)<.05&&diffFilteredError/diffReference<.15&&specFilteredError/specReference<.15;
            }
            Require(rawQuality,"Curved raw/normal/depth reference failed");
            Debug.Log("[HoRTDIValidation] PASS curved primary guides and raw analytic mesh reference; compatibility="+compatibility);
            Require(filteredQuality,"Curved filtered diffuse/specular quality failed");
            feature.SetActive(false);RenderCamera();Require(!HoAttributeCompositeConsumerRegistry.LastSnapshot.NeedsHighPrecisionSurfaceNormal,"Disabled DI retained the high-precision request");
            if(compatibility)Require(HoSurfaceBufferPass.CompatibilitySource.NormalTexture.rt.graphicsFormat==HoSurfaceBufferFormatUtility.GetNormalGraphicsFormat(false),"Disabled DI did not restore the compact SB normal format");
            feature.SetActive(true);RenderCamera();Require(HoAttributeCompositeConsumerRegistry.LastSnapshot.NeedsHighPrecisionSurfaceNormal,"DI re-enable lost the precision request");
            Debug.Log("[HoRTDIValidation] PASS curved single/pair-light NRD energy and shape; compatibility="+compatibility);
        }

        private static void ValidateCurvedMotion(GameObject receiver,Light far,Texture2D rawDiffuse,Texture2D rawSpecular,Texture2D filteredDiffuse,Texture2D filteredSpecular)
        {
            const int size=128,observations=64;var references=new CurvedHit[observations][];
            for(int frame=0;frame<observations;frame++){SetLocalView((frame+1)*.25f);references[frame]=CurvedReference(receiver.GetComponent<MeshFilter>().sharedMesh,receiver.transform,size);}
            bool rawQuality=true,filteredQuality=true;
            foreach(float roughness in new[]{.3f,.7f})foreach(int mode in new[]{0,1,2,3,4})foreach(int seed in new[]{0,1024,4096})
            {
                SetLocalView(0);far.enabled=mode>0;feature.Settings.initialSamples=mode==0?1:8;feature.Settings.temporal=mode==2||mode==4;feature.Settings.spatial=mode==3||mode==4;
                surfaceMaterial.SetFloat("_Smoothness",1-roughness);feature.Settings.denoise=false;RenderCamera();
                var context=feature.LastContext;context.frame=seed;context.historyValid=context.nrdHistoryValid=false;feature.Settings.denoise=true;
                for(int frame=0;frame<192;frame++)RenderCamera();
                float rough=Mathf.Round(Mathf.Abs(ReadCenter(context.nrdNormal.rt).b*2-1)*255)/255;
                double diffReference=0,specReference=0,diffRaw=0,specRaw=0,diffFiltered=0,specFiltered=0,diffRawL1=0,specRawL1=0,diffL1=0,specL1=0,rawMse=0,filteredMse=0,referenceSquares=0;
                for(int frame=0;frame<observations;frame++)
                {
                    SetLocalView((frame+1)*.25f);RenderCamera();ReadImage(context.diffuse.rt,rawDiffuse);ReadImage(context.specular.rt,rawSpecular);ReadImage(context.filteredDiffuse.rt,filteredDiffuse);ReadImage(context.filteredSpecular.rt,filteredSpecular);
                    for(int y=48;y<80;y++)for(int x=48;x<80;x++)
                    {
                        var hit=references[frame][y*size+x];var a=CurvedLightReference(hit,light,rough);var b=CurvedLightReference(hit,far,rough);float d=a.diffuse+b.diffuse,s=a.specular+b.specular;
                        float dr=rawDiffuse.GetPixel(x,y).r,sr=rawSpecular.GetPixel(x,y).r,df=filteredDiffuse.GetPixel(x,y).r,sf=filteredSpecular.GetPixel(x,y).r;
                        diffReference+=d;specReference+=s;diffRaw+=dr;specRaw+=sr;diffFiltered+=df;specFiltered+=sf;
                        diffRawL1+=Math.Abs(dr-d);specRawL1+=Math.Abs(sr-s);diffL1+=Math.Abs(df-d);specL1+=Math.Abs(sf-s);
                        rawMse+=(sr-s)*(sr-s);filteredMse+=(sf-s)*(sf-s);referenceSquares+=s*s;
                    }
                }
                Debug.Log($"[HoRTDIValidation] CURVED_MOTION rough={rough:F4} mode={mode} seed={seed} rawDiffuseRatio={diffRaw/diffReference:F6} rawSpecularRatio={specRaw/specReference:F6} filteredDiffuseRatio={diffFiltered/diffReference:F6} filteredSpecularRatio={specFiltered/specReference:F6} rawDiffuseL1={diffRawL1/diffReference:F6} rawSpecularL1={specRawL1/specReference:F6} filteredDiffuseL1={diffL1/diffReference:F6} filteredSpecularL1={specL1/specReference:F6} rawNormalizedMSE={rawMse/referenceSquares:F6} filteredNormalizedMSE={filteredMse/referenceSquares:F6}; 64 frames, 15deg/s, 60Hz, independent mesh reference");
                rawQuality&=Math.Abs(diffRaw/diffReference-1)<.03&&Math.Abs(specRaw/specReference-1)<.03;
                if(mode==0)rawQuality&=diffRawL1/diffReference<.02&&specRawL1/specReference<.02;
                filteredQuality&=Math.Abs(diffFiltered/diffReference-1)<.05&&Math.Abs(specFiltered/specReference-1)<.05&&diffL1/diffReference<.15&&specL1/specReference<.15;
            }
            Require(rawQuality,"Curved camera motion raw energy/shape failed");Debug.Log("[HoRTDIValidation] PASS curved camera motion raw across three independent sequences; compatibility="+compatibility);
            Require(filteredQuality,"Curved camera motion filtered energy/shape failed");Debug.Log("[HoRTDIValidation] PASS curved camera motion filtered energy/shape across three independent sequences; compatibility="+compatibility);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CurvedHistory20 {internal uint depth,shadingNormal,geometricNormal,diffuse,specular;}
        private static Vector3 CurvedDecodeSbNormal(float x,float y)
        {
            var n=new Vector3(x*2-1,y*2-1,1-Mathf.Abs(x*2-1)-Mathf.Abs(y*2-1));
            if(n.z<0){float oldX=n.x;n.x=(1-Mathf.Abs(n.y))*(oldX<0?-1:1);n.y=(1-Mathf.Abs(oldX))*(n.y<0?-1:1);}
            return n.normalized;
        }
        private static void AuditCurvedHistory(lilToon.URP.Extensions.RTDI.HoRTDIContext context,CurvedHit[] reference,float rough,Texture2D normals,int size)
        {
            Require(context.history.stride==20&&System.Runtime.InteropServices.Marshal.SizeOf<CurvedHistory20>()==20,"Primary history ABI must be 20 bytes");
            var history=new CurvedHistory20[context.history.count];context.history.GetData(history);
            ReadImage(HoSurfaceBufferPass.CompatibilitySource.NormalTexture.rt,normals);
            double scoreReference=0,scoreError=0;float angleWorst=0,relativeWorst=0;int mismatches=0;
            for(int y=48;y<80;y++)for(int x=48;x<80;x++)
            {
                int p=y*size+x;var h=history[p];Require((h.diffuse>>24)!=0,"History audit encountered an invalid receiver");var encoded=normals.GetPixel(x,y);
                uint expectedShading=(uint)Mathf.RoundToInt(encoded.r*65535)|((uint)Mathf.RoundToInt(encoded.g*65535)<<16),expectedGeometric=(uint)Mathf.RoundToInt(encoded.b*65535)|((uint)Mathf.RoundToInt(encoded.a*65535)<<16);
                if(h.shadingNormal!=expectedShading||h.geometricNormal!=expectedGeometric)mismatches++;
                var current=reference[p];current.normal=CurvedDecodeSbNormal(encoded.r,encoded.g);var previous=current;previous.normal=CurvedDecodeSbNormal((h.shadingNormal&65535)/65535f,(h.shadingNormal>>16)/65535f);
                angleWorst=Mathf.Max(angleWorst,Vector3.Angle(current.normal,previous.normal));
                var a=CurvedLightReference(current,light,rough);var b=CurvedLightReference(previous,light,rough);float currentScore=a.diffuse+4*a.specular,previousScore=b.diffuse+4*b.specular;
                scoreReference+=currentScore;scoreError+=Math.Abs(currentScore-previousScore);relativeWorst=Mathf.Max(relativeWorst,Mathf.Abs(previousScore/currentScore-1));
            }
            Debug.Log($"[HoRTDIValidation] CURVED_HISTORY_AUDIT stride={context.history.stride} mismatches={mismatches}/1024 normalMaxDegrees={angleWorst:F6} targetL1={scoreError/scoreReference:F9} targetMaxRelative={relativeWorst:F9}; actual stationary SB/history normals, independent CPU DI score; not a GPU bias proof");
            Require(mismatches==0&&relativeWorst<.0001f,"Current and history normal representation differ");
        }
    }
}
