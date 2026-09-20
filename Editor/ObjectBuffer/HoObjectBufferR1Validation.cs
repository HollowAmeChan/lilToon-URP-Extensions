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
                // 放到远处 + 相机远平面 20：验证场景里有什么都不影响结果，
                // 否则"中心像素"可能落到场景里别的物体上，断言就变成看运气。
                Vector3 stage = new Vector3(0.0f, 1000.0f, 0.0f);
                subjectObject.transform.position = stage + new Vector3(0.0f, 0.0f, 3.0f);
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
                    tags = HoObjectBufferPartTags.CharacterFull,
                    displayColor = Color.white,
                    includeChildren = false,
                    renderers = new Object[] { subjectRenderer }
                });
                group.Apply();

                cameraObject = new GameObject("HoObjectBuffer R1 Validation Camera");
                cameraObject.transform.position = stage;
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
                readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);

                // 四个模式各断一件事：身份出得来、覆盖率出得来、valid 哨兵没被改、单物体不该有第 4 层。
                // 这四条正好覆盖 R1 踩过的四类失败（draw 被丢 / 没清附件 / 探针没撤 / resolve 层错位）。
                Color id0 = SampleCenterPixel(camera, target, readback, settings, HoObjectBufferDebugMode.Id0);
                RequireNeutralBright(id0, "Id0");

                Color coverage = SampleCenterPixel(camera, target, readback, settings, HoObjectBufferDebugMode.CoverageTotal);
                float coverageLuminance = Luminance(coverage);
                if (coverageLuminance < 0.5f)
                {
                    throw new InvalidOperationException(
                        $"Ho-ObjectBuffer R1 validation failed: center coverage debug pixel was {coverage}. " +
                        "Expected a visible registered subject.");
                }

                Color valid = SampleCenterPixel(camera, target, readback, settings, HoObjectBufferDebugMode.Valid);
                if (!(valid.g > 0.5f && valid.g > valid.r && valid.g > valid.b))
                {
                    throw new InvalidOperationException(
                        $"Ho-ObjectBuffer R1 validation failed: Valid view center pixel was {valid}, expected the green sentinel. " +
                        "A non-green value means the Valid view was replaced by a temporary probe.");
                }

                Color layer3 = SampleCenterPixel(camera, target, readback, settings, HoObjectBufferDebugMode.Id3);
                float layer3Max = Mathf.Max(layer3.r, Mathf.Max(layer3.g, layer3.b));
                if (layer3Max > 0.45f)
                {
                    throw new InvalidOperationException(
                        $"Ho-ObjectBuffer R1 validation failed: layer 3 center pixel was {layer3} for a single opaque subject. " +
                        "Expected the 0.06 background grey; anything brighter means the layer ranking is wrong.");
                }

                string result =
                    $"Ho-ObjectBuffer R1 validation passed. Id0={id0}, CoverageTotal={coverage}, Valid={valid}, Id3={layer3}.";
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

        /// <summary>把一个调试模式渲染到 target，并取正中心那个像素。</summary>
        private static Color SampleCenterPixel(
            Camera camera,
            RenderTexture target,
            Texture2D readback,
            HoObjectBufferSettings settings,
            HoObjectBufferDebugMode mode)
        {
            settings.debugMode = mode;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(target.width * 0.5f, target.height * 0.5f, 1, 1), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        /// <summary>
        /// 单一物体 + 白色 displayColor 时，层0 必须是"亮且中性"。
        /// 这条断言专门用来钉死"身份被写死成常量"的退路：残余的常量探针（例如 0x0100 = 红）
        /// 会让中心像素变成饱和色，中性性直接不成立。
        /// </summary>
        private static void RequireNeutralBright(Color color, string label)
        {
            float minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (minimum > 0.5f && maximum - minimum < 0.25f)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Ho-ObjectBuffer R1 validation failed: {label} center pixel was {color}, expected the palette displayColor " +
                "(white) modulated by coverage 1. A saturated color usually means the identity source is hardcoded.");
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
