# HoShadowCast 软阴影 PCSS 规划

> 历史资料：本文记录旧 controller/manual-list 阶段的 PCSS 规划，不作为当前 `Ho-ShadowCast` 使用说明。当前 ShadowCast 行为以 `../RPComponentRework/06-ShadowCast改造计划.md`、用户顺序以 `../RPComponentRework/07-用户向RendererFeature使用与顺序.md`、完成计划以 `../RPComponentRework/09-重构完成计划.md` 为准。

本文记录 HoShadowCast 软阴影升级方案。结论：不先做轻量 PCF，直接做 PCSS。

## 2026-05-18 当前落地状态

已实现：

```text
1. Controller 暴露 PCSS 启用、质量、点光/聚光半径、第二天光半径、blocker search 半径、最大半影半径和 depth bias。
2. CPU 每帧上传普通 atlas 与第二天光 atlas 各自的 PCSS softness 参数，并共享 blocker/filter 采样数。
3. HoShadowCastSampling.hlsl 已从 hardware compare 改为 raw depth + manual compare。
4. PCSS 关闭或 softness 为 0 时，会走 raw depth 的 3x3 manual PCF。
5. PCSS 开启时，会先做 blocker search，再按平均 blocker depth 算半影半径，最后做 variable-radius PCF。
6. 普通 atlas 和第二天光 atlas 都接入同一套 PCSS 采样函数，采样会 clamp 在当前 tile 内。
```

暂未实现：

```text
1. PCSS 专用 debug overlay：BlockerCount / Penumbra / FilterRadius。
2. 点光 cube face 跨 face 软化。
3. 按第二天光 cascade index 自动调整半径。
4. HoAOV depth 的 receiver guard / 屏幕空间重建版本。
```

## 前提

当前 HoShadowCast 已经有两张 atlas：

```text
_HoShadowCastAtlas                    点光 / 聚光
_HoShadowCastSecondDirectionalAtlas   额外方向光级联
```

系统运行在 HoAOV 之后时，可以读取 HoAOV 已经生成的 depth / coverage：

```text
_lilHoAovNormalDepthTexture.a = linear eye depth
```

但 PCSS 的 blocker search 不能用 HoAOV depth 代替。PCSS 需要比较 receiver 与 blocker 在同一盏灯的 shadow map 里的深度，因此 blocker search 必须读取 HoShadowCast 自己的 shadow atlas raw depth。HoAOV depth 用于 receiver 端保护、屏幕空间覆盖判断和后续调试，不作为 blocker depth。

## 目标

```text
1. 点光、聚光、第二天光级联都走 PCSS。
2. atlas tile 之间绝不串采样。
3. 默认值尽量接近当前硬阴影，不让旧场景突然大面积变糊。
4. 性能不是第一优先级，可以接受较多采样。
5. 失败时回退为 lit 或当前硬阴影，不允许出现整片黑。
```

## 参数

Controller 建议新增：

```text
PCSS 启用
点光/聚光软阴影半径
第二天光软阴影半径
Blocker Search 半径
Blocker Search 采样数
PCF Filter 采样数
最大半影半径
PCSS Depth Bias
```

建议默认：

```text
PCSS 启用: true
点光/聚光软阴影半径: 0.5
第二天光软阴影半径: 0.35
Blocker Search 半径: 2.0 texels
Blocker Search 采样数: 16
PCF Filter 采样数: 32
最大半影半径: 12 texels
PCSS Depth Bias: 0.0005
```

采样数可以先固定成 shader 常量，UI 只给 Low / Medium / High / Ultra 档，避免材质侧动态循环太碎：

```text
Low:     8 blocker + 16 filter
Medium: 16 blocker + 32 filter
High:   24 blocker + 48 filter
Ultra:  32 blocker + 64 filter
```

## Shadow Atlas 读取

当前 receiver 使用 `TEXTURE2D_SHADOW` 和 hardware compare。PCSS 需要 raw depth，因此采样头要改成 raw depth 路径：

```text
TEXTURE2D_FLOAT(_HoShadowCastAtlas)
TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas)
```

然后自己实现比较：

```text
lit = rawDepth >= receiverDepth - bias
```

注意当前 debug 里空 depth 接近 1，因此空 atlas 区域应视为 lit。所有 raw depth 采样都必须 clamp 到当前 slice tile 内：

```text
atlasMin = slice.xy + halfTexel
atlasMax = slice.xy + slice.zz - halfTexel
sampleUv = clamp(sampleUv, atlasMin, atlasMax)
```

## PCSS 流程

每次采样某个 slice：

```text
1. world -> shadowCoord。
2. shadowCoord.xy / z 不在有效范围时返回 1。
3. 用小半径做 blocker search。
4. 如果没有 blocker，返回 1。
5. 根据 receiverDepth 与 averageBlockerDepth 算 penumbra。
6. 用 penumbra 半径做 PCF filter。
7. 返回 0..1 shadow attenuation。
```

伪代码：

```hlsl
float blockerDepth = 0;
int blockerCount = 0;

for sample in blockerPattern:
    rawDepth = SampleRawDepth(tileUv + sample * searchRadius);
    if rawDepth < receiverDepth - bias:
        blockerDepth += rawDepth;
        blockerCount++;

if blockerCount == 0:
    return 1;

averageBlockerDepth = blockerDepth / blockerCount;
penumbra = saturate((receiverDepth - averageBlockerDepth) / max(averageBlockerDepth, 0.0001));
filterRadius = min(maxRadius, baseSoftness * penumbra);

shadow = 0;
for sample in filterPattern:
    rawDepth = SampleRawDepth(tileUv + sample * filterRadius);
    shadow += rawDepth >= receiverDepth - bias ? 1 : 0;

return shadow / filterSampleCount;
```

## 点光 / 聚光

点光仍然只采当前 cube face，不跨 face。第一版不处理 cube face 边缘连续性，避免引入接缝方向重投影。

点光 / 聚光的最终强度仍然乘已有范围衰减：

```text
shadowStrength *= HoShadowCastLightInfluence(...)
```

PCSS filter 半径可以乘 light range 中的距离因子，让远处略软：

```text
punctualRadius = basePunctualSoftness * lerp(0.5, 1.5, saturate(distance / light.range))
```

## 第二天光级联

第二天光每盏方向光每次只采命中的 cascade。PCSS 半径需要避免 cascade 边界突然变糊：

```text
1. 半径以 texel 为单位。
2. 每个 cascade 使用相同最大 texel 半径。
3. 远 cascade 可乘一个很小的增益，但必须有上限。
```

第一版建议：

```text
secondDirectionalRadius = baseSecondDirectionalSoftness
```

先不根据 cascade index 自动放大，等确认 cascade 稳定后再加。

## HoAOV Depth 的用途

HoAOV depth 不参与 blocker search，但可以做三件事：

```text
1. Receiver coverage guard：没有 HoAOV coverage 的像素不做后续屏幕空间调试/合成。
2. Debug overlay：显示 PCSS blocker count、penumbra、filter radius 时，用 HoAOV depth 只覆盖主体区域。
3. 如果将来把 HoShadowCast 从材质 forward 接收改为 HoAOV 后的屏幕空间合成，HoAOV depth 可用于重建 receiver positionWS，再调用同一套 PCSS 采样函数。
```

如果仍由 lilToon / lilPBR forward pass 消费，材质阶段天然拿不到“之后才生成”的 HoAOV depth。那时 HoAOV depth 只用于 debug / 后续屏幕空间版本，不影响当前材质 receiver。

## 实施顺序

```text
1. 把 HoShadowCastSampling.hlsl 从 hardware compare 改成 raw depth + manual compare。已完成。
2. 保留当前 3x3 PCF 结果，确认 raw compare 与旧效果一致。已完成代码路径，仍需 Unity 画面验证。
3. 加 PCSS 参数和 Controller UI。已完成。
4. 实现 blocker search + variable PCF。已完成。
5. 普通 atlas 与第二天光 atlas 分别接入 PCSS。已完成。
6. Debug 增加 PCSS 模式：RawDepth / BlockerCount / Penumbra / FilterRadius。
7. 文档记录 atlas tile clamp、raw depth 比较方向、HoAOV depth 只做 receiver guard 的规则。
```

## 风险点

```text
1. Raw depth 比较方向必须和当前平台 shadow map 一致。先用 raw compare 复刻现有硬阴影，再上 PCSS。
2. blocker search 半径太大时会采到 tile 边界，必须 clamp。
3. 空 depth 必须返回 lit。
4. 点光 face 边缘第一版可能仍有软阴影接缝，先接受，不跨 face。
5. 第二天光 cascade 边界可能因 penumbra 不一致变明显，第一版不要按 cascade index 自动放大半径。
6. shader variant / include 影响 lilToon 和 lilPBR，两边都要验证。
```
