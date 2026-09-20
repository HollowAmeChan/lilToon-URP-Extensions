#ifndef LIL_HO_OBJECT_BUFFER_PALETTE_INCLUDED
#define LIL_HO_OBJECT_BUFFER_PALETTE_INCLUDED

// palette 访问层。结构布局必须与 Runtime/ObjectBuffer/HoObjectBufferPaletteData.cs 逐字段一致。
// 规则（规划 §5.3）：像素里只有索引、属性永远在表里；越界一律回 unknown 行，
// **绝不 clamp 行号**（rowBase + slot 越界会落到别的角色的行上，读出来看着合法其实是错的）。

struct HoCharacterPartData
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

struct HoCharacterData
{
    uint rowBase;
    uint slotCount;
    uint tags;
    uint reserved;
};                          // 16 B

struct HoCharacterSelectionData
{
    uint selectionId;
    uint nameHash;
    uint tags;
    uint reserved;
    float4 displayColor;
};                          // 32 B

StructuredBuffer<HoCharacterPartData> _HoObjectBufferPalette;
StructuredBuffer<HoCharacterData> _HoObjectBufferCharacters;
StructuredBuffer<HoCharacterSelectionData> _HoObjectBufferSelections;
float _HoObjectBufferPartCount;
float _HoObjectBufferSelectionCount;
float _HoObjectBufferSelectionLayerCount;

uint HoObjectBufferCharacterId(uint partId)
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

HoCharacterPartData HoObjectBufferLoadPartByRow(uint row)
{
    uint count = (uint)max(0.0, _HoObjectBufferPartCount);
    if (count == 0u || row >= count)
    {
        row = 0u;   // unknown 行：显示色是洋红，让"未注册"看得见
    }

    return _HoObjectBufferPalette[row];
}

HoCharacterPartData HoObjectBufferLoadPart(uint partId)
{
    if (partId == 0u)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    uint characterId = HoObjectBufferCharacterId(partId);
    uint slotId = HoObjectBufferSlotId(partId);
    if (characterId == 0u || characterId >= 256u)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    HoCharacterData character = _HoObjectBufferCharacters[characterId];
    if (slotId >= object.slotCount)
    {
        return HoObjectBufferLoadPartByRow(0u);
    }

    return HoObjectBufferLoadPartByRow(object.rowBase + slotId);
}

HoCharacterSelectionData HoObjectBufferLoadSelection(uint selectionId)
{
    uint count = (uint)max(0.0, _HoObjectBufferSelectionCount);
    if (selectionId >= count)
    {
        selectionId = 0u;   // 0 = 无选择
    }

    return _HoObjectBufferSelections[selectionId];
}

// 同角色判断退化成一次高字节比较（规划 §5.1）。热路径不该为隔离判断查表。
bool HoObjectBufferIsSameCharacter(uint partIdA, uint partIdB)
{
    return partIdA != 0u && partIdB != 0u && HoObjectBufferCharacterId(partIdA) == HoObjectBufferCharacterId(partIdB);
}

#endif
