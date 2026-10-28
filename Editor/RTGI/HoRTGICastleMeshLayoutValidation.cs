using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Security.Cryptography;
using lilToon.URP.Extensions.RT;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // CPU metadata only: no acceleration structure build or reference rendering.
    public static class HoRTGICastleMeshLayoutValidation
    {
        [Serializable]sealed class Row
        {
            public string renderer,kind,mesh,indexFormat,material;public bool readable,baked,excludedSky,canonical;
            public int meshVertices,submesh,indexStart,indexCount,baseVertex,firstVertex,submeshVertices,positionStream,positionOffset,positionStride,minIndex=-1,maxIndex=-1;
            public long fullVertexBytes,predictedFullCountSpanBytes,baseOffsetEndBytes,firstOffsetEndBytes;
        }
        [Serializable]sealed class Report{public bool pass,vertexBuffersUnchanged,indexTuplesUnchanged;public int normalizedSubmeshes;public string error;public Row[] rows;}
        public static void RunCanonical()=>Run(true);
        public static void Run()=>Run(false);
        static void Run(bool canonicalize)
        {
            var rows=new List<Row>();var owned=new List<Mesh>();var report=new Report();int exit=1;string directory=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-gi-castle-mesh-layout"+(canonicalize?"-canonical":"")));Directory.CreateDirectory(directory);
                EditorSceneManager.OpenScene("Assets/mmd场景测试/血月古堡/血月古堡.unity",OpenSceneMode.Single);
                foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID))
                {
                    if(renderer is SkinnedMeshRenderer skin)
                    {
                        Capture(renderer,skin.sharedMesh,false);var pose=new Mesh{name="Layout current pose",hideFlags=HideFlags.HideAndDontSave};pose.MarkDynamic();owned.Add(pose);skin.BakeMesh(pose,true);Capture(renderer,pose,true);
                        if(canonicalize)
                        {
                            string before=VertexHash(pose);var indices=Enumerable.Range(0,pose.subMeshCount).Select(i=>pose.GetIndices(i,true)).ToArray();
                            report.normalizedSubmeshes+=Enumerable.Range(0,pose.subMeshCount).Count(i=>pose.GetBaseVertex(i)!=0);
                            HoRTRayMeshIndices.CanonicalizeOwned(pose);report.vertexBuffersUnchanged=before==VertexHash(pose);report.indexTuplesUnchanged=Enumerable.Range(0,pose.subMeshCount).All(i=>pose.GetBaseVertex(i)==0&&pose.GetIndices(i,false).SequenceEqual(indices[i]));
                            if(!report.vertexBuffersUnchanged||!report.indexTuplesUnchanged)throw new Exception("Ray canonicalization changed vertex bytes or absolute triangle tuples");Capture(renderer,pose,true,true);
                        }
                    }
                    else if(renderer is MeshRenderer&&renderer.TryGetComponent<MeshFilter>(out var filter))Capture(renderer,filter.sharedMesh,false);
                }
                report.pass=rows.Count>0;exit=report.pass?0:1;Debug.Log("[HoGIMeshLayout] PASS rows="+rows.Count);
                void Capture(Renderer renderer,Mesh mesh,bool baked,bool canonical=false)
                {
                    if(mesh==null)return;int stream=mesh.GetVertexAttributeStream(VertexAttribute.Position),stride=mesh.GetVertexBufferStride(stream),offset=mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<mesh.subMeshCount;i++)
                    {
                        var descriptor=mesh.GetSubMesh(i);var material=i<materials.Length?materials[i]:null;
                        var row=new Row{renderer=renderer.name,kind=renderer.GetType().Name,mesh=mesh.name,indexFormat=mesh.indexFormat.ToString(),material=material!=null?material.name:null,readable=mesh.isReadable,baked=baked,canonical=canonical,excludedSky=material!=null&&material.name=="天空",meshVertices=mesh.vertexCount,submesh=i,indexStart=descriptor.indexStart,indexCount=descriptor.indexCount,baseVertex=descriptor.baseVertex,firstVertex=descriptor.firstVertex,submeshVertices=descriptor.vertexCount,positionStream=stream,positionStride=stride,positionOffset=offset,fullVertexBytes=(long)mesh.vertexCount*stride,predictedFullCountSpanBytes=(long)(mesh.vertexCount-1)*stride+12};
                        row.baseOffsetEndBytes=(long)row.baseVertex*stride+offset+row.predictedFullCountSpanBytes;row.firstOffsetEndBytes=(long)row.firstVertex*stride+offset+row.predictedFullCountSpanBytes;
                        if(mesh.isReadable&&descriptor.indexCount>0){var indices=mesh.GetIndices(i,false);row.minIndex=indices.Min();row.maxIndex=indices.Max();}rows.Add(row);
                    }
                }
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally{report.rows=rows.ToArray();if(directory!=null)File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));foreach(var mesh in owned)UnityEngine.Object.DestroyImmediate(mesh);}
            EditorApplication.Exit(exit);
        }
        static string VertexHash(Mesh mesh)
        {
            using var data=Mesh.AcquireReadOnlyMeshData(mesh);using var hash=SHA256.Create();
            for(int stream=0;stream<mesh.vertexBufferCount;stream++){var bytes=data[0].GetVertexData<byte>(stream).ToArray();hash.TransformBlock(bytes,0,bytes.Length,null,0);}
            hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(hash.Hash).Replace("-","");
        }
    }
}
