using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using lilToon.URP.Extensions.Editor.PostProcessing;
using lilToon.URP.Extensions.PostProcessing;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class LensMigrationTests
    {
        VolumeProfile profile;
        string assetPath;

        [SetUp]
        public void SetUp() { profile = ScriptableObject.CreateInstance<VolumeProfile>(); }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                assetPath = null;
                return;
            }
            foreach(var component in profile.components) if(component != null) Object.DestroyImmediate(component);
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void MigrationPreservesUnrelatedLayersAndExistingFocusAndIsIdempotent()
        {
            var source = profile.Add<ImageProcessStackVolume>(true);
            source.Enable.Override(true);
            var unrelated = new ImageProcessLayer { effect=ImageProcessEffect.ToonMap };
            source.layers.value.Add(unrelated);
            source.layers.value.Add(new ImageProcessLayer {
                effect=ImageProcessEffect.BokehZoomBlur,name="Existing zoom",intensity=.4f,
                parameters0=new Vector4(.3f,2,.2f,1.5f),parameters1=new Vector4(.2f,-.3f,2,1),
                parameters2=new Vector4(6,.7f,30,.1f),parameters3=new Vector4(1,0,2,0)
            });
            source.layers.value.Add(new ImageProcessLayer {
                effect=ImageProcessEffect.ApertureBokeh,name="Existing aperture",enabled=false,
                parameters0=new Vector4(.5f,1,.3f,2),parameters1=new Vector4(0,.8f,0,2),
                parameters2=new Vector4(6,.7f,30,.2f),parameters3=new Vector4(0,1,3,0)
            });
            var destination = profile.Add<ScreenProcessStackVolume>(true);
            var existing = new ScreenProcessLayer { effect=ScreenProcessEffect.DepthOfField,parameters0=new Vector4(2,7,85,1.4f) };
            destination.layers.value.Add(existing);
            Assert.That(ScreenProcessLensMigration.Migrate(profile),Is.EqualTo(2));
            Assert.That(source.layers.value,Has.Count.EqualTo(1));
            Assert.That(source.layers.value[0],Is.SameAs(unrelated));
            Assert.That(destination.layers.value,Has.Count.EqualTo(3));
            Assert.That(destination.layers.value[0],Is.SameAs(existing));
            Assert.That(existing.parameters0,Is.EqualTo(new Vector4(2,7,85,1.4f)));
            var zoom=destination.layers.value[1];
            Assert.That(zoom.effect,Is.EqualTo(ScreenProcessEffect.BokehZoomBlur));
            Assert.That(zoom.parameters1,Is.EqualTo(new Vector4(.3f,2,.2f,1.5f)));
            Assert.That(zoom.intensity,Is.EqualTo(.4f));
            Assert.That(zoom.blendMode,Is.EqualTo(ScreenProcessBlendMode.Screen));
            var aperture=destination.layers.value[2];
            Assert.That(aperture.effect,Is.EqualTo(ScreenProcessEffect.DepthOfField));
            Assert.That(aperture.parameters4,Is.EqualTo(new Vector4(1,1,.3f,3)));
            Assert.That(aperture.parameters5,Is.EqualTo(new Vector4(2,.8f,.2f,1)));
            Assert.That(aperture.enabled,Is.False);
            Assert.That(ScreenProcessLensMigration.Migrate(profile),Is.EqualTo(0));
        }

        [Test]
        public void MigrationCreatesStackWithoutEnablingAnInactiveSource()
        {
            var source=profile.Add<ImageProcessStackVolume>(true); source.Enable.Override(false);
            source.layers.value.Add(new ImageProcessLayer { effect=ImageProcessEffect.BokehZoomBlur });
            Assert.That(ScreenProcessLensMigration.Migrate(profile),Is.EqualTo(1));
            Assert.That(profile.TryGet(out ScreenProcessStackVolume destination),Is.True);
            Assert.That(destination.Enable.value,Is.False);
            Assert.That(destination.layers.value[0].enabled,Is.False);
            Assert.That(destination.layers.value[0].bokehZoomFocusTarget,Is.Null);
        }

        [Test]
        public void MigrationCanBeUndoneAndSkipsCustomPassContracts()
        {
            var source=profile.Add<ImageProcessStackVolume>(true); source.Enable.Override(true);
            source.layers.value.Add(new ImageProcessLayer { effect=ImageProcessEffect.BokehZoomBlur });
            source.layers.value.Add(new ImageProcessLayer { effect=ImageProcessEffect.ApertureBokeh,passIndex=2 });
            Assert.That(ScreenProcessLensMigration.Migrate(profile),Is.EqualTo(1));
            Assert.That(source.layers.value,Has.Count.EqualTo(1));
            Assert.That(source.layers.value[0].passIndex,Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(source.layers.value,Has.Count.EqualTo(2));
            Assert.That(profile.Has<ScreenProcessStackVolume>(),Is.False);
        }

        [Test]
        public void MigrationPersistsTheNewStackAsAProfileSubAsset()
        {
            assetPath="Assets/HoLensMigration-"+System.Guid.NewGuid().ToString("N")+".asset";
            AssetDatabase.CreateAsset(profile,assetPath);
            var source=profile.Add<ImageProcessStackVolume>(true);
            AssetDatabase.AddObjectToAsset(source,profile);
            source.Enable.Override(true);
            source.layers.value.Add(new ImageProcessLayer { effect=ImageProcessEffect.BokehZoomBlur });
            Assert.That(ScreenProcessLensMigration.Migrate(profile),Is.EqualTo(1));
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.ImportAsset(assetPath,ImportAssetOptions.ForceUpdate);
            var loaded=AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
            Assert.That(loaded.TryGet(out ScreenProcessStackVolume stack),Is.True);
            Assert.That(AssetDatabase.GetAssetPath(stack),Is.EqualTo(assetPath));
            Assert.That(stack.layers.value,Has.Count.EqualTo(1));
            Assert.That(stack.layers.value[0].effect,Is.EqualTo(ScreenProcessEffect.BokehZoomBlur));
        }
    }
}
