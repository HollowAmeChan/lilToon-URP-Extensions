using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace lilToon.URP.Extensions.RayTracing
{
    public static partial class HoRTSceneValidation
    {
        static bool staticControlReport;public static void RunStaticControl(){staticControlReport=true;Run();}
        static bool forceStaticBaseVertex;static readonly List<string> staticBaseChecks=new();
        static void ValidateStaticCopyCache(Color[] image)
        {
            uint id=(uint)Pixel(image,1,0).g;Require(scene.TryResolve(id,out var instance),"static mirror identity resolves");var copy=instance.mesh;
            Require(copy!=mesh&&mesh.GetBaseVertex(1)==4&&copy.GetBaseVertex(1)==0,"static canonical copy preserves original source base");
            Require(copy.GetIndices(1,false).SequenceEqual(new[]{4,6,5,6,7,5}),"static canonical copy preserves absolute triangle tuple");
            Require(scene.StaticMeshCount==1&&scene.StaticMeshCopyCount==1&&scene.StaticIndexReadbackCount==0,"readable static source creates one copy without GPU readback");
            uint copies=scene.StaticMeshCopyCount,builds=scene.BuildCount,revision=scene.Revision;
            scene.Synchronize(sources,1);Render();Require(scene.StaticMeshCopyCount==copies&&scene.BuildCount==builds&&scene.Revision==revision,"stable static frame reuses copy and AS");
            var other=Keep(new GameObject("Shared static mesh registration"));other.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=other.AddComponent<MeshRenderer>();renderer.sharedMaterials=multi.sharedMaterials;other.transform.position=Vector3.right*30;
            scene.Synchronize(new[]{multi,outside,renderer},1);Render();Require(scene.StaticMeshCount==1&&scene.StaticMeshCopyCount==copies,"two renderers share one source mirror");
            scene.Synchronize(sources,1);Render();Require(scene.StaticMeshCount==1&&scene.StaticMeshCopyCount==copies,"removing one consumer retains shared mirror");
        }
        [Serializable]sealed class StaticBaseReport{public bool pass,compatibility;public string error;public uint copies,readbacks;public string[] checks;}
        static void SaveStaticBaseReport(Exception error)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-static-basevertex-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(new StaticBaseReport{pass=error==null,compatibility=compatibility,error=error?.ToString(),copies=scene?.StaticMeshCopyCount??0,readbacks=scene?.StaticIndexReadbackCount??0,checks=staticBaseChecks.ToArray()},true));
        }
    }
}
