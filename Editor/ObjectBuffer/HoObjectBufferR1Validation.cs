using System;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    internal static class HoObjectBufferR1Validation
    {
        [MenuItem("HoLil/Validation/Validate Ho-ObjectBuffer R1")]
        public static void ValidateFromMenu()
        {
            RunValidation(true);
        }

        public static void RunBatchValidation()
        {
            try
            {
                RunValidation(false);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RunPtpSceneDiagnostic()
        {
            try
            {
                const string scenePath = "Assets/Hollow/土豆/PTP.unity";
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Camera camera = Camera.main;
                if (camera == null)
                {
                    throw new InvalidOperationException($"{scenePath} has no enabled MainCamera.");
                }

                HoObjectBufferRendererFeature feature = FindFeature();
                bool previousActive = feature.isActive;
                HoObjectBufferSettings settings = feature.Settings;
                HoObjectBufferDebugMode previousMode = settings.debugMode;
                bool previousEnabled = settings.enabled;
                bool previousGame = settings.debugInGameView;
                try
                {
                    feature.SetActive(true);
                    settings.enabled = true;
                    settings.debugInGameView = true;
                    DiagnoseMode(camera, settings, HoObjectBufferDebugMode.Id0);
                    DiagnoseMode(camera, settings, HoObjectBufferDebugMode.CoverageTotal);
                    DiagnoseMode(camera, settings, HoObjectBufferDebugMode.Valid);
                    DiagnoseMode(camera, settings, HoObjectBufferDebugMode.Id3);
                }
                finally
                {
                    feature.SetActive(previousActive);
                    settings.enabled = previousEnabled;
                    settings.debugMode = previousMode;
                    settings.debugInGameView = previousGame;
                }

                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void DiagnoseMode(Camera camera, HoObjectBufferSettings settings, HoObjectBufferDebugMode mode)
        {
            settings.debugMode = mode;
            RenderTexture target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            Texture2D readback = null;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback = new Texture2D(256, 256, TextureFormat.RGBA32, false, false);
                readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false);
                readback.Apply(false, false);

                Color32[] pixels = readback.GetPixels32();
                int visible = 0;
                byte maximum = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    byte value = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
                    maximum = Math.Max(maximum, value);
                    if (value > 12)
                    {
                        visible++;
                    }
                }

                Debug.Log($"[Ho-ObjectBuffer PTP] mode={mode}, visiblePixels={visible}/{pixels.Length}, maxByte={maximum}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
            }
        }

        private static void RunValidation(bool showDialog)
        {
            HoObjectBufferRendererFeature feature = FindFeature();
            bool previousActive = feature.isActive;
            HoObjectBufferSettings settings = feature.Settings;
            bool previousEnabled = settings.enabled;
            HoObjectBufferDebugMode previousDebugMode = settings.debugMode;
            bool previousDebugGame = settings.debugInGameView;
            bool previousDebugScene = settings.debugInSceneView;

            GameObject cameraObject = null;
            GameObject subjectObject = null;
            Material material = null;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                feature.SetActive(true);
                settings.enabled = true;
                settings.debugMode = HoObjectBufferDebugMode.CoverageTotal;
                settings.debugInGameView = true;
                settings.debugInSceneView = false;
                settings.selectionLayers = HoObjectBufferSelectionLayers.Off;

                subjectObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                subjectObject.name = "HoObjectBuffer R1 Validation Subject";
                subjectObject.transform.position = new Vector3(0.0f, 0.0f, 3.0f);
                Renderer subjectRenderer = subjectObject.GetComponent<Renderer>();
                // 用 cutout lilToon 强制走材质自有 HoObjectBuffer pass：opaque fallback
                // 刻意不渲染 AlphaTest 队列，所以这能真正验证跨仓 LightMode 协议。
                Shader shader = Shader.Find("Hidden/lilToonCutout");
                if (shader == null)
                {
                    throw new InvalidOperationException("Hidden/lilToonCutout shader was not found.");
                }

                material = new Material(shader) { renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest };
                if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.5f);
                subjectRenderer.sharedMaterial = material;

                HoObjectBufferGroup group = subjectObject.AddComponent<HoObjectBufferGroup>();
                group.groupId = 1;
                group.parts.Add(new HoObjectBufferPartEntry
                {
                    name = "Validation",
                    category = HoObjectBufferPartCategory.Other,
                    displayColor = Color.white,
                    includeChildren = false,
                    renderers = new Object[] { subjectRenderer }
                });
                group.Apply();

                cameraObject = new GameObject("HoObjectBuffer R1 Validation Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 20.0f;
                camera.fieldOfView = 45.0f;

                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32)
                {
                    name = "HoObjectBufferR1ValidationTarget"
                };
                target.Create();
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
                readback.ReadPixels(new Rect(32, 32, 1, 1), 0, 0, false);
                readback.Apply(false, false);
                RenderTexture.active = previous;

                Color center = readback.GetPixel(0, 0);
                float luminance = center.r * 0.2126f + center.g * 0.7152f + center.b * 0.0722f;
                if (luminance < 0.5f)
                {
                    throw new InvalidOperationException(
                        $"Ho-ObjectBuffer R1 validation failed: center coverage debug pixel was {center}. " +
                        "Expected a visible registered subject.");
                }

                string result = $"Ho-ObjectBuffer R1 validation passed. Center pixel={center}.";
                Debug.Log(result);
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Ho-ObjectBuffer R1", result, "OK");
                }
            }
            finally
            {
                feature.SetActive(previousActive);
                settings.enabled = previousEnabled;
                settings.debugMode = previousDebugMode;
                settings.debugInGameView = previousDebugGame;
                settings.debugInSceneView = previousDebugScene;
                if (cameraObject != null && cameraObject.TryGetComponent(out Camera validationCamera))
                {
                    validationCamera.targetTexture = null;
                }
                if (target != null)
                {
                    target.Release();
                }

                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(subjectObject);
                HoObjectBufferRegistry.Release();
                HoObjectBufferRegistry.MarkDirty();
            }
        }

        private static HoObjectBufferRendererFeature FindFeature()
        {
            string[] rendererGuids = AssetDatabase.FindAssets("t:UniversalRendererData");
            for (int i = 0; i < rendererGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(rendererGuids[i]);
                UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null)
                {
                    continue;
                }

                for (int featureIndex = 0; featureIndex < rendererData.rendererFeatures.Count; featureIndex++)
                {
                    if (rendererData.rendererFeatures[featureIndex] is HoObjectBufferRendererFeature feature)
                    {
                        return feature;
                    }
                }
            }

            throw new InvalidOperationException(
                "No HoObjectBufferRendererFeature was found in any UniversalRendererData asset. " +
                "Add the feature before running the validation.");
        }
    }
}
