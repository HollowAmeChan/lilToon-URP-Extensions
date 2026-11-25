using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using lilToon.URP.Extensions.RTGI;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class RTGIMaterialHistoryTests
    {
        [TestCase("_EmissionBlink", false)]
        [TestCase("_EmissionMap_ScrollRotate", true)]
        [TestCase("_EmissionBlendMask_ScrollRotate", true)]
        public void DormantEmissionAnimationDoesNotInvalidateStaticHistory(string animation, bool scroll)
        {
            var shader = Shader.Find("Hidden/HoRP/Tests/MaterialHistoryProbe");
            Assert.That(shader, Is.Not.Null, "Legacy animation policy uses a pipeline-owned property probe.");
            var material = new Material(shader);
            try
            {
                material.SetFloat("_UseEmission", 0);
                material.SetVector(animation, scroll ? new Vector4(1, 0, 0, 0) : new Vector4(1, 1, 0, 1));
                var type = typeof(HoRTGIRendererFeature).Assembly.GetType("lilToon.URP.Extensions.RTGI.HoRTGISecondaryMaterials", true);
                var method = type.GetMethod("Animated", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)method.Invoke(null, new object[] { material }), Is.False, "Disabled emission cannot animate the hit result.");
                material.SetFloat("_UseEmission", 1);
                Assert.That((bool)method.Invoke(null, new object[] { material }), Is.True, "Active emission animation must still invalidate history.");
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Category("MaterialPackageIntegration")]
        [TestCase("Cloth"), TestCase("Hair")]
        public void DedicatedStaticMaterialTracksColorAndTextureTransformChanges(string family)
        {
            var shader = Shader.Find("HoShader/" + family + " URP");
            Assert.That(shader, Is.Not.Null, "Install the dedicated material package in the material integration host.");
            var material = new Material(shader);
            try
            {
                string ns = "HoShader." + family + "URP.Editor";
                var gui = System.Type.GetType(ns + "." + family + "ShaderGUI, " + ns, true);
                var sync = gui.GetMethod(family == "Hair" ? "SyncSurfaceMode" : "SyncMaterial", BindingFlags.Public | BindingFlags.Static);
                Assert.That(sync, Is.Not.Null);
                sync.Invoke(null, new object[] { material });
                var type = typeof(HoRTGIRendererFeature).Assembly.GetType("lilToon.URP.Extensions.RTGI.HoRTGISecondaryMaterials", true);
                var animated = type.GetMethod("Animated", BindingFlags.Static | BindingFlags.NonPublic);
                var signature = type.GetMethod("MaterialSignature", BindingFlags.Static | BindingFlags.NonPublic);
                int before = (int)signature.Invoke(null, new object[] { material });
                Assert.That((bool)animated.Invoke(null, new object[] { material }), Is.False);
                material.SetColor("_BaseColor", Color.green);
                int colored = (int)signature.Invoke(null, new object[] { material });
                Assert.That(colored, Is.Not.EqualTo(before), "Authored color edits invalidate cached material state.");
                material.SetTextureScale("_BaseMap", new Vector2(2, 3));
                Assert.That((int)signature.Invoke(null, new object[] { material }), Is.Not.EqualTo(colored),
                    "Texture transforms participate even when the input uses its neutral texture.");
                Assert.That((bool)animated.Invoke(null, new object[] { material }), Is.False,
                    "Static family inputs do not acquire unsupported legacy animation controls.");
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
