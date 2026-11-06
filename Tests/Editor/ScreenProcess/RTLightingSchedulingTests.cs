using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;
using lilToon.URP.Extensions.PostProcessing;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class RTLightingSchedulingTests
    {
        [Test]
        public void AddRenderPassesDoesNotReadUnallocatedCameraTargets()
        {
            var package = PackageInfo.FindForAssembly(typeof(ScreenProcessRendererFeature).Assembly);
            string source = File.ReadAllText(Path.Combine(package.assetPath, "Runtime/ScreenProcess/ScreenProcessRendererFeature.cs"));
            int start = source.IndexOf("public override void AddRenderPasses(");
            int end = source.IndexOf("protected override void Dispose(", start);
            string add = source.Substring(start, end - start);
            StringAssert.DoesNotContain("renderer.cameraColorTargetHandle", add);
            StringAssert.DoesNotContain("renderer.cameraDepthTargetHandle", add);
            StringAssert.Contains("rtLightingPass?.SetMaterialAndConsumerSettings(rtLightingMaterial, rtSettings)", add);
            StringAssert.DoesNotContain("rtLightingPass?.Setup(", add);
        }
    }
}
