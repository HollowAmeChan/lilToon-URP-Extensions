using UnityEngine;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    /// <summary>
    /// MB 调试视图的模式号。**与 `HoMetadataBufferDebug.shader` 的 `mode == N` 字面量、
    /// 以及 `HoMetadataBufferDebugViewInfo.Views` 的登记表必须三处一致**。
    /// <para>
    /// R6 把 surface 族（thickness / curvature / material / transmittanceHint / surfaceColor /
    /// reflectionMaterial）删掉后重排过：4..19 是 custom0-3 / objectCustom0-7 / RSUV 四项。
    /// 旧场景里存的调试模式号会落到别的视图上 —— 它只是调试显示，不做兼容映射。
    /// </para>
    /// </summary>
    public enum HoMetadataBufferDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Mask")]
        Mask,
        [InspectorName("ID")]
        Id,
        [InspectorName("Flags")]
        Flags,
        [InspectorName("Material Custom 0")]
        Custom0,
        [InspectorName("Material Custom 1")]
        Custom1,
        [InspectorName("Material Custom 2")]
        Custom2,
        [InspectorName("Material Custom 3")]
        Custom3,
        [InspectorName("CharacterFull")]
        ObjectCustom0,
        [InspectorName("Face")]
        ObjectCustom1,
        [InspectorName("Front Hair")]
        ObjectCustom2,
        [InspectorName("Eye")]
        ObjectCustom3,
        [InspectorName("Eye Reveal Area")]
        ObjectCustom4,
        [InspectorName("Accessory")]
        ObjectCustom5,
        [InspectorName("CharacterBody")]
        ObjectCustom6,
        [InspectorName("Reserved 7")]
        ObjectCustom7,
        [InspectorName("RSUV Packed")]
        RsuvPacked,
        [InspectorName("RSUV Character Group ID")]
        RsuvCharacterId,
        [InspectorName("RSUV Part ID")]
        RsuvPartId,
        [InspectorName("RSUV Flags")]
        RsuvFlags
    }
}
