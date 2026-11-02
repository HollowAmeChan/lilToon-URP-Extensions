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
        static readonly MethodInfo GetReceiverFlags = IsLightCollectable.DeclaringType
            .GetMethod("GetReceiverFlags", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void CustomShadowLayersAreIndependentFromLightingLayers()
        {
            var owner = new GameObject("HoShadowCast layer test");
            try
            {
                var light = owner.AddComponent<Light>();
                light.renderingLayerMask = 1;
                var extra = owner.AddComponent<UniversalAdditionalLightData>();
                extra.renderingLayers = (RenderingLayerMask)1u;
                extra.customShadowLayers = true;
                extra.shadowRenderingLayers = (RenderingLayerMask)4u;
                var method = IsLightCollectable.DeclaringType.GetMethod("GetShadowCasterRenderingLayerMask",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.AreEqual(4u, (uint)method.Invoke(null, new object[] { light, null }));
                Assert.AreEqual(1u, (uint)extra.renderingLayers);
                Assert.AreEqual(4u, (uint)light.renderingLayerMask, "URP stores caster layers in the native light mask.");
                var layerMethod = IsLightCollectable.DeclaringType.GetMethod("IsLightRenderingLayerAllowed",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var configType = layerMethod.GetParameters()[1].ParameterType;
                var config = System.Activator.CreateInstance(configType);
                configType.GetField("lightRenderingLayerMask").SetValue(config, 1u);
                configType.GetField("casterRenderingLayerMask").SetValue(config, 4u);
                Assert.IsTrue((bool)layerMethod.Invoke(null, new[] { light, config }),
                    "Lighting and caster masks must pass independently.");
                extra.customShadowLayers = false;
                Assert.AreEqual(1u, (uint)method.Invoke(null, new object[] { light, null }));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void CasterFilteringPreservesPerLightCullingMaskAndEmptyMasks()
        {
            var assembly = IsLightCollectable.DeclaringType.Assembly;
            var sliceType = assembly.GetType("lilToon.URP.Extensions.ShadowCast.ShadowSliceInfo");
            var slice = System.Activator.CreateInstance(sliceType);
            sliceType.GetField("casterLayerMask").SetValue(slice, 1 << 7);
            sliceType.GetField("renderingLayerMask").SetValue(slice, 4u);
            var filter = assembly.GetType("lilToon.URP.Extensions.ShadowCast.HoShadowCastPass")
                .GetMethod("CreateShadowCasterFilteringSettings", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (FilteringSettings)filter.Invoke(null, new[] { null, slice });
            Assert.AreEqual(1 << 7, result.layerMask);
            Assert.AreEqual(4u, result.renderingLayerMask);
            sliceType.GetField("casterLayerMask").SetValue(slice, 0);
            sliceType.GetField("renderingLayerMask").SetValue(slice, 0u);
            result = (FilteringSettings)filter.Invoke(null, new[] { null, slice });
            Assert.AreEqual(0, result.layerMask);
            Assert.AreEqual(0u, result.renderingLayerMask);
        }

        [Test]
        public void MainPoolDeclarationPreservesHardSoftAndDoesNotGrantShadowEligibility()
        {
            var owner = new GameObject("HoShadowCast main pool test");
            try
            {
                var light = owner.AddComponent<Light>();
                light.type = LightType.Spot;
                light.shadows = LightShadows.Hard;
                var declaration = owner.AddComponent<HoShadowCastLight>();
                Assert.AreEqual(2f, Flags(light));
                light.shadows = LightShadows.Soft;
                Assert.AreEqual(3f, Flags(light));
                declaration.enabled = false;
                Assert.AreEqual(1f, Flags(light));
                declaration.enabled = true;
                light.shadows = LightShadows.None;
                Assert.IsFalse(IsEligible(light));
            }
            finally { Object.DestroyImmediate(owner); }
        }

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
        static float Flags(Light light) => (float)GetReceiverFlags.Invoke(null, new object[] { light });
    }
}
