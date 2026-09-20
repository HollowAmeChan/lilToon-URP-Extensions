#ifndef LIL_HO_SURFACE_BUFFER_COMMON_INCLUDED
#define LIL_HO_SURFACE_BUFFER_COMMON_INCLUDED

// Ho-SurfaceBuffer 的**跨仓共享编解码**：生产者（lilToon 的 SB 材质 pass）与所有消费者都必须用这一份，
// 否则 octa 的约定、owner 的布局会在两边各写一遍、各自漂移。
//
// 注意：这里**不依赖任何 URP/core 的 hlsl**，只用 HLSL 内建函数。

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
// Owner：16-bit IdentityId 存成 **两个字节**（R = 高字节、G = 低字节）
// ---------------------------------------------------------------------------
//
// 与 OB 身份池的 `Id0.r/.g` 同一套做法：**不用 R16_UNorm / R16_UINT 单独承载**。
// 理由：数值 pass 是 6 个 MRT，R16_UNorm 作为 MRT 在本仓库从未验证过（D3D 在附件组合非法时
// 会整趟丢 draw，表现就是"什么都没写"）；而"两个字节塞进 RGBA8"是 OB 已经在跑的组合。
// 解出来的 16 bit 与 OB 层 0 的 IdentityId 直接可比。

float2 HoSurfaceOwnerEncode(uint identityId)
{
    uint id = identityId & 0xFFFFu;
    return float2((float)((id >> 8) & 0xFFu) / 255.0, (float)(id & 0xFFu) / 255.0);
}

uint HoSurfaceOwnerDecode(float2 encoded)
{
    uint high = (uint)round(saturate(encoded.x) * 255.0);
    uint low = (uint)round(saturate(encoded.y) * 255.0);
    return (high << 8) | low;
}

#endif
