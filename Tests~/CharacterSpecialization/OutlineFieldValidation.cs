// Copy this file and OutlineValidationUV.shader into an isolated project's Assets/Editor and run with
// -batchmode -executeMethod OutlineFieldValidation.Run -logFile <path> (graphics enabled).
using System;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class OutlineFieldValidation
{
    const int Width = 192, Height = 128;
    static readonly List<Object> Objects = new();
    static Material outline, composite;
    static RenderTexture output;
    static HoCharacterReferenceFrameBuffer frames;
    static Color[] uvPixels;
    static int checks;

    public static void Run()
    {
        bool async = ShaderUtil.allowAsyncCompilation;
        int exit = 0;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            outline = Keep(new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/SubjectOutline")));
            composite = Keep(new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/Composite")));
            output = Target();
            var uvMaterial = Keep(new Material(Shader.Find("Hidden/OutlineValidationUV")));
            Draw(uvMaterial, 0, output);
            uvPixels = Read(output);
            frames = new HoCharacterReferenceFrameBuffer();
            Shader.SetGlobalBuffer(HoCharacterReferenceFrameBuffer.BufferId, frames.Capture());
            Shader.SetGlobalBuffer(HoCharacterReferenceFrameBuffer.IdentityMapId, HoCharacterReferenceFrameBuffer.IdentityMapBuffer);
            Shader.SetGlobalInt(HoCharacterReferenceFrameBuffer.FrameCountId, 0);
            Shader.SetGlobalFloat("_HoACActive", 1);
            Shader.SetGlobalVector("_HoCharacterEyeAngleParams", new Vector4(0, 180, 180, 1));
            Shader.SetGlobalVector("_lilHoCharacterScreenTexelSize", new Vector4(1f / Width, 1f / Height, Width, Height));
            foreach (bool orthographic in new[] { false, true })
                foreach (float pitch in new[] { 0f, 25f }) CheckHeight(orthographic, pitch);
            CheckDirections();
            CheckReferenceDistance();
            foreach (var material in new[] { outline, composite })
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            Debug.Log($"[OutlineFieldValidation] PASS checks={checks}, API={SystemInfo.graphicsDeviceType}");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            frames?.Dispose();
            foreach (var obj in Objects) Object.DestroyImmediate(obj);
            ShaderUtil.allowAsyncCompilation = async;
            EditorApplication.Exit(exit);
        }
    }

    static void CheckHeight(bool orthographic, float pitch)
    {
        const float near = .1f, far = 100f, eyeDepth = 5f;
        var cameraTransform = Matrix4x4.TRS(new Vector3(2, 1, -5), Quaternion.Euler(pitch, 0, 15), Vector3.one);
        var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraTransform.inverse;
        var projection = orthographic ? Matrix4x4.Ortho(-3, 3, -2, 2, near, far) :
            Matrix4x4.Perspective(45, Width / (float)Height, near, far);
        var gpu = GL.GetGPUProjectionMatrix(projection, true);
        var passType = typeof(HoCharacterSpecializationRendererFeature).Assembly.GetType("lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationPass");
        var plane = (Vector4)passType.GetMethod("CreateOutlineHeightPlane", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { view, gpu, eyeDepth });
        Shader.SetGlobalVector("_HoCharacterOutlineHeightPlane0", plane);
        Shader.SetGlobalVector("_HoCharacterOutlineHeightPlane1", plane);
        Shader.SetGlobalMatrix("unity_MatrixInvVP", (gpu * view).inverse);
        Shader.SetGlobalMatrix("unity_MatrixV", view);
        Shader.SetGlobalVector("unity_OrthoParams", new Vector4(6, 4, 0, orthographic ? 1 : 0));
        Shader.SetGlobalVector("_ProjectionParams", new Vector4(-1, near, far, 1 / far));
        Vector4 z = SystemInfo.usesReversedZBuffer ?
            new Vector4(far / near - 1, 1, (far / near - 1) / far, 1 / far) :
            new Vector4(1 - far / near, far / near, (1 - far / near) / far, 1 / near);
        Shader.SetGlobalVector("_ZBufferParams", z);
        float deviceDepth = orthographic ? (eyeDepth - near) / (far - near) : (1 / eyeDepth - z.w) / z.z;
        if (orthographic && SystemInfo.usesReversedZBuffer) deviceDepth = 1 - deviceDepth;
        var semantic = Texture((x, y) => new Color(.6f, 0, 0, 0));
        // Deliberately discontinuous/irrelevant depth must not affect the source or fade.
        var depth = Texture((x, y) => new Color((x + y) % 2, 0, 0, 0));
        Shader.SetGlobalTexture("_lilHoCharacterObjectSemantic0_3Texture", semantic);
        Shader.SetGlobalTexture("_lilHoCharacterObjectSemantic4_7Texture", Texture2D.blackTexture);
        Shader.SetGlobalTexture("_HoGeometryBufferDepthTexture", depth);
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineSourceParams", Vector4.zero);
        Draw(outline, 0, output);
        var source = Read(output);
        Near(source[Height / 2 * Width + Width / 2].r, .6f, .002f, "source coverage without depth");

        float ExpectedFade(int index, int mode)
        {
            if (mode == 0) return 1;
            float u = uvPixels[index].r, v = uvPixels[index].g;
            float halfHeight = orthographic ? 2 : Mathf.Tan(22.5f * Mathf.Deg2Rad) * eyeDepth;
            float worldY = cameraTransform.MultiplyPoint3x4(new Vector3(
                (u * 2 - 1) * halfHeight * Width / Height, (v * 2 - 1) * halfHeight, eyeDepth)).y;
            float t = Mathf.Clamp01((Mathf.Abs(worldY) - .25f) / 1.5f);
            t = t * t * (3 - 2 * t);
            return mode == 1 ? t : 1 - t;
        }

        Shader.SetGlobalTexture("_lilHoCharacterSubjectOutlineSourceTexture", Texture2D.blackTexture);
        Shader.SetGlobalTexture("_lilHoCharacterEnhancedOutlineSourceTexture", Texture2D.blackTexture);
        // Amplify faint coverage so the final composite also exercises the old height threshold.
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineLevels", new Vector4(0, .00001f, 100000, 0));
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineParams", new Vector4(1, 80, 0, 0));
        Shader.SetGlobalColor("_HoCharacterSubjectOutlineColor", Color.white);
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineOptions", new Vector4(1, 1, 0, 1));
        Shader.SetGlobalVector("_HoCharacterEnhancedOutlineOptions", new Vector4(0, 1, 0, 1));
        Shader.SetGlobalVector("_HoCharacterEnhancedOutlineFogParams", new Vector4(0, 1, 1, 1));
        // Junk metadata and very faint coverage cannot perturb the camera plane.
        foreach (float coverage in new[] { .5f, .00005f, 0f })
        {
            var field = Texture((x, y) => new Color(coverage, x % 2 == 0 ? -500 : 1000, y % 2, 1));
            Shader.SetGlobalTexture("_lilHoCharacterSubjectOutlineTexture", field);
            Shader.SetGlobalTexture("_lilHoCharacterEnhancedOutlineTexture", field);
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var fade = new Vector4(mode, 0, .25f, 1 / 1.5f);
                Shader.SetGlobalVector("_HoCharacterSubjectOutlineHeightFadeParams", fade);
                Shader.SetGlobalVector("_HoCharacterEnhancedOutlineHeightFadeParams", fade);
                foreach (int debug in new[] { 0, 15 })
                {
                    Shader.SetGlobalVector("_HoCharacterOptions", new Vector4(0, 0, 0, debug));
                    Draw(composite, 0, output);
                    var pixels = Read(output);
                    foreach (int y in new[] { 20, 52, 84, 108 })
                    {
                        int x = Width / 2;
                        float expected = ExpectedFade(y * Width + x, mode);
                        float baseAmount = debug == 0 ? Mathf.Clamp01(coverage * 100000) : coverage;
                        float amount = expected * baseAmount;
                        if (debug == 0 && amount <= .0001f) amount = 0;
                        Near(pixels[y * Width + x].r, amount,
                            Mathf.Max(baseAmount * .003f, 1e-8f), $"fade ortho={orthographic} pitch={pitch} coverage={coverage} mode={mode} debug={debug} y={y}");
                    }
                }
            }
        }
        // Exercise both real blur passes: radius must not move a world-height plane.
        var seed = Texture((x, y) =>
        {
            // Overlapping silhouettes with unrelated depth/position metadata.
            float a = Mathf.Clamp01(25.5f - Vector2.Distance(new Vector2(x, y), new Vector2(72, 60)));
            float b = Mathf.Clamp01(30.5f - Vector2.Distance(new Vector2(x, y), new Vector2(115, 72)));
            return new Color(Mathf.Clamp01(a + b), a * 2 + b * 100, x % 2, 1);
        });
        var temp = Target(RenderTextureFormat.RFloat); var blurred = Target(RenderTextureFormat.RFloat);
        Shader.SetGlobalVector("_HoCharacterOptions", new Vector4(0, 0, 0, 15));
        Shader.SetGlobalVector("_HoCharacterEnhancedOutlineHeightFadeParams", new Vector4(1, 0, .25f, 1 / 1.5f));
        foreach (float radius in new[] { 8f, 40f, 80f, 200f })
        {
            Shader.SetGlobalVector("_HoCharacterSubjectOutlineBlurParams", new Vector4(radius / Mathf.Sqrt(2), 0, 0, 0));
            Draw(outline, 1, temp, seed);
            Shader.SetGlobalVector("_HoCharacterSubjectOutlineBlurParams", new Vector4(radius / Mathf.Sqrt(2), 1.61803399f, 0, 0));
            Draw(outline, 1, blurred, temp);
            var data = Read(blurred);
            Shader.SetGlobalTexture("_lilHoCharacterEnhancedOutlineTexture", blurred);
            Draw(composite, 0, output);
            var pixels = Read(output);
            foreach (int y in new[] { 20, 52, 84, 108 })
                foreach (int x in new[] { 48, 72, 120, 144 })
                {
                    int i = y * Width + x;
                    float t = ExpectedFade(i, 1);
                    Near(pixels[i].r, data[i].r * t, Mathf.Max(data[i].r * .004f, 1e-7f), "radius/overlap-independent height fade");
                }
        }
    }

    static void CheckReferenceDistance()
    {
        var cameraObject = Keep(new GameObject("Outline plane camera"));
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.transform.position = new Vector3(0, 1, -5);
        var method = typeof(HoCharacterReferenceFrameBuffer).GetMethod("GetOutlineReferenceDepth", BindingFlags.Instance | BindingFlags.NonPublic);
        float Distance(float referenceDistance = 0) => (float)method.Invoke(frames, new object[] { camera, referenceDistance });
        frames.Capture();
        Near(Distance(), 5, .001f, "reference-free camera uses fixed plane");
        Near(Distance(7), 7, .001f, "manual camera distance");
        Near(Distance(.01f), camera.nearClipPlane, .001f, "manual plane remains in front of camera");
        void Reference(Vector3 position)
        {
            var go = Keep(new GameObject("Outline plane reference")); go.transform.position = position;
            var reference = go.AddComponent<lilToon.URP.Extensions.GeometryData.HoGeometryDataReferenceFrame>();
            reference.reference = go.transform;
        }
        Reference(new Vector3(0, 1, -2)); frames.Capture();
        Near(Distance(), 3, .001f, "camera plane follows character distance");
        Reference(new Vector3(0, 1, 3));
        Reference(new Vector3(0, 1, -6));
        Reference(new Vector3(1000, 1, 0)); frames.Capture();
        Near(Distance(), 5.5f, .001f, "one plane averages visible references and excludes behind/offscreen references");
    }

    static void CheckDirections()
    {
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineOptions", new Vector4(1, 1, 0, 1));
        Shader.SetGlobalTexture("_lilHoCharacterSubjectOutlineSourceTexture", Texture2D.blackTexture);
        Shader.SetGlobalVector("_HoCharacterSubjectOutlineHeightFadeParams", Vector4.zero);
        Shader.SetGlobalVector("_HoCharacterEnhancedOutlineHeightFadeParams", Vector4.zero);
        Shader.SetGlobalVector("_HoCharacterOptions", new Vector4(0, 0, 0, 12));
        foreach (bool horizontal in new[] { true, false })
        {
            var ramp = Texture((x, y) => new Color(.3f + (horizontal ? x : y) * .000001f, 0, 0, 1));
            Shader.SetGlobalTexture("_lilHoCharacterSubjectOutlineTexture", ramp);
            Draw(composite, 0, output);
            var pixels = Read(output);
            var expected = Color.HSVToRGB(horizontal ? 0f : .75f, 1, 1);
            foreach (int x in new[] { 20, 60, 100, 160 })
                foreach (int y in new[] { 20, 50, 90, 110 })
                {
                    var actual = pixels[y * Width + x];
                    Near(actual.r, expected.r, .01f, "direction R");
                    Near(actual.g, expected.g, .01f, "direction G");
                    Near(actual.b, expected.b, .01f, "direction B");
                }
        }
        Shader.SetGlobalTexture("_lilHoCharacterSubjectOutlineTexture", Texture2D.blackTexture);
        Draw(composite, 0, output);
        Near(Read(output)[Height / 2 * Width + Width / 2].r, 0, .001f, "empty direction background");
    }

    static void Draw(Material material, int pass, RenderTexture destination, Texture source = null)
    {
        using var cmd = new CommandBuffer();
        cmd.SetRenderTarget(destination);
        source = source != null ? source : Texture2D.blackTexture;
        cmd.SetGlobalTexture("_BlitTexture", source);
        cmd.SetGlobalVector("_BlitTexture_TexelSize", new Vector4(1f / source.width, 1f / source.height, source.width, source.height));
        cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
        cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
        Graphics.ExecuteCommandBuffer(cmd);
    }

    static Texture2D Texture(Func<int, int, Color> pixel)
    {
        var texture = Keep(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
        texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
        var data = new Color[Width * Height];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) data[y * Width + x] = pixel(x, y);
        texture.SetPixels(data); texture.Apply(); return texture;
    }

    static RenderTexture Target(RenderTextureFormat format = RenderTextureFormat.ARGBFloat)
    {
        var target = Keep(new RenderTexture(Width, Height, 0, format, RenderTextureReadWrite.Linear));
        target.Create(); return target;
    }

    static Color[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
        var data = texture.GetPixels(); Object.DestroyImmediate(texture); RenderTexture.active = previous; return data;
    }

    static T Keep<T>(T obj) where T : Object { Objects.Add(obj); return obj; }
    static void Near(float actual, float expected, float tolerance, string label)
        => Require(Mathf.Abs(actual - expected) <= tolerance, $"{label}: {actual} expected {expected} (+/-{tolerance})");
    static void Require(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
}
