using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.PostProcessing;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class ScreenProcessSamplingStrideTests
    {
        const int Width = 128;
        const int Height = 96;
        const float LeakTolerance = 0.0005f;

        readonly List<Object> owned = new List<Object>();
        RenderTexture target;
        bool oldAsyncCompilation;

        T Keep<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a GPU; do not run with -nographics.");
            oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            target = Keep(new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            target.Create();
        }

        [TearDown]
        public void TearDown()
        {
            ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
            for (int i = owned.Count - 1; i >= 0; --i)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        Texture2D Texture(Color[] colors)
        {
            var texture = Keep(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }

        Texture2D Solid(Color color)
        {
            var colors = new Color[Width * Height];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            return Texture(colors);
        }

        Material Material(string effect)
        {
            Shader shader = Shader.Find("Hidden/lilToon/URP/ScreenProcess/" + effect);
            Assert.That(shader, Is.Not.Null, effect + " shader missing");
            var material = Keep(new Material(shader));
            material.SetFloat("_Intensity", 1);
            material.SetFloat("_LayerBlendMode", (float)ScreenProcessBlendMode.Add);
            material.SetColor("_LayerColor", Color.white);
            material.SetFloat("_LayerMaskEnabled", 0);
            material.SetFloat("_LayerMaskInvert", 0);
            material.SetFloat("_LayerMaskDebugOutput", 0);
            return material;
        }

        Color[] Draw(Material material, Texture source, Texture normalDepth, Texture outlineDepth = null, Texture sky = null)
        {
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, Width, Height));
                cmd.SetGlobalTexture("_BlitTexture", source);
                cmd.SetGlobalVector("_BlitTexture_TexelSize", new Vector4(1f / Width, 1f / Height, Width, Height));
                cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                cmd.SetGlobalVector("_ProjectionParams", new Vector4(1, .1f, 100, .01f));
                cmd.SetGlobalVector("_ScreenParams", new Vector4(Width, Height, 1f + 1f / Width, 1f + 1f / Height));
                cmd.SetGlobalVector("unity_OrthoParams", new Vector4(2, 2, 0, 1));
                cmd.SetGlobalMatrix("unity_MatrixInvVP", Matrix4x4.identity);
                cmd.SetGlobalMatrix("unity_MatrixV", Matrix4x4.identity);
                cmd.SetGlobalFloat("_HoGeometryBufferValid", 1);
                cmd.SetGlobalFloat("_lilHoSPCameraDepthValid", 0);
                cmd.SetGlobalFloat("_lilHoSPCameraNormalsValid", 0);
                cmd.SetGlobalFloat("_lilHoSPOutlineDepthValid", outlineDepth != null ? 1 : 0);
                cmd.SetGlobalFloat("_HoGeometryBufferOutlineCoverageTextureValid", 0);
                cmd.SetGlobalFloat("_HoGeometryBufferCoverageTextureValid", 0);
                cmd.SetGlobalFloat("_HoGeometryBufferSkyTextureValid", sky != null ? 1 : 0);
                cmd.SetGlobalTexture("_HoGeometryBufferNormalDepthTexture", normalDepth);
                cmd.SetGlobalTexture("_HoGeometryBufferOutlineNormalDepthTexture",
                    outlineDepth != null ? outlineDepth : Texture2D.blackTexture);
                cmd.SetGlobalTexture("_HoGeometryBufferSkyTexture", sky != null ? sky : Texture2D.blackTexture);
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False, material.shader.name + " failed to compile");
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            try
            {
                var readback = Keep(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        void CaptureFullOutput(string name, Color[] pixels)
        {
            string directory = System.Environment.GetEnvironmentVariable("HO_SP_SAMPLING_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            using (var stream = File.Create(Path.Combine(directory, name + ".rgba32")))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (Color pixel in pixels)
                {
                    writer.Write(pixel.r);
                    writer.Write(pixel.g);
                    writer.Write(pixel.b);
                    writer.Write(pixel.a);
                }
            }
        }

        static Color[] BuildPattern()
        {
            var colors = new Color[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float u = (x + .5f) / Width;
                float v = (y + .5f) / Height;
                colors[x + Width * y] = new Color(.15f + .75f * u, .1f + .7f * v, .2f + .55f * (1 - u * v), 1);
            }
            return colors;
        }

        static Color[] BuildDepth(bool outline)
        {
            var depth = new Color[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool shell = x >= 59 && x < 65;
                bool near = x < 59;
                depth[x + Width * y] = outline && shell
                    ? new Color(.5f, .5f, 1, 3)
                    : new Color(.5f, .5f, 1, near ? 5 : 40);
            }
            return depth;
        }

        static Color[] BuildSky()
        {
            var sky = new Color[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float u = (x + .5f) / Width;
                float v = (y + .5f) / Height;
                sky[x + Width * y] = new Color(1.5f * u, 1.2f * v, .8f + .7f * u, .35f + .6f * v);
            }
            return sky;
        }

        [Test]
        public void FullDensityCapturesReferenceFilterOutputs()
        {
            Texture2D source = Texture(BuildPattern());
            Texture2D depth = Texture(BuildDepth(false));
            Texture2D outlineDepth = Texture(BuildDepth(true));
            Color[] pixels;

            Material dof = Material("DepthOfField");
            dof.SetVector("_LayerParams0", new Vector4(1, 10, 50, 5.6f));
            dof.SetVector("_LayerParams1", new Vector4(4, 18, 36, 1));
            dof.SetVector("_LayerParams3", new Vector4(3, 1, 1, 1));
            dof.SetFloat("_HoSPBlurSampleStride", 1);
            pixels = Draw(dof, source, depth, outlineDepth);
            CaptureFullOutput("full-depth-of-field", pixels);

            Material bokeh = Material("BokehZoomBlur");
            bokeh.SetVector("_LayerParams0", new Vector4(0, 10, 50, 5.6f));
            bokeh.SetVector("_LayerParams1", new Vector4(.65f, .25f, .2f, 1));
            bokeh.SetVector("_LayerParams2", new Vector4(0, 0, 1, 2));
            bokeh.SetVector("_LayerParams3", new Vector4(3, 1, 1, 1));
            bokeh.SetVector("_LayerParams4", new Vector4(6, 1, 0, .2f));
            bokeh.SetVector("_LayerParams5", new Vector4(1, 0, 0, 0));
            bokeh.SetFloat("_HoSPBlurSampleStride", 1);
            pixels = Draw(bokeh, source, depth, outlineDepth);
            CaptureFullOutput("full-bokeh-zoom", pixels);

            Material skyMaterial = Material("SkyTyndall");
            skyMaterial.SetVector("_LayerParams0", new Vector4(.35f, .1f, .25f, 1));
            skyMaterial.SetVector("_LayerParams1", new Vector4(.5f, .5f, .86f, 2));
            skyMaterial.SetVector("_LayerParams2", new Vector4(.1f, 0, 1, .7f));
            skyMaterial.SetVector("_LayerParams3", new Vector4(1, 0, 1, 0));
            skyMaterial.SetVector("_LayerParams4", new Vector4(0, -1, 90, 1));
            skyMaterial.SetVector("_LayerParams5", new Vector4(.05f, .6f, 2, 0));
            skyMaterial.SetFloat("_HoSPBlurSampleStride", 1);
            pixels = Draw(skyMaterial, source, depth, sky: Texture(BuildSky()));
            CaptureFullOutput("full-sky-tyndall", pixels);
        }

        void AssertFocusedOutlineDoesNotBleed(Material material, string effect)
        {
            var colors = new Color[Width * Height];
            var geometry = new Color[colors.Length];
            var outline = new Color[colors.Length];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int index = x + Width * y;
                bool subject = x < 60;
                bool shell = x >= 60 && x < 64;
                colors[index] = subject ? Color.green : shell ? Color.red : Color.blue;
                geometry[index] = new Color(.5f, .5f, 1, subject ? 3 : 40);
                if (shell) outline[index] = new Color(.5f, .5f, 1, 3);
            }

            var original = Texture(colors);
            var normalDepth = Texture(geometry);
            var outlineDepth = Texture(outline);
            foreach (float stride in new[] { 1f, 2f, 4f })
            {
                material.SetFloat("_HoSPBlurSampleStride", stride);
                Color[] result = Draw(material, original, normalDepth, outlineDepth);
                float backgroundRed = 0;
                float shellError = 0;
                for (int i = 0; i < result.Length; i++)
                {
                    if (colors[i].b == 1) backgroundRed = Mathf.Max(backgroundRed, result[i].r);
                    if (colors[i].r == 1) shellError = Mathf.Max(shellError, Mathf.Abs(result[i].r - 1));
                }
                Assert.That(backgroundRed, Is.LessThanOrEqualTo(LeakTolerance),
                    $"{effect} focused outline leaked at stride {stride}");
                Assert.That(shellError, Is.LessThanOrEqualTo(LeakTolerance),
                    $"{effect} focused outline changed at stride {stride}");
            }
        }

        [Test]
        public void DensitySettingMapsToFullHalfAndQuarterSampleStride()
        {
            var passType = typeof(ScreenProcessRendererFeature).Assembly.GetType(
                "lilToon.URP.Extensions.PostProcessing.ScreenProcessPass");
            var resolve = passType.GetMethod("ResolveBlurSampleStride",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);
            foreach (var effect in new[] { ScreenProcessEffect.SkyTyndall, ScreenProcessEffect.BokehZoomBlur, ScreenProcessEffect.DepthOfField })
            {
                var layer = new ScreenProcessLayer { effect = effect };
                layer.blurResolution = ScreenProcessBlurResolution.Full;
                Assert.That((float)resolve.Invoke(null, new object[] { layer }), Is.EqualTo(1));
                layer.blurResolution = ScreenProcessBlurResolution.Half;
                Assert.That((float)resolve.Invoke(null, new object[] { layer }), Is.EqualTo(2));
                layer.blurResolution = ScreenProcessBlurResolution.Quarter;
                Assert.That((float)resolve.Invoke(null, new object[] { layer }), Is.EqualTo(4));
            }
        }

        [Test]
        public void FilterLimitsClampLegacyValuesAtEveryDensity()
        {
            var passType = typeof(ScreenProcessRendererFeature).Assembly.GetType(
                "lilToon.URP.Extensions.PostProcessing.ScreenProcessPass");
            var apply = passType.GetMethod("ApplyLayerProperties",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var layer = new ScreenProcessLayer
            {
                effect = ScreenProcessEffect.SkyTyndall,
                blurResolution = ScreenProcessBlurResolution.Half,
                parameters1 = new Vector4(.5f, .5f, .8f, 8),
                parameters5 = new Vector4(2, 8, 0, 0)
            };
            var material = Material("SkyTyndall");
            foreach (var density in new[] { ScreenProcessBlurResolution.Full, ScreenProcessBlurResolution.Half, ScreenProcessBlurResolution.Quarter })
            {
                layer.blurResolution = density;
                apply.Invoke(null, new object[] { layer, material, -1f });
                Assert.That(material.GetVector("_LayerParams1").w, Is.EqualTo(2));
                Assert.That(material.GetVector("_LayerParams5").x, Is.EqualTo(.1f).Within(.0001f));
                Assert.That(material.GetVector("_LayerParams5").y, Is.EqualTo(1.5f));
            }

            var zoom = new ScreenProcessLayer
            {
                effect = ScreenProcessEffect.BokehZoomBlur,
                blurResolution = ScreenProcessBlurResolution.Quarter,
                parameters0 = new Vector4(0, 3, 300, 32),
                parameters1 = new Vector4(4, 0, 0, 1),
                parameters2 = new Vector4(0, 0, 1, 8),
                parameters3 = new Vector4(12, 4, 4, 1),
                parameters4 = new Vector4(6, 1, 0, 1)
            };
            Material zoomMaterial = Material("BokehZoomBlur");
            apply.Invoke(null, new object[] { zoom, zoomMaterial, -1f });
            Assert.That(zoomMaterial.GetVector("_LayerParams0").z, Is.EqualTo(180));
            Assert.That(zoomMaterial.GetVector("_LayerParams1").x, Is.EqualTo(.65f).Within(.0001f));
            Assert.That(zoomMaterial.GetVector("_LayerParams2").w, Is.EqualTo(2));
            Assert.That(zoomMaterial.GetVector("_LayerParams4").w, Is.EqualTo(.5f).Within(.0001f));

            var dof = new ScreenProcessLayer
            {
                effect = ScreenProcessEffect.DepthOfField,
                blurResolution = ScreenProcessBlurResolution.Half,
                parameters0 = new Vector4(1, 3, 300, 32),
                parameters1 = new Vector4(4, 18, 96, 1),
                parameters3 = new Vector4(12, 4, 4, 1)
            };
            Material dofMaterial = Material("DepthOfField");
            apply.Invoke(null, new object[] { dof, dofMaterial, -1f });
            Assert.That(dofMaterial.GetVector("_LayerParams0").z, Is.EqualTo(180));
            Assert.That(dofMaterial.GetVector("_LayerParams0").w, Is.EqualTo(24));
            Assert.That(dofMaterial.GetVector("_LayerParams1").z, Is.EqualTo(48));
            Assert.That(dofMaterial.GetVector("_LayerParams3").x, Is.EqualTo(8));
            Assert.That(dofMaterial.GetVector("_LayerParams3").y, Is.EqualTo(2));
        }

        [Test]
        public void DepthOfFieldKeepsConstantColorAtEverySampleDensity()
        {
            var material = Material("DepthOfField");
            material.SetVector("_LayerParams0", new Vector4(1, 3, 50, 5.6f));
            material.SetVector("_LayerParams1", new Vector4(4, 18, 36, 1));
            material.SetVector("_LayerParams3", new Vector4(3, 1, 1, 1));
            var sourceColor = new Color(.2f, .5f, .8f, .7f);
            Texture2D source = Solid(sourceColor);
            Texture2D depth = Solid(new Color(.5f, .5f, 1, 40));
            foreach (float stride in new[] { 1f, 2f, 4f })
            {
                material.SetFloat("_HoSPBlurSampleStride", stride);
                foreach (Color pixel in Draw(material, source, depth))
                {
                    Assert.That(pixel.r, Is.EqualTo(sourceColor.r).Within(.001f));
                    Assert.That(pixel.g, Is.EqualTo(sourceColor.g).Within(.001f));
                    Assert.That(pixel.b, Is.EqualTo(sourceColor.b).Within(.001f));
                    Assert.That(pixel.a, Is.EqualTo(sourceColor.a).Within(.001f));
                }
            }
        }

        [Test]
        public void DepthOfFieldStridePreservesFocusedOutlineDepthGate()
        {
            var material = Material("DepthOfField");
            material.SetVector("_LayerParams0", new Vector4(1, 3, 50, 5.6f));
            material.SetVector("_LayerParams1", new Vector4(4, 18, 36, 1));
            material.SetVector("_LayerParams3", new Vector4(3, 1, 1, 1));
            AssertFocusedOutlineDoesNotBleed(material, "DepthOfField");
        }

        [Test]
        public void BokehZoomStridePreservesFocusedOutlineDepthGate()
        {
            var material = Material("BokehZoomBlur");
            material.SetFloat("_LayerBlendMode", (float)ScreenProcessBlendMode.Add);
            material.SetVector("_LayerParams0", new Vector4(0, 3, 50, 5.6f));
            material.SetVector("_LayerParams1", new Vector4(.65f, 0, 0, 1));
            material.SetVector("_LayerParams2", new Vector4(-1, 0, 1, 2));
            material.SetVector("_LayerParams3", new Vector4(3, 1, 1, 1));
            material.SetVector("_LayerParams4", new Vector4(6, 1, 0, 0));
            material.SetVector("_LayerParams5", new Vector4(1, 0, 0, 0));
            AssertFocusedOutlineDoesNotBleed(material, "BokehZoomBlur");
        }

        [Test]
        public void SkyTyndallStridePreservesConstantSignalEnergy()
        {
            var material = Material("SkyTyndall");
            material.SetVector("_LayerParams0", new Vector4(.1f, 0, 0, 1));
            material.SetVector("_LayerParams1", new Vector4(.5f, .5f, .8f, 2));
            material.SetVector("_LayerParams2", new Vector4(0, 0, 1, 1));
            material.SetVector("_LayerParams3", new Vector4(1, 0, 1, 0));
            material.SetVector("_LayerParams4", new Vector4(0, -1, 90, 1));
            material.SetVector("_LayerParams5", new Vector4(.05f, 0, 2, 0));
            var baseline = Draw(material, Solid(Color.black), Solid(new Color(.5f, .5f, 1, 40)), sky: Solid(Color.white));
            Color[] half;
            Color[] quarter;
            material.SetFloat("_HoSPBlurSampleStride", 2);
            half = Draw(material, Solid(Color.black), Solid(new Color(.5f, .5f, 1, 40)), sky: Solid(Color.white));
            material.SetFloat("_HoSPBlurSampleStride", 4);
            quarter = Draw(material, Solid(Color.black), Solid(new Color(.5f, .5f, 1, 40)), sky: Solid(Color.white));

            for (int y = 20; y < Height - 20; y++)
            for (int x = 4; x < Width - 4; x++)
            {
                int index = x + Width * y;
                Assert.That(half[index].r, Is.EqualTo(baseline[index].r).Within(.0005f));
                Assert.That(quarter[index].r, Is.EqualTo(baseline[index].r).Within(.0005f));
            }
        }
    }
}
