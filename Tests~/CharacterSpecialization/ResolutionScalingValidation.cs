// Copy to an isolated project's Assets/Editor. Run with graphics enabled:
// -batchmode -executeMethod ResolutionScalingValidation.Run -logFile <path>
using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using Object = UnityEngine.Object;

public static class ResolutionScalingValidation
{
    static readonly Type Pass = typeof(HoCharacterSpecializationRendererFeature).Assembly.GetType(
        "lilToon.URP.Extensions.CharacterSpecialization.HoCharacterSpecializationPass", true);
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static int checks;

    public static void Run()
    {
        Material material = null;
        int exit = 0;
        try
        {
            material = new Material(Shader.Find("Hidden/lilToon-HoCharacterSpecialization/URP/Composite"));
            var settings = new HoCharacterSpecializationSettings
            {
                eyeRevealFeatherPixels = 3,
                eyeRevealDilationPixels = 5,
                hairShadowDistancePixels = 15,
                hairShadowSoftnessPixels = 4,
                hairShadowSpreadPixels = 2,
                faceHairDiffuseRadiusPixels = 48,
                subjectOutlineRadiusPixels = 6,
                enhancedOutlineRadiusPixels = 18,
            };
            foreach (int reference in new[] { 720, 1080, 1440 })
            foreach (bool enabled in new[] { true, false })
            {
                settings.pixelReferenceHeight = reference;
                settings.scalePixelEffectsWithResolution = enabled;
                foreach (var size in new[] {
                    new Vector2Int(1280, 720), new Vector2Int(1920, 1080),
                    new Vector2Int(2560, 1440), new Vector2Int(3840, 2160),
                    new Vector2Int(1080, 1920), new Vector2Int(3440, 1440),
                    new Vector2Int(1001, 777), new Vector2Int(1, 1) })
                foreach (HoCharacterRenderScale capture in new[] {
                    HoCharacterRenderScale.Full, HoCharacterRenderScale.Half, HoCharacterRenderScale.Quarter })
                {
                    settings.renderScale = capture;
                    CheckCamera(settings, size, material);
                }
            }
            CheckCopyAndSerialization(settings);
            settings.scalePixelEffectsWithResolution = true;
            settings.pixelReferenceHeight = 0;
            Near((float)Call("GetEffectPixelScale", settings, 0), 1, "zero sizes remain finite");
            settings.pixelReferenceHeight = -10;
            Near((float)Call("GetEffectPixelScale", settings, -10), 1, "negative sizes remain finite");
            Debug.Log($"[ResolutionScalingValidation] PASS checks={checks}");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            exit = 1;
        }
        finally
        {
            if (material != null) Object.DestroyImmediate(material);
            EditorApplication.Exit(exit);
        }
    }

    static void CheckCamera(HoCharacterSpecializationSettings settings, Vector2Int size, Material material)
    {
        var camera = new RenderTextureDescriptor(size.x, size.y);
        var texel = (Vector4)Call("GetScreenTexelSize", camera, settings);
        float captureHeight = Mathf.Max(1, size.y / (int)settings.renderScale);
        float scale = (float)Call("GetEffectPixelScale", settings, (int)texel.w);
        float expectedScale = settings.scalePixelEffectsWithResolution
            ? captureHeight / settings.pixelReferenceHeight : 1;
        Near(scale, expectedScale, "capture pixel scale");
        Pass.GetMethod("ApplyMaterialProperties", PrivateStatic, null,
            new[] { typeof(Material), typeof(HoCharacterSpecializationSettings), typeof(float) }, null)
            .Invoke(null, new object[] { material, settings, scale });
        CheckPixel(material, "_HoCharacterEyeRevealParams", 1, settings.eyeRevealFeatherPixels);
        CheckPixel(material, "_HoCharacterEyeRevealParams", 2, settings.eyeRevealDilationPixels);
        CheckPixel(material, "_HoCharacterHairShadowParams", 1, settings.hairShadowDistancePixels);
        CheckPixel(material, "_HoCharacterHairShadowParams", 3, settings.hairShadowSoftnessPixels);
        CheckPixel(material, "_HoCharacterHairShadowParams1", 0, settings.hairShadowSpreadPixels);
        CheckPixel(material, "_HoCharacterFaceHairDiffuseParams", 1, settings.faceHairDiffuseRadiusPixels);
        CheckPixel(material, "_HoCharacterSubjectOutlineParams", 1, settings.subjectOutlineRadiusPixels);
        CheckPixel(material, "_HoCharacterEnhancedOutlineParams", 1, settings.enhancedOutlineRadiusPixels);
        // Non-pixel parameters must retain their own units.
        Near(material.GetVector("_HoCharacterEyeRevealParams").w, settings.eyeRevealDepthBias, "eye depth bias");
        Near(material.GetVector("_HoCharacterHairShadowParams").z, settings.hairShadowAngleDegrees, "shadow angle");
        Near(material.GetVector("_HoCharacterHairShadowParams2").y, settings.hairShadowDistanceReferenceDepth, "reference depth");

        var texture = new TextureDesc((int)texel.z, (int)texel.w);
        foreach (var effect in new[] {
            ("CreateFaceHairDiffuseBlurParams", settings.faceHairDiffuseRadiusPixels, "FaceHairDiffuseBlurIterationCount"),
            ("CreateSubjectOutlineBlurParams", settings.subjectOutlineRadiusPixels, "SubjectOutlineBlurIterationCount"),
            ("CreateEnhancedOutlineBlurParams", settings.enhancedOutlineRadiusPixels, "SubjectOutlineBlurIterationCount") })
        {
            int iterations = (int)Pass.GetField(effect.Item3, PrivateStatic).GetRawConstantValue();
            float iterationScale = 1 / Mathf.Sqrt(iterations);
            for (int i = 0; i < iterations; i++)
            {
                var blur = (Vector4)Call(effect.Item1, settings, camera, texture, iterationScale, i);
                float expected = settings.scalePixelEffectsWithResolution
                    ? effect.Item2 * texture.height / settings.pixelReferenceHeight
                    : effect.Item2 * texture.width / size.x;
                Near(blur.x, expected * iterationScale, effect.Item1);
                if (settings.scalePixelEffectsWithResolution)
                    Near(blur.x / texture.height / iterationScale,
                        effect.Item2 / settings.pixelReferenceHeight, "normalized blur radius");
            }
        }

        void CheckPixel(Material target, string property, int channel, float authored)
        {
            float value = target.GetVector(property)[channel];
            Near(value, authored * expectedScale, property);
            if (settings.scalePixelEffectsWithResolution)
                Near(value * texel.y, authored / settings.pixelReferenceHeight, "normalized " + property);
        }
    }

    static void CheckCopyAndSerialization(HoCharacterSpecializationSettings settings)
    {
        settings.scalePixelEffectsWithResolution = false;
        settings.pixelReferenceHeight = 900;
        var copy = new HoCharacterSpecializationSettings();
        copy.CopyFrom(settings);
        Require(!copy.scalePixelEffectsWithResolution && copy.pixelReferenceHeight == 900, "runtime settings copy");
        var roundTrip = JsonUtility.FromJson<HoCharacterSpecializationSettings>(JsonUtility.ToJson(settings));
        Require(!roundTrip.scalePixelEffectsWithResolution && roundTrip.pixelReferenceHeight == 900, "settings serialization");
        // Existing renderer assets lack the new fields; their initialized defaults must enable adaptation.
        var existingAsset = new HoCharacterSpecializationSettings();
        JsonUtility.FromJsonOverwrite("{\"enabled\":true,\"renderScale\":1}", existingAsset);
        Require(existingAsset.scalePixelEffectsWithResolution && existingAsset.pixelReferenceHeight == 1080,
            "existing assets receive reference-height defaults");
    }

    static object Call(string method, params object[] arguments)
        => Pass.GetMethod(method, PrivateStatic).Invoke(null, arguments);

    static void Near(float actual, float expected, string label)
        => Require(!float.IsNaN(actual) && !float.IsInfinity(actual) &&
            Mathf.Abs(actual - expected) <= Mathf.Max(0.000001f, Mathf.Abs(expected) * 0.00001f),
            $"{label}: expected {expected}, got {actual}");

    static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
}
