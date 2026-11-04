using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using lilToon.URP.Extensions.GTAO;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoGTAOQualityPresetTests
    {
        HoGTAOVolume volume;

        [SetUp]
        public void SetUp()
        {
            volume = ScriptableObject.CreateInstance<HoGTAOVolume>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(volume);
        }

        [TestCase(HoGTAOQuality.Low, HoGTAOResolution.Quarter, 2, 8, 0, 1)]
        [TestCase(HoGTAOQuality.Medium, HoGTAOResolution.Half, 2, 12, 4, 2)]
        [TestCase(HoGTAOQuality.High, HoGTAOResolution.Full, 4, 32, 12, 3)]
        public void PresetFillsOnlyDensityAndDenoiseValues(HoGTAOQuality preset,
            HoGTAOResolution resolution, int slices, int steps, int frames, int boxPasses)
        {
            volume.active = false;
            volume.enable.value = false;
            volume.worldSpaceRadius.value = 1.7f;
            volume.screenSpaceRadius.value = 41;
            volume.thickness.value = .35f;
            volume.useAttenuation.value = false;
            volume.useLinearThickness.value = false;
            volume.temporalRejection.value = .3f;
            volume.filterRadius.value = .8f;
            volume.filterAdaptivity.value = .6f;
            volume.debugMode.value = HoGTAODebugMode.Temporal;
            volume.debugIntensity.value = 2.1f;

            HoGTAOQualityPresets.Apply(preset, volume);

            Assert.That(volume.resolution.value, Is.EqualTo(resolution));
            Assert.That(volume.sliceCount.value, Is.EqualTo(slices));
            Assert.That(volume.stepCount.value, Is.EqualTo(steps));
            Assert.That(volume.temporalFrameCount.value, Is.EqualTo(frames));
            Assert.That(volume.spatialFilter.value, Is.EqualTo(HoGTAOSpatialFilter.Box));
            Assert.That(volume.boxPassCount.value, Is.EqualTo(boxPasses));
            Assert.That(volume.active, Is.False);
            Assert.That(volume.enable.value, Is.False);
            Assert.That(volume.worldSpaceRadius.value, Is.EqualTo(1.7f));
            Assert.That(volume.screenSpaceRadius.value, Is.EqualTo(41));
            Assert.That(volume.thickness.value, Is.EqualTo(.35f));
            Assert.That(volume.useAttenuation.value, Is.False);
            Assert.That(volume.useLinearThickness.value, Is.False);
            Assert.That(volume.temporalRejection.value, Is.EqualTo(.3f));
            Assert.That(volume.filterRadius.value, Is.EqualTo(.8f));
            Assert.That(volume.filterAdaptivity.value, Is.EqualTo(.6f));
            Assert.That(volume.debugMode.value, Is.EqualTo(HoGTAODebugMode.Temporal));
            Assert.That(volume.debugIntensity.value, Is.EqualTo(2.1f));
        }

        [TestCase(HoGTAOQuality.Low, 0)]
        [TestCase(HoGTAOQuality.Low, 1)]
        [TestCase(HoGTAOQuality.Low, 2)]
        [TestCase(HoGTAOQuality.Medium, 0)]
        [TestCase(HoGTAOQuality.Medium, 1)]
        [TestCase(HoGTAOQuality.Medium, 2)]
        [TestCase(HoGTAOQuality.High, 0)]
        [TestCase(HoGTAOQuality.High, 1)]
        [TestCase(HoGTAOQuality.High, 2)]
        public void PresetPreservesAllUserOverrideChoices(HoGTAOQuality preset, int mode)
        {
            for (int i = 0; i < volume.parameters.Count; i++)
                volume.parameters[i].overrideState = mode == 1 || mode == 2 && i % 2 == 0;
            var before = volume.parameters.Select(x => x.overrideState).ToArray();

            HoGTAOQualityPresets.Apply(preset, volume);

            Assert.That(volume.parameters.Select(x => x.overrideState).ToArray(), Is.EqualTo(before));
        }

        [Test]
        public void UserCanAdjustValuesAndExplicitlyRefillTheSamePreset()
        {
            HoGTAOQualityPresets.Apply(HoGTAOQuality.Medium, volume);
            volume.stepCount.Override(19);
            volume.temporalFrameCount.Override(5);
            Assert.That(volume.stepCount.value, Is.EqualTo(19));
            Assert.That(volume.temporalFrameCount.value, Is.EqualTo(5));

            HoGTAOQualityPresets.Apply(HoGTAOQuality.Medium, volume);

            Assert.That(volume.stepCount.value, Is.EqualTo(12));
            Assert.That(volume.temporalFrameCount.value, Is.EqualTo(4));
            Assert.That(volume.stepCount.overrideState, Is.True);
            Assert.That(volume.temporalFrameCount.overrideState, Is.True);
        }

        [Test]
        public void InspectorFillSupportsUndoAndRedoWithoutChangingOverrides()
        {
            volume.stepCount.Override(19);
            volume.temporalFrameCount.value = 5;
            var before = volume.parameters.Select(x => x.overrideState).ToArray();
            var editor = UnityEditor.Editor.CreateEditor(volume);
            try
            {
                var fill = editor.GetType().GetMethod("ApplyPreset", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fill, Is.Not.Null, "The real GTAO Volume Inspector must expose its fill action.");
                Undo.IncrementCurrentGroup();
                fill.Invoke(editor, new object[] { HoGTAOQuality.Medium });
                Undo.FlushUndoRecordObjects();
                Assert.That(volume.stepCount.value, Is.EqualTo(12));
                Assert.That(volume.temporalFrameCount.value, Is.EqualTo(4));

                Undo.PerformUndo();
                Assert.That(volume.stepCount.value, Is.EqualTo(19));
                Assert.That(volume.temporalFrameCount.value, Is.EqualTo(5));
                Assert.That(volume.parameters.Select(x => x.overrideState).ToArray(), Is.EqualTo(before));

                Undo.PerformRedo();
                Assert.That(volume.stepCount.value, Is.EqualTo(12));
                Assert.That(volume.temporalFrameCount.value, Is.EqualTo(4));
                Assert.That(volume.parameters.Select(x => x.overrideState).ToArray(), Is.EqualTo(before));
            }
            finally
            {
                Undo.ClearUndo(volume);
                Object.DestroyImmediate(editor);
            }
        }
    }
}
