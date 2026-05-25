using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.ShadowCast
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("lilToon/URP Extensions/HoShadowCast 额外投影控制器")]
    public sealed class HoShadowCastController : MonoBehaviour
    {
        private static readonly List<HoShadowCastController> ActiveControllers = new List<HoShadowCastController>();

        [InspectorName("优先级")]
        public int priority;

        [InspectorName("额外方向光投影列表")]
        public Light[] directionalLights = new Light[HoShadowCastShaderConstants.MaxDirectionalLights];

        [InspectorName("额外聚光投影列表")]
        public Light[] spotLights = new Light[HoShadowCastShaderConstants.MaxSpotLights];

        [InspectorName("额外点光投影列表")]
        public Light[] pointLights = new Light[HoShadowCastShaderConstants.MaxPointLights];

        [InspectorName("Caster 图层遮罩")]
        [Tooltip("HoShadowCast 生成 shadow atlas 时使用的投射物图层过滤。接收侧不看这个遮罩。")]
        public LayerMask casterLayerMask = -1;

        [InspectorName("投影强度")]
        [Range(0.0f, 1.0f)]
        public float shadowStrength = 1.0f;

        [InspectorName("点光/聚光投影强度")]
        [Range(0.0f, 1.0f)]
        public float punctualShadowStrength = 0.5f;

        [InspectorName("点光/聚光范围衰减速度")]
        [Range(0.1f, 4.0f)]
        public float punctualShadowFadeSpeed = 1.0f;

        [Header("PCSS 软阴影")]
        [InspectorName("启用 PCSS")]
        public bool pcssEnabled = true;

        [InspectorName("PCSS 质量")]
        public HoShadowCastPcssQuality pcssQuality = HoShadowCastPcssQuality.High;

        [InspectorName("点光/聚光软阴影半径")]
        [Range(0.0f, 4.0f)]
        public float punctualPcssSoftness = 0.6f;

        [InspectorName("第二天光软阴影半径")]
        [Range(0.0f, 4.0f)]
        public float secondDirectionalPcssSoftness = 4.0f;

        [InspectorName("Blocker Search 半径")]
        [Range(0.25f, 8.0f)]
        public float pcssBlockerSearchRadius = 2.8f;

        [InspectorName("最大半影半径")]
        [Range(1.0f, 32.0f)]
        public float pcssMaxPenumbraRadius = 7.4f;

        [InspectorName("PCSS Depth Bias")]
        [Range(0.0f, 0.01f)]
        public float pcssDepthBias = 0.0f;

        [Header("第二天光级联")]
        [InspectorName("第二天光投影强度")]
        [Range(0.0f, 1.0f)]
        public float secondDirectionalShadowStrength = 0.3f;

        [InspectorName("第二天光 Atlas 尺寸")]
        [Min(256)]
        public int secondDirectionalAtlasSize = 4096;

        [InspectorName("第二天光级联数")]
        [Range(1, HoShadowCastShaderConstants.MaxSecondDirectionalCascades)]
        public int secondDirectionalCascadeCount = 4;

        [InspectorName("第二天光最大距离")]
        [Min(0.01f)]
        public float secondDirectionalMaxDistance = 80.0f;

        [InspectorName("第二天光深度")]
        [Min(0.01f)]
        public float secondDirectionalShadowDepth = 80.0f;

        [InspectorName("第二天光级联分割")]
        public Vector3 secondDirectionalCascadeSplits = new Vector3(0.08f, 0.22f, 0.5f);

        [InspectorName("Atlas 尺寸")]
        [Min(256)]
        public int atlasSize = 4096;

        [InspectorName("方向光分辨率")]
        [Min(64)]
        public int directionalResolution = 1024;

        [InspectorName("聚光分辨率")]
        [Min(64)]
        public int spotResolution = 512;

        [InspectorName("点光单面分辨率")]
        [Min(64)]
        public int pointFaceResolution = 512;

        [InspectorName("方向光近裁剪")]
        [Min(0.001f)]
        public float directionalNearPlane = 0.1f;

        [InspectorName("方向光投影范围")]
        [Min(0.01f)]
        public float directionalShadowSize = 20.0f;

        [InspectorName("方向光投影深度")]
        [Min(0.01f)]
        public float directionalShadowDepth = 40.0f;

        [InspectorName("调试模式")]
        public HoShadowCastDebugMode debugMode = HoShadowCastDebugMode.Off;

        public static HoShadowCastController ActiveController
        {
            get
            {
                HoShadowCastController best = null;
                for (int i = 0; i < ActiveControllers.Count; i++)
                {
                    HoShadowCastController controller = ActiveControllers[i];
                    if (controller == null || !controller.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (best == null || controller.priority > best.priority)
                    {
                        best = controller;
                    }
                }

                return best;
            }
        }

        private void OnEnable()
        {
            if (!ActiveControllers.Contains(this))
            {
                ActiveControllers.Add(this);
            }

            ValidateState();
        }

        private void OnDisable()
        {
            ActiveControllers.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveControllers.Remove(this);
        }

        private void OnValidate()
        {
            ValidateState();
        }

        private void ValidateState()
        {
            EnsureArraySize(ref directionalLights, HoShadowCastShaderConstants.MaxDirectionalLights);
            EnsureArraySize(ref spotLights, HoShadowCastShaderConstants.MaxSpotLights);
            EnsureArraySize(ref pointLights, HoShadowCastShaderConstants.MaxPointLights);

            shadowStrength = Mathf.Clamp01(shadowStrength);
            punctualShadowStrength = Mathf.Clamp01(punctualShadowStrength);
            punctualShadowFadeSpeed = punctualShadowFadeSpeed <= 0.0f ? 1.0f : Mathf.Clamp(punctualShadowFadeSpeed, 0.1f, 4.0f);
            pcssQuality = (HoShadowCastPcssQuality)Mathf.Clamp((int)pcssQuality, 0, 3);
            punctualPcssSoftness = Mathf.Clamp(punctualPcssSoftness, 0.0f, 4.0f);
            secondDirectionalPcssSoftness = Mathf.Clamp(secondDirectionalPcssSoftness, 0.0f, 4.0f);
            pcssBlockerSearchRadius = Mathf.Clamp(pcssBlockerSearchRadius, 0.25f, 8.0f);
            pcssMaxPenumbraRadius = Mathf.Clamp(pcssMaxPenumbraRadius, 1.0f, 32.0f);
            pcssDepthBias = Mathf.Clamp(pcssDepthBias, 0.0f, 0.01f);
            secondDirectionalShadowStrength = Mathf.Clamp01(secondDirectionalShadowStrength);
            secondDirectionalAtlasSize = Mathf.Max(256, secondDirectionalAtlasSize);
            secondDirectionalCascadeCount = Mathf.Clamp(secondDirectionalCascadeCount, 1, HoShadowCastShaderConstants.MaxSecondDirectionalCascades);
            secondDirectionalMaxDistance = Mathf.Max(0.01f, secondDirectionalMaxDistance);
            secondDirectionalShadowDepth = Mathf.Max(0.01f, secondDirectionalShadowDepth);
            secondDirectionalCascadeSplits = ClampCascadeSplits(secondDirectionalCascadeSplits);
            atlasSize = Mathf.Max(256, atlasSize);
            directionalResolution = Mathf.Max(64, directionalResolution);
            spotResolution = Mathf.Max(64, spotResolution);
            pointFaceResolution = Mathf.Max(64, pointFaceResolution);
            directionalNearPlane = Mathf.Max(0.001f, directionalNearPlane);
            directionalShadowSize = Mathf.Max(0.01f, directionalShadowSize);
            directionalShadowDepth = Mathf.Max(0.01f, directionalShadowDepth);
        }

        private static Vector3 ClampCascadeSplits(Vector3 splits)
        {
            float x = Mathf.Clamp(splits.x, 0.001f, 0.997f);
            float y = Mathf.Clamp(splits.y, x + 0.001f, 0.998f);
            float z = Mathf.Clamp(splits.z, y + 0.001f, 0.999f);
            return new Vector3(x, y, z);
        }

        private static void EnsureArraySize(ref Light[] array, int size)
        {
            if (array == null)
            {
                array = new Light[size];
                return;
            }

            if (array.Length == size)
            {
                return;
            }

            Light[] resized = new Light[size];
            int copyCount = Mathf.Min(array.Length, size);
            for (int i = 0; i < copyCount; i++)
            {
                resized[i] = array[i];
            }

            array = resized;
        }
    }
}
