using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryData;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Animations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using NUnit.Framework;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    public sealed class HoCharacterRimValidation
    {
        RenderPipelineAsset previousDefault, previousQuality;
        UniversalRenderPipelineAsset testPipeline;
        UniversalRendererData testRenderer;

        [OneTimeSetUp]
        public void InitializeRealUrpResourcePool()
        {
            previousDefault = GraphicsSettings.defaultRenderPipeline;
            previousQuality = QualitySettings.renderPipeline;
            testRenderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            testPipeline = UniversalRenderPipelineAsset.Create(testRenderer);
            GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = testPipeline;
            var cameraObject = new GameObject("Rim test pipeline warmup");
            var target = new RenderTexture(16, 16, 24);
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.StandardRequest { destination = target });
                Assert.That(RenderPipelineManager.currentPipeline, Is.TypeOf<UniversalRenderPipeline>());
            }
            finally { Object.DestroyImmediate(cameraObject); target.Release(); Object.DestroyImmediate(target); }
        }

        [OneTimeTearDown]
        public void RestorePipelineAssets()
        {
            GraphicsSettings.defaultRenderPipeline = previousDefault;
            QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(testPipeline); Object.DestroyImmediate(testRenderer);
        }

        [Test] public void SourceLifecycleBudgetsAndOriginalCasterIsolation() => Validate();

        [Test]
        public void SmallAtlasRetainsValidShadowTilesAndExtremeSizeIsClamped()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Small Rim atlas");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Material material = null; HoCharacterRimPass pass = null;
            try
            {
                var group = Child(root.transform, "Role").AddComponent<HoObjectBufferGroup>();
                var renderer = Cube(group.transform, "Cloth", Vector3.zero);
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")); renderer.sharedMaterial = material;
                group.parts.Add(new HoObjectBufferPartEntry { name="Cloth", includeChildren=false, renderers=new Object[] { renderer } });
                for(int i=0;i<65;i++) Child(group.transform,"Rim " + i.ToString("D2")).AddComponent<HoCharacterRimLight>();
                var camera = Child(root.transform,"Camera").AddComponent<Camera>();
                camera.enabled=false; camera.transform.position=new Vector3(0,0,-5);
                HoObjectBufferRegistry.MarkDirty(); HoObjectBufferRegistry.EnsureBuilt();
                var config = HoCharacterRimConfiguration.Default;
                config.enabled=true; config.maxLightsPerCharacter=256; config.shadowBudget=256; config.atlasSize=128;
                pass = new HoCharacterRimPass();
                var frame = Prepare(pass,config,camera);
                Assert.That(pass.LastLightCount,Is.EqualTo(64),"A small atlas discarded valid sources instead of filling its capacity");
                Assert.That(pass.LastShadowCount,Is.EqualTo(64)); Assert.That(pass.LastRejectedCount,Is.EqualTo(1));
                foreach(object slice in (IEnumerable)Field<object>(frame,"slices"))
                {
                    Rect viewport=Field<Rect>(slice,"viewport");
                    Assert.That(viewport.width,Is.GreaterThanOrEqualTo(16)); Assert.That(viewport.xMax,Is.LessThanOrEqualTo(128));
                    Assert.That(viewport.height,Is.GreaterThanOrEqualTo(16)); Assert.That(viewport.yMax,Is.LessThanOrEqualTo(128));
                }
                config.atlasSize=int.MaxValue;
                var sanitize=typeof(HoCharacterRimConfiguration).GetMethod("Sanitized",BindingFlags.Instance|BindingFlags.NonPublic);
                var sanitized=(HoCharacterRimConfiguration)sanitize.Invoke(config,null);
                Assert.That(sanitized.atlasSize,Is.EqualTo(Mathf.Max(128,Mathf.Min(16384,SystemInfo.maxTextureSize))),
                    "An extreme atlas request overflowed NextPowerOfTwo and fell back to minimum resolution");
            }
            finally
            {
                pass?.Dispose(); Object.DestroyImmediate(root); if(material!=null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene); HoObjectBufferRegistry.MarkDirty();
            }
        }

        [Test]
        public void AtlasFormatUsesRawShadowDepthCapabilityAndRenderableDepth()
        {
            MethodInfo method = typeof(HoCharacterRimPass).GetMethod("SelectShadowFormat", BindingFlags.Static | BindingFlags.NonPublic);
            var format = (GraphicsFormat)method.Invoke(null, null);
            bool depthSrv = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth);
            if (!SystemInfo.supportsRawShadowDepthSampling && !depthSrv) Assert.That(format, Is.EqualTo(GraphicsFormat.None));
            else if (SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat, GraphicsFormatUsage.Render))
                Assert.That(format, Is.Not.EqualTo(GraphicsFormat.None), "D32 Render plus raw-shadow sampling must not be rejected by D32 Sample=false");
            if (format != GraphicsFormat.None) Assert.That(SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render), Is.True);
            Debug.Log($"[Ho Rim Depth Capability] raw={SystemInfo.supportsRawShadowDepthSampling}, D32Render={SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat, GraphicsFormatUsage.Render)}, D32Sample={SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat, GraphicsFormatUsage.Sample)}, selected={format}. Actual shadow texels/visibility are qualified separately.");
        }

        [UnityTest]
        public IEnumerator ParentConstraintPoseFeedsOnePrepareSnapshotWithoutChangingOwner()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Rim Constraint Test");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Material material = null;
            HoCharacterRimPass pass = null;
            try
            {
                var originalRole = Child(root.transform, "Owner").AddComponent<HoObjectBufferGroup>();
                var targetRole = Child(root.transform, "Constraint target role").AddComponent<HoObjectBufferGroup>();
                var renderer = Cube(originalRole.transform, "Body", Vector3.zero);
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                renderer.sharedMaterial = material;
                originalRole.parts.Add(new HoObjectBufferPartEntry { name = "Body", includeChildren = false,
                    renderers = new Object[] { renderer } });
                var bone = Child(targetRole.transform, "Bone").transform;
                bone.position = new Vector3(.7f, 1.2f, .4f);
                bone.rotation = Quaternion.Euler(12, 26, 41);
                var source = Child(originalRole.transform, "Rim").AddComponent<HoCharacterRimLight>();
                var constraint = source.gameObject.AddComponent<ParentConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = bone, weight = 1 });
                constraint.SetTranslationOffset(0, Vector3.zero);
                constraint.SetRotationOffset(0, Vector3.zero);
                constraint.weight = 1; constraint.locked = true; constraint.constraintActive = true;
                EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                Assert.That(Vector3.Distance(source.transform.position, bone.position), Is.LessThan(1e-4f),
                    "ParentConstraint was not evaluated by the EditMode player loop; source pose was never assigned manually");
                Assert.That(Quaternion.Angle(source.transform.rotation, bone.rotation), Is.LessThan(.05f));
                Vector3 oldDirection = source.Direction;
                bone.position = new Vector3(1.1f, 1.5f, -.2f);
                bone.rotation = Quaternion.Euler(-31, 68, 23);
                EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                Assert.That(Vector3.Distance(source.transform.position, bone.position), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(source.transform.rotation, bone.rotation), Is.LessThan(.05f));
                Assert.That(Vector3.Angle(oldDirection, source.Direction), Is.GreaterThan(1), "Bone motion did not change the evaluated source direction");
                Assert.That(source.OwnerGroup, Is.SameAs(originalRole), "Constraint target replaced parent-chain ownership");
                HoObjectBufferRegistry.MarkDirty(); HoObjectBufferRegistry.EnsureBuilt();
                var camera = Child(root.transform, "Camera").AddComponent<Camera>();
                camera.enabled = false; camera.transform.position = new Vector3(0, 0, -5);
                var config = HoCharacterRimConfiguration.Default;
                config.enabled = true; config.atlasSize = 256;
                pass = new HoCharacterRimPass();
                object frame = Prepare(pass, config, camera);
                Assert.That(pass.LastLightCount, Is.EqualTo(1));
                Assert.That(pass.LastShadowCount, Is.EqualTo(1));
                object gpu = ((Array)Field<object>(frame, "lights")).GetValue(0);
                Vector4 uploadedDirection = Field<Vector4>(gpu, "directionWidth");
                Assert.That(Vector3.Distance((Vector3)uploadedDirection, -source.transform.forward), Is.LessThan(1e-5));
                object slice = ((Array)Field<object>(frame, "slices")).GetValue(0);
                Matrix4x4 actualView = Field<Matrix4x4>(slice, "view");
                Matrix4x4 expectedRotation = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Rotate(Quaternion.Inverse(source.transform.rotation));
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 3; column++)
                        Assert.That(actualView[row, column], Is.EqualTo(expectedRotation[row, column]).Within(1e-5),
                            "Shadow view and source direction used different constraint poses");
                Assert.That(Field<Vector4[]>(frame, "ranges")[targetRole.groupId].y, Is.EqualTo(0));
                Debug.Log("[Ho Rim Constraint Test] PASS: real ParentConstraint evaluated across editor ticks; CPU Prepare light-data direction and shadow-view rotation match one evaluated pose. This is not final shader GPU evidence.");
            }
            finally
            {
                pass?.Dispose(); Object.DestroyImmediate(root);
                if (material != null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene); HoObjectBufferRegistry.MarkDirty();
            }
        }

        [Test]
        public void CameraViewportKeepsDirectOffsetsAndUsesScaledIntermediateSize()
        {
            var root = new GameObject("Rim Viewport Test");
            var texture = new RenderTexture(400, 200, 0);
            try
            {
                var camera = Child(root.transform, "Base").AddComponent<Camera>();
                camera.enabled = false; camera.targetTexture = texture;
                camera.rect = new Rect(.25f, .2f, .5f, .6f);
                var overlay = Child(root.transform, "Overlay").AddComponent<Camera>();
                overlay.enabled = false; overlay.rect = new Rect(0, 0, .1f, .1f);
                MethodInfo method = typeof(HoCharacterRimPass).GetMethod("ResolveCameraViewport", BindingFlags.Static | BindingFlags.NonPublic);
                var direct = (Rect)method.Invoke(null, new object[] { camera, null, true, 100, 60 });
                var intermediate = (Rect)method.Invoke(null, new object[] { camera, null, false, 100, 60 });
                var stacked = (Rect)method.Invoke(null, new object[] { overlay, camera, true, 100, 60 });
                Assert.That(direct, Is.EqualTo(camera.pixelRect), "Direct output lost its pixel viewport");
                Assert.That(intermediate, Is.EqualTo(new Rect(0, 0, 100, 60)), "Render scale was applied twice or retained an output offset");
                Assert.That(stacked, Is.EqualTo(camera.pixelRect), "Overlay did not inherit the base output viewport");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void OutsideShadowProjectionStillHonorsShadowStrength()
        {
            Shader shader = Shader.Find("Hidden/HoRim/IdentityProbe");
            Assert.That(shader != null && !ShaderUtil.ShaderHasError(shader), Is.True);
            var material = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture old = RenderTexture.active;
            try
            {
                material.SetFloat("_HoRimProbeBoundary", 1);
                material.SetTexture("_HoRimShadowAtlas", Texture2D.blackTexture);
                target.Create(); Graphics.Blit(Texture2D.blackTexture, target, material);
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); readback.Apply();
                Color result = readback.GetPixel(0, 0);
                Assert.That(result.r, Is.EqualTo(1).Within(1e-5));
                Assert.That(result.g, Is.EqualTo(.75f).Within(1e-5));
                Assert.That(result.b, Is.EqualTo(0).Within(1e-5));
            }
            finally
            {
                RenderTexture.active = old; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(material);
            }
        }

        public static void Validate()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Rim Validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Material material = null;
            HoCharacterRimPass pass = null;
            try
            {
                var roleA = Child(root.transform, "Character A");
                var roleB = Child(root.transform, "Character B");
                var a = roleA.AddComponent<HoObjectBufferGroup>();
                var b = roleB.AddComponent<HoObjectBufferGroup>();
                var near = Cube(roleA.transform, "Coat", Vector3.zero);
                var hidden = Cube(roleA.transform, "Offscreen Arm", new Vector3(50, 0, 0));
                var other = Cube(roleB.transform, "Other Role", Vector3.zero);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "URP Lit is required for original ShadowCaster validation");
                material = new Material(shader);
                near.sharedMaterial = hidden.sharedMaterial = other.sharedMaterial = material;
                hidden.shadowCastingMode = ShadowCastingMode.TwoSided;
                a.parts.Add(new HoObjectBufferPartEntry { name = "Coat", includeChildren = false,
                    renderers = new Object[] { near } });
                a.parts.Add(new HoObjectBufferPartEntry { name = "Arm", includeChildren = false,
                    renderers = new Object[] { hidden } });
                b.parts.Add(new HoObjectBufferPartEntry { name = "Coat", includeChildren = false,
                    renderers = new Object[] { other } });
                a.selections.Add(new HoObjectBufferSelectionEntry { name = "Rim Validation Coat",
                    includeChildren = false, renderers = new Object[] { near } });
                HoObjectBufferRegistry.MarkDirty();
                HoObjectBufferRegistry.EnsureBuilt();
                ValidateGpuIdentityQueries(a, b, near);
                var lightRoot = Child(roleA.transform, "Sources");
                var sources = new List<HoCharacterRimLight>();
                for (int i = 0; i < 4; i++)
                {
                    var source = Child(lightRoot.transform, "Source " + i).AddComponent<HoCharacterRimLight>();
                    source.castShadows = false;
                    source.receiverRegion = new HoCharacterRimRegion { kind = HoCharacterRimRegionKind.Part, name = "Coat" };
                    sources.Add(source);
                }
                Require(sources[0].OwnerGroup == a, "Automatic parent OB binding failed");
                // Reparent an ancestor, not the source itself: collection must re-check its current chain.
                lightRoot.transform.SetParent(roleB.transform);
                Require(sources[0].OwnerGroup == b, "Ancestor reparent kept the old role");
                lightRoot.transform.SetParent(roleA.transform);
                var nested = lightRoot.AddComponent<HoObjectBufferGroup>();
                nested.enabled = false;
                Require(sources[0].OwnerGroup == nested, "Disabled nearest group was skipped for an upper role");
                Object.DestroyImmediate(nested);
                Require(sources[0].OwnerGroup == a, "Owner did not recover after nearest group removal");
                var camera = Child(root.transform, "Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -5);
                var config = HoCharacterRimConfiguration.Default;
                config.enabled = true; config.maxLightsPerCharacter = 3; config.shadowBudget = 2; config.atlasSize = 256;
                pass = new HoCharacterRimPass();
                object frame = Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3 && pass.LastShadowCount == 0, "Four sources were not limited by the role budget");
                var ranges = Field<Vector4[]>(frame, "ranges");
                Require(ranges[a.groupId].y == 3 && ranges[b.groupId].y == 0, "Per-role source ranges leaked to another role");
                config.maxLightsPerCharacter = 8;
                sources[0].castShadows = true;
                pass.Setup(config, camera);
                typeof(HoCharacterRimPass).GetField("currentXr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pass, true);
                MethodInfo prepareXr = typeof(HoCharacterRimPass).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic);
                prepareXr.Invoke(pass, new object[] { camera, camera.worldToCameraMatrix, camera.projectionMatrix, 128, 128 });
                Require(pass.LastLightCount == 3 && pass.LastShadowCount == 0 && pass.LastRejectedCount == 1
                    && sources[0].Status.Contains("XR"), "XR did not reject only the requested-shadow source");
                typeof(HoCharacterRimPass).GetField("currentXr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pass, false);
                sources[0].castShadows = false;
                sources[0].width = float.NaN;
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3, "Runtime NaN width entered the source buffer");
                sources[0].width = .3f; sources[0].softness = float.PositiveInfinity;
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3, "Runtime infinite softness entered the source buffer");
                sources[0].softness = .5f; sources[0].shadowStrength = float.NaN;
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3, "Runtime NaN shadow strength entered the source buffer");
                sources[0].shadowStrength = 1; sources[0].intensity = float.MaxValue;
                sources[0].color = new Color(4, 4, 4, 1);
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3, "Finite HDR inputs overflowed into the source buffer");
                sources[0].intensity = 1; sources[0].color = new Color(-1, .25f, 2, 1);
                frame = Prepare(pass, config, camera);
                Require(pass.LastLightCount == 4, "Valid HDR with a negative component was rejected");
                object firstLight = ((Array)Field<object>(frame, "lights")).GetValue(0);
                Vector4 colorSoftness = Field<Vector4>(firstLight, "colorSoftness");
                Require(colorSoftness.x == 0 && colorSoftness.z > 1 && HoCharacterRimLightFinite(colorSoftness.z),
                    "Negative RGB was not zeroed or valid HDR was capped");
                sources[0].color = Color.white;
                sources[0].castShadows = true;
                // Guarantee the final world-space offset overflows even when
                // Mono retains intermediate float arithmetic at higher precision.
                hidden.transform.position = new Vector3(1000, 0, 0);
                config.depthBias = config.normalBias = float.MaxValue;
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3 && pass.LastShadowCount == 0 && pass.LastRejectedCount == 1,
                    $"Overflowing shadow-offset rejection: lights={pass.LastLightCount}, shadows={pass.LastShadowCount}, rejected={pass.LastRejectedCount}, status={sources[0].Status}");
                config.depthBias = config.normalBias = 1;
                hidden.transform.position = new Vector3(50, 0, 0);
                MethodInfo finiteBounds = typeof(HoCharacterRimPass).GetMethod("FiniteBounds", BindingFlags.Static | BindingFlags.NonPublic);
                var overflowingBounds = new Bounds(new Vector3(float.MaxValue, 0, 0), new Vector3(float.MaxValue, 1, 1));
                Require(!(bool)finiteBounds.Invoke(null, new object[] { overflowingBounds }),
                    "Finite center/extents with overflowing min/max passed shadow bounds validation");
                sources[0].castShadows = sources[1].castShadows = sources[2].castShadows = true;
                frame = Prepare(pass, config, camera);
                Require(pass.LastLightCount == 3 && pass.LastShadowCount == 2 && pass.LastRejectedCount == 1,
                    "Shadow-budget exhaustion silently published an unshadowed requested source");
                var slices = (IEnumerable)Field<object>(frame, "slices");
                foreach (object slice in slices)
                {
                    bool foundHidden = false;
                    foreach (object caster in (IEnumerable)Field<object>(slice, "casters"))
                    {
                        var renderer = Field<Renderer>(caster, "renderer");
                        Require(renderer != other, "An overlapping other-role renderer entered the caster list");
                        Require(Field<int>(caster, "pass") >= 0, "Caster did not use an actual original shader pass");
                        if (renderer == hidden)
                        { foundHidden = true; Require(Field<bool>(caster, "twoSided"), "TwoSided shadow mode was lost"); }
                    }
                    Require(foundHidden, "Offscreen non-receiving role member was omitted from the shadow");
                }
                config.enabled = false;
                Prepare(pass, config, camera);
                Require(pass.LastLightCount == 0 && pass.LastShadowCount == 0, "Disabled feature retained sources");
                Type gpu = typeof(HoCharacterRimPass).Assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterRimGpuLight");
                Require(gpu != null && Marshal.SizeOf(gpu) == 144, "GPU source stride is not 144 bytes");
                Debug.Log("[Ho Rim Validation] PASS: GPU current-identity group/part/selection queries (including zero w and alias), parent/ancestor rebinding, nearest disabled group, four sources, group ranges, independent shadow budget, exact original-pass caster group, offscreen nonreceiver caster, TwoSided policy, disable, GPU stride. GPU shadow visibility is validated separately.");
            }
            finally
            {
                pass?.Dispose();
                Object.DestroyImmediate(root);
                if (material != null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
                HoObjectBufferRegistry.MarkDirty();
            }
        }

        private static object Prepare(HoCharacterRimPass pass, HoCharacterRimConfiguration config, Camera camera)
        {
            pass.Setup(config, camera);
            MethodInfo prepare = typeof(HoCharacterRimPass).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic);
            return prepare.Invoke(pass, new object[] { camera, camera.worldToCameraMatrix, camera.projectionMatrix, 128, 128 });
        }
        private static void ValidateGpuIdentityQueries(HoObjectBufferGroup a, HoObjectBufferGroup b, Renderer receiver)
        {
            Shader shader = Shader.Find("Hidden/HoRim/IdentityProbe");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Identity probe shader is unavailable");
            var material = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture old = RenderTexture.active;
            try
            {
                uint identity = HoGeometryRendererBinding.GetManagedValue(receiver) & 0xffffu;
                Require(identity != 0, "Receiver lacks a draw-local identity");
                material.SetFloat("_HoRimProbeIdentity", identity);
                Vector4 group = HoACQueryDescriptor.Resolve(HoACQueryKind.Group, null, a.groupId, HoACMaskDomain.Objects).ShaderValue;
                Vector4 part = HoACQueryDescriptor.Resolve(HoACQueryKind.Identity, null,
                    (int)HoObjectBufferRegistry.GetPartId(a.groupId, "Coat"), HoACMaskDomain.Objects).ShaderValue;
                Vector4 selection = HoACQueryDescriptor.Resolve(HoACQueryKind.SelectionGroup,
                    "Rim Validation Coat", 0, HoACMaskDomain.Objects).ShaderValue;
                // Object query validity is kind/value based; w is reserved for semantic expected IDs.
                group.w = part.w = selection.w = 0;
                material.SetVector("_HoRimProbeGroup", group);
                material.SetVector("_HoRimProbePart", part);
                material.SetVector("_HoRimProbeSelection", selection);
                material.SetVector("_HoRimProbeOther", HoACQueryDescriptor.Resolve(HoACQueryKind.Group,
                    null, b.groupId, HoACMaskDomain.Objects).ShaderValue);
                target.Create();
                Graphics.Blit(Texture2D.blackTexture, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); readback.Apply();
                Color result = readback.GetPixel(0, 0);
                Require(result.r > .999f && result.g > .999f && result.b > .999f && result.a < .001f,
                    "GPU object query contributions/other-role rejection failed: " + result);
            }
            finally
            {
                RenderTexture.active = old; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(material);
            }
        }
        private static T Field<T>(object value, string name) =>
            (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(value);
        private static bool HoCharacterRimLightFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static GameObject Child(Transform parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child; }
        private static MeshRenderer Cube(Transform parent, string name, Vector3 position)
        {
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = name; child.transform.SetParent(parent, false); child.transform.localPosition = position;
            return child.GetComponent<MeshRenderer>();
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Ho Rim Validation] " + message); }
    }
}
