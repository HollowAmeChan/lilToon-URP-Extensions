# Debug Shader 按需编译策略

> 2026-05-26 收口版。本文记录当前 debug view contract、registry 和 tile view 边界。

## 0. 当前结论

Debug 不再由一个旧 AOV / HoPost 中央 shader 集合统一兜底。每个 feature 声明自己的 debug view，公共 UI 只做只读汇总和 tiled 展示。

当前所有权：

- `MetadataBuffer`：metadata channel debug。
- `GeometryBuffer`：normal / depth debug。
- `ShadowCast`：atlas、block、light slice、receiver 相关 debug。
- `SSS`：SSS 输入与中间结果 debug。
- `ScreenProcess`：公共 registry 只展示可观察入口；rule mask debug 由 layer-local 直出。
- `ImageProcess`：只观察 image-domain 输入输出。

## 1. Feature-local view contract

每个 feature 用自己的 `<Feature>DebugViewInfo.cs` 声明：

- view id
- short name
- display name
- shader / material 来源
- fallback 说明
- 是否可进入公共 tile

公共代码不硬编码各 feature 的 shader pass 细节。

## 2. Registry

`HoDebugViewRegistry` 是只读汇总层。

它负责：

- 收集 feature-local view info。
- 暴露缺失 shader collection / fallback 状态。
- 给公共 debug window 和 tile renderer 提供 view 列表。

它不负责：

- 生成 shader collection。
- 创建 feature shader、material 或 render target。
- 推断 ScreenProcess rule mask 的 layer 内部状态。

## 3. Tile View

最终 tile view 参考 `D:\Unity_Fork\HoUrp-Extensions` 的 `RenderCacheDebugRendererFeature` / `RenderCacheDebugTile` 思路：由 registry 自动生成 tiled debug 画面，而不是手写固定 tile 列表。

当前 tile 应覆盖：

- MetadataBuffer
- GeometryBuffer
- ShadowCast
- SSS

ScreenProcess rule mask 不进入 RendererFeature 级 tile；它仍由具体 layer 的 debug output 直出。

## 4. 按需编译规则

- Debug shader 只在对应 feature 或 view 被启用时进入需求集合。
- 缺失 shader / material 时必须有明确 fallback 和 UI 状态。
- 不为了兼容旧 HoAOV / HoPost 视图常驻编译无消费者 shader。
- 新 debug view 必须先归属到 feature-local owner，再进入 registry。

## 5. 最终摘要

Debug 系统已经从“中央旧调试集合”收敛为 feature-local 声明 + registry 汇总 + 自动 tile。后续扩展只加具体 feature 的 debug view，不恢复旧 AOV / HoPost 统一调试入口。
