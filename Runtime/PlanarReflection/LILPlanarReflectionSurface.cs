using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PlanarReflection
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/lilToon URP/Planar Reflection Surface")]
    public sealed class LILPlanarReflectionSurface : MonoBehaviour
    {
        private static readonly List<LILPlanarReflectionSurface> ActiveSurfaces = new List<LILPlanarReflectionSurface>();
        private static readonly int UsePlanarReflectionId = Shader.PropertyToID("_UsePlanarReflection");
        private static readonly int ReflectionTextureId = Shader.PropertyToID("_LILPBRPlanarReflectionTexture");
        private static readonly int ReflectionTextureMatrixId = Shader.PropertyToID("_LILPBRPlanarReflectionTextureMatrix");
        private static readonly int ReflectionParamsId = Shader.PropertyToID("_LILPBRPlanarReflectionParams");
        private static bool registered;
        private static bool isRenderingReflection;

        [SerializeField]
        private Renderer targetRenderer;

        [SerializeField]
        private LayerMask reflectionMask = -1;

        [SerializeField, Range(64, 4096)]
        private int resolution = 1024;

        [SerializeField, Min(0.0f)]
        private float clipPlaneOffset = 0.05f;

        [SerializeField]
        private bool hideSurfaceInReflection = true;

        [SerializeField]
        private bool overrideMaterialToggle = true;

        [SerializeField]
        private bool copyCameraClearFlags = true;

        [SerializeField]
        private CameraClearFlags fallbackClearFlags = CameraClearFlags.SolidColor;

        [SerializeField]
        private Color fallbackBackgroundColor = Color.black;

        [SerializeField]
        private bool reflectSceneView = true;

        [SerializeField, Min(1)]
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
            if (!ActiveSurfaces.Contains(this))
            {
                ActiveSurfaces.Add(this);
            }

            Register();
            ApplyDisabledPropertyBlock();
        }

        private void OnDisable()
        {
            ActiveSurfaces.Remove(this);
            ApplyDisabledPropertyBlock();
            ReleaseResources();

            if (ActiveSurfaces.Count == 0)
            {
                Unregister();
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void OnValidate()
        {
            resolution = Mathf.Clamp(resolution, 64, 4096);
            frameInterval = Mathf.Max(1, frameInterval);

            if (isActiveAndEnabled)
            {
                ApplyDisabledPropertyBlock();
            }
        }

        private static void Register()
        {
            if (registered)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += RenderAllSurfaces;
            registered = true;
        }

        private static void Unregister()
        {
            if (!registered)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= RenderAllSurfaces;
            registered = false;
        }

        private static void RenderAllSurfaces(ScriptableRenderContext context, Camera camera)
        {
            if (isRenderingReflection || camera == null || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
            {
                return;
            }

            isRenderingReflection = true;
            try
            {
                for (int i = 0; i < ActiveSurfaces.Count; i++)
                {
                    LILPlanarReflectionSurface surface = ActiveSurfaces[i];
                    if (surface != null)
                    {
                        surface.RenderReflection(context, camera);
                    }
                }
            }
            finally
            {
                isRenderingReflection = false;
            }
        }

        private void RenderReflection(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!isActiveAndEnabled || sourceCamera == null)
            {
                return;
            }

            if (sourceCamera.cameraType == CameraType.SceneView && !reflectSceneView)
            {
                ApplyDisabledPropertyBlock();
                return;
            }

            if (frameInterval > 1 && lastRenderedFrame >= 0 && Time.frameCount - lastRenderedFrame < frameInterval)
            {
                ApplyEnabledPropertyBlock();
                return;
            }

            Renderer surfaceRenderer = TargetRenderer;
            if (surfaceRenderer == null || !surfaceRenderer.enabled)
            {
                ApplyDisabledPropertyBlock();
                return;
            }

            EnsureResources(sourceCamera);
            if (reflectionCamera == null || reflectionTexture == null)
            {
                ApplyDisabledPropertyBlock();
                return;
            }

            Vector3 planePosition = transform.position;
            Vector3 planeNormal = transform.up.normalized;
            if (planeNormal.sqrMagnitude < 0.0001f)
            {
                ApplyDisabledPropertyBlock();
                return;
            }

            Matrix4x4 reflectionMatrix = CalculateReflectionMatrix(new Vector4(
                planeNormal.x,
                planeNormal.y,
                planeNormal.z,
                -Vector3.Dot(planeNormal, planePosition)));

            ConfigureReflectionCamera(sourceCamera, reflectionMatrix, planePosition, planeNormal);

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

            GameObject cameraObject = new GameObject($"{nameof(LILPlanarReflectionSurface)} ({name})");
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
                DestroyObject(reflectionTexture);
            }

            reflectionTexture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = $"{nameof(LILPlanarReflectionSurface)} RT ({name})",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            reflectionTexture.Create();
        }

        private void ConfigureReflectionCamera(Camera sourceCamera, Matrix4x4 reflectionMatrix, Vector3 planePosition, Vector3 planeNormal)
        {
            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.cullingMask = reflectionMask;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.useOcclusionCulling = false;

            if (!copyCameraClearFlags)
            {
                reflectionCamera.clearFlags = fallbackClearFlags;
                reflectionCamera.backgroundColor = fallbackBackgroundColor;
            }

            Vector3 reflectedPosition = reflectionMatrix.MultiplyPoint(sourceCamera.transform.position);
            Vector3 reflectedForward = reflectionMatrix.MultiplyVector(sourceCamera.transform.forward);
            Vector3 reflectedUp = reflectionMatrix.MultiplyVector(sourceCamera.transform.up);
            reflectionCamera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));

            Matrix4x4 worldToCamera = sourceCamera.worldToCameraMatrix * reflectionMatrix;
            reflectionCamera.worldToCameraMatrix = worldToCamera;

            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, planePosition, planeNormal, 1.0f);
            reflectionCamera.projectionMatrix = sourceCamera.CalculateObliqueMatrix(clipPlane);
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
                DestroyObject(reflectionTexture);
                reflectionTexture = null;
            }

            if (reflectionCamera != null)
            {
                DestroyObject(reflectionCamera.gameObject);
                reflectionCamera = null;
            }
        }

        private static void DestroyObject(Object target)
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
