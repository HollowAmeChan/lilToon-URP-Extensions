using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PlanarReflection
{
    internal readonly struct HoPlanarReflectionRenderSettings
    {
        public readonly bool Enabled;
        public readonly bool RenderGameView;
        public readonly bool RenderSceneView;
        public readonly int MaxSurfacesPerCamera;

        public HoPlanarReflectionRenderSettings(
            bool enabled,
            bool renderGameView,
            bool renderSceneView,
            int maxSurfacesPerCamera)
        {
            Enabled = enabled;
            RenderGameView = renderGameView;
            RenderSceneView = renderSceneView;
            MaxSurfacesPerCamera = Mathf.Max(0, maxSurfacesPerCamera);
        }
    }

    internal readonly struct HoPlanarReflectionRenderStats
    {
        public readonly int SurfaceCount;
        public readonly int ActiveSurfaceCount;
        public readonly string Reason;

        public HoPlanarReflectionRenderStats(int surfaceCount, int activeSurfaceCount, string reason)
        {
            SurfaceCount = surfaceCount;
            ActiveSurfaceCount = activeSurfaceCount;
            Reason = reason ?? string.Empty;
        }
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/lilToon URP/Ho 平面反射表面")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "lilToon.URP.Extensions.PlanarReflection", null, "LILPlanarReflectionSurface")]
    public sealed class HoPlanarReflectionSurface : MonoBehaviour
    {
        private static readonly List<HoPlanarReflectionSurface> ActiveSurfaces = new List<HoPlanarReflectionSurface>();
        private static readonly int UsePlanarReflectionId = Shader.PropertyToID("_UsePlanarReflection");
        private static readonly int ReflectionTextureId = Shader.PropertyToID("_LILPBRPlanarReflectionTexture");
        private static readonly int ReflectionTextureMatrixId = Shader.PropertyToID("_LILPBRPlanarReflectionTextureMatrix");
        private static readonly int ReflectionParamsId = Shader.PropertyToID("_LILPBRPlanarReflectionParams");
        private const float DegeneratePlaneDistance = 0.0001f;
        private static readonly Rect FullViewportRect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
        private static bool isRenderingReflection;

        internal static int SurfaceCount => ActiveSurfaces.Count;

        [Header("目标")]
        [SerializeField, InspectorName("目标渲染器"), Tooltip("接收平面反射纹理和材质参数的 Renderer。留空时会自动使用当前物体上的 Renderer。")]
        private Renderer targetRenderer;

        [Header("反射平面")]
        [SerializeField, InspectorName("反射平面锚点"), Tooltip("指定反射平面的位置和法线。留空时会自动使用目标渲染器作为平面来源。")]
        private Transform planeTransform;

        [SerializeField, InspectorName("使用渲染器中心"), Tooltip("未指定反射平面锚点时，用目标渲染器的包围盒中心作为反射平面位置，避免组件挂载物体的 Transform 偏移影响反射。")]
        private bool useTargetRendererBoundsCenter = true;

        [Header("反射渲染")]
        [SerializeField, InspectorName("反射层遮罩"), Tooltip("只有这些层会被渲染进平面反射。")]
        private LayerMask reflectionMask = -1;

        [SerializeField, Range(64, 4096), InspectorName("反射分辨率"), Tooltip("反射纹理的宽度。高度会按源相机宽高比自动计算。")]
        private int resolution = 1024;

        [SerializeField, Min(0.0f), InspectorName("裁剪平面偏移"), Tooltip("把反射相机的裁剪面沿反射平面法线偏移，0 表示精确贴合反射平面。")]
        private float clipPlaneOffset = 0.0f;

        [SerializeField, InspectorName("使用平面裁剪"), Tooltip("开启后用反射平面作为斜裁剪面，避免镜面背后的物体进入反射。关闭后使用普通相机裁剪，适合排查裁剪导致的反射消失。")]
        private bool useObliqueClipPlane = true;

        [SerializeField, Min(0.0f), InspectorName("最小裁剪距离"), Tooltip("源相机太贴近反射平面时，把裁剪面略微后退，避免斜裁剪面贴到反射相机导致整张反射消失。")]
        private float minClipPlaneDistance = 0.02f;

        [SerializeField, Min(0.001f), InspectorName("反射相机近裁剪"), Tooltip("反射相机的最小 near clip。调小可减少贴近镜面时的裁切，调大可减少近处穿模噪声。")]
        private float reflectionNearClipPlane = 0.01f;

        [SerializeField, InspectorName("反射中隐藏本体"), Tooltip("渲染反射时临时隐藏目标渲染器，避免平面自己递归反射。")]
        private bool hideSurfaceInReflection = true;

        [SerializeField, InspectorName("自动启用材质开关"), Tooltip("通过 MaterialPropertyBlock 自动写入 _UsePlanarReflection。关闭后需要在材质上手动启用平面反射。")]
        private bool overrideMaterialToggle = true;

        [Header("背景")]
        [SerializeField, InspectorName("复制相机清屏设置"), Tooltip("开启时使用源相机的清屏方式和背景色；关闭时使用下面的备用设置。")]
        private bool copyCameraClearFlags = true;

        [SerializeField, InspectorName("备用清屏方式")]
        private CameraClearFlags fallbackClearFlags = CameraClearFlags.SolidColor;

        [SerializeField, InspectorName("备用背景色")]
        private Color fallbackBackgroundColor = Color.black;

        [Header("调试与性能")]
        [SerializeField, InspectorName("反射场景视图"), Tooltip("在 Scene View 相机中也渲染平面反射，方便编辑时预览。")]
        private bool reflectSceneView = true;

        [SerializeField, Min(1), InspectorName("更新帧间隔"), Tooltip("每隔多少帧更新一次反射。1 表示每帧更新。")]
        private int frameInterval = 1;

        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private MaterialPropertyBlock propertyBlock;
        private int lastRenderedFrame = -1;

        private Renderer TargetRenderer
        {
            get
            {
                if (targetRenderer == null)
                {
                    targetRenderer = GetComponent<Renderer>();
                }

                return targetRenderer;
            }
        }

        private void OnEnable()
        {
            Register();
            ApplyDisabledPropertyBlock();
        }

        private void OnDisable()
        {
            ActiveSurfaces.Remove(this);
            ApplyDisabledPropertyBlock();
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void OnValidate()
        {
            resolution = Mathf.Clamp(resolution, 64, 4096);
            frameInterval = Mathf.Max(1, frameInterval);
            clipPlaneOffset = Mathf.Max(0.0f, clipPlaneOffset);
            minClipPlaneDistance = Mathf.Max(0.0f, minClipPlaneDistance);
            reflectionNearClipPlane = Mathf.Max(0.001f, reflectionNearClipPlane);

            if (isActiveAndEnabled)
            {
                ApplyDisabledPropertyBlock();
            }
        }

        internal static HoPlanarReflectionRenderStats RenderAllSurfaces(
            ScriptableRenderContext context,
            Camera camera,
            HoPlanarReflectionRenderSettings settings)
        {
            if (!settings.Enabled)
            {
                DisableAllSurfaces();
                return new HoPlanarReflectionRenderStats(ActiveSurfaces.Count, 0, "RendererFeature 已关闭。");
            }

            if (isRenderingReflection || camera == null)
            {
                return new HoPlanarReflectionRenderStats(ActiveSurfaces.Count, 0, "反射渲染递归或相机为空。");
            }

            if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
            {
                return new HoPlanarReflectionRenderStats(ActiveSurfaces.Count, 0, "当前 camera type 不支持。");
            }

            if (camera.cameraType == CameraType.SceneView && !settings.RenderSceneView)
            {
                DisableAllSurfaces();
                return new HoPlanarReflectionRenderStats(ActiveSurfaces.Count, 0, "Scene View 反射已关闭。");
            }

            if (camera.cameraType == CameraType.Game && !settings.RenderGameView)
            {
                DisableAllSurfaces();
                return new HoPlanarReflectionRenderStats(ActiveSurfaces.Count, 0, "Game View 反射已关闭。");
            }

            isRenderingReflection = true;
            int activeSurfaceCount = 0;
            try
            {
                int maxSurfaces = settings.MaxSurfacesPerCamera;
                for (int i = 0; i < ActiveSurfaces.Count; i++)
                {
                    if (maxSurfaces > 0 && activeSurfaceCount >= maxSurfaces)
                    {
                        ActiveSurfaces[i]?.ApplyDisabledPropertyBlock();
                        continue;
                    }

                    HoPlanarReflectionSurface surface = ActiveSurfaces[i];
                    if (surface != null && surface.RenderReflection(context, camera))
                    {
                        activeSurfaceCount++;
                    }
                }
            }
            finally
            {
                isRenderingReflection = false;
            }

            return new HoPlanarReflectionRenderStats(
                ActiveSurfaces.Count,
                activeSurfaceCount,
                activeSurfaceCount > 0 ? "输入有效。" : "没有可渲染的平面反射表面。");
        }

        private static void DisableAllSurfaces()
        {
            for (int i = 0; i < ActiveSurfaces.Count; i++)
            {
                ActiveSurfaces[i]?.ApplyDisabledPropertyBlock();
            }
        }

        private void Register()
        {
            if (!ActiveSurfaces.Contains(this))
            {
                ActiveSurfaces.Add(this);
            }
        }

        private bool RenderReflection(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!isActiveAndEnabled || sourceCamera == null)
            {
                return false;
            }

            if (sourceCamera.cameraType == CameraType.SceneView && !reflectSceneView)
            {
                ApplyDisabledPropertyBlock();
                return false;
            }

            if (frameInterval > 1 && lastRenderedFrame >= 0 && Time.frameCount - lastRenderedFrame < frameInterval)
            {
                ApplyEnabledPropertyBlock();
                return true;
            }

            Renderer surfaceRenderer = TargetRenderer;
            if (surfaceRenderer == null || !surfaceRenderer.enabled)
            {
                ApplyDisabledPropertyBlock();
                return false;
            }

            EnsureResources(sourceCamera);
            if (reflectionCamera == null || reflectionTexture == null)
            {
                ApplyDisabledPropertyBlock();
                return false;
            }

            Vector3 planePosition = GetPlanePosition(surfaceRenderer);
            Vector3 planeNormal = GetPlaneNormal(surfaceRenderer);
            if (planeNormal.sqrMagnitude < 0.0001f)
            {
                ApplyDisabledPropertyBlock();
                return false;
            }

            Matrix4x4 reflectionMatrix = CalculateReflectionMatrix(new Vector4(
                planeNormal.x,
                planeNormal.y,
                planeNormal.z,
                -Vector3.Dot(planeNormal, planePosition)));

            Vector3 sourcePosition = sourceCamera.transform.position;
            float cameraPlaneDistance = Vector3.Dot(sourcePosition - planePosition, planeNormal);
            Vector3 clipNormal = cameraPlaneDistance >= 0.0f ? planeNormal : -planeNormal;
            float absCameraPlaneDistance = Mathf.Abs(cameraPlaneDistance);
            Vector3 adjustedSourcePosition = sourcePosition;
            if (absCameraPlaneDistance < DegeneratePlaneDistance)
            {
                adjustedSourcePosition += clipNormal * (DegeneratePlaneDistance - absCameraPlaneDistance);
                absCameraPlaneDistance = DegeneratePlaneDistance;
            }

            ConfigureReflectionCamera(
                sourceCamera,
                reflectionMatrix,
                adjustedSourcePosition,
                planePosition,
                clipNormal,
                absCameraPlaneDistance);

            bool previousInvertCulling = GL.invertCulling;
            bool previousForceRenderingOff = surfaceRenderer.forceRenderingOff;
            if (hideSurfaceInReflection)
            {
                surfaceRenderer.forceRenderingOff = true;
            }

            GL.invertCulling = !previousInvertCulling;
            try
            {
#pragma warning disable CS0618
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
                lastRenderedFrame = Time.frameCount;
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
                if (hideSurfaceInReflection)
                {
                    surfaceRenderer.forceRenderingOff = previousForceRenderingOff;
                }
            }

            ApplyEnabledPropertyBlock();
            return true;
        }

        private Vector3 GetPlanePosition(Renderer surfaceRenderer)
        {
            if (planeTransform != null)
            {
                return planeTransform.position;
            }

            if (useTargetRendererBoundsCenter && surfaceRenderer != null)
            {
                return surfaceRenderer.bounds.center;
            }

            return transform.position;
        }

        private Vector3 GetPlaneNormal(Renderer surfaceRenderer)
        {
            Transform normalSource = planeTransform;
            if (normalSource == null && surfaceRenderer != null)
            {
                normalSource = surfaceRenderer.transform;
            }

            if (normalSource == null)
            {
                normalSource = transform;
            }

            Vector3 normal = normalSource.up;
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.zero;
        }

        private void EnsureResources(Camera sourceCamera)
        {
            EnsureReflectionCamera();
            EnsureReflectionTexture(sourceCamera);
        }

        private void EnsureReflectionCamera()
        {
            if (reflectionCamera != null)
            {
                return;
            }

            GameObject cameraObject = new GameObject($"{nameof(HoPlanarReflectionSurface)} ({name})");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.cameraType = CameraType.Reflection;
        }

        private void EnsureReflectionTexture(Camera sourceCamera)
        {
            int width = Mathf.Max(64, resolution);
            int height = Mathf.Max(64, Mathf.RoundToInt(resolution / Mathf.Max(sourceCamera.aspect, 0.01f)));
            height = Mathf.Clamp(height, 64, 4096);

            if (reflectionTexture != null && reflectionTexture.width == width && reflectionTexture.height == height)
            {
                return;
            }

            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyUnityObject(reflectionTexture);
            }

            reflectionTexture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = $"{nameof(HoPlanarReflectionSurface)} RT ({name})",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            reflectionTexture.Create();
        }

        private void ConfigureReflectionCamera(
            Camera sourceCamera,
            Matrix4x4 reflectionMatrix,
            Vector3 sourcePosition,
            Vector3 planePosition,
            Vector3 clipNormal,
            float cameraPlaneDistance)
        {
            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.rect = FullViewportRect;
            reflectionCamera.pixelRect = new Rect(0.0f, 0.0f, reflectionTexture.width, reflectionTexture.height);
            reflectionCamera.aspect = reflectionTexture.width / (float)Mathf.Max(1, reflectionTexture.height);
            reflectionCamera.cullingMask = reflectionMask;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.nearClipPlane = Mathf.Max(0.001f, reflectionNearClipPlane);
            if (reflectionCamera.farClipPlane <= reflectionCamera.nearClipPlane)
            {
                reflectionCamera.farClipPlane = reflectionCamera.nearClipPlane + 0.001f;
            }

            if (!copyCameraClearFlags)
            {
                reflectionCamera.clearFlags = fallbackClearFlags;
                reflectionCamera.backgroundColor = fallbackBackgroundColor;
            }

            Vector3 reflectedPosition = reflectionMatrix.MultiplyPoint(sourcePosition);
            Vector3 reflectedForward = reflectionMatrix.MultiplyVector(sourceCamera.transform.forward);
            Vector3 reflectedUp = reflectionMatrix.MultiplyVector(sourceCamera.transform.up);
            reflectionCamera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));

            Vector3 sourceOffset = sourcePosition - sourceCamera.transform.position;
            Matrix4x4 sourceWorldToCamera = sourceCamera.worldToCameraMatrix;
            if (sourceOffset.sqrMagnitude > 0.0f)
            {
                sourceWorldToCamera *= Matrix4x4.Translate(-sourceOffset);
            }

            Matrix4x4 worldToCamera = sourceWorldToCamera * reflectionMatrix;
            reflectionCamera.worldToCameraMatrix = worldToCamera;

            if (useObliqueClipPlane)
            {
                Vector3 clipPlanePosition = planePosition;
                if (cameraPlaneDistance < minClipPlaneDistance)
                {
                    clipPlanePosition += clipNormal * (minClipPlaneDistance - cameraPlaneDistance);
                }

                Vector4 clipPlane = CameraSpacePlane(reflectionCamera, clipPlanePosition, clipNormal, 1.0f);
                reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
            }

            reflectionCamera.ResetCullingMatrix();
        }

        private void ApplyEnabledPropertyBlock()
        {
            Renderer surfaceRenderer = TargetRenderer;
            if (surfaceRenderer == null || reflectionTexture == null || reflectionCamera == null)
            {
                ApplyDisabledPropertyBlock();
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            surfaceRenderer.GetPropertyBlock(propertyBlock);
            if (overrideMaterialToggle)
            {
                propertyBlock.SetFloat(UsePlanarReflectionId, 1.0f);
            }
            propertyBlock.SetTexture(ReflectionTextureId, reflectionTexture);
            propertyBlock.SetMatrix(ReflectionTextureMatrixId, GetReflectionViewProjectionMatrix(reflectionCamera));
            propertyBlock.SetVector(ReflectionParamsId, new Vector4(1.0f, reflectionTexture.width, reflectionTexture.height, 0.0f));
            surfaceRenderer.SetPropertyBlock(propertyBlock);
        }

        private void ApplyDisabledPropertyBlock()
        {
            Renderer surfaceRenderer = TargetRenderer;
            if (surfaceRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            surfaceRenderer.GetPropertyBlock(propertyBlock);
            if (overrideMaterialToggle)
            {
                propertyBlock.SetFloat(UsePlanarReflectionId, 0.0f);
            }
            propertyBlock.SetVector(ReflectionParamsId, Vector4.zero);
            surfaceRenderer.SetPropertyBlock(propertyBlock);
        }

        private void ReleaseResources()
        {
            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyUnityObject(reflectionTexture);
                reflectionTexture = null;
            }

            if (reflectionCamera != null)
            {
                DestroyUnityObject(reflectionCamera.gameObject);
                reflectionCamera = null;
            }
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private Matrix4x4 GetReflectionViewProjectionMatrix(Camera camera)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return projection * camera.worldToCameraMatrix;
        }

        private Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float sideSign)
        {
            Vector3 offsetPosition = position + normal * clipPlaneOffset;
            Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
            Vector3 cameraPosition = worldToCamera.MultiplyPoint(offsetPosition);
            Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 reflection = Matrix4x4.identity;
            reflection.m00 = 1.0f - 2.0f * plane[0] * plane[0];
            reflection.m01 = -2.0f * plane[0] * plane[1];
            reflection.m02 = -2.0f * plane[0] * plane[2];
            reflection.m03 = -2.0f * plane[3] * plane[0];

            reflection.m10 = -2.0f * plane[1] * plane[0];
            reflection.m11 = 1.0f - 2.0f * plane[1] * plane[1];
            reflection.m12 = -2.0f * plane[1] * plane[2];
            reflection.m13 = -2.0f * plane[3] * plane[1];

            reflection.m20 = -2.0f * plane[2] * plane[0];
            reflection.m21 = -2.0f * plane[2] * plane[1];
            reflection.m22 = 1.0f - 2.0f * plane[2] * plane[2];
            reflection.m23 = -2.0f * plane[3] * plane[2];

            reflection.m30 = 0.0f;
            reflection.m31 = 0.0f;
            reflection.m32 = 0.0f;
            reflection.m33 = 1.0f;
            return reflection;
        }
    }
}
