#ifndef LIL_HO_OBJECT_BUFFER_IDPASS_INCLUDED
#define LIL_HO_OBJECT_BUFFER_IDPASS_INCLUDED

// 打包要用到 HoObjectBufferCharacterId / SlotId，所以这里把 palette 表一起带进来——
// 否则每个用它的 shader 都得自己记得包含两次（漏一次就是 "undeclared identifier"）。
#include "HoObjectBufferPalette.hlsl"

// ID pass 的写入约定（规划 §5.1 / §5.4）：
//   非 MSAA：直接写 3 张 RGBA8 层图（层0/层1 装进 Id0、层2/层3 装进 Id1、覆盖率逐通道）；
//   MSAA   ：逐样本只写一个 16 bit ID（一个样本只属于一个部件），由 resolve 数票产生 4 层。
// 选择层按 Cryptomatte 的成对布局：一张 RGBA8 = (ID0, 覆盖率0, ID1, 覆盖率1)。

uint HoObjectBufferPackIdByte(uint value)
{
    return value & 0xFFu;
}

// 一张 RGBA8 装两个 (角色, 槽位) 对：R/G = 层A，B/A = 层B。
float4 HoObjectBufferPackIdRow(uint partIdA, uint partIdB)
{
    return float4(
        (float)HoObjectBufferPackIdByte(HoObjectBufferCharacterId(partIdA)) / 255.0,
        (float)HoObjectBufferPackIdByte(HoObjectBufferSlotId(partIdA)) / 255.0,
        (float)HoObjectBufferPackIdByte(HoObjectBufferCharacterId(partIdB)) / 255.0,
        (float)HoObjectBufferPackIdByte(HoObjectBufferSlotId(partIdB)) / 255.0);
}

// 一张 RGBA8 装两个 (选择 ID, 覆盖率) 对。
float4 HoObjectBufferPackSelectionRow(uint selectionIdA, float coverageA, uint selectionIdB, float coverageB)
{
    return float4(
        (float)HoObjectBufferPackIdByte(selectionIdA) / 255.0,
        saturate(coverageA),
        (float)HoObjectBufferPackIdByte(selectionIdB) / 255.0,
        saturate(coverageB));
}

// 选择层的解包（消费端用）：ID 必须 round 回整数，覆盖率保持线性。
void HoObjectBufferUnpackSelection(float4 packed, out uint selectionIdA, out float coverageA, out uint selectionIdB, out float coverageB)
{
    selectionIdA = (uint)round(saturate(packed.r) * 255.0);
    coverageA = packed.g;
    selectionIdB = (uint)round(saturate(packed.b) * 255.0);
    coverageB = packed.a;
}

#endif
