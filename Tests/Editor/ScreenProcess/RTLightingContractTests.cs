using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using lilToon.URP.Extensions.PostProcessing;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class RTLightingContractTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private static Array Snapshot(params ScreenProcessLayer[] layers)
        {
            Type type = typeof(ScreenProcessRendererFeature).Assembly.GetType(
                "lilToon.URP.Extensions.PostProcessing.ScreenProcessRTLightingConsumerSettings", true);
            object value = type.GetMethod("FromLayers", Hidden).Invoke(null, new object[] { new List<ScreenProcessLayer>(layers) });
            return (Array)type.GetField("layers", Hidden).GetValue(value);
        }
        private static T Field<T>(object value, string field) =>
            (T)value.GetType().GetField(field, Hidden).GetValue(value);

        [Test]
        public void ConsumerSnapshotsPreserveOrderDuplicatesAndDoNotFollowVolumeMutations()
        {
            var gi = new ScreenProcessLayer { effect = ScreenProcessEffect.RTGI, intensity = .25f };
            var di = new ScreenProcessLayer { effect = ScreenProcessEffect.RTDI, intensity = .6f };
            Array snapshot = Snapshot(gi, di, gi, null, new ScreenProcessLayer { enabled = false });
            Assert.That(snapshot.Length, Is.EqualTo(3));
            Assert.That(Field<bool>(snapshot.GetValue(0), "indirect"), Is.True);
            Assert.That(Field<bool>(snapshot.GetValue(1), "indirect"), Is.False);
            Assert.That(Field<bool>(snapshot.GetValue(2), "indirect"), Is.True);
            gi.intensity = 1; gi.rtGain = 10;
            Assert.That(Field<Vector4>(snapshot.GetValue(0), "blend").y, Is.EqualTo(.25f));
            Assert.That(Field<Vector4>(snapshot.GetValue(0), "blend").z, Is.EqualTo(1));
        }

        [Test]
        public void LegacyGiAssetsRemainAdditiveAndNewFieldsHaveNeutralDefaults()
        {
            var legacy = JsonUtility.FromJson<ScreenProcessLayer>(
                "{\"effect\":10,\"rtLightingMode\":0,\"intensity\":0.25}");
            Assert.That(legacy.rtGain, Is.EqualTo(1));
            Assert.That(legacy.rtTint, Is.EqualTo(Color.white));
            Assert.That(legacy.rtIndirectMode, Is.EqualTo(ScreenProcessRTLightingMode.Additive));
            Vector4 blend = Field<Vector4>(Snapshot(legacy).GetValue(0), "blend");
            Assert.That(blend.x, Is.EqualTo(1));
            Assert.That(blend.y, Is.EqualTo(.25f));
        }

        [Test]
        public void ConsumerSanitizesInvalidSettingsAndAppliesExposureOutsideOpacity()
        {
            var layer = new ScreenProcessLayer
            {
                effect = ScreenProcessEffect.RTDI, intensity = .5f, rtGain = 2, rtExposure = 2,
                rtDiffuseWeight = float.NaN, rtSpecularWeight = -1,
                rtLightingMode = (ScreenProcessRTLightingMode)99
            };
            object snapshot = Snapshot(layer).GetValue(0);
            Assert.That(Field<Vector4>(snapshot, "blend"), Is.EqualTo(new Vector4(0, .5f, 8, 0)));
            Assert.That(Field<Vector4>(snapshot, "components").x, Is.Zero);
            Assert.That(Field<Vector4>(snapshot, "components").y, Is.Zero);
            Assert.That(Snapshot(new ScreenProcessLayer { intensity = float.NaN }).Length, Is.Zero);
        }
        private static string ReadPackageFile(string relativePath)
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(ScreenProcessRendererFeature).Assembly);
            Assert.That(package, Is.Not.Null, "Runtime package metadata is unavailable.");
            string path = Path.Combine(package.assetPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Missing contract source: " + relativePath);
            return File.ReadAllText(path);
        }

        [Test]
        public void ProducersPublishTypedProductsBeforeOpaqueAndOwnNoCompositePass()
        {
            string di = ReadPackageFile("Runtime/RTDI/HoRTDIRendererFeature.cs");
            string gi = ReadPackageFile("Runtime/RTGI/HoRTGIRendererFeature.cs");

            StringAssert.Contains("pass.renderPassEvent=RenderPassEvent.BeforeRenderingOpaques", di);
            StringAssert.Contains("pass.renderPassEvent=RenderPassEvent.BeforeRenderingOpaques", gi);
            StringAssert.Contains("ScreenProcessRTLightingResources", di);
            StringAssert.Contains("ScreenProcessRTLightingResources", gi);
            StringAssert.Contains("PublishDirect", di);
            StringAssert.Contains("PublishIndirect", gi);
            StringAssert.Contains("PublishDirectProduct", di);
            StringAssert.Contains("PublishIndirectProduct", gi);
            StringAssert.DoesNotContain("compositePass", di);
            StringAssert.DoesNotContain("compositeShader", di);
            StringAssert.DoesNotContain("compositePass", gi);
            StringAssert.DoesNotContain("compositeShader", gi);
        }

        [Test]
        public void ScreenProcessConsumerUsesFrameProductsBeforeTransparents()
        {
            string events = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessRenderPassEvents.cs");
            string pass = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessRTLightingPass.cs");
            string resources = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessRTLightingResources.cs");

            StringAssert.Contains(
                "RTLightingComposite = RenderPassEvent.BeforeRenderingTransparents",
                events);
            StringAssert.Contains("GetOrCreate<ScreenProcessRTLightingResources>()", pass);
            StringAssert.Contains("builder.UseTexture(data.directDiffuse", pass);
            StringAssert.Contains("resourceData.cameraColor = destination", pass);
            StringAssert.Contains("ContextItem", resources);
            StringAssert.Contains("ScreenProcessRTLightingCompatibility", resources);
            StringAssert.Contains("internal static void Clear()", resources);
        }

        [Test]
        public void ConsumerShaderKeepsReceiverDepthAndLinearHdrProductGates()
        {
            string shader = ReadPackageFile("Runtime/ScreenProcess/Shaders/ScreenProcess/RTLighting.shader");

            StringAssert.Contains("_HoSPRTDirectDiffuse", shader);
            StringAssert.Contains("_HoSPRTDirectSpecular", shader);
            StringAssert.Contains("_HoSPRTIndirectDiffuse", shader);
            StringAssert.Contains("HoRTTryLoadPrimarySurfaceAtUV", shader);
            StringAssert.Contains("_CameraDepthTexture", shader);
            StringAssert.Contains("MixFogColor", shader);
            StringAssert.Contains("UNITY_COLORSPACE_GAMMA", shader);
        }

        [Test]
        public void ScreenProcessOwnsRTLayerNamesAndRTVolumesDoNotExposeCompositionUi()
        {
            string effects = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessEffect.cs");
            string layer = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessLayer.cs");
            string renderer = ReadPackageFile("Runtime/ScreenProcess/ScreenProcessRendererFeature.cs");
            string stackEditor = ReadPackageFile("Editor/PostProcessing/ScreenProcess/ScreenProcessStackVolumeEditor.cs");
            string volume = ReadPackageFile("Runtime/RTDI/HoRTDIVolume.cs");
            string giVolume = ReadPackageFile("Runtime/RTGI/HoRTGIVolume.cs");
            string diEditor = ReadPackageFile("Editor/RTDI/HoRTDIVolumeEditor.cs");
            string giEditor = ReadPackageFile("Editor/RTGI/HoRTGIVolumeEditor.cs");

            StringAssert.Contains("RTDI = 9", effects);
            StringAssert.Contains("RTGI = 10", effects);
            StringAssert.Contains("rtLightingMode", layer);
            StringAssert.Contains("RTDI 直接光", stackEditor);
            StringAssert.Contains("RTGI 间接光", stackEditor);
            StringAssert.Contains("runtimeRTLightingLayers.Clear()", renderer);
            StringAssert.Contains("runtimeLayers.Count == 0 && !rtSettings.HasAny", renderer);
            StringAssert.DoesNotContain("合成到画面", diEditor);
            StringAssert.DoesNotContain("合成到场景", giEditor);
            StringAssert.Contains("[HideInInspector]", volume);
            StringAssert.Contains("[HideInInspector]", giVolume);
        }
    }
}
