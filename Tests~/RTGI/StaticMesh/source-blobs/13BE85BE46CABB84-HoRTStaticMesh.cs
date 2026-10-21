using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RayTracing
{
    // Owned immutable geometry copy, shared by registrations of the same source
    // inside one scene service. Source edits follow MarkGeometryDirty's contract.
    internal sealed class HoRTStaticMesh:IDisposable
    {
        internal readonly Mesh source,mesh;
        internal readonly bool usedGpuReadback;
        internal static bool NeedsCanonical(Mesh source)
        {for(int i=0;i<source.subMeshCount;i++)if(source.GetBaseVertex(i)!=0)return true;return false;}
        internal HoRTStaticMesh(Mesh source)
        {
            this.source=source;mesh=UnityEngine.Object.Instantiate(source);mesh.name="Ho RT canonical static: "+source.name;mesh.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                if(source.isReadable){HoRTRayMeshIndices.CanonicalizeOwned(mesh);return;}
                // Read only the index allocation. Do not change source buffer
                // targets, Read/Write flags, vertex streams or raster ownership.
                using var buffer=source.GetIndexBuffer();if(buffer==null)throw new InvalidOperationException("Non-readable ray source has no GPU index allocation");
                var request=AsyncGPUReadback.Request(buffer);request.WaitForCompletion();if(request.hasError)throw new InvalidOperationException("Non-readable ray index readback failed");
                usedGpuReadback=true;
                int count=source.subMeshCount;var descriptors=new SubMeshDescriptor[count];var indices=new int[count][];
                if(source.indexFormat==IndexFormat.UInt16)
                {
                    var data=request.GetData<ushort>();
                    for(int i=0;i<count;i++){descriptors[i]=source.GetSubMesh(i);var d=descriptors[i];if(d.indexStart<0||d.indexCount<0||(long)d.indexStart+d.indexCount>data.Length)throw new InvalidOperationException("Source submesh outside GPU index allocation");indices[i]=new int[d.indexCount];for(int j=0;j<d.indexCount;j++)indices[i][j]=checked(data[d.indexStart+j]+d.baseVertex);}
                }
                else
                {
                    var data=request.GetData<uint>();
                    for(int i=0;i<count;i++){descriptors[i]=source.GetSubMesh(i);var d=descriptors[i];if(d.indexStart<0||d.indexCount<0||(long)d.indexStart+d.indexCount>data.Length)throw new InvalidOperationException("Source submesh outside GPU index allocation");indices[i]=new int[d.indexCount];for(int j=0;j<d.indexCount;j++)indices[i][j]=checked((int)data[d.indexStart+j]+d.baseVertex);}
                }
                HoRTRayMeshIndices.WriteAbsoluteOwned(mesh,descriptors,indices);
            }
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
        public void Dispose()=>UnityEngine.Object.DestroyImmediate(mesh);
    }
}
