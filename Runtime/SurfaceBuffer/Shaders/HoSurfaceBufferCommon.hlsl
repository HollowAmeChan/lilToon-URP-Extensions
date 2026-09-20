#ifndef LIL_HO_SURFACE_BUFFER_COMMON_INCLUDED
#define LIL_HO_SURFACE_BUFFER_COMMON_INCLUDED

// Ho-SurfaceBuffer 的**跨仓共享编解码**：生产者（lilToon 的 SB 材质 pass）与所有消费者都必须用这一份，
// 否则 octa 的约定、owner 的缩放会在两边各写一遍、各自漂移。
//
// 注意：这里**不依赖任何 URP/core 的 hlsl**（URP 的 Core.hlsl 并不包含 core 的 Packing.hlsl，
// 依赖它就会变成"编译不过 → 整屏什么都不输出"）。只用 HLSL 内建函数。

// ---------------------------------------------------------------------------
// 法线：octa 编码，直接存到 [0,1]（RT 里就是最终值，消费端一次解码）
// ---------------------------------------------------------------------------

float2 HoSurfaceOctEncode(float3 n)
{
    n /= max(1e-6, abs(n.x) + abs(n.y) + abs(n.z));
    float2 res = n.xy;
    if (n.z < 0.0)
    {
        res = (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    }

    return res * 0.5 + 0.5;
}

float3 HoSurfaceOctDecode(float2 encoded)
{
    float2 f = encoded * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t.xx : t.xx;
    return normalize(n);
}

// ---------------------------------------------------------------------------
// Owner：16-bit IdentityId ↔ `R16_UNorm`（0..65535 在 UNorm16 上是逐值精确的）
// ---------------------------------------------------------------------------

float HoSurfaceOwnerEncode(uint identityId)
{
    return (float)(identityId & 0xFFFFu) / 65535.0;
}

uint HoSurfaceOwnerDecode(float encoded)
{
    return (uint)round(saturate(encoded) * 65535.0);
}

#endif
