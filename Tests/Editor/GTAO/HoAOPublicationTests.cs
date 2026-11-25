using System;
using System.Collections;
using System.Reflection;
using lilToon.URP.Extensions.GTAO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoAOPublicationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Assembly Runtime = typeof(HoGTAOSettings).Assembly;
        private static readonly Type Publisher = Runtime.GetType("lilToon.URP.Extensions.GTAO.HoGTAOPublishOnlyPass", true);
        private static readonly Type Resources = Runtime.GetType("lilToon.URP.Extensions.GTAO.HoGTAORenderGraphResources", true);

        [Test]
        public void ExistingProfilesKeepGtaoComputationByDefault()
        {
            var volume = ScriptableObject.CreateInstance<HoGTAOVolume>();
            try
            {
                Assert.That(new HoGTAOSettings().publishAOTextureOnly, Is.False);
                Assert.That(volume.publishAOTextureOnly.value, Is.False);
                Assert.That(volume.publishAOTextureOnly.overrideState, Is.False);
            }
            finally { Object.DestroyImmediate(volume); }
        }

        [Test]
        public void WhitePublicationNeedsNoGeometryColorOrMotionAndOwnsWritableCameraSizedOutput()
        {
            var graph = new RenderGraph("White AO publication validation");
            var frame = new ContextContainer();
            var command = new CommandBuffer();
            var cameraObject = new GameObject("AO publication camera");
            var otherObject = new GameObject("Other AO camera");
            var pass = (ScriptableRenderPass)Activator.CreateInstance(Publisher, true);
            bool recording = false;
            try
            {
                object parameters = new RenderGraphParameters { commandBuffer = command };
                typeof(RenderGraphParameters).GetField("invalidContextForTesting", Private).SetValue(parameters, true);
                graph.BeginRecording((RenderGraphParameters)parameters);
                recording = true;
                var camera = frame.GetOrCreate<UniversalCameraData>();
                camera.camera = cameraObject.AddComponent<Camera>();
                camera.cameraTargetDescriptor = new RenderTextureDescriptor(19, 11, RenderTextureFormat.ARGBHalf, 24)
                    { msaaSamples = 4 };
                // No UniversalResourceData or GB is provided on purpose.
                pass.RecordRenderGraph(graph, frame);
                var passes = (IList)typeof(RenderGraph).GetField("m_RenderPasses", Private).GetValue(graph);
                Assert.That(passes.Count, Is.EqualTo(1), "White publication must not record trace, motion or filtering.");
                object output = typeof(ContextContainer).GetMethod("GetOrCreate").MakeGenericMethod(Resources).Invoke(frame, null);
                TextureHandle target = (TextureHandle)Resources.GetField("aoTexture").GetValue(output);
                TextureDesc desc = graph.GetTextureDesc(target);
                Assert.That(desc.width, Is.EqualTo(19));
                Assert.That(desc.height, Is.EqualTo(11));
                Assert.That(desc.msaaSamples, Is.EqualTo(MSAASamples.None));
                Assert.That(desc.clearBuffer, Is.True);
                Assert.That(desc.clearColor, Is.EqualTo(Color.white));
                Assert.That(desc.depthBufferBits, Is.EqualTo(DepthBits.None));
                Assert.That(pass.input, Is.EqualTo(ScriptableRenderPassInput.None));
                MethodInfo canAppend = Resources.GetMethod("CanAppend");
                Assert.That(canAppend.Invoke(output, new object[] { camera.camera, 19, 11 }), Is.True);
                Assert.That(canAppend.Invoke(output, new object[] { otherObject.AddComponent<Camera>(), 19, 11 }), Is.False);
                Assert.That(canAppend.Invoke(output, new object[] { camera.camera, 20, 11 }), Is.False);
                Resources.GetMethod("Reset").Invoke(output, null);
                Assert.That(canAppend.Invoke(output, new object[] { camera.camera, 19, 11 }), Is.False);
            }
            finally
            {
                if (recording) graph.ResetGraphAndLogException(new OperationCanceledException("Recording-only AO validation"));
                graph.Cleanup(); frame.Dispose(); command.Release();
                Publisher.GetMethod("Dispose", Private).Invoke(pass, null);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(otherObject);
            }
        }
    }
}
