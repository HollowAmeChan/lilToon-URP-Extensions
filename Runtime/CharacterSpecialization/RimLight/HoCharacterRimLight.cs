using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    public enum HoCharacterRimRegionKind { WholeGroup, Part, SelectionGroup }

    [Serializable]
    public struct HoCharacterRimRegion
    {
        public HoCharacterRimRegionKind kind;
        public string name;
    }

    /// <summary>A dedicated artistic source. No Unity Light is created or required.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ho Character Rim Light")]
    public sealed class HoCharacterRimLight : MonoBehaviour
    {
        internal static readonly HashSet<HoCharacterRimLight> Active = new HashSet<HoCharacterRimLight>();

        [ColorUsage(false, true), InspectorName("颜色"), Tooltip("这盏专用边光的 HDR 颜色。")] public Color color = Color.white;
        [Min(0), InspectorName("强度"), Tooltip("独立调节这盏边光的亮度，可用于动画调光。")] public float intensity = 1;
        [Range(0, 1), InspectorName("宽度"), Tooltip("按法线夹角控制亮边向表面内部扩展的程度。")] public float width = .3f;
        [Range(0, 1), InspectorName("边缘柔化"), Tooltip("控制亮边边界的过渡范围。")] public float softness = .5f;
        [InspectorName("接收区域"), Tooltip("限定哪些表面接收；投射集合始终取所属 OB 整组。")] public HoCharacterRimRegion receiverRegion;
        [InspectorName("投射阴影"), Tooltip("使用所属角色完整几何生成这盏边光的局部阴影。")] public bool castShadows = true;
        [Range(0, 1), InspectorName("阴影强度"), Tooltip("0 保留无遮挡边光并停止该源投影；1 完全接收该源阴影。")] public float shadowStrength = 1;

        public HoObjectBufferGroup OwnerGroup => GetComponentInParent<HoObjectBufferGroup>(true);
        public string Status { get; internal set; } = "等待角色特化 Rim 阶段。";
        public Vector3 Direction => -transform.forward;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() { Active.Remove(this); Status = "已停用。"; }
        private void OnValidate()
        {
            intensity = Finite(intensity) ? Mathf.Max(0, intensity) : 0;
            width = Finite(width) ? Mathf.Clamp01(width) : .3f;
            softness = Finite(softness) ? Mathf.Clamp01(softness) : .5f;
            shadowStrength = Finite(shadowStrength) ? Mathf.Clamp01(shadowStrength) : 1;
        }

        internal bool TryResolve(out HoObjectBufferGroup owner, out HoACQueryDescriptor query)
        {
            owner = OwnerGroup;
            query = default;
            if (!isActiveAndEnabled || owner == null || !owner.isActiveAndEnabled)
            { Status = "未找到有效的最近父级 OB 组。"; return false; }
            HoObjectBufferRegistry.EnsureBuilt();
            if (!HoObjectBufferRegistry.IsGroupValid(owner) || owner.groupId < 1 || owner.groupId > 255)
            { Status = "父级 OB 组尚无有效身份。"; return false; }
            if (receiverRegion.kind == HoCharacterRimRegionKind.WholeGroup)
                query = HoACQueryDescriptor.Resolve(HoACQueryKind.Group, null, owner.groupId, HoACMaskDomain.Objects);
            else if (receiverRegion.kind == HoCharacterRimRegionKind.Part)
            {
                uint part = HoObjectBufferRegistry.GetPartId(owner.groupId, receiverRegion.name);
                query = HoACQueryDescriptor.Resolve(HoACQueryKind.Identity, null, (int)part, HoACMaskDomain.Objects);
            }
            else if (receiverRegion.kind == HoCharacterRimRegionKind.SelectionGroup)
                query = HoACQueryDescriptor.Resolve(HoACQueryKind.SelectionGroup, receiverRegion.name, 0, HoACMaskDomain.Objects);
            else { Status = "接收区域类型无效。"; return false; }
            if (!query.IsValid) { Status = query.Error; return false; }
            Vector3 direction = Direction;
            if (!Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z) || direction.sqrMagnitude < 1e-8f
                || !Finite(intensity) || !Finite(color.r) || !Finite(color.g) || !Finite(color.b)
                || !Finite(width) || !Finite(softness) || !Finite(shadowStrength))
            { Status = "源方向、颜色或响应参数不是有效数值。"; return false; }
            if (!TryGetLinearRadiance(out Color radiance))
            { Status = "源的线性 HDR 颜色与强度乘积溢出；已跳过。"; return false; }
            if (intensity <= 0 || Mathf.Max(radiance.r, Mathf.Max(radiance.g, radiance.b)) <= 0)
            { Status = "源贡献为零。"; return false; }
            return true;
        }

        internal bool TryGetLinearRadiance(out Color radiance)
        {
            // Match Unity light authoring: color is converted to the active color space once.
            // Negative RGB contributes zero; valid HDR is never capped to an arbitrary maximum.
            Color converted = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            float strength = Mathf.Max(0, intensity);
            radiance = new Color(Mathf.Max(0, converted.r) * strength,
                Mathf.Max(0, converted.g) * strength, Mathf.Max(0, converted.b) * strength, 1);
            return Finite(radiance.r) && Finite(radiance.g) && Finite(radiance.b);
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
