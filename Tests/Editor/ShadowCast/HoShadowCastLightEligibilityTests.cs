using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.ShadowCast;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoShadowCastLightEligibilityTests
    {
        static readonly MethodInfo IsLightCollectable = typeof(HoShadowCastRendererFeature).Assembly
            .GetType("lilToon.URP.Extensions.ShadowCast.HoShadowCastFrameCollector")
            .GetMethod("IsLightCollectable", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void NoShadowLightIsExcludedWithoutChangingItsLightingState()
        {
            var owner = new GameObject("HoShadowCast no-shadow light test");
            try
            {
                var light = owner.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.intensity = 2.0f;
                light.color = Color.cyan;

                Assert.IsFalse(IsEligible(light));
                Assert.IsTrue(light.enabled);
                Assert.AreEqual(2.0f, light.intensity);
                Assert.AreEqual(Color.cyan, light.color);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ZeroStrengthAndBakedLightsAreExcluded()
        {
            var owner = new GameObject("HoShadowCast ineligible light test");
            try
            {
                var light = owner.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 0.0f;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                Assert.IsFalse(IsEligible(light));

                light.shadowStrength = 1.0f;
                light.lightmapBakeType = LightmapBakeType.Baked;
                Assert.IsFalse(IsEligible(light));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RealtimeAndMixedHardOrSoftLightsRemainEligible()
        {
            var owner = new GameObject("HoShadowCast eligible light test");
            try
            {
                var light = owner.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadowStrength = 1.0f;

                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.shadows = LightShadows.Hard;
                Assert.IsTrue(IsEligible(light));
                light.shadows = LightShadows.Soft;
                Assert.IsTrue(IsEligible(light));

                light.lightmapBakeType = LightmapBakeType.Mixed;
                light.shadows = LightShadows.Hard;
                Assert.IsTrue(IsEligible(light));
                light.shadows = LightShadows.Soft;
                Assert.IsTrue(IsEligible(light));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        static bool IsEligible(Light light) =>
            (bool)IsLightCollectable.Invoke(null, new object[] { light, null, light.type });
    }
}
