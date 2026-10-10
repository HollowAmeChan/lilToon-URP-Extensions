using UnityEngine;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static void SetupSkinBlocker()
        {
            var rigid = blocker.GetComponent<MeshRenderer>(); var material = rigid.sharedMaterial;
            var mesh = Keep(Object.Instantiate(blocker.GetComponent<MeshFilter>().sharedMesh));
            Object.DestroyImmediate(rigid); Object.DestroyImmediate(blocker.GetComponent<MeshFilter>());
            skinBlockerBone = Keep(new GameObject("DI caster bone")).transform;
            skinBlockerBone.SetParent(blocker.transform, false);
            mesh.bindposes = new[] { Matrix4x4.identity }; var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            mesh.boneWeights = weights;
            var skin = blocker.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.sharedMaterial = material;
            skin.bones = new[] { skinBlockerBone }; skin.rootBone = skinBlockerBone;
        }
    }
}
