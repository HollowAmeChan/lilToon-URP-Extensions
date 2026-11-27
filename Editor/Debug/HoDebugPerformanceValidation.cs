#pragma warning disable CS0618, CS0672
using System;
using System.Linq;
using System.Reflection;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    // Run in an isolated batch project. No scene or renderer asset is saved.
    public static class HoDebugPerformanceValidation
    {
        private sealed class Pass : ScriptableRenderPass { }

        public static void Run()
        {
            GameObject first = null, second = null;
            try
            {
                HoDebugPerformanceRegistry.EnsureInitialized();
                string[] required = { "AttributeComposite", "WeightedOIT", "RT", "GTAO", "SSGI",
                    "ObjectBuffer", "GeometryBuffer", "SurfaceBuffer", "Subsurface Scattering", "PLR",
                    "HoAtmosphere", "Ho-VolumeLight", "RTDI", "RTGI", "RTShadow", "ScreenProcess", "ImageProcess",
                    "GeometryData", "CharacterShadow", "CharacterSpecialization" };
                foreach (string feature in required)
                    Require(HoDebugPerformanceRegistry.All.Any(item => item.FeatureName == feature), "registered " + feature);

                first = new GameObject("HUD schedule validation A");
                second = new GameObject("HUD schedule validation B");
                Camera a = first.AddComponent<Camera>(), b = second.AddComponent<Camera>();
                var late = HoDebugPerformanceRegistry.Register("ValidationLate", "LATE");
                var early = HoDebugPerformanceRegistry.Register("ValidationEarly", "EARLY");
                var tie = HoDebugPerformanceRegistry.Register("ValidationTie", "TIE");
                var latePass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
                var earlyPass = new Pass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
                var tiePass = new Pass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
                HoDebugPerformanceRegistry.TrackPass(a, latePass, late);
                HoDebugPerformanceRegistry.TrackPass(a, earlyPass, early);
                HoDebugPerformanceRegistry.TrackPass(a, tiePass, tie);
                HoDebugPerformanceRegistry.TrackPass(a, latePass, early); // Same feature spanning stages.
                HoDebugPerformanceRegistry.TrackPass(b, latePass, tie);
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(a).SequenceEqual(new[] { early, tie, late }),
                    "event order, stable equal-event order, feature deduplication");
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(b).SequenceEqual(new[] { tie }), "camera isolation");

                latePass.renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(a)[0] == late, "read final pass event at presentation");
                string text = string.Join("\n", HoDebugPerformanceRegistry.BuildLines(HoDebugTilePerformanceMode.Features, b)
                    .Select(line => string.Concat(line.Segments.Select(segment => segment.Text))));
                Require(text.Contains("TIE G") && !text.Contains("EARLY G") && !text.Contains("LATE G"), "hide nonparticipating features");

                MethodInfo begin = typeof(HoDebugPerformanceRegistry).GetMethod("BeginCamera", BindingFlags.Static | BindingFlags.NonPublic);
                begin.Invoke(null, new object[] { a });
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(a).Count == 0, "disabled features disappear on next render, including same frame");
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(b).Count == 1, "reset does not clear other camera");
                HoDebugPerformanceRegistry.TrackPreparation(a, early);
                HoDebugPerformanceRegistry.TrackPass(a, tiePass, tie);
                HoDebugPerformanceRegistry.TrackPreparation(a, late, HoDebugPreparationStage.BeginCamera);
                Require(HoDebugPerformanceRegistry.GetActiveFeatures(a).SequenceEqual(new[] { late, early, tie }),
                    "begin-camera reflection work precedes AddRenderPasses preparation and GPU passes");
                Debug.Log("HO_DEBUG_PERFORMANCE_VALIDATION_PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (second != null) UnityEngine.Object.DestroyImmediate(second);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Performance HUD validation: " + message);
        }
    }
}
