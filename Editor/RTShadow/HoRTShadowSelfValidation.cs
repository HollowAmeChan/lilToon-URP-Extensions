#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using lilToon.URP.Extensions.RTShadow;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.RTShadow
{
    public static partial class HoRTShadowValidation
    {
        private static bool selfAudit;
        [Serializable] private sealed class SelfReport
        { public bool pass, compatibility; public int validPixels, falseShadowPixels, stableCompared, jitterFlips, contactBlocked, skinnedValid, skinnedFalse; public float nearestSelfDistance=65504; public List<string> evidence=new(); }
        public static void RunSelf() { selfAudit = true; nprIntegration = true; rawOnly = true; Run(); }
        public static void RunSelfCompatibility() { compatibility = true; RunSelf(); }
        private static void ValidateSelfVisibility()
        {
            var result = new SelfReport { compatibility = compatibility };
            near.shadowCastingMode = far.shadowCastingMode = ShadowCastingMode.Off;
            var members = new List<Renderer>(); group.GetAssignedRenderers(members);
            var body = members.Find(item => item.sharedMaterial == toon);
            body.shadowCastingMode = ShadowCastingMode.TwoSided;
            body.transform.position = new Vector3(0,0,.137f);
            body.transform.rotation = Quaternion.Euler(0,23,0);
            Vector3 normal = body.transform.TransformDirection(Vector3.back), tangent = body.transform.right;
            var direction = (tangent + normal*.002f).normalized;
            light.transform.rotation = Quaternion.LookRotation(-direction);
            shadow.Settings.angularDiameter = 0; shadow.Settings.rayBias = .0001f;
            target = Target(128);
            Color[] previous = null;
            foreach (bool perspective in new[] { false, true })
            {
                camera.orthographic = !perspective; camera.fieldOfView = 60; camera.nearClipPlane = .1f; camera.farClipPlane = 30;
                for (int frame = 0; frame < 8; frame++)
                {
                    camera.ResetProjectionMatrix(); var projection = camera.projectionMatrix;
                    float x = ((frame*5)%8+.5f)/8-.5f, y = ((frame*3)%8+.5f)/8-.5f;
                    if (perspective) { projection.m02 += x*2/128; projection.m12 += y*2/128; }
                    else { projection.m03 += x*2/128; projection.m13 += y*2/128; }
                    camera.projectionMatrix = projection;
                    var raw = Render();
                    for (int py = 12; py < 116; py++) for (int px = 12; px < 116; px++)
                    {
                        int i = py*128+px; var p = raw[i];
                        if (p.b != 1 || p.a != bodyId) continue;
                        result.validPixels++; if (p.r < .5f) { result.falseShadowPixels++; result.nearestSelfDistance = Mathf.Min(result.nearestSelfDistance,p.g); }
                        if (previous != null && previous[i].b == 1 && previous[i].a == bodyId)
                        { result.stableCompared++; if (p.r != previous[i].r) result.jitterFlips++; }
                    }
                    if (frame == 0) File.WriteAllBytes(Path.Combine(Folder(), "self-"+(perspective?"perspective":"ortho")+".rgba32"), Bytes(raw));
                    previous = raw;
                }
                previous = null;
            }
            // A nearby parallel opaque card must still cast a real shadow.
            camera.ResetProjectionMatrix();camera.orthographic=true;
            var contact = Quad(body.transform.position + normal*.002f, new Vector3(3,3,1), true);
            contact.transform.rotation = body.transform.rotation;
            contact.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            light.transform.rotation = Quaternion.LookRotation(-normal);
            var blocked = Render(); foreach(var p in blocked) if(p.b==1 && p.a==bodyId && p.r==0 && p.g>.0005f && p.g<.004f) result.contactBlocked++;
            contact.enabled = false;
            var go = body.gameObject;
            var source = go.GetComponent<MeshFilter>(); var mesh = Keep(Object.Instantiate(source.sharedMesh));
            Object.DestroyImmediate(body); Object.DestroyImmediate(source);
            var bone = Keep(new GameObject("Self-caster bone")).transform;bone.SetParent(go.transform,false);
            mesh.bindposes = new[] { Matrix4x4.identity };var weights = new BoneWeight[mesh.vertexCount];
            for(int i=0;i<weights.Length;i++)weights[i]=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=weights;
            var skin = go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=toon;skin.bones=new[]{bone};skin.rootBone=bone;skin.localBounds=mesh.bounds;skin.shadowCastingMode=ShadowCastingMode.TwoSided;skin.updateWhenOffscreen=true;
            foreach(var part in group.parts)if(part.name=="body")part.renderers=new Object[]{skin};group.Apply();
            light.transform.rotation=Quaternion.LookRotation(-direction);
            for(int pose=0;pose<4;pose++)
            {
                bone.localPosition=new Vector3(.011f*pose,0,.0013f*pose);var raw=Render();
                foreach(var p in raw)if(p.b==1&&p.a==bodyId){result.skinnedValid++;if(p.r!=1)result.skinnedFalse++;}
            }
            result.evidence.Add("Self-shadowing plane is analytically unoccluded on its outgoing hemisphere; two-sided source remains in AS");
            result.evidence.Add("Sixteen current jittered surface requests exercise perspective and orthographic raster depth reconstruction");
            result.evidence.Add("Nearby separate surface at 2mm must remain a real occluder");
            result.evidence.Add("Four moving bone poses keep the self-caster raster and shared RT skinned snapshot aligned");
            result.pass = result.validPixels>10000 && result.falseShadowPixels==0 && result.jitterFlips==0 && result.contactBlocked>100 && result.skinnedValid>1000 && result.skinnedFalse==0;
            File.WriteAllText(Path.Combine(Folder(), "self-report.json"), JsonUtility.ToJson(result,true));
            Check(result.pass, "Self Cast false="+result.falseShadowPixels+" / "+result.validPixels+" jitterFlips="+result.jitterFlips+" nearest="+result.nearestSelfDistance+" contact="+result.contactBlocked+" skinned="+result.skinnedFalse+" / "+result.skinnedValid);
            Debug.Log("[HoRTShadowSelf] PASS grazing self-caster / camera jitter / perspective and ortho / nearby independent occluder");
        }
    }
}
