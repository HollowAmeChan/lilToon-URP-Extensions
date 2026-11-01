using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.PostProcessing;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoPostProcessingOwnershipTests
    {
        [Test]
        public void DisabledLayersDoNotResurrectNativeEffects_AndLegacyModeIsExplicit()
        {
            var manager = VolumeManager.instance;
            var previous = manager.stack;
            var isolated = manager.CreateStack();
            var feature = ScriptableObject.CreateInstance<ImageProcessRendererFeature>();
            var owner = new GameObject("Ho post ownership test") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            try
            {
                manager.stack = isolated;
                var volume = isolated.GetComponent<ImageProcessStackVolume>();
                volume.active = true;
                volume.Enable.Override(false);
                volume.ReplaceBuiltinPostProcessing.Override(true);
                Assert.IsTrue(feature.ReplacesBuiltinPostProcessing(camera),
                    "The layer master switch must not re-enable old native Volume effects.");
                volume.ReplaceBuiltinPostProcessing.Override(false);
                Assert.IsFalse(feature.ReplacesBuiltinPostProcessing(camera));
                volume.ReplaceBuiltinPostProcessing.Override(true);
                feature.Settings.enabled = false;
                Assert.IsFalse(feature.ReplacesBuiltinPostProcessing(camera));
            }
            finally
            {
                manager.stack = previous;
                manager.DestroyStack(isolated);
                Object.DestroyImmediate(feature);
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ReusedCameraFrameDataDoesNotLeakHoOwnership()
        {
            var data = new UniversalCameraData { useHoPostProcessing = true, postProcessEnabled = true };
            data.Reset();
            Assert.IsFalse(data.useHoPostProcessing);
            Assert.IsFalse(data.postProcessEnabled);
        }
    }
}
