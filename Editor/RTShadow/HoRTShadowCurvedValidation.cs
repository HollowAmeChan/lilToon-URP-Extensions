#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool curvedAudit;
        [Serializable]private sealed class CurvedReport{public bool pass,compatibility;public int valid,falseShadows,contactBlocked,contactOccluded,contactVisible,terminatorSamples,geometricBlocked,correctedBlocked;public float contactMinimum=65504,contactMaximum;public List<string> evidence=new();}
        public static void RunCurved(){curvedAudit=true;nprIntegration=true;rawOnly=true;Run();}
        public static void RunCurvedCompatibility(){compatibility=true;RunCurved();}
        private static void ValidateCurvedNormals()
        {
            near.enabled=far.enabled=false;
            var members=new List<Renderer>();group.GetAssignedRenderers(members);var body=members.Find(item=>item.sharedMaterial==toon);
            body.shadowCastingMode=ShadowCastingMode.TwoSided;
            var filter=body.GetComponent<MeshFilter>();var mesh=Keep(Object.Instantiate(filter.sharedMesh));filter.sharedMesh=mesh;
            var vertices=mesh.vertices;var normals=new Vector3[vertices.Length];
            for(int i=0;i<normals.Length;i++)normals[i]=new Vector3(vertices[i].x*.8f,vertices[i].y*.2f,-1).normalized;
            mesh.normals=normals;body.transform.position=new Vector3(0,0,.137f);body.transform.rotation=Quaternion.Euler(0,23,0);
            var normal=body.transform.TransformDirection(Vector3.back);var direction=(body.transform.right+normal*.002f).normalized;
            light.transform.rotation=Quaternion.LookRotation(-direction);shadow.Settings.angularDiameter=0;target=Target(128);
            var result=new CurvedReport{compatibility=compatibility};
            foreach(bool perspective in new[]{false,true})
            {
                camera.orthographic=!perspective;camera.nearClipPlane=.1f;camera.farClipPlane=30;camera.fieldOfView=60;
                for(int pose=0;pose<4;pose++)
                {
                    camera.ResetProjectionMatrix();var projection=camera.projectionMatrix;
                    if(perspective)projection.m02+=(pose-1.5f)/128;else projection.m03+=(pose-1.5f)/128;
                    camera.projectionMatrix=projection;var raw=Render();
                    foreach(var p in raw)if(p.b==1&&p.a==bodyId){result.valid++;if(p.r<.5f)result.falseShadows++;}
                    if(pose==0)File.WriteAllBytes(Path.Combine(Folder(),"curved-"+(perspective?"perspective":"ortho")+".rgba32"),Bytes(raw));
                }
            }
            camera.ResetProjectionMatrix();camera.orthographic=true;
            var contact=Quad(body.transform.position+normal*.002f,new Vector3(3,3,1),true);contact.transform.rotation=body.transform.rotation;contact.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            light.transform.rotation=Quaternion.LookRotation(-normal);var blocked=Render();
            // Distance is from the corrected ray origin, not the original
            // raster point. Require actual valid occlusion at positive distance.
            foreach(var p in blocked)if(p.b==1&&p.a==bodyId&&p.r==0&&p.g>0&&p.g<.004f)result.contactBlocked++;
            foreach(var p in blocked)if(p.b==1&&p.a==bodyId){if(p.r==0){result.contactOccluded++;result.contactMinimum=Mathf.Min(result.contactMinimum,p.g);result.contactMaximum=Mathf.Max(result.contactMaximum,p.g);}else result.contactVisible++;}
            File.WriteAllBytes(Path.Combine(Folder(),"curved-contact.rgba32"),Bytes(blocked));
            contact.enabled=false;
            var cylinder=Keep(new Mesh{name="Eight-sided smooth-normal closed limb"});var points=new List<Vector3>();var ns=new List<Vector3>();var uv=new List<Vector2>();var ids=new List<int>();
            const int sides=8;
            for(int ring=0;ring<2;ring++)for(int side=0;side<sides;side++)
            {float a=side*2*Mathf.PI/sides;var n=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));points.Add(n*.7f+Vector3.up*(ring*2-1));ns.Add(n);uv.Add(new Vector2(side/(float)sides,ring));}
            for(int side=0;side<sides;side++){int next=(side+1)%sides;ids.AddRange(new[]{side,next,side+sides,next,next+sides,side+sides});}
            int bottom=points.Count;points.Add(Vector3.down);ns.Add(Vector3.down);uv.Add(Vector2.zero);int top=points.Count;points.Add(Vector3.up);ns.Add(Vector3.up);uv.Add(Vector2.one);
            for(int side=0;side<sides;side++){int next=(side+1)%sides;ids.AddRange(new[]{bottom,next,side,top,side+sides,next+sides});}
            cylinder.SetVertices(points);cylinder.SetNormals(ns);cylinder.SetUVs(0,uv);cylinder.SetTriangles(ids,0);cylinder.RecalculateBounds();filter.sharedMesh=cylinder;
            body.transform.localScale=Vector3.one;body.transform.rotation=Quaternion.identity;camera.orthographicSize=1.3f;camera.ResetProjectionMatrix();
            for(int angle=0;angle<16;angle++)
            {
                float a=(angle*3+66)*Mathf.Deg2Rad;var d=new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a));light.transform.rotation=Quaternion.LookRotation(-d);
                shadow.Settings.terminatorCorrection=0;var geometric=Render();shadow.Settings.terminatorCorrection=1;var corrected=Render();
                var inverse=camera.projectionMatrix.inverse;var cameraToWorld=camera.cameraToWorldMatrix;
                for(int py=16;py<112;py++)for(int px=8;px<120;px++)
                {
                    int index=py*128+px;if(geometric[index].b!=1||corrected[index].b!=1||geometric[index].a!=bodyId)continue;
                    float x=(px+.5f)/128*2-1,y=(py+.5f)/128*2-1;
                    var h0=inverse*new Vector4(x,y,-1,1);var h1=inverse*new Vector4(x,y,1,1);
                    Vector3 o=cameraToWorld.MultiplyPoint3x4(new Vector3(h0.x,h0.y,h0.z)/h0.w),end=cameraToWorld.MultiplyPoint3x4(new Vector3(h1.x,h1.y,h1.z)/h1.w),rd=(end-o).normalized;
                    float best=float.MaxValue;Vector3 smooth=Vector3.zero,face=Vector3.zero;
                    for(int tri=0;tri<ids.Count;tri+=3)
                    {
                        int ia=ids[tri],ib=ids[tri+1],ic=ids[tri+2];Vector3 p0=body.transform.TransformPoint(points[ia]),e1=body.transform.TransformVector(points[ib]-points[ia]),e2=body.transform.TransformVector(points[ic]-points[ia]);
                        var cross=Vector3.Cross(rd,e2);float det=Vector3.Dot(e1,cross);if(Mathf.Abs(det)<1e-7f)continue;
                        var t=o-p0;float u=Vector3.Dot(t,cross)/det;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(rd,q)/det,dist=Vector3.Dot(e2,q)/det;
                        if(u<0||v<0||u+v>1||dist<0||dist>=best)continue;
                        best=dist;smooth=(ns[ia]*(1-u-v)+ns[ib]*u+ns[ic]*v).normalized;face=Vector3.Cross(e1,e2).normalized;
                        if(Vector3.Dot(face,smooth)<0)face=-face;
                    }
                    if(best==float.MaxValue||Vector3.Dot(smooth,d)<.03f||Vector3.Dot(face,d)>-.001f)continue;
                    result.terminatorSamples++;if(geometric[index].r<.5f)result.geometricBlocked++;if(corrected[index].r<.5f)result.correctedBlocked++;
                }
                if(angle==8){File.WriteAllBytes(Path.Combine(Folder(),"curved-cylinder-geometry.rgba32"),Bytes(geometric));File.WriteAllBytes(Path.Combine(Folder(),"curved-cylinder-corrected.rgba32"),Bytes(corrected));}
            }
            result.evidence.Add("Smooth interpolated mesh normals vary across the two exact planar triangles, like a facet of a smooth limb; no real occluder can explain interior strips");
            result.evidence.Add("Perspective and ortho current jittered raster samples keep the original two-sided receiver in the native AS");
            result.evidence.Add("Independent parallel blocker at 2mm remains real geometry, even when absent from raster");
            result.evidence.Add("Sixteen rotating light directions over an actual eight-sided limb compare exact polygon geometry against smooth vertex-normal terminator region");
            result.pass=result.valid>10000&&result.falseShadows==0&&result.contactBlocked>100&&result.terminatorSamples>100&&result.correctedBlocked<result.geometricBlocked*.2f;
            File.WriteAllText(Path.Combine(Folder(),"curved-report.json"),JsonUtility.ToJson(result,true));
            Check(result.pass,"Smooth-normal self Cast false="+result.falseShadows+" / "+result.valid+" contact="+result.contactBlocked+" terminator="+result.geometricBlocked+" -> "+result.correctedBlocked+" / "+result.terminatorSamples);
            Debug.Log("[HoRTShadowCurved] PASS actual triangle versus interpolated normal / jitter / preserved nearby occluder");
        }
    }
}
