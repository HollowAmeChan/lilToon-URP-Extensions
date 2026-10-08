// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// Neighbor-offset generation is adapted from UnityPathTracing's RtxdiUtils.
// NVIDIA portions are governed by ThirdParty/RTXDI/LICENSE.txt, not the package MIT license.
#pragma warning disable CS0618
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI
{
    internal sealed class HoRTDIContext : IDisposable
    {
        internal GraphicsBuffer reservoirs,history,lights,neighbors;
        internal RTHandle diffuse,specular;
        internal int width,height,rowPitch,arrayPitch,frame;
        internal uint sceneRevision;
        internal int lightSignature;
        internal bool historyValid,lastTemporal;
        internal Matrix4x4 previousVP=Matrix4x4.identity,previousInverseVP=Matrix4x4.identity;
        internal Vector4 previousCamera,previousTowardsEye,previousViewZ,previousClip;
        internal void Prepare(int w,int h,int lightCount,GraphicsFormat format)
        {
            if(width!=w||height!=h)
            {
                reservoirs?.Dispose();history?.Dispose(); width=w;height=h;
                // Reference RTXDI block-linear layout / three-buffer DI schedule.
                rowPitch=((w+15)/16)*256;arrayPitch=rowPitch*((h+15)/16);
                reservoirs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,arrayPitch*3,24){name="HoRTDI SDK reservoirs"};
                history=new GraphicsBuffer(GraphicsBuffer.Target.Structured,w*h,16){name="HoRTDI previous surface guides"};
                historyValid=false;frame=0;
            }
            if(lights==null||lights.count<Mathf.Max(1,lightCount))
            {lights?.Dispose();lights=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.NextPowerOfTwo(Mathf.Max(1,lightCount)),64){name="HoRTDI analytic lights"};historyValid=false;}
            if(neighbors==null)
            {
                neighbors=new GraphicsBuffer(GraphicsBuffer.Target.Structured,256,8){name="HoRTDI reference neighbor offsets"};
                // Adapted from the reference RtxdiUtils.FillNeighborOffsetBuffer, decoded from SNORM8.
                var offsets=new Vector2[256];float u=.5f,v=.5f;const float phi=1.0f/1.3247179572447f;int index=0;
                while(index<offsets.Length)
                {
                    u+=phi;v+=phi*phi;if(u>=1)u-=1;if(v>=1)v-=1;
                    if((u-.5f)*(u-.5f)+(v-.5f)*(v-.5f)>.25f)continue;
                    offsets[index++]=new Vector2((sbyte)((u-.5f)*250)/127f,(sbyte)((v-.5f)*250)/127f);
                }
                neighbors.SetData(offsets);
            }
            var desc=new RenderTextureDescriptor(w,h){graphicsFormat=format,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,enableRandomWrite=true};
            RenderingUtils.ReAllocateIfNeeded(ref diffuse,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTDI Diffuse");
            RenderingUtils.ReAllocateIfNeeded(ref specular,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTDI Specular");
        }
        internal void Complete(Matrix4x4 view,Matrix4x4 projection,Camera camera,uint revision,int signature)
        {
            previousVP=GL.GetGPUProjectionMatrix(projection,true)*view;previousInverseVP=previousVP.inverse;
            previousCamera=view.inverse.GetColumn(3);previousTowardsEye=view.GetRow(2);previousTowardsEye.w=0;previousViewZ=view.GetRow(2);
            previousClip=new Vector4(camera.nearClipPlane,camera.farClipPlane,camera.orthographic?1:0,SystemInfo.usesReversedZBuffer?1:0);
            sceneRevision=revision;lightSignature=signature;historyValid=true;frame++;
        }
        public void Dispose()
        {reservoirs?.Dispose();history?.Dispose();lights?.Dispose();neighbors?.Dispose();diffuse?.Release();specular?.Release();reservoirs=history=lights=neighbors=null;diffuse=specular=null;}
    }
}
