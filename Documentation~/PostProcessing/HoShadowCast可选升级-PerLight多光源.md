# HoShadowCast 可选升级：Per-Light 多光源染色

本文记录 HoShadowCast 后续可选升级方向。当前实现已经可用：所有 HoShadowCast 灯统一合成一个 receiver shadow field，混进材质主阴影入口，并用同一个 attenuation 约束 additional / HDR 补光，避免暗区被重新提亮。

这个方案虽然不严格物理，但简单、稳定、和 lilToon / lilPBR 自身阴影模型兼容，足够作为第一版默认行为。Per-light 多光源染色属于可选升级，不应阻塞当前版本。

## 目标

把 HoShadowCast 从“只提供投射阴影”升级为“受控多光源集合”：

```text
HoShadowCast light = color + direction + range/spot 衰减 + shadow attenuation
```

材质侧不再只把 HoShadowCast 当作一个统一阴影 gate，而是可以逐盏灯评估颜色贡献。这样点光/聚光的亮面染色、范围衰减、spot cone 和投射阴影都来自同一份 HoShadowCast light data，会比“URP additional light 颜色 + HoShadowCast gate”更对齐。

## 当前方案的风险

当前统一 receiver/gate 方案有两个已知取舍：

1. 点光/聚光 cast 会影响主阴影入口，亮面可能被所有投影一起削弱。
2. additional / HDR 补光只是被统一 attenuation 约束，不知道具体是哪一盏 cast light 造成的遮挡。

这些问题在风格化画面里通常可以接受，因为开启 HoShadowCast 的目的就是额外压暗 receiver。但当后续希望多光源染色更精确时，应考虑 per-light 升级。

## 设计语义

建议拆成两个不同用途的函数：

```hlsl
float HoShadowCastReceiverAttenuation(float3 positionWS);
float HoShadowCastBrighteningAttenuation(float3 positionWS);
```

`HoShadowCastReceiverAttenuation` 用于材质主阴影入口。它可以保守一些，避免所有 punctual cast 全量压亮面。

`HoShadowCastBrighteningAttenuation` 用于约束材质自己的 additional / HDR 补光。它可以使用完整的 HoShadowCast attenuation，避免补光冲掉 cast 暗区。

per-light 升级再新增逐灯评估函数：

```hlsl
void HoShadowCastEvaluatePunctualLighting(
    float3 positionWS,
    float3 normalWS,
    out half3 lightColor,
    out half3 lightDirection);
```

这个函数只评估 HoShadowCast 管理的 point / spot lights。每盏灯贡献大致为：

```text
light.color * light.intensity
* rangeFade
* spotFade
* shadowAttenuation
* controller/user strength
```

对于 lilToon，输出可以进入 `addLightColor` / HDR additional light；对于 lilPBR，输出可以进入 `DoLight`，让 diffuse/spec 也按同一盏 HoShadowCast light 计算。

## Fallback

无 HoShadowCast 时必须完全回退到材质原本行为：

```hlsl
if (_HoShadowCastActive < 0.5 || _HoShadowCastLightCount <= 0 || _HoShadowCastSliceCount <= 0)
{
    // 使用原本 URP additional / HDR 多光源路径
}
```

如果 HoShadowCast 存在，但某些普通 URP additional lights 不属于 HoShadowCast collection，它们仍应走原本材质路径。

## 去重问题

per-light 升级最大的风险是双算光。

如果同一盏 Unity Light 同时存在于 URP additional lights 和 HoShadowCast collection 中，而材质又同时累加两边颜色，就会亮两遍。

需要明确 ownership：

```text
无 HoShadowCast：
  全部走材质原本 URP additional / HDR 路径。

有 HoShadowCast：
  未被 HoShadowCast 管理的 additional lights 继续走原路径。
  被 HoShadowCast 管理的 point / spot lights 由 HoShadowCast collection 接管颜色、衰减和 shadow。
```

实现上需要 shader 能判断“当前 URP additional light 是否属于 HoShadowCast”。优先方案是 CPU 上传 Ho light 对应的 URP additional-light index；备选方案是 shader 用 position / range / type 做近似匹配，但这更脆弱。

## 数据需求

当前 HoShadowCast 已经有大部分数据：

```text
_HoShadowCastLightData0: type, firstSlice, sliceCount, shadowStrength
_HoShadowCastLightData1: position, range
_HoShadowCastLightData2: direction, spotCos
_HoShadowCastLightAttenuation: range fade speed / spot attenuation 参数
_HoShadowCastLightColor: color * intensity
```

per-light 去重可能还需要新增：

```text
_HoShadowCastLightIndexData:
  x = visibleLightIndex 或 additionalLightIndex
  y = ownership flags
  z/w = reserved
```

如果以后想让 HoShadowCast 完全接管点光/聚光染色，也可以给 Controller 增加每类 light 的 color strength / additional contribution strength，但第一版不需要。

## 建议落地顺序

1. 保留当前统一 receiver/gate 方案作为默认实现。
2. 新增 `HoShadowCastReceiverAttenuation` 和 `HoShadowCastBrighteningAttenuation`，先只是包装当前统一函数，建立语义边界。
3. 新增 HoShadowCast punctual lighting evaluation，只在 lilToon 中试接 `addLightColor`，先不做 URP additional 去重。
4. 增加 CPU 上传 light index mapping，解决同一盏灯双算问题。
5. lilToon 对被 Ho 接管的 light 跳过原 URP additional contribution，普通 additional lights 保持原逻辑。
6. lilPBR 再接 per-light diffuse/spec，确认高光和染色都符合预期。

## 暂不做的原因

这套升级值得做，但不是当前问题的必要解。

当前统一 receiver/gate 方案已经解决：

```text
atlas 正确写入
receiver 正确采样
点光/聚光范围淡出
材质阴影颜色跟随自身模型
additional/HDR 补光不冲掉 cast 暗区
无 HoShadowCast 时 fallback 为 1
```

per-light 版本会引入 light ownership、URP additional index mapping、双算光去重、lilToon/lilPBR 两套材质路径适配等复杂度。建议等当前版本稳定后，再作为“多光源染色质量升级”单独推进。
