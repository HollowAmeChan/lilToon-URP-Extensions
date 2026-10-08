// Copyright (c) 2020-2026, NVIDIA CORPORATION. All rights reserved.
// Neighbor-offset generation is adapted from UnityPathTracing's RtxdiUtils.
// NVIDIA portions are governed by ThirdParty/RTXDI/LICENSE.txt, not the package MIT license.
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace lilToon.URP.Extensions.RTDI
{
    internal sealed class HoRTDIContext : IDisposable
    {
        internal GraphicsBuffer reservoirs,history,lights,neighbors,previousLights,currentToPrevious,previousToCurrent;
        internal RTHandle diffuse,specular;
        internal RTHandle powerPdf;
        internal GraphicsBuffer ris;
        internal const int RISTileSize=256,RISTileCount=64;
        internal int pdfSize;
        internal bool powerSampling;
        internal int width,height,rowPitch,arrayPitch,frame;
        internal uint sceneRevision;
        internal int settingsSignature;
        internal Light[] previousLightKeys=Array.Empty<Light>();
        private HoRTDIRendererFeature.AnalyticLight[] previousLightData=new HoRTDIRendererFeature.AnalyticLight[1];
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
            // Buffer growth does not invalidate the reservoirs: the previous
            // light snapshot is retained on the CPU and uploaded independently.
            EnsureLightBuffer(ref lights,lightCount,64,"HoRTDI analytic lights");
            EnsureLightBuffer(ref previousLights,previousLightKeys.Length,64,"HoRTDI previous analytic lights");
            EnsureLightBuffer(ref currentToPrevious,lightCount,4,"HoRTDI current to previous light indices");
            EnsureLightBuffer(ref previousToCurrent,previousLightKeys.Length,4,"HoRTDI previous to current light indices");
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
        private static void EnsureLightBuffer(ref GraphicsBuffer buffer,int count,int stride,string name)
        {
            if(buffer!=null&&buffer.count>=Mathf.Max(1,count))return;
            buffer?.Dispose();buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.NextPowerOfTwo(Mathf.Max(1,count)),stride){name=name};
        }
        internal void PreparePower(int localCount,bool requested)
        {
            powerSampling=requested&&localCount>0&&SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat,GraphicsFormatUsage.LoadStore)
                &&SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat,GraphicsFormatUsage.Linear);
            int risCount=powerSampling?RISTileSize*RISTileCount:1;
            if(ris==null||ris.count!=risCount)
            {ris?.Dispose();ris=new GraphicsBuffer(GraphicsBuffer.Target.Structured,risCount,8){name="HoRTDI SDK RIS tiles"};}
            if(!powerSampling){powerPdf?.Release();powerPdf=null;pdfSize=0;return;}
            // Square power-of-two Morton domain, with a minimum 2x2 root.
            pdfSize=2;while((long)pdfSize*pdfSize<localCount)pdfSize*=2;
            if(pdfSize>16384)throw new InvalidOperationException("RTXDI PDF mip domain exceeds 16k");
            var desc=new RenderTextureDescriptor(pdfSize,pdfSize)
            {graphicsFormat=GraphicsFormat.R32_SFloat,depthStencilFormat=GraphicsFormat.None,msaaSamples=1,enableRandomWrite=true,useMipMap=true,autoGenerateMips=false};
            RenderingUtils.ReAllocateIfNeeded(ref powerPdf,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"HoRTDI local light power PDF");
        }
        private sealed class LightReferences:IEqualityComparer<Light>
        {
            internal static readonly LightReferences Instance=new();
            public bool Equals(Light a,Light b)=>ReferenceEquals(a,b);
            public int GetHashCode(Light value)=>RuntimeHelpers.GetHashCode(value);
        }
        internal sealed class LightFrame
        {
            internal readonly HoRTDIRendererFeature.AnalyticLight[] current,previous;
            internal readonly Light[] keys;
            internal readonly int[] currentToPrevious,previousToCurrent;
            internal readonly int previousCount;
            internal LightFrame(HoRTDIRendererFeature.AnalyticLight[] current,Light[] keys,
                HoRTDIRendererFeature.AnalyticLight[] previous,Light[] previousKeys)
            {
                this.current=current;this.keys=keys;this.previous=previous;previousCount=previousKeys.Length;
                currentToPrevious=new int[Mathf.Max(1,keys.Length)];previousToCurrent=new int[Mathf.Max(1,previousCount)];
                Array.Fill(currentToPrevious,-1);Array.Fill(previousToCurrent,-1);
                var oldIndices=new Dictionary<Light,int>(LightReferences.Instance);
                for(int i=0;i<previousCount;i++)oldIndices.Add(previousKeys[i],i);
                for(int i=0;i<keys.Length;i++)if(oldIndices.TryGetValue(keys[i],out int old))
                {currentToPrevious[i]=old;previousToCurrent[old]=i;}
            }
        }
        internal LightFrame CaptureLights(HoRTDIRendererFeature.AnalyticLight[] current,Light[] keys)
            =>new LightFrame(current,keys,previousLightData,previousLightKeys);
        internal void Complete(Matrix4x4 view,Matrix4x4 projection,Camera camera,uint revision,int signature,LightFrame lightFrame)
        {
            previousVP=GL.GetGPUProjectionMatrix(projection,true)*view;previousInverseVP=previousVP.inverse;
            previousCamera=view.inverse.GetColumn(3);previousTowardsEye=view.GetRow(2);previousTowardsEye.w=0;previousViewZ=view.GetRow(2);
            previousClip=new Vector4(camera.nearClipPlane,camera.farClipPlane,camera.orthographic?1:0,SystemInfo.usesReversedZBuffer?1:0);
            sceneRevision=revision;settingsSignature=signature;historyValid=true;frame++;
            previousLightKeys=lightFrame.keys;previousLightData=lightFrame.current;
        }
        public void Dispose()
        {reservoirs?.Dispose();history?.Dispose();lights?.Dispose();neighbors?.Dispose();previousLights?.Dispose();currentToPrevious?.Dispose();previousToCurrent?.Dispose();ris?.Dispose();powerPdf?.Release();ris=null;powerPdf=null;diffuse?.Release();specular?.Release();reservoirs=history=lights=neighbors=previousLights=currentToPrevious=previousToCurrent=null;diffuse=specular=null;previousLightKeys=Array.Empty<Light>();previousLightData=null;}
    }
}
