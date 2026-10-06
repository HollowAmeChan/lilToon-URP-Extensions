using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    // These tests draw the shipping shaders and real features. Analytic colors/coverage are the
    // oracle: no duplicated DoF implementation and no 5% tolerance that can hide a thin outline halo.
    public sealed class ScreenProcessRegressionTests
    {
        const int Width = 192, Height = 128, TestLayer = 31;
        const float LeakTolerance = 0.0005f;
        readonly List<Object> owned = new List<Object>();
        RenderPipelineAsset oldGraphics, oldQuality;
        bool oldCompatibility, oldAsync;
        Scene testScene, oldScene;
        RenderTexture output;
        Camera camera;

        T Keep<T>(T obj) where T : Object { owned.Add(obj); return obj; }
        GameObject GameObject(string name)
        {
            var go = Keep(new GameObject(name)); go.layer = TestLayer;
            SceneManager.MoveGameObjectToScene(go, testScene); return go;
        }

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a GPU; do not run with -nographics.");
            oldGraphics = GraphicsSettings.defaultRenderPipeline; oldQuality = QualitySettings.renderPipeline;
            oldCompatibility = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>()?.enableRenderCompatibilityMode ?? false;
            oldAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            oldScene = SceneManager.GetActiveScene();
            // A preview scene does not require saving or modifying an existing untitled scene.
            testScene = EditorSceneManager.NewPreviewScene();
            var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            SetPipeline(renderer);
            camera = GameObject("SP regression camera").AddComponent<Camera>(); camera.enabled = false;
            camera.scene = testScene;
            camera.transform.position = new Vector3(0,0,-5); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 1; camera.cullingMask = 1 << TestLayer;
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << TestLayer;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            output = Keep(new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            output.Create(); Render(); // Initialize URP's shared samplers, blitter and camera state.
        }

        void SetPipeline(UniversalRendererData renderer, int samples = 1)
        {
            var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.msaaSampleCount = samples;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        }

        [TearDown]
        public void TearDown()
        {
            var graphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (graphSettings != null) graphSettings.enableRenderCompatibilityMode = oldCompatibility;
            GraphicsSettings.defaultRenderPipeline = oldGraphics; QualitySettings.renderPipeline = oldQuality;
            ShaderUtil.allowAsyncCompilation = oldAsync;
            for (int i = owned.Count - 1; i >= 0; --i) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (oldScene.IsValid() && oldScene.isLoaded) SceneManager.SetActiveScene(oldScene);
            if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
        }

        Texture2D Texture(Color[] pixels)
        {
            var texture = Keep(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        Texture2D Solid(Color color)
        {
            var pixels = new Color[Width * Height]; Array.Fill(pixels, color); return Texture(pixels);
        }
        Color[] Read()
        {
            var prior = RenderTexture.active; RenderTexture.active = output;
            try
            {
                var texture = Keep(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
                texture.ReadPixels(new Rect(0,0,Width,Height),0,0); texture.Apply(); return texture.GetPixels();
            }
            finally { RenderTexture.active = prior; }
        }
        Color[] Render()
        {
            VolumeManager.instance.Update(camera.transform, 1 << TestLayer);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.StandardRequest { destination = output });
            return Read();
        }
        Material Material(string effect)
        {
            var shader = Shader.Find("Hidden/lilToon/URP/ScreenProcess/" + effect);
            Assert.That(shader, Is.Not.Null, effect + " shader missing");
            var material = Keep(new Material(shader)); material.SetFloat("_Intensity",1);
            material.SetVector("_LayerParams0",new Vector4(1,3,50,5.6f));
            material.SetVector("_LayerParams1",new Vector4(4,18,18,1));
            material.SetVector("_LayerParams2",new Vector4(7,1,0,0)); material.SetVector("_LayerParams3",Vector4.one);
            return material;
        }
        Color[] Draw(Material material, Texture source, Texture depth, Texture outline, Texture coverage,
            bool geometry = true, bool outlineValid = true, bool coverageValid = false, bool cameraDepthValid = false)
        {
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(output); cmd.SetViewport(new Rect(0,0,Width,Height));
                cmd.SetGlobalTexture("_BlitTexture",source);
                cmd.SetGlobalVector("_BlitTexture_TexelSize",new Vector4(1f/Width,1f/Height,Width,Height));
                cmd.SetGlobalVector("_BlitScaleBias",new Vector4(1,1,0,0));
                cmd.SetGlobalVector("_ProjectionParams",new Vector4(1,.1f,100,.01f));
                cmd.SetGlobalFloat("_HoGeometryBufferValid",geometry ? 1 : 0);
                cmd.SetGlobalFloat("_lilHoSPOutlineDepthValid",outlineValid ? 1 : 0);
                cmd.SetGlobalFloat("_HoGeometryBufferOutlineCoverageTextureValid",coverageValid ? 1 : 0);
                cmd.SetGlobalFloat("_lilHoSPCameraDepthValid",cameraDepthValid ? 1 : 0);
                cmd.SetGlobalFloat("_lilHoSPCameraNormalsValid",0);
                cmd.SetGlobalTexture("_HoGeometryBufferNormalDepthTexture",depth);
                cmd.SetGlobalTexture("_HoGeometryBufferOutlineNormalDepthTexture",outline);
                cmd.SetGlobalTexture("_HoGeometryBufferOutlineCoverageTexture",coverage);
                cmd.SetGlobalVector("unity_OrthoParams",new Vector4(2,2,0,1));
                cmd.SetGlobalMatrix("unity_MatrixInvVP",Matrix4x4.identity); cmd.SetGlobalMatrix("unity_MatrixV",Matrix4x4.identity);
                cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3); Graphics.ExecuteCommandBuffer(cmd);
            }
            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False, material.shader.name + " failed to compile");
            return Read();
        }
        void Inputs(float slope, out Color[] colors, out Texture depth, out Texture outline)
        {
            colors = new Color[Width * Height]; var depths = new Color[colors.Length]; var shells = new Color[colors.Length];
            for (int y = 0; y < Height; ++y) for (int x = 0; x < Width; ++x)
            {
                int i = x + Width * y; float d = x + .5f - (76 + slope * (y - Height * .5f));
                bool shell = d >= 0 && d < 3;
                colors[i] = d < 0 ? Color.green : shell ? Color.red : Color.blue;
                depths[i] = new Color(.5f,.5f,1,d < 0 ? 3 : 40);
                shells[i] = shell ? new Color(.5f,.5f,1,3) : Color.clear;
            }
            depth = Texture(depths); outline = Texture(shells);
        }
        static void AssertNoLeak(Color[] original, Color[] actual)
        {
            float leak = 0, shellError = 0;
            for (int i = 0; i < actual.Length; ++i)
            {
                if (original[i].b == 1) leak = Mathf.Max(leak,actual[i].r);
                if (original[i].r == 1) shellError = Mathf.Max(shellError,Mathf.Abs(actual[i].r - 1));
            }
            Assert.That(leak, Is.LessThanOrEqualTo(LeakTolerance), "Focused outline leaked into background");
            Assert.That(shellError, Is.LessThanOrEqualTo(LeakTolerance), "Focused outline itself was blurred");
        }

        [TestCase(0f,0,false), TestCase(.6f,0,true), TestCase(1.3f,0,true)]
        [TestCase(0f,1,false), TestCase(.6f,1,true), TestCase(1.3f,1,true)]
        [TestCase(0f,2,false), TestCase(.6f,2,true), TestCase(1.3f,2,true)]
        public void FocusedOutlineDoesNotLeak(float slope, int mode, bool highQuality)
        {
            Inputs(slope,out var colors,out var depth,out var outline);
            var material = Material("DepthOfField"); material.SetVector("_LayerParams0",new Vector4(mode,3,50,5.6f));
            material.SetVector("_LayerParams1",new Vector4(4,18,18,highQuality ? 1 : 0));
            foreach (float coverage in new[] { .25f,.5f,1f })
                AssertNoLeak(colors,Draw(material,Texture(colors),depth,outline,Solid(new Color(coverage,coverage,0,1)),coverageValid:true));
            AssertNoLeak(colors,Draw(material,Texture(colors),depth,outline,Texture2D.blackTexture)); // 1x depth-validity fallback.
        }

        [Test]
        public void ZeroCoverageAndMissingOutlineDoNotReuseShellDepth()
        {
            Inputs(.6f,out var colors,out var depth,out var outline); var material = Material("DepthOfField");
            var zero = Draw(material,Texture(colors),depth,outline,Texture2D.blackTexture,coverageValid:true);
            var missing = Draw(material,Texture(colors),depth,outline,Texture2D.whiteTexture,outlineValid:false,coverageValid:true);
            float delta = 0, blurredShell = 0;
            for (int i = 0; i < zero.Length; ++i)
            {
                delta = Mathf.Max(delta,Mathf.Abs(zero[i].r - missing[i].r));
                if (colors[i].r == 1) blurredShell = Mathf.Max(blurredShell,1-zero[i].r);
            }
            Assert.That(delta,Is.LessThan(LeakTolerance));
            Assert.That(blurredShell,Is.GreaterThan(.1f),"Explicit zero coverage must override a stale nonzero depth");
        }

        [Test]
        public void DefocusedOutlineStillBlurs()
        {
            Inputs(.6f,out var colors,out var depth,out var outline); var material = Material("DepthOfField");
            material.SetVector("_LayerParams0",new Vector4(1,10,50,5.6f)); material.SetVector("_LayerParams3",new Vector4(6,1,1,1));
            var actual = Draw(material,Texture(colors),depth,outline,Texture2D.blackTexture);
            float change = 0; for (int i = 0; i < actual.Length; ++i) if (colors[i].r == 1) change = Mathf.Max(change,1-actual[i].r);
            Assert.That(change,Is.GreaterThan(.1f),"Do not reintroduce the old force-sharp outline workaround");
        }

        [Test]
        public void OutlineDepthOffsetCanDefocusAnOtherwiseFocusedOwner()
        {
            Inputs(.6f,out var colors,out var depth,out var originalOutline);
            var material = Material("DepthOfField");
            AssertNoLeak(colors,Draw(material,Texture(colors),depth,originalOutline,Texture2D.blackTexture));
            var shifted = new Color[colors.Length];
            for (int i = 0; i < shifted.Length; ++i) if (colors[i].r == 1) shifted[i] = new Color(.5f,.5f,1,6);
            var actual = Draw(material,Texture(colors),depth,Texture(shifted),Texture2D.blackTexture);
            float change = 0;
            for (int i = 0; i < actual.Length; ++i) if (colors[i].r == 1) change = Mathf.Max(change,1-actual[i].r);
            Assert.That(change,Is.GreaterThan(.05f),"A shell displaced from the focus plane must retain its own visual depth");
        }

        [Test]
        public void WeightedGatherPreservesUniformColor()
        {
            Inputs(.6f,out _,out var depth,out var outline);
            var color = new Color(.2f,.5f,.8f,1);
            var actual = Draw(Material("DepthOfField"),Solid(color),depth,outline,Texture2D.blackTexture);
            foreach (var pixel in actual)
            {
                Assert.That(pixel.r,Is.EqualTo(color.r).Within(.001f));
                Assert.That(pixel.g,Is.EqualTo(color.g).Within(.001f));
                Assert.That(pixel.b,Is.EqualTo(color.b).Within(.001f));
            }
        }

        [Test]
        public void MissingDepthInputsPreserveColor()
        {
            Inputs(.6f,out var colors,out var depth,out var outline);
            var actual = Draw(Material("DepthOfField"),Texture(colors),depth,outline,Texture2D.whiteTexture,geometry:false);
            for (int i = 0; i < colors.Length; ++i) Assert.That(actual[i].r,Is.EqualTo(colors[i].r).Within(LeakTolerance));
        }

        [Test]
        public void OrthographicDepthFallbackKeepsFocusedColor()
        {
            Inputs(.6f,out var colors,out var depth,out var outline);
            float raw = (3-.1f)/(100-.1f); if (SystemInfo.usesReversedZBuffer) raw = 1-raw;
            Shader.SetGlobalTexture("_CameraDepthTexture",Solid(new Color(raw,0,0,1)));
            var actual = Draw(Material("DepthOfField"),Texture(colors),depth,outline,Texture2D.blackTexture,geometry:false,cameraDepthValid:true);
            for (int i = 0; i < colors.Length; ++i) Assert.That(actual[i].r,Is.EqualTo(colors[i].r).Within(LeakTolerance));
        }

        [Test]
        public void HeightFogUsesScreenUvExactlyOnce()
        {
            var material = Material("DepthFog"); material.SetVector("_LayerParams0",Vector4.zero);
            material.SetVector("_LayerParams2",new Vector4(0,0,0,1)); material.SetVector("_LayerParams3",new Vector4(0,0,-1,1));
            material.SetVector("_LayerParams4",new Vector4(1,1,1,0)); material.SetVector("_LayerParams5",new Vector4(0,1,1,0));
            Shader.SetGlobalTexture("_CameraDepthTexture",Texture2D.whiteTexture);
            var actual = Draw(material,Solid(Color.black),Solid(new Color(.5f,.5f,1,5)),Texture2D.blackTexture,Texture2D.blackTexture,cameraDepthValid:true);
            for (int y = 0; y < Height; ++y)
            {
                float height = ((y+.5f)/Height)*2-1; if (SystemInfo.graphicsUVStartsAtTop) height = -height;
                float t = Mathf.Clamp01((height+1)*.5f); float expected = 1-t*t*(3-2*t);
                Assert.That(actual[Width/2+Width*y].r,Is.EqualTo(expected).Within(.001f),"Height fog row " + y);
            }
        }

        [TestCase(1), TestCase(2), TestCase(4)]
        public void PrivateOutlineDepthCopyPreservesStencil(int samples)
        {
            var desc = new RenderTextureDescriptor(Width,Height,UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt) { msaaSamples=samples,bindMS=samples>1 };
            var source = Keep(new RenderTexture(desc)); source.Create(); var destination = Keep(new RenderTexture(desc)); destination.Create();
            output.Release(); output.antiAliasing = samples; output.Create();
            var material = Keep(new Material(Shader.Find("Hidden/lilToon/URP/Tests/OutlineStencilCopyProbe")));
            foreach (bool copy in new[]{false,true})
            {
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(output,destination); cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
                    cmd.SetRenderTarget(output,source); cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
                    cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
                    cmd.SetRenderTarget(output);
                    if (copy) cmd.CopyTexture(source,destination);
                    cmd.SetRenderTarget(output,destination); cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                    cmd.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,3); Graphics.ExecuteCommandBuffer(cmd);
                }
                Assert.That(Read()[Width/2 + Width*Height/2].r,Is.EqualTo(copy ? 1 : 0).Within(.001f),"Depth-only copy preserves stencil at " + samples + "x");
            }
        }

        [TestCase(false,1), TestCase(true,1), TestCase(false,2), TestCase(true,2), TestCase(false,4), TestCase(true,4)]
        public void AcOwnsOutlineIdentityAndSemanticsWithoutChangingPhysicalDomain(bool compatibility, int cameraSamples)
        {
            var rd = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            var ob = Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>());
            ob.Settings.useFallbackMaterial = false; ob.Settings.sampleCount = HoObjectBufferSampleCount.Four;
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
            rd.rendererFeatures.Add(ob); rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()));
            SetPipeline(rd,cameraSamples); GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
            output.Release(); output.antiAliasing = cameraSamples; output.Create();
            var actor = Keep(UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Quad)); actor.layer = TestLayer;
            SceneManager.MoveGameObjectToScene(actor,testScene); actor.transform.localScale = Vector3.one * 4;
            var material = Keep(new Material(Shader.Find("Hidden/lilToon/URP/Tests/OutlineHandoffSubject"))); material.enableInstancing = true;
            actor.GetComponent<Renderer>().sharedMaterial = material;
            var group = actor.AddComponent<HoObjectBufferGroup>();
            group.parts.Add(new HoObjectBufferPartEntry { name="Face",tags=HoObjectBufferPartTags.CharacterFull | HoObjectBufferPartTags.Face,
                renderers=new Object[]{actor.GetComponent<Renderer>()} }); group.Apply();
            var volume = GameObject("AC outline owner volume").AddComponent<Volume>(); volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume.sharedProfile = profile;
            var stack = profile.Add<ScreenProcessStackVolume>(true); stack.Enable.Override(true);
            var layer = new ScreenProcessLayer { name="AC outline inheritance",effect=ScreenProcessEffect.DepthOfField,intensity=0 };
            stack.layers.Override(new List<ScreenProcessLayer>{layer}); var original = Render();
            layer.intensity = 1; layer.debugMask = true;
            Color[] Query(HoACQueryKind kind, HoACMaskDomain domain = HoACMaskDomain.Screen)
            {
                layer.maskSource = kind; layer.maskDomain = domain; layer.maskSemanticName = "Face";
                layer.maskId = kind == HoACQueryKind.Identity ? (int)HoObjectBufferRegistry.GetPartId(group.groupId,"Face") : group.groupId;
                return Render();
            }
            foreach (var kind in new[]{HoACQueryKind.TotalCoverage,HoACQueryKind.Group,HoACQueryKind.Identity,HoACQueryKind.Semantic})
            {
                var visual = Query(kind); var physical = Query(kind,HoACMaskDomain.Geometry);
                for (int i = 0; i < original.Length; ++i)
                {
                    float visible = original[i].r + original[i].g;
                    Assert.That(visual[i].r,Is.EqualTo(visible).Within(.006f),kind + " visible subject + shell pixel " + i);
                    Assert.That(physical[i].r,Is.EqualTo(original[i].g).Within(.006f),kind + " physical pool excludes shell pixel " + i);
                }
            }
            var shellOnly = Query(HoACQueryKind.Semantic,HoACMaskDomain.Outline);
            for (int i = 0; i < original.Length; ++i) Assert.That(shellOnly[i].r,Is.EqualTo(original[i].r).Within(.006f));
            foreach (float weight in new[]{.25f,0f})
            {
                material.SetFloat("_HoSemanticWeight",weight); var semantic = Query(HoACQueryKind.Semantic);
                for (int i = 0; i < original.Length; ++i)
                    Assert.That(semantic[i].r,Is.EqualTo((original[i].r+original[i].g)*weight).Within(.006f),"Surface semantic weight=" + weight + " pixel=" + i);
                var identity = Query(HoACQueryKind.Identity);
                for (int i = 0; i < original.Length; ++i) Assert.That(identity[i].r,Is.EqualTo(original[i].r+original[i].g).Within(.006f));
            }
            actor.SetActive(false); var cleared = Query(HoACQueryKind.Semantic);
            foreach (var pixel in cleared) Assert.That(pixel.r,Is.LessThan(.001f),"Removed outline cannot retain old ownership");
        }

        [TestCase(false,1), TestCase(true,1), TestCase(false,4), TestCase(true,4)]
        public void NativeLilToonOutlineRetainsItsRendererOwnerDuringCameraMotion(bool compatibility, int samples)
        {
            var shader = Shader.Find("Hidden/lilToonOutline");
            if (shader == null) Assert.Ignore("Native lilToon integration requires the lilToon package.");
            var rd = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()));
            SetPipeline(rd,samples); GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
            output.Release(); output.antiAliasing = samples; output.Create();
            var actor = Keep(UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Sphere)); actor.layer = TestLayer;
            SceneManager.MoveGameObjectToScene(actor,testScene); actor.transform.localScale = Vector3.one * 1.25f;
            var material = Keep(new Material(shader)); material.enableInstancing = true;
            material.SetFloat("_OutlineWidth",2); material.SetFloat("_OutlineFixWidth",0); material.SetFloat("_HoSemanticWeight",1);
            actor.GetComponent<Renderer>().sharedMaterial = material;
            var group = actor.AddComponent<HoObjectBufferGroup>(); group.parts.Add(new HoObjectBufferPartEntry {
                name="Face",tags=HoObjectBufferPartTags.CharacterFull | HoObjectBufferPartTags.Face,
                renderers=new Object[]{actor.GetComponent<Renderer>()} }); group.Apply();
            var volume = GameObject("Native owner volume").AddComponent<Volume>(); volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume.sharedProfile = profile;
            var stack = profile.Add<ScreenProcessStackVolume>(true); stack.Enable.Override(true);
            var layer = new ScreenProcessLayer { name="Native outline owner",effect=ScreenProcessEffect.DepthOfField,intensity=1,
                debugMask=true,maskSource=HoACQueryKind.Group,maskId=group.groupId };
            stack.layers.Override(new List<ScreenProcessLayer>{layer});
            for (int frame = 0; frame < 4; ++frame)
            {
                camera.transform.position = new Vector3((frame-1.5f)*.13f,0,-5);
                layer.maskSource = HoACQueryKind.Outline; layer.maskDomain = HoACMaskDomain.Screen; var shell = Render();
                layer.maskSource = HoACQueryKind.Group; layer.maskDomain = HoACMaskDomain.Outline; var owner = Render();
                layer.maskSource = HoACQueryKind.Semantic; layer.maskSemanticName = "Face"; var semantic = Render();
                int shellPixels = 0;
                for (int i = 0; i < shell.Length; ++i)
                {
                    if (shell[i].r < .99f) continue; // Full shell pixels have an unambiguous forward owner.
                    shellPixels++;
                    Assert.That(owner[i].r,Is.GreaterThan(.99f),"Native outline loses owner frame=" + frame + " pixel=" + i);
                    Assert.That(semantic[i].r,Is.GreaterThan(.99f),"Native outline loses Face semantics frame=" + frame + " pixel=" + i);
                }
                Assert.That(shellPixels,Is.GreaterThan(8),"Native writer fixture must have a visible expanded outline");
                Assert.That(ShaderUtil.ShaderHasError(shader),Is.False,"Native lilToon outline writer failed to compile");
            }
            material.SetFloat("_HoSemanticMaskOn",1); material.EnableKeyword("_HO_SEMANTIC_MASK"); material.SetTexture("_HoSemanticWeightTex",Solid(new Color(.75f,0,0,1)));
            layer.maskSource = HoACQueryKind.Outline; var weightedShell = Render();
            layer.maskSource = HoACQueryKind.Semantic; var weightedSemantic = Render();
            for (int i = 0; i < weightedShell.Length; ++i)
                if (weightedShell[i].r > .99f) Assert.That(weightedSemantic[i].r,Is.EqualTo(.75f).Within(.006f),"Native shell inherits the SurfaceBuffer material mask R channel");
        }

        [TestCase(false), TestCase(true)]
        public void OverlappingNativeOutlinesInheritTheForwardVisibleCharacter(bool compatibility)
        {
            var shader = Shader.Find("Hidden/lilToonOutline");
            if (shader == null) Assert.Ignore("Requires native lilToon.");
            var rd = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()));
            SetPipeline(rd); GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
            HoObjectBufferGroup Actor(string name, Vector3 position, Color color, HoObjectBufferPartTags tags)
            {
                var actor = Keep(UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Sphere)); actor.layer = TestLayer;
                SceneManager.MoveGameObjectToScene(actor,testScene); actor.transform.position = position; actor.transform.localScale = Vector3.one * 1.2f;
                var material = Keep(new Material(shader)); material.enableInstancing = true;
                material.SetFloat("_OutlineWidth",3); material.SetFloat("_OutlineFixWidth",0); material.SetColor("_OutlineColor",color);
                material.SetFloat("_OutlineEnableLighting",0); material.SetFloat("_HoSemanticWeight",1);
                actor.GetComponent<Renderer>().sharedMaterial = material;
                var group = actor.AddComponent<HoObjectBufferGroup>(); group.parts.Add(new HoObjectBufferPartEntry {
                    name=name,tags=tags | HoObjectBufferPartTags.CharacterFull,renderers=new Object[]{actor.GetComponent<Renderer>()} }); group.Apply(); return group;
            }
            var face = Actor("Face",new Vector3(-.2f,0,.15f),Color.red,HoObjectBufferPartTags.Face);
            var hair = Actor("Hair",new Vector3(.2f,.13f,0),Color.blue,HoObjectBufferPartTags.FrontHair);
            var volume = GameObject("Cross-owner volume").AddComponent<Volume>(); volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume.sharedProfile = profile;
            var stack = profile.Add<ScreenProcessStackVolume>(true); stack.Enable.Override(true);
            var layer = new ScreenProcessLayer { name="Cross-owner",effect=ScreenProcessEffect.DepthOfField,intensity=0 };
            stack.layers.Override(new List<ScreenProcessLayer>{layer}); var forward = Render();
            layer.intensity = 1; layer.debugMask = true; layer.maskSource = HoACQueryKind.Outline; var shell = Render();
            layer.maskSource = HoACQueryKind.Group; layer.maskId = face.groupId; var faceMask = Render();
            layer.maskId = hair.groupId; var hairMask = Render();
            int facePixels = 0, hairPixels = 0;
            for (int i = 0; i < forward.Length; ++i)
            {
                if (shell[i].r < .99f) continue;
                bool red = forward[i].r > .98f && forward[i].g < .01f && forward[i].b < .01f;
                bool blue = forward[i].b > .98f && forward[i].g < .01f && forward[i].r < .01f;
                if (!red && !blue) continue;
                if (red) facePixels++; else hairPixels++;
                Assert.That(faceMask[i].r,Is.EqualTo(red ? 1 : 0).Within(.006f),"Forward-visible Face outline owner pixel " + i);
                Assert.That(hairMask[i].r,Is.EqualTo(blue ? 1 : 0).Within(.006f),"Forward-visible Hair outline owner pixel " + i);
            }
            Assert.That(facePixels,Is.GreaterThan(8)); Assert.That(hairPixels,Is.GreaterThan(8));
        }

        [TestCase(false,1), TestCase(true,1), TestCase(false,4), TestCase(true,4)]
        public void MovingStencilCharacterCannotLeaveAnyAcMaskOutsideCurrentForwardSilhouette(bool compatibility, int samples)
        {
            var shader = Shader.Find("Hidden/lilToonOutline"); if (shader == null) Assert.Ignore("Requires native lilToon.");
            var rd = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoObjectBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>()));
            rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()));
            SetPipeline(rd,samples); GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
            output.Release(); output.antiAliasing = samples; output.Create();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.blue;
            var actor = Keep(UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Sphere)); actor.layer = TestLayer;
            SceneManager.MoveGameObjectToScene(actor,testScene); actor.transform.localScale = Vector3.one;
            var material = Keep(new Material(shader)); material.enableInstancing = true;
            material.SetFloat("_AsUnlit",1); material.SetColor("_Color",Color.green); material.SetColor("_OutlineColor",Color.red);
            material.SetFloat("_OutlineEnableLighting",0); material.SetFloat("_OutlineWidth",3); material.SetFloat("_OutlineFixWidth",0);
            material.SetFloat("_StencilRef",42); material.SetFloat("_StencilComp",(float)CompareFunction.GreaterEqual);
            material.SetFloat("_StencilPass",(float)StencilOp.Zero);
            actor.GetComponent<Renderer>().sharedMaterial = material;
            var group = actor.AddComponent<HoObjectBufferGroup>(); group.parts.Add(new HoObjectBufferPartEntry {
                name="Face",tags=HoObjectBufferPartTags.CharacterFull | HoObjectBufferPartTags.Face,renderers=new Object[]{actor.GetComponent<Renderer>()} }); group.Apply();
            var volume = GameObject("Stencil motion volume").AddComponent<Volume>(); volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume.sharedProfile = profile;
            var stack = profile.Add<ScreenProcessStackVolume>(true); stack.Enable.Override(true);
            var layer = new ScreenProcessLayer { name="Stencil motion",effect=ScreenProcessEffect.DepthOfField,intensity=0,maskSemanticName="Face",maskId=group.groupId };
            stack.layers.Override(new List<ScreenProcessLayer>{layer});
            for (int frame = 0; frame < 10; ++frame)
            {
                actor.transform.position = new Vector3(frame%2 == 0 ? -.6f : .6f,(frame%3-1)*.15f,0);
                camera.transform.position = new Vector3((frame%4-1.5f)*.15f,0,-5);
                layer.debugMask = false; layer.intensity = 0; var color = Render();
                int visible = 0; foreach (var p in color) if (p.r+p.g > .1f) visible++;
                Assert.That(visible,Is.GreaterThan(500),"Forward silhouette oracle must contain the actor");
                foreach (var kind in new[]{HoACQueryKind.TotalCoverage,HoACQueryKind.Group,HoACQueryKind.Semantic})
                {
                    layer.intensity = 1; layer.debugMask = true; layer.maskSource = kind; var mask = Render();
                    int ghosts = 0;
                    // OB has independent 4x AA while the camera may be 1x, and its visible shell
                    // adds a narrow border. Reject history outside that CURRENT border, rather than
                    // mislabelling a legitimate shell/AA edge as a temporal trail.
                    for (int y = 0; y < Height; ++y) for (int x = 0; x < Width; ++x)
                    {
                        int i = x + Width*y; if (mask[i].r <= .01f) continue;
                        bool current = false;
                        for (int dy = -5; dy <= 5 && !current; ++dy) for (int dx = -5; dx <= 5; ++dx)
                        {
                            int sx = Mathf.Clamp(x+dx,0,Width-1), sy = Mathf.Clamp(y+dy,0,Height-1);
                            if (color[sx+Width*sy].r+color[sx+Width*sy].g > .1f) { current = true; break; }
                        }
                        if (!current) ghosts++;
                    }
                    Assert.That(ghosts,Is.Zero,"Outside current forward silhouette: frame=" + frame + " query=" + kind);
                }
            }
        }

        [TestCase(false,1), TestCase(true,1), TestCase(false,2), TestCase(true,2), TestCase(false,4), TestCase(true,4)]
        public void RealPipelineTransfersOutlineAndProtectsItsColor(bool compatibility, int samples)
        {
            var rd = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            var gb = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
            var ac = Keep(ScriptableObject.CreateInstance<HoAttributeCompositeRendererFeature>());
            rd.rendererFeatures.Add(gb); rd.rendererFeatures.Add(ac); rd.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()));
            SetPipeline(rd,samples); GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
            output.Release(); output.antiAliasing = samples; output.Create();
            var actor = Keep(UnityEngine.GameObject.CreatePrimitive(PrimitiveType.Quad)); actor.layer = TestLayer;
            SceneManager.MoveGameObjectToScene(actor,testScene); actor.transform.localScale = Vector3.one * 4;
            actor.GetComponent<Renderer>().sharedMaterial = Keep(new Material(Shader.Find("Hidden/lilToon/URP/Tests/OutlineHandoffSubject")));
            var volume = GameObject("SP regression volume").AddComponent<Volume>(); volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>()); volume.sharedProfile = profile;
            var stack = profile.Add<ScreenProcessStackVolume>(true); stack.Enable.Override(true);
            var layer = new ScreenProcessLayer { name="Outline regression",effect=ScreenProcessEffect.DepthOfField,intensity=0,
                parameters0=new Vector4(1,3,50,5.6f),parameters1=new Vector4(4,18,18,1),parameters2=new Vector4(7,1,0,0),parameters3=Vector4.one };
            stack.layers.Override(new List<ScreenProcessLayer>{layer});
            var baseline = Render(); layer.intensity = 1; var dof = Render(); AssertNoLeak(baseline,dof);
            Assert.That(ScreenProcessRuntimeDiagnostics.CurrentSnapshot.RequiresOutlineDepth,Is.True);
            Assert.That(ScreenProcessRuntimeDiagnostics.CurrentSnapshot.OutlineDepthAvailable,Is.True);
            layer.debugMask = true; layer.maskSource = HoACQueryKind.Outline; var mask = Render();
            int shellCount = 0;
            for (int i = 0; i < mask.Length; ++i)
            {
                Assert.That(mask[i].r,Is.EqualTo(baseline[i].r).Within(LeakTolerance),"AC outline handoff pixel " + i);
                if (mask[i].r > .5f) shellCount++;
            }
            Assert.That(shellCount,Is.GreaterThan(200),"Fixture must contain a visible outline");
            layer.debugMask = false; ac.SetActive(false); var noAc = Render();
            for (int i = 0; i < dof.Length; ++i) Assert.That(noAc[i].r,Is.EqualTo(dof[i].r).Within(LeakTolerance));
            gb.SetActive(false); layer.parameters0 = new Vector4(1,5,50,5.6f);
            var fallback = Render(); // The real quad is at eye depth 5, even though the fixture's GB encoded 3/40.
            for (int i = 0; i < fallback.Length; ++i)
                Assert.That(fallback[i].r,Is.EqualTo(baseline[i].r).Within(.001f),"Focused orthographic camera-depth fallback pixel " + i);
            layer.effect = ScreenProcessEffect.DepthFog;
            layer.parameters0 = new Vector4(1,0,0,10); layer.parameters1 = new Vector4(1,1,0,0);
            layer.parameters2 = Vector4.zero; layer.color = Color.red; var fog = Render();
            Assert.That(fog[Width/2 + Width*10].r,Is.EqualTo(.5f).Within(.005f),"Actual orthographic depth-fog fallback uses the camera's eye depth 5");
            Assert.That(ScreenProcessRuntimeDiagnostics.CurrentSnapshot.RequiresNormalDepth,Is.True,"DepthFog must declare its desired GB input in diagnostics");
        }
    }
}
