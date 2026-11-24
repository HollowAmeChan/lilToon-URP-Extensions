using System;
using System.Reflection;
using lilToon.URP.Extensions.SubsurfaceScattering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.SubsurfaceScattering
{
    public static class HoSubsurfaceScatteringVolumeValidation
    {
        [MenuItem("HoLil/Validation/Validate SSS Volume")]
        public static void Validate()
        {
            Require(Application.isBatchMode, "Run in an isolated batch editor.");
            var feature = ScriptableObject.CreateInstance<HoSubsurfaceScatteringRendererFeature>();
            var volume = ScriptableObject.CreateInstance<HoSubsurfaceScatteringVolume>();
            var restored = ScriptableObject.CreateInstance<HoSubsurfaceScatteringVolume>();
            var copy = new HoSubsurfaceScatteringSettings();
            var source = feature.Settings;
            string original = JsonUtility.ToJson(source);
            try
            {
                var copyFrom = typeof(HoSubsurfaceScatteringSettings).GetMethod("CopyFrom", BindingFlags.Instance | BindingFlags.NonPublic);
                var apply = typeof(HoSubsurfaceScatteringVolume).GetMethod("ApplyTo", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (FieldInfo field in typeof(HoSubsurfaceScatteringSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.FieldType == typeof(float)) field.SetValue(source, (float)field.GetValue(source) + .07f);
                    else if (field.FieldType == typeof(int)) field.SetValue(source, 7);
                    else if (field.FieldType == typeof(bool)) field.SetValue(source, !(bool)field.GetValue(source));
                    else if (field.FieldType == typeof(Color)) field.SetValue(source, new Color(.12f, .34f, .56f, .78f));
                    else if (field.FieldType.IsEnum)
                    {
                        Array values = Enum.GetValues(field.FieldType);
                        field.SetValue(source, values.GetValue(values.Length - 1));
                    }
                }
                source.profiles[0].profileId = 42;
                source.profiles[0].diffusionRadius = 17;
                copyFrom.Invoke(copy, new object[] { source });
                Require(JsonUtility.ToJson(copy) == JsonUtility.ToJson(source), "Working settings copy lost a field.");
                Require(!ReferenceEquals(copy.profiles, source.profiles) && !ReferenceEquals(copy.profiles[0], source.profiles[0]), "Profile fallback aliases the asset.");
                string fallback = JsonUtility.ToJson(copy);
                apply.Invoke(volume, new object[] { copy });
                Require(JsonUtility.ToJson(copy) == fallback, "Un-overridden Volume changed Feature fallback.");

                // Every runtime setting must be represented, with matching defaults and an operative override.
                var defaults = new HoSubsurfaceScatteringSettings();
                int coverage = 0;
                foreach (FieldInfo field in typeof(HoSubsurfaceScatteringSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.Name == "shader" || field.Name == "sourcePassEvent" || field.Name == "compositePassEvent") continue;
                    FieldInfo volumeField = typeof(HoSubsurfaceScatteringVolume).GetField(field.Name == "enabled" ? "enable" : field.Name);
                    Require(volumeField != null, "Missing Volume setting: " + field.Name);
                    var parameter = (VolumeParameter)volumeField.GetValue(volume);
                    PropertyInfo value = parameter.GetType().GetProperty("value");
                    object defaultValue = value.GetValue(parameter);
                    if (field.Name != "profiles") Require(Equals(defaultValue, field.GetValue(defaults)), "Default differs: " + field.Name);
                    parameter.overrideState = true;
                    apply.Invoke(volume, new object[] { copy });
                    Require(field.Name == "profiles" || Equals(field.GetValue(copy), defaultValue), "Override not applied: " + field.Name);
                    coverage++;
                }
                volume.enable.Override(false);
                apply.Invoke(volume, new object[] { copy });
                Require(!copy.enabled, "Enable=false was ignored through IsActive.");
                volume.enable.Override(true);
                apply.Invoke(volume, new object[] { copy });
                Require(copy.enabled, "Volume cannot enable a disabled Feature fallback.");
                volume.profiles.value[0].profileId = 21;
                volume.profiles.value[0].transmissionStrength = .73f;
                apply.Invoke(volume, new object[] { copy });
                Require(copy.profiles[0].profileId == 21 && Mathf.Approximately(copy.profiles[0].transmissionStrength, .73f), "Profile table override lost values.");
                copy.profiles[0].profileId = 99;
                Require(volume.profiles.value[0].profileId == 21 && source.profiles[0].profileId == 42, "Resolved profile modified a source asset.");
                volume.active = false;
                copyFrom.Invoke(copy, new object[] { source });
                apply.Invoke(volume, new object[] { copy });
                Require(JsonUtility.ToJson(copy) == fallback, "Inactive component changed fallback.");
                volume.active = true;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(volume), restored);
                Require(restored.profiles.overrideState && restored.profiles.value[0].profileId == 21
                    && restored.enable.overrideState, "Volume serialization lost table/overrides.");

                ValidateStack(feature);
                Debug.Log($"HO_SSS_VOLUME_VALIDATION_PASSED runtimeFields={coverage} fallback/override/off/inactive/profiles/save/cameraStacks=passed");
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(original, source);
                Object.DestroyImmediate(restored);
                Object.DestroyImmediate(volume);
                Object.DestroyImmediate(feature);
            }
        }

        private static void ValidateStack(HoSubsurfaceScatteringRendererFeature feature)
        {
            var manager = VolumeManager.instance;
            bool initializedHere = !manager.isInitialized;
            if (initializedHere)
            {
                // Batch editors can start without a render pipeline. Use URP's component set.
                typeof(VolumeManager).GetMethod("LoadBaseTypesByReflection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager, new object[] { typeof(UniversalRenderPipelineAsset) });
                typeof(VolumeManager).GetMethod("InitializeInternal", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager, new object[] { null, null });
            }
            var previous = manager.stack;
            var stackA = manager.CreateStack();
            var stackB = manager.CreateStack();
            var globalObject = new GameObject("SSS volume validation") { layer = 30 };
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                var global = globalObject.AddComponent<Volume>();
                global.isGlobal = true;
                global.sharedProfile = profile;
                var control = profile.Add<HoSubsurfaceScatteringVolume>(false);
                control.enable.Override(false);
                control.strength.Override(1.37f);
                control.profiles.overrideState = true;
                control.profiles.value[0].profileId = 37;
                feature.Settings.enabled = true;
                feature.Settings.strength = .23f;
                var resolve = feature.GetType().GetMethod("ResolveSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                manager.Update(stackA, null, 1 << 30);
                manager.stack = stackA;
                var first = (HoSubsurfaceScatteringSettings)resolve.Invoke(feature, null);
                Require(!first.enabled && Mathf.Approximately(first.strength, 1.37f) && first.profiles[0].profileId == 37, "Camera A did not receive blended Volume values.");
                manager.Update(stackB, null, 0);
                manager.stack = stackB;
                var second = (HoSubsurfaceScatteringSettings)resolve.Invoke(feature, null);
                Require(second.enabled && Mathf.Approximately(second.strength, .23f) && second.profiles[0].profileId == 42, "Camera B retained Camera A overrides.");
                Require(Mathf.Approximately(feature.Settings.strength, .23f) && feature.Settings.profiles[0].profileId == 42, "Resolution mutated Feature defaults.");
                manager.stack = stackA;
                var again = (HoSubsurfaceScatteringSettings)resolve.Invoke(feature, null);
                Require(!again.enabled && again.profiles[0].profileId == 37, "Switching back to Camera A lost its table.");
                ValidateEnqueue(feature, control, stackA);
            }
            finally
            {
                manager.stack = previous;
                Object.DestroyImmediate(globalObject);
                foreach (var component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
                manager.DestroyStack(stackA);
                manager.DestroyStack(stackB);
                if (initializedHere)
                {
                    manager.Deinitialize();
                    manager.stack = null;
                }
            }
        }

        private sealed class ValidationRenderer : ScriptableRenderer
        {
            public ValidationRenderer(ScriptableRendererData data) : base(data) { }
            public int Count => activeRenderPassQueue.Count;
            public void ClearPasses() => activeRenderPassQueue.Clear();
            public void CheckSettings(float strength)
            {
                foreach (ScriptableRenderPass pass in activeRenderPassQueue)
                {
                    Require(pass != null, "Feature enqueued a null pass.");
                    // Shared presentation is a separate pass; it holds its owner, not SSS settings.
                    if (pass.GetType().Name == "HoDebugPresentationPass") continue;
                    var field = pass.GetType().GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
                    Require(field != null, "Missing settings on " + pass.GetType().Name);
                    var settings = (HoSubsurfaceScatteringSettings)field.GetValue(pass);
                    Require(Mathf.Approximately(settings.strength, strength) && settings.profiles[0].profileId == 37,
                        "Enqueued pass is still using Feature defaults: " + pass.GetType().Name);
                }
            }
        }

        private static void ValidateEnqueue(HoSubsurfaceScatteringRendererFeature feature, HoSubsurfaceScatteringVolume control, VolumeStack stack)
        {
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            var cameraObject = new GameObject("SSS enqueue validation camera");
            var renderer = new ValidationRenderer(data);
            var frame = new ContextContainer();
            try
            {
                feature.Create();
                var camera = frame.GetOrCreate<UniversalCameraData>();
                camera.camera = cameraObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Game;
                var rendering = (RenderingData)Activator.CreateInstance(typeof(RenderingData), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { frame }, null);
                control.enable.Override(false);
                VolumeManager.instance.Update(stack, null, 1 << 30);
                feature.AddRenderPasses(renderer, ref rendering);
                Require(renderer.Count == 0, "Volume off still enqueued SSS passes.");
                control.enable.Override(true);
                control.debugMode.Override(HoSubsurfaceScatteringDebugMode.Off);
                VolumeManager.instance.Update(stack, null, 1 << 30);
                feature.AddRenderPasses(renderer, ref rendering);
                Require(renderer.Count == 7, "Normal SSS pass count changed.");
                renderer.CheckSettings(1.37f);
                renderer.ClearPasses();
                control.debugMode.Override(HoSubsurfaceScatteringDebugMode.Mask);
                VolumeManager.instance.Update(stack, null, 1 << 30);
                feature.AddRenderPasses(renderer, ref rendering);
                Require(renderer.Count == 9, "Volume debug mode did not enqueue debug capture and presentation.");
                renderer.CheckSettings(1.37f);
                renderer.ClearPasses();
                camera.cameraType = CameraType.SceneView;
                control.renderInSceneView.Override(false);
                VolumeManager.instance.Update(stack, null, 1 << 30);
                feature.AddRenderPasses(renderer, ref rendering);
                Require(renderer.Count == 0, "Scene View off still enqueued SSS passes.");
                Debug.Log("HO_SSS_VOLUME_ENQUEUE_PASSED off=0 normal=7 debugWithPresentation=9 sceneOff=0 allPassesUseVolume=passed");
            }
            finally
            {
                renderer.Dispose();
                frame.Dispose();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(data);
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("[Ho SSS Volume] " + message);
        }
    }
}
