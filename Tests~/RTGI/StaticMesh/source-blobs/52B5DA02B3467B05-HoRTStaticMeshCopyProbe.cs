using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RayTracing
{
    public static class HoRTStaticMeshCopyProbe
    {
        [Serializable]sealed class Report{public bool pass,sourceReadable,cloneReadable,cloneWritable;public string error,writeError;public ushort[] sourceIndices,cloneIndices;}
        public static void Run()
        {
            Mesh source=null,clone=null;var report=new Report();int exit=1;string output=null;
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.codex-research/RTGIValidation/"))throw new Exception("Dedicated GI project required");
                output=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-static-copy-probe.json"));
                source=new Mesh{name="Readonly clone capability"};source.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.one,new Vector3(2,0,0),new Vector3(3,0,0),new Vector3(2,1,0),new Vector3(3,1,0)};
                source.subMeshCount=2;source.SetTriangles(new[]{0,2,1,2,3,1},0);source.SetTriangles(new[]{0,2,1,2,3,1},1,false,4);source.RecalculateNormals();source.RecalculateBounds();source.UploadMeshData(true);
                report.sourceReadable=source.isReadable;report.sourceIndices=Read(source);clone=UnityEngine.Object.Instantiate(source);report.cloneReadable=clone.isReadable;
                try
                {
                    clone.SetIndexBufferParams(12,IndexFormat.UInt16);clone.SetIndexBufferData(new ushort[]{0,2,1,2,3,1,4,6,5,6,7,5},0,0,12,MeshUpdateFlags.DontRecalculateBounds);
                    clone.SetSubMeshes(new[]{new SubMeshDescriptor(0,6){baseVertex=0},new SubMeshDescriptor(6,6){baseVertex=0}},MeshUpdateFlags.DontRecalculateBounds);
                    report.cloneIndices=Read(clone);report.cloneWritable=report.cloneIndices.Length==12&&report.cloneIndices[6]==4;
                }
                catch(Exception e){report.writeError=e.ToString();}
                report.pass=!report.sourceReadable&&report.sourceIndices.Length==12;exit=report.pass?0:1;Debug.Log("[HoRTStaticCopyProbe] END cloneWritable="+report.cloneWritable);
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally{if(output!=null)File.WriteAllText(output,JsonUtility.ToJson(report,true));if(source!=null)UnityEngine.Object.DestroyImmediate(source);if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);}
            EditorApplication.Exit(exit);
        }
        static ushort[] Read(Mesh mesh)
        {
            using var buffer=mesh.GetIndexBuffer();var request=AsyncGPUReadback.Request(buffer);request.WaitForCompletion();if(request.hasError)throw new Exception("Index readback failed");return request.GetData<ushort>().ToArray();
        }
    }
}
