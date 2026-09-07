# PCSS 占位（暂缓）

> 状态：**占位，暂不做**（2026-XX）。
> 依据：URP17 **没有内置 PCSS**（源码 0 处命中，只有 PCF `_SHADOWS_SOFT_LOW/MEDIUM/HIGH`）；ShadowCast 已有 `HoShadowCastPcssQuality`（自研质量档）可供未来接入。

## 目标（未来做的时候）

- 给“需要软阴影的 cast 组”提供 **percentage-closer soft shadows**（可独立于 PCF）。
- 主光（URP）与 ShadowCast 附加灯都可选接入；**每 cast 组可独立开关/调参**（半径/采样数/软度）。

## 接入点（已预留）

- 生产：`Ho-ShadowCast` 的 cast 组 → 阴影纹理 → 材质/常量数组（§9.2 分组模型）。
- 消费：材质 toon 门控（`shadow.main` / `shadow.add0..N` 通道）不变——**换 PCSS 对材质零改动**（这正是“材质不碰生成逻辑”的实证）。
- 常量：组内 `PCSSRadius` / `PCSSSoftness`（每组的 `pcssEnabled` + 参数数组）。

## 验收（未来）

1. 软阴影边过渡可调、无 PCSS 常见“光环/漏光”。
2. 关闭时回退 PCF（降级即回退）。
3. AOV `shadow.*` 导出不受影响（只是纹理内容不同）。

## 不做清单

- 不做“每灯一个 PCSS 档”（按 cast 组即可）。
- 不做 PCSS 全屏后处理替代。
