#pragma warning disable CS0618
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.RT;
using lilToon.URP.Extensions.RTGI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    // Descriptor payload is reproducible allocation accounting, not physical VRAM residency.
    internal static class HoRTGIResourceAudit
    {
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        [Serializable]internal sealed class Row
        {
            public string binding,name,kind,format;
            public int width,height,volumeDepth,mips,samples,count,stride,instanceId;
            public long payloadBytes,unityRuntimeBytes=-1;
        }
        [Serializable]internal sealed class Report
        {
            public string accounting="Live owned descriptor payload; excludes driver allocation/residency, GB/SB, imported assets and AS scratch. Unity runtime statistics are separate and must not be summed as VRAM.";
            public string asApi,asError;
            public long giPayloadBytes,nrdPayloadBytes,sceneMappingPayloadBytes,totalPayloadBytes,asApiBytes=-1,poseUnityRuntimeBytes;
            public int width,height,reservoirArrayPitch;
            public Row[] resources,poseMeshes;
            public string[] aliases;
        }
        internal static Report Capture(HoRTGIContext context,HoRTScene scene)
        {
            var report=new Report{width=context.width,height=context.height,reservoirArrayPitch=context.arrayPitch};
            var rows=new List<Row>();var aliases=new List<string>();var buffers=new HashSet<GraphicsBuffer>();var textures=new HashSet<int>();
            Scan(context,"GI.");Scan(context.nrd,"NRD.");if(context.nrd!=null)Scan(context.nrd.core,"NRD.Core.");
            Add("Scene.instanceBuffer",Read(scene,"instanceBuffer"));
            // PublishedDiffuse aliases NRD.filtered (or GI.diffuse); count the underlying texture once.
            Add("GI.PublishedDiffuse",context.PublishedDiffuse);
            var acceleration=Read(scene,"acceleration");
            var getSize=acceleration?.GetType().GetMethod("GetSize",Type.EmptyTypes);
            report.asApi="RayTracingAccelerationStructure.GetSize";
            if(getSize==null)report.asError="API unavailable";
            else try{report.asApiBytes=checked((long)Convert.ToUInt64(getSize.Invoke(acceleration,null)));}catch(Exception e){report.asError=e.ToString();}
            var poses=new List<Row>();var poseIds=new HashSet<int>();
            foreach(DictionaryEntry pair in (IDictionary)Read(scene,"skinnedMeshes"))
            {
                var mesh=(Mesh)Read(pair.Value,"mesh");if(mesh==null||!poseIds.Add(mesh.GetInstanceID()))continue;
                long memory=Profiler.GetRuntimeMemorySizeLong(mesh);poses.Add(new Row{binding="Scene.poseMesh",name=mesh.name,kind="Unity Mesh runtime statistic (CPU/GPU unspecified)",instanceId=mesh.GetInstanceID(),count=mesh.vertexCount,unityRuntimeBytes=memory});report.poseUnityRuntimeBytes+=memory;
            }
            report.giPayloadBytes=rows.Where(x=>x.binding.StartsWith("GI.")).Sum(x=>x.payloadBytes);
            report.nrdPayloadBytes=rows.Where(x=>x.binding.StartsWith("NRD.")).Sum(x=>x.payloadBytes);
            report.sceneMappingPayloadBytes=rows.Where(x=>x.binding.StartsWith("Scene.")).Sum(x=>x.payloadBytes);
            report.totalPayloadBytes=rows.Sum(x=>x.payloadBytes);report.resources=rows.ToArray();report.aliases=aliases.ToArray();report.poseMeshes=poses.ToArray();return report;

            void Scan(object owner,string prefix)
            {
                if(owner==null)return;
                foreach(var field in owner.GetType().GetFields(Fields))
                {
                    var value=field.GetValue(owner);if(value is GraphicsBuffer||value is RTHandle)Add(prefix+field.Name,value);
                    else if(value is GraphicsBuffer[]||value is RTHandle[]){int i=0;foreach(var resource in (IEnumerable)value)Add(prefix+field.Name+"["+i+++"]",resource);}
                }
            }
            void Add(string binding,object resource)
            {
                if(resource is GraphicsBuffer buffer)
                {
                    if(!buffers.Add(buffer)){aliases.Add(binding);return;}
                    if(!buffer.IsValid())throw new Exception("Invalid live buffer "+binding);
                    // Unity 6000.3 exposes GraphicsBuffer.name as setter-only; identify it by its owner field.
                    rows.Add(new Row{binding=binding,name=binding,kind="GraphicsBuffer",count=buffer.count,stride=buffer.stride,payloadBytes=checked((long)buffer.count*buffer.stride)});
                }
                else if(resource is RTHandle handle)
                {
                    var texture=handle.rt;if(texture==null||!texture.IsCreated())throw new Exception("Missing live texture "+binding);
                    if(!textures.Add(texture.GetInstanceID())){aliases.Add(binding);return;}
                    if(texture.dimension!=TextureDimension.Tex2D||texture.depthStencilFormat!=GraphicsFormat.None)throw new Exception("Unsupported audit texture descriptor "+binding);
                    int blockW=(int)GraphicsFormatUtility.GetBlockWidth(texture.graphicsFormat),blockH=(int)GraphicsFormatUtility.GetBlockHeight(texture.graphicsFormat),blockBytes=(int)GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat);
                    long bytes=0;for(int mip=0;mip<texture.mipmapCount;mip++){int w=Math.Max(1,texture.width>>mip),h=Math.Max(1,texture.height>>mip);bytes+=checked((long)((w+blockW-1)/blockW)*((h+blockH-1)/blockH)*blockBytes*texture.volumeDepth*texture.antiAliasing);}
                    rows.Add(new Row{binding=binding,name=texture.name,kind="RenderTexture",format=texture.graphicsFormat.ToString(),instanceId=texture.GetInstanceID(),width=texture.width,height=texture.height,volumeDepth=texture.volumeDepth,mips=texture.mipmapCount,samples=texture.antiAliasing,payloadBytes=bytes,unityRuntimeBytes=Profiler.GetRuntimeMemorySizeLong(texture)});
                }
            }
        }
        static object Read(object owner,string name)=>owner.GetType().GetField(name,Fields)?.GetValue(owner)??throw new Exception("Missing audit field "+owner.GetType().Name+"."+name);
    }
}
