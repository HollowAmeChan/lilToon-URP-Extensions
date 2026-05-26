# ShadowCast 改造总结

> 2026-05-26 收口版。本文记录 ShadowCast 当前架构、过滤语义和后续边界。

## 0. 当前结论

`ShadowCast` 已从旧手动 controller / list 模型改为 Lighting/Shadow 组件：每帧从 URP `visibleLights` 自动收集额外投影灯，并按 feature 设置决定 atlas 写入和 debug 输出。

旧入口已删除：

- 手动 light list
- scene controller
- renderer 侧 compatibility target 常驻状态
- 与 MetadataBuffer / GeometryBuffer 混写的资源语义

## 1. 资源职责

ShadowCast 只负责额外投影资源：

- visible light 收集
- atlas block 分配
- shadow slice / block 写入
- light 与 receiver 过滤
- debug view / tile 观察

它不写 MetadataBuffer / GeometryBuffer，不把 attenuation 伪装成 buffer channel。

如果未来 ScreenProcess 需要 ShadowCast receiver / attenuation，应作为 Lighting/Shadow 资源显式读取，而不是塞回 MetadataBuffer。

## 2. 过滤语义

当前保留两类过滤：

- `lightRenderingLayerMask`：从 URP visible light 侧筛选可进入 ShadowCast 的光。
- `casterRenderingLayerMask`：控制哪些 renderer 可参与额外 shadow caster。

GameObject layer 与 URP Rendering Layer 语义必须分开，不再混用一个旧 mask 表达。

## 3. RenderGraph 边界

RenderGraph 是主线。

- RDG pass 内声明 atlas 写入和后续 debug / consumer 读取。
- 非 RG compatibility target 只作为 fallback。
- 进入 RDG 记录时释放 compatibility-only atlas / camera target 状态。
- 禁用、无有效 visible light 或 camera reset 时清空 frame-local collector 状态。

## 4. Debug 边界

ShadowCast debug 归 ShadowCast 自己声明，并通过 `HoDebugViewRegistry` 暴露给公共 UI / DebugTile。

应能观察：

- atlas
- block occupancy
- light slice
- receiver / caster 过滤结果
- fallback 或缺资源状态

不通过 ScreenProcess rule mask 或 ImageProcess debug 间接观察 ShadowCast。

## 5. 后续准入

后续只在有真实消费者时新增：

- ScreenProcess receiver 读取。
- 多层 atlas 策略。
- temporal shadow cache。
- 更细的 light type / shadow mode debug。

新增能力必须保持 Lighting/Shadow 资源所有权，不回退到旧 AOV 或 PostProcess 通道。

## 6. 最终摘要

ShadowCast 当前边界完成：自动 visible-light 收集、Rendering Layer 过滤、atlas / block debug 和 RDG compatibility 状态收口已经成为主线。后续只做明确消费者驱动的 ShadowCast 能力扩展。
