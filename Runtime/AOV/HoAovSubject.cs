using System;
using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.AOV
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HoAovSubject : MonoBehaviour
    {
        private static readonly List<Renderer> RendererCache = new List<Renderer>();

        [InspectorName("系统写入通道")]
        [Tooltip("此对象允许写入的系统 AOV 通道。最终输出还会受 HoAOV RendererFeature 的系统通道过滤。")]
        public HoAovChannelMask systemWriteChannels = HoAovChannelMask.Default;

        [InspectorName("自定义写入遮罩")]
        [Tooltip("自定义通道写入位。Bit 0 对应 Custom0。")]
        public uint customWriteMask;

        [InspectorName("遮罩权重")]
        [Tooltip("写入 HoAOV Mask 通道的权重。")]
        [Range(0.0f, 1.0f)]
        public float maskWeight = 1.0f;

        [InspectorName("分组 ID")]
        [Tooltip("给 HoPost/Shoost/HTrace 等消费者使用的分组 ID。")]
        public int groupId;

        [InspectorName("对象 ID")]
        [Tooltip("对象 ID。为 0 时使用组件实例 ID。")]
        public int objectId;

        [InspectorName("材质分类")]
        [Tooltip("材质或区域分类。未来用于承接 lilToon/lilPBR 的层语义。")]
        public int materialClass;

        [InspectorName("标记")]
        [Tooltip("给 AOV 消费者使用的自由标记值。")]
        public uint flags = 1;

        [InspectorName("厚度")]
        [Tooltip("近似材质厚度。第一版由用户/材质手动提供。")]
        [Min(0.0f)]
        public float thickness;

        [InspectorName("曲率")]
        [Tooltip("近似曲率。第一版由用户/材质手动提供。")]
        public float curvature;

        [InspectorName("系统预留")]
        [Tooltip("系统预留值，可用于 AO、trace 输入或后续桥接。")]
        public float utility;

        [InspectorName("调试颜色")]
        [Tooltip("调试视图和 ID 可视化可使用的颜色。")]
        public Color debugColor = Color.white;

        [InspectorName("自定义通道值")]
        [Tooltip("用户自定义通道值。缺失的项按 0 处理。")]
        public float[] customValues = new float[HoAovCustomChannels.DefaultCount];

        private MaterialPropertyBlock propertyBlock;

        private void Reset()
        {
            ApplyToRenderers();
        }

        private void OnEnable()
        {
            ApplyToRenderers();
        }

        private void OnValidate()
        {
            EnsureCustomValues();
            ApplyToRenderers();
        }

        public void ApplyToRenderers()
        {
            EnsureCustomValues();
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            GetComponentsInChildren(true, RendererCache);
            for (int i = 0; i < RendererCache.Count; i++)
            {
                Renderer targetRenderer = RendererCache[i];
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(propertyBlock);
                ApplyProperties(propertyBlock);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }

            RendererCache.Clear();
        }

        private void ApplyProperties(MaterialPropertyBlock block)
        {
            block.SetFloat(HoAovShaderConstants.MaskWeightId, maskWeight);
            block.SetFloat(HoAovShaderConstants.SystemWriteMaskId, (float)systemWriteChannels);
            block.SetFloat(HoAovShaderConstants.CustomWriteMaskId, customWriteMask);
            block.SetFloat(HoAovShaderConstants.GroupIdId, groupId);
            block.SetFloat(HoAovShaderConstants.ObjectIdId, GetEffectiveObjectId());
            block.SetFloat(HoAovShaderConstants.MaterialClassId, materialClass);
            block.SetFloat(HoAovShaderConstants.FlagsId, flags);
            block.SetFloat(HoAovShaderConstants.ThicknessId, thickness);
            block.SetFloat(HoAovShaderConstants.CurvatureId, curvature);
            block.SetFloat(HoAovShaderConstants.UtilityId, utility);
            block.SetColor(HoAovShaderConstants.DebugColorId, debugColor);
            block.SetVector(HoAovShaderConstants.CustomValues0Id, GetCustomVector(0));
            block.SetVector(HoAovShaderConstants.CustomValues1Id, GetCustomVector(4));
            block.SetVector(HoAovShaderConstants.CustomValues2Id, GetCustomVector(8));
        }

        private int GetEffectiveObjectId()
        {
            return objectId != 0 ? objectId : Math.Abs(GetInstanceID());
        }

        private Vector4 GetCustomVector(int startIndex)
        {
            return new Vector4(
                GetCustomValue(startIndex),
                GetCustomValue(startIndex + 1),
                GetCustomValue(startIndex + 2),
                GetCustomValue(startIndex + 3));
        }

        private float GetCustomValue(int index)
        {
            return customValues != null && index >= 0 && index < customValues.Length
                ? customValues[index]
                : 0.0f;
        }

        private void EnsureCustomValues()
        {
            if (customValues == null)
            {
                customValues = new float[HoAovCustomChannels.DefaultCount];
                return;
            }

            if (customValues.Length >= HoAovCustomChannels.DefaultCount)
            {
                return;
            }

            Array.Resize(ref customValues, HoAovCustomChannels.DefaultCount);
        }
    }
}
