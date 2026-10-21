using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.RayTracing
{
    public static partial class HoRTSceneValidation
    {
        private static bool forceSkinned;
        private static bool forceSkinnedBaseVertex;
        private static readonly List<string> baseVertexChecks=new();
        public static void RunSkinnedBaseVertex(){forceSkinnedBaseVertex=true;RunSkinned();}
        public static void RunSkinnedBaseVertexCompatibility(){compatibility=true;RunSkinnedBaseVertex();}
        private static Transform bone0, bone1;
        private static SkinnedMeshRenderer skin;
        public static void RunSkinned() { forceSkinned = true; Run(); }
        public static void RunSkinnedCompatibility() { compatibility = true; RunSkinned(); }

        private static void SetupSkinned(GameObject root)
        {
            var materials = multi.sharedMaterials;
            if(forceSkinnedBaseVertex)mesh.SetTriangles(new[]{0,2,1,2,3,1},1,false,4);
            Object.DestroyImmediate(multi); Object.DestroyImmediate(root.GetComponent<MeshFilter>());
            bone0 = Keep(new GameObject("RT bone zero")).transform; bone0.SetParent(root.transform, false);
            bone1 = Keep(new GameObject("RT bone one")).transform; bone1.SetParent(root.transform, false);
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            var weights = new BoneWeight[8];
            for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = i < 4 ? 0 : 1, weight0 = 1 };
            mesh.boneWeights = weights;
            var deltas = new Vector3[8]; for (int i = 0; i < deltas.Length; ++i) deltas[i] = new Vector3(0, 0, i < 4 ? 2 : -1);
            mesh.AddBlendShapeFrame("RT displacement", 100, deltas, new Vector3[8], new Vector3[8]);
            skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = new[] { bone0, bone1 };
            skin.rootBone = bone0; skin.sharedMaterials = materials; skin.updateWhenOffscreen = false;
            multi = skin;
        }

        private static void ValidateSkinned()
        {
            var image = Render(); Hit(image, 0, 3, skin, 0); Hit(image, 1, 3, skin, 1);
            Require(scene.SkinnedMeshCount == 1 && scene.SkinnedBakeCount == 1, "one owned pose for both submeshes");
            if(forceSkinnedBaseVertex)
            {
                var canonical=ResolveMesh((uint)Pixel(image,1,0).g);
                Require(mesh.GetBaseVertex(1)==4&&mesh.GetIndices(1,false).Max()==3,"original base-vertex source indices untouched");
                Require(canonical.GetBaseVertex(0)==0&&canonical.GetBaseVertex(1)==0,"owned ray snapshot bases canonical");
                Require(canonical.GetIndices(1,false).SequenceEqual(new[]{4,6,5,6,7,5}),"owned ray snapshot preserves absolute triangle vertices");
            }
            uint builds = scene.BuildCount, revision = scene.Revision, structure = scene.StructureRevision;
            scene.Synchronize(new[] { outside, skin, skin }, 1); Render();
            Require(scene.SkinnedBakeCount == 1 && scene.BuildCount == builds && scene.Revision == revision, "stable/offscreen skin must reuse pose/BLAS");
            uint identity = (uint)Pixel(image, 0, 0).g;
            bone0.localPosition = Vector3.forward * .25f; bone1.localPosition = Vector3.forward * .75f;
            scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 3.25f, skin, 0); Hit(image, 1, 3.75f, skin, 1);
            Require(scene.SkinnedBakeCount == 2 && scene.BuildCount == builds + 1 && scene.Revision != revision, "bone pose refresh");
            Require(scene.StructureRevision == structure, "bone pose must keep structure history identity");
            Require((uint)Pixel(image, 0, 0).g == identity, "deformation preserves primitive identity");
            skin.SetBlendShapeWeight(0, 50); scene.Synchronize(sources, 1); image = Render();
            Hit(image, 0, 4.25f, skin, 0); Hit(image, 1, 3.25f, skin, 1);
            skin.SetBlendShapeWeight(0, 0);
            skin.transform.localScale = new Vector3(2, 3, 4);
            rays[0] = Ray(.2f, -3, 1); rays[1] = Ray(4.2f, -3, 1); raysBuffer.SetData(rays);
            scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 4, skin, 0); Hit(image, 1, 6, skin, 1);
            skin.transform.localScale = new Vector3(-2, 3, -4);
            rays[0] = Ray(-.2f, -3, 1); rays[1] = Ray(-4.2f, -4, 1); raysBuffer.SetData(rays);
            scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 2, skin, 0); Hit(image, 1, 1, skin, 1);
            skin.transform.localScale = Vector3.one; rays[0] = Ray(.1f, -3, 1); rays[1] = Ray(2.1f, -3, 1); raysBuffer.SetData(rays);
            var stale = scene.ReadCompatibility(); bone0.localPosition = Vector3.forward * .5f; scene.Synchronize(sources, 1);
            using (var cmd = new CommandBuffer())
            {
                bool rejected = false; try { stale.BuildAndBind(cmd, probe.shader, 0); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "pose mutation after input capture rejected");
            }
            Hit(Render(), 0, 3.5f, skin, 0);
            var oldSnapshot = ResolveMesh(identity); skin.enabled = false; scene.Synchronize(sources, 1);
            Status(Render(), 0, 0, "disabled skin removed"); Require(scene.SkinnedMeshCount == 0 && oldSnapshot == null, "disabled skin snapshot released");
            skin.enabled = true; scene.Synchronize(sources, 1); image = Render(); Hit(image, 0, 3.5f, skin, 0);
            var replacement = Keep(Object.Instantiate(mesh));
            var mixedWeights = replacement.boneWeights;
            for (int i = 0; i < 4; ++i) mixedWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = .25f, boneIndex1 = 1, weight1 = .75f };
            replacement.boneWeights = mixedWeights; skin.sharedMesh = replacement;structure=scene.StructureRevision;
            bone0.localPosition = Vector3.forward; scene.Synchronize(sources, 1); Hit(Render(), 0, 3.8125f, skin, 0);
            Require(scene.StructureRevision != structure, "same-topology source mesh replacement rejects structure history");
            var snapshot = ResolveMesh((uint)Pixel(Render(), 0, 0).g); scene.MarkGeometryDirty();
            Require(snapshot == null && scene.SkinnedMeshCount == 0, "explicit geometry edit releases owned snapshot");
            var vertices = replacement.vertices; for (int i = 0; i < vertices.Length; ++i) vertices[i].z += .125f;
            replacement.vertices = vertices; replacement.UploadMeshData(true);
            scene.Synchronize(sources, 1); Hit(Render(), 0, 3.9375f, skin, 0);
            skin.bones = new[] { (Transform)null, bone1 }; scene.Synchronize(sources, 1);
            Status(Render(), 0, 0, "invalid bone removes stale caster"); Require(scene.SkinnedMeshCount == 0, "invalid pose releases snapshot");
            skin.bones = new[] { bone0, bone1 }; scene.Synchronize(sources, 1); Hit(Render(), 0, 3.9375f, skin, 0);
            Object.DestroyImmediate(skin.gameObject); scene.Synchronize(sources, 1);
            Status(Render(), 0, 0, "destroyed skin removed"); Require(scene.SkinnedMeshCount == 0, "destroyed skin snapshot released");
            foreach (var message in UnityEditor.ShaderUtil.GetComputeShaderMessages(probe.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error
                    && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
            Debug.Log("[HoRTSceneValidation] PASS skinned world visibility: bones/blend-shapes, offscreen, submesh IDs, stable cache, nonuniform/mirrored scale, lifecycle and stale capture; compatibility=" + compatibility);
        }

        [Serializable]private sealed class BaseVertexReport{public bool pass,compatibility;public string error;public string[] checks;}
        private static void SaveBaseVertexReport(Exception error)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../ho-rt-skinned-basevertex-"+(compatibility?"compat":"rg")));Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(new BaseVertexReport{pass=error==null,compatibility=compatibility,error=error?.ToString(),checks=baseVertexChecks.ToArray()},true));
        }
        private static Mesh ResolveMesh(uint identity)
        { Require(scene.TryResolve(identity, out var instance), "missing skin identity"); return instance.mesh; }
    }
}
