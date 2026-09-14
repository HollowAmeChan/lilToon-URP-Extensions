#ifndef LIL_HO_CHARACTER_BUFFER_PALETTE_INCLUDED
#define LIL_HO_CHARACTER_BUFFER_PALETTE_INCLUDED

// palette 访问层。结构布局必须与 Runtime/CharacterBuffer/HoCharacterBufferPaletteData.cs 逐字段一致。
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

StructuredBuffer<HoCharacterPartData> _HoCharacterBufferPalette;
StructuredBuffer<HoCharacterData> _HoCharacterBufferCharacters;
StructuredBuffer<HoCharacterSelectionData> _HoCharacterBufferSelections;
float _HoCharacterBufferPartCount;
float _HoCharacterBufferSelectionCount;
float _HoCharacterBufferSelectionLayerCount;

uint HoCharacterBufferCharacterId(uint partId)
{
    return partId >> 8;
}

uint HoCharacterBufferSlotId(uint partId)
{
    return partId & 0xFFu;
}

// ID 是整数身份：比较一律在整数上做，绝不在插值/滤波后的值上做（规划 §6 第 1 条）。
uint HoCharacterBufferDecodeId(float encoded)
{
    return (uint)round(saturate(encoded) * 255.0);
}

uint HoCharacterBufferDecodeIdExact(float2 encoded)
{
    return (HoCharacterBufferDecodeId(encoded.x) << 8) | HoCharacterBufferDecodeId(encoded.y);
}

HoCharacterPartData HoCharacterBufferLoadPartByRow(uint row)
{
    uint count = (uint)max(0.0, _HoCharacterBufferPartCount);
    if (count == 0u || row >= count)
    {
        row = 0u;   // unknown 行：显示色是洋红，让"未注册"看得见
    }

    return _HoCharacterBufferPalette[row];
}

HoCharacterPartData HoCharacterBufferLoadPart(uint partId)
{
    if (partId == 0u)
    {
        return HoCharacterBufferLoadPartByRow(0u);
    }

    uint characterId = HoCharacterBufferCharacterId(partId);
    uint slotId = HoCharacterBufferSlotId(partId);
    if (characterId == 0u || characterId >= 256u)
    {
        return HoCharacterBufferLoadPartByRow(0u);
    }

    HoCharacterData character = _HoCharacterBufferCharacters[characterId];
    if (slotId >= character.slotCount)
    {
        return HoCharacterBufferLoadPartByRow(0u);
    }

    return HoCharacterBufferLoadPartByRow(character.rowBase + slotId);
}

HoCharacterSelectionData HoCharacterBufferLoadSelection(uint selectionId)
{
    uint count = (uint)max(0.0, _HoCharacterBufferSelectionCount);
    if (selectionId >= count)
    {
        selectionId = 0u;   // 0 = 无选择
    }

    return _HoCharacterBufferSelections[selectionId];
}

// 同角色判断退化成一次高字节比较（规划 §5.1）。热路径不该为隔离判断查表。
bool HoCharacterBufferIsSameCharacter(uint partIdA, uint partIdB)
{
    return partIdA != 0u && partIdB != 0u && HoCharacterBufferCharacterId(partIdA) == HoCharacterBufferCharacterId(partIdB);
}

#endif
