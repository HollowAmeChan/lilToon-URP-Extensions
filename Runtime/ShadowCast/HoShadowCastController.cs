using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.ShadowCast
{
    public enum HoShadowCastDebugMode
    {
        Off = 0,
        Atlas = 1,
        SelectedLights = 2
    }

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
        [Tooltip("预留给后续 caster scope 过滤。第一版直接使用 URP 可见 shadow caster。")]
        public LayerMask casterLayerMask = -1;

        [InspectorName("投影强度")]
        [Range(0.0f, 1.0f)]
        public float shadowStrength = 1.0f;

        [InspectorName("Atlas 尺寸")]
        [Min(256)]
        public int atlasSize = 4096;

        [InspectorName("方向光分辨率")]
        [Min(64)]
        public int directionalResolution = 1024;

        [InspectorName("聚光分辨率")]
        [Min(64)]
        public int spotResolution = 1024;

        [InspectorName("点光单面分辨率")]
        [Min(64)]
        public int pointFaceResolution = 512;

        [InspectorName("方向光近裁剪")]
        [Min(0.001f)]
        public float directionalNearPlane = 0.1f;

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
            atlasSize = Mathf.Max(256, atlasSize);
            directionalResolution = Mathf.Max(64, directionalResolution);
            spotResolution = Mathf.Max(64, spotResolution);
            pointFaceResolution = Mathf.Max(64, pointFaceResolution);
            directionalNearPlane = Mathf.Max(0.001f, directionalNearPlane);
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
