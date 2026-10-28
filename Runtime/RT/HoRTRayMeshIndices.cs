using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RT
{
    internal static class HoRTRayMeshIndices
    {
        // Unity 6000.3 D3D12 mesh-instance AS construction can combine a
        // submesh's base-vertex address with the whole mesh vertex count.
        // Canonicalize only owned ray meshes: zero base and absolute indices
        // keep every declared vertex range inside the unchanged vertex buffer.
        internal static bool CanonicalizeOwned(Mesh mesh)
        {
            int count=mesh.subMeshCount;bool needed=false;
            for(int i=0;i<count;i++)needed|=mesh.GetSubMesh(i).baseVertex!=0;
            if(!needed)return false;
            if(!mesh.isReadable)throw new InvalidOperationException("Ray index canonicalization requires an owned readable snapshot");
            var descriptors=new SubMeshDescriptor[count];var values=new int[count][];int total=0,max=0;
            for(int i=0;i<count;i++)
            {
                descriptors[i]=mesh.GetSubMesh(i);values[i]=mesh.GetIndices(i,true);
                foreach(int index in values[i]){if(index<0||index>=mesh.vertexCount)throw new InvalidOperationException("Ray snapshot index outside vertex domain");max=Math.Max(max,index);}
                total=checked(total+values[i].Length);
            }
            bool narrow=mesh.indexFormat==IndexFormat.UInt16&&max<=ushort.MaxValue;
            var indices=new int[total];int cursor=0;
            for(int i=0;i<count;i++)
            {
                Array.Copy(values[i],0,indices,cursor,values[i].Length);
                var descriptor=descriptors[i];descriptor.indexStart=cursor;descriptor.indexCount=values[i].Length;descriptor.baseVertex=0;descriptor.firstVertex=0;descriptor.vertexCount=mesh.vertexCount;descriptors[i]=descriptor;cursor+=values[i].Length;
            }
            const MeshUpdateFlags flags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
            mesh.SetIndexBufferParams(total,narrow?IndexFormat.UInt16:IndexFormat.UInt32);
            if(narrow){var packed=new ushort[total];for(int i=0;i<total;i++)packed[i]=(ushort)indices[i];mesh.SetIndexBufferData(packed,0,0,total,flags);}
            else mesh.SetIndexBufferData(indices,0,0,total,flags);
            mesh.SetSubMeshes(descriptors,flags);return true;
        }
    }
}
