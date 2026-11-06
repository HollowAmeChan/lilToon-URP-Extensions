using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using lilToon.URP.Extensions.RTGI;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class RTGIMaterialHistoryTests
    {
        [TestCase("_UseEmission", "_EmissionBlink", false)]
        [TestCase("_UseEmission2nd", "_Emission2ndBlink", false)]
        [TestCase("_UseEmission", "_EmissionMap_ScrollRotate", true)]
        [TestCase("_UseEmission2nd", "_Emission2ndBlendMask_ScrollRotate", true)]
        public void DormantEmissionAnimationDoesNotInvalidateStaticHistory(string enable, string animation, bool scroll)
        {
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                material.SetFloat("_UseEmission", 0); material.SetFloat("_UseEmission2nd", 0);
                material.SetVector(animation, scroll ? new Vector4(1, 0, 0, 0) : new Vector4(1, 1, 0, 1));
                var type = typeof(HoRTGIRendererFeature).Assembly.GetType("lilToon.URP.Extensions.RTGI.HoRTGISecondaryMaterials", true);
                var method = type.GetMethod("Animated", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)method.Invoke(null, new object[] { material }), Is.False, "Disabled emission cannot animate the hit result.");
                material.SetFloat(enable, 1);
                Assert.That((bool)method.Invoke(null, new object[] { material }), Is.True, "Active emission animation must still invalidate history.");
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
