using NUnit.Framework;
using UnityEngine;
using lilToon.URP.Extensions.PostProcessing;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class PostProcessEnableRegressionTests
    {
        [Test]
        public void ImageProcess_MasterEnableCanDisableExistingLayers()
        {
            var volume = ScriptableObject.CreateInstance<ImageProcessStackVolume>();
            try
            {
                volume.layers.value.Add(new ImageProcessLayer {
                    enabled = true, intensity = 1, effect = ImageProcessEffect.ColorGradingCustom
                });
                volume.Enable.Override(true);
                Assert.IsTrue(volume.IsActive());
                volume.Enable.Override(false);
                Assert.IsFalse(volume.IsActive(), "Existing active layers must not bypass the master switch.");
                volume.Enable.Override(true);
                Assert.IsTrue(volume.IsActive());
                volume.active = false;
                Assert.IsFalse(volume.IsActive());
            }
            finally { Object.DestroyImmediate(volume); }
        }

        [Test]
        public void ScreenProcess_MasterEnableCanDisableExistingLayers()
        {
            var volume = ScriptableObject.CreateInstance<ScreenProcessStackVolume>();
            try
            {
                volume.layers.value.Add(new ScreenProcessLayer {
                    enabled = true, intensity = 1, effect = ScreenProcessEffect.DepthOfField
                });
                volume.Enable.Override(true);
                Assert.IsTrue(volume.IsActive());
                volume.Enable.Override(false);
                Assert.IsFalse(volume.IsActive(), "Existing active layers must not bypass the master switch.");
                volume.Enable.Override(true);
                Assert.IsTrue(volume.IsActive());
                volume.active = false;
                Assert.IsFalse(volume.IsActive());
            }
            finally { Object.DestroyImmediate(volume); }
        }
    }
}
