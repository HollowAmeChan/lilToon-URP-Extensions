using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ScreenProcessStackVolumeEditor
    {
        // DepthFog presets. Each one is a recipe for the two slots (depth fog / height fog); the
        // layout they write is documented in Documentation~/PostProcessing/DepthFog.md:
        //   color = depth slot near colour
        //   p0 = (depth on, depth mode, start, far)      p1 = (density, depth max opacity, far mix, desaturate)
        //   p2 = (far colour rgb, height on)             p3 = (height mode, reference, A, B)
        //   p4 = (hardness, height max opacity, height colour rg)
        //   p5 = (height colour b, sky mode, sky strength, dither)

        private static void ApplyScreenProcessDistantAirDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.66f, 0.72f, 0.79f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 4.0f, 450.0f));
            SetVector4(element, "parameters1", new Vector4(0.006f, 0.55f, 1.0f, 0.35f));
            SetVector4(element, "parameters2", new Vector4(0.47f, 0.57f, 0.72f, 0.0f));
            SetVector4(element, "parameters5", new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Tint, 0.35f, 0.5f));
        }

        private static void ApplyScreenProcessMorningMistDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.72f, 0.77f, 0.82f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 3.0f, 220.0f));
            SetVector4(element, "parameters1", new Vector4(0.012f, 0.5f, 0.7f, 0.2f));
            SetVector4(element, "parameters2", new Vector4(0.60f, 0.68f, 0.78f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, 2.0f, 26.0f));
            SetVector4(element, "parameters4", new Vector4(1.2f, 0.6f, 0.86f, 0.89f));
            SetVector4(element, "parameters5", new Vector4(0.93f, (float)ScreenProcessFogSkyMode.Tint, 0.45f, 0.5f));
        }

        private static void ApplyScreenProcessDuskHazeDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.86f, 0.72f, 0.52f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 6.0f, 380.0f));
            SetVector4(element, "parameters1", new Vector4(0.009f, 0.55f, 1.0f, 0.25f));
            SetVector4(element, "parameters2", new Vector4(0.76f, 0.53f, 0.36f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.FalloffBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 0.03f));
            SetVector4(element, "parameters4", new Vector4(1.0f, 0.5f, 0.85f, 0.68f));
            SetVector4(element, "parameters5", new Vector4(0.45f, (float)ScreenProcessFogSkyMode.Tint, 0.5f, 0.5f));
        }

        private static void ApplyScreenProcessRainFogDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.70f, 0.73f, 0.76f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 2.0f, 120.0f));
            SetVector4(element, "parameters1", new Vector4(0.045f, 0.85f, 0.8f, 0.15f));
            SetVector4(element, "parameters2", new Vector4(0.55f, 0.58f, 0.62f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.FalloffBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 0.05f));
            SetVector4(element, "parameters4", new Vector4(1.0f, 0.7f, 0.66f, 0.69f));
            SetVector4(element, "parameters5", new Vector4(0.72f, (float)ScreenProcessFogSkyMode.Include, 0.5f, 1.0f));
        }

        private static void ApplyScreenProcessHeavyFogDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.76f, 0.78f, 0.80f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // Linear falloff to the far distance: the "visibility limit" look.
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Linear, 1.0f, 40.0f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.5f, 0.1f));
            SetVector4(element, "parameters2", new Vector4(0.68f, 0.71f, 0.74f, 0.0f));
            SetVector4(element, "parameters5", new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Include, 0.5f, 0.75f));
        }

        private static void ApplyScreenProcessSnowHazeDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.90f, 0.93f, 0.96f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 4.0f, 260.0f));
            SetVector4(element, "parameters1", new Vector4(0.02f, 0.6f, 1.0f, 0.25f));
            SetVector4(element, "parameters2", new Vector4(0.76f, 0.83f, 0.90f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, -2.0f, 20.0f));
            SetVector4(element, "parameters4", new Vector4(1.1f, 0.5f, 0.92f, 0.95f));
            SetVector4(element, "parameters5", new Vector4(0.98f, (float)ScreenProcessFogSkyMode.Include, 0.5f, 1.5f));
        }

        private static void ApplyScreenProcessValleyFogDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.80f, 0.84f, 0.88f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // Height slot only: fog pools in the valley and thins out with altitude.
            SetVector4(element, "parameters0", new Vector4(0.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 400.0f));
            SetVector4(element, "parameters2", new Vector4(0.49f, 0.58f, 0.71f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.FalloffBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 0.02f));
            SetVector4(element, "parameters4", new Vector4(1.0f, 0.9f, 0.84f, 0.87f));
            SetVector4(element, "parameters5", new Vector4(0.91f, (float)ScreenProcessFogSkyMode.Skip, 0.4f, 0.5f));
        }

        private static void ApplyScreenProcessCloudSeaDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.86f, 0.88f, 0.90f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // Reversed falloff: dense from the cloud base upwards.
            SetVector4(element, "parameters0", new Vector4(0.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 400.0f));
            SetVector4(element, "parameters2", new Vector4(0.49f, 0.58f, 0.71f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.FalloffAbove, (float)ScreenProcessFogHeightReference.World, 60.0f, 0.012f));
            SetVector4(element, "parameters4", new Vector4(1.0f, 1.0f, 0.95f, 0.96f));
            SetVector4(element, "parameters5", new Vector4(0.98f, (float)ScreenProcessFogSkyMode.Skip, 0.4f, 0.5f));
        }

        private static void ApplyScreenProcessGroundMistDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.84f, 0.87f, 0.90f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // A thin band right above the ground, with a hard transition.
            SetVector4(element, "parameters0", new Vector4(0.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 400.0f));
            SetVector4(element, "parameters2", new Vector4(0.49f, 0.58f, 0.71f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, 0.0f, 3.5f));
            SetVector4(element, "parameters4", new Vector4(2.0f, 0.5f, 0.86f, 0.90f));
            SetVector4(element, "parameters5", new Vector4(0.93f, (float)ScreenProcessFogSkyMode.Skip, 0.4f, 0.5f));
        }

        private static void ApplyScreenProcessHazeAndMistDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.62f, 0.70f, 0.80f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // The two-slot showcase: distance haze plus a low mist band in one effect.
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 5.0f, 600.0f));
            SetVector4(element, "parameters1", new Vector4(0.005f, 0.5f, 1.0f, 0.3f));
            SetVector4(element, "parameters2", new Vector4(0.43f, 0.53f, 0.66f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, 0.5f, 8.0f));
            SetVector4(element, "parameters4", new Vector4(1.2f, 0.55f, 0.85f, 0.89f));
            SetVector4(element, "parameters5", new Vector4(0.91f, (float)ScreenProcessFogSkyMode.Tint, 0.3f, 0.5f));
        }

        private static void ApplyScreenProcessNightNeonFogDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.16f, 0.14f, 0.25f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 10.0f, 420.0f));
            SetVector4(element, "parameters1", new Vector4(0.02f, 0.6f, 1.0f, 0.15f));
            SetVector4(element, "parameters2", new Vector4(0.30f, 0.23f, 0.42f, 0.0f));
            SetVector4(element, "parameters5", new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Tint, 0.45f, 0.75f));
        }

        private static void ApplyScreenProcessDesertHeatHazeDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.88f, 0.82f, 0.62f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // Exponential-squared plus a strong dither reads as shimmering heat.
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.ExponentialSquared, 5.0f, 350.0f));
            SetVector4(element, "parameters1", new Vector4(0.02f, 0.5f, 1.0f, 0.4f));
            SetVector4(element, "parameters2", new Vector4(0.85f, 0.70f, 0.45f, 1.0f));
            SetVector4(element, "parameters3", new Vector4((float)ScreenProcessFogHeightMode.WindowBelow, (float)ScreenProcessFogHeightReference.World, -1.0f, 25.0f));
            SetVector4(element, "parameters4", new Vector4(1.0f, 0.4f, 0.92f, 0.86f));
            SetVector4(element, "parameters5", new Vector4(0.70f, (float)ScreenProcessFogSkyMode.Include, 0.5f, 2.0f));
        }

        private static void ApplyScreenProcessSkyTintDepthFogPreset(SerializedProperty element, ScreenProcessEffect effect)
        {
            ApplyScreenProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.60f, 0.70f, 0.82f, 1.0f));
            SetEnum(element, "blendMode", (int)ScreenProcessBlendMode.Normal);
            // Ground stays untouched (density and depth opacity are 0); only the sky is tinted, with
            // the far colour and its own strength.
            SetVector4(element, "parameters0", new Vector4(1.0f, (float)ScreenProcessFogDepthMode.Exponential, 0.0f, 200.0f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.50f, 0.65f, 0.88f, 0.0f));
            SetVector4(element, "parameters5", new Vector4(0.92f, (float)ScreenProcessFogSkyMode.Tint, 0.6f, 0.0f));
        }
    }
}
