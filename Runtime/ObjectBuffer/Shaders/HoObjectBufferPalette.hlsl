#ifndef LIL_HO_OBJECT_BUFFER_PALETTE_INCLUDED
#define LIL_HO_OBJECT_BUFFER_PALETTE_INCLUDED

// palette 访问层。结构布局必须与 Runtime/ObjectBuffer/HoObjectBufferPaletteData.cs 逐字段一致。
// 规则（规划 §5.3）：像素里只有索引、属性永远在表里；越界一律回 unknown 行，
// **绝不 clamp 行号**（rowBase + slot 越界会落到别的角色的行上，读出来看着合法其实是错的）。

struct HoObjectPartData
{
    uint partId;            // 角色 8 + 槽位 8
    uint nameHash;
    uint category;
    uint tags;              // 位掩码
    float thickness;
    float curvature;
    float transmittance;
    float roughness;
    float metallic;
    float reflectance;
    float plrStrength;
    uint materialClass;
    float4 displayColor;
};                          // 64 B

struct HoObjectGroupData
{
    uint rowBase;
    uint slotCount;
    uint tags;
    uint reserved;
};                          // 16 B

struct HoObjectSelectionData
{
    uint selectionId;
    uint nameHash;
    uint tags;
    uint reserved;
    float4 displayColor;
};                          // 32 B

// 部件行表（规划里叫"条目表"）：名字与注册表上传时用的全局名保持一致。
StructuredBuffer<HoObjectPartData> _HoObjectBufferEntries;
StructuredBuffer<HoObjectGroupData> _HoObjectBufferGroups;
StructuredBuffer<HoObjectSelectionData> _HoObjectBufferSelections;
float _HoObjectBufferPartCount;
float _HoObjectBufferSelectionCount;
float _HoObjectBufferSelectionLayerCount;

uint HoObjectBufferGroupId(uint partId)
{
    return partId >> 8;
}

uint HoObjectBufferSlotId(uint partId)
{
    return partId & 0xFFu;
}

// ID 是整数身份：比较一律在整数上做，绝不在插值/滤波后的值上做（规划 §6 第 1 条）。
uint HoObjectBufferDecodeId(float encoded)
{
    return (uint)round(saturate(encoded) * 255.0);
}

uint HoObjectBufferDecodeIdExact(float2 encoded)
{
    return (HoObjectBufferDecodeId(encoded.x) << 8) | HoObjectBufferDecodeId(encoded.y);
}

HoObjectPartData HoObjectBufferLoadPartByRow(uint row)
{
    uint count = (uint)max(0.0, _HoObjectBufferPartCount);
    if (count == 0u || row >= count)
    {
        row = 0u;   // unknown 行：显示色是洋红，让"未注册"看得见
    }

    return _HoObjectBufferEntries[row];
}

HoObjectPartData HoObjectBufferLoadPart(uint partId)
{
    if (partId == 0u)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    uint groupId = HoObjectBufferGroupId(partId);
    uint slotId = HoObjectBufferSlotId(partId);
    if (groupId == 0u || groupId >= 256u)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    HoObjectGroupData group = _HoObjectBufferGroups[groupId];
    if (slotId >= group.slotCount)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    return HoObjectBufferLoadPartByRow(group.rowBase + slotId);
}

HoObjectSelectionData HoObjectBufferLoadSelection(uint selectionId)
{
    uint count = (uint)max(0.0, _HoObjectBufferSelectionCount);
    if (selectionId >= count)
    {
        selectionId = 0u;   // 0 = 无选择
    }

    return _HoObjectBufferSelections[selectionId];
}

// 同组判断退化成一次高字节比较（规划 §5.1）。热路径不该为隔离判断查表。
bool HoObjectBufferIsSameGroup(uint partIdA, uint partIdB)
{
    return partIdA != 0u && partIdB != 0u && HoObjectBufferGroupId(partIdA) == HoObjectBufferGroupId(partIdB);
}

#endif
