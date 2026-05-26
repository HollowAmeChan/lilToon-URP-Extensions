# PostProcess 顺序与输入边界

> 2026-05-26 收口版。本文记录 ScreenProcess / ImageProcess 的当前边界、顺序和输入诊断。

## 0. 当前结论

PostProcess 被拆成两层：

- `ScreenProcess`：需要屏幕语义输入的效果层。
- `ImageProcess`：最终 image-domain 图像链。

旧 `HoPost` / `ShoostStack` 不再作为 Runtime / Editor / shader 主路径入口。

## 1. 推荐顺序

当前用户侧顺序以 `07-用户向RendererFeature使用与顺序.md` 为准：

1. `ShadowCast`
2. `MetadataBuffer`
3. `GeometryBuffer`
4. `SSS`
5. `OIT`
6. `CharacterSpecialization`
7. `ScreenProcess`
8. `ImageProcess`
9. `DebugTile`

`ScreenProcess` 必须晚于它真实读取的 Buffer / semantic feature；`ImageProcess` 位于最终图像链阶段。

## 2. ScreenProcess 输入边界

ScreenProcess 按 active layer 聚合真实需求。

可能读取：

- MetadataBuffer mask / surfaceData / custom / objectCustom。
- GeometryBuffer normalDepth / depth。
- 后续明确声明的 ShadowCast Lighting/Shadow 资源。

不默认读取：

- ImageProcess 中间图像。
- OIT accumulation / revealage。
- SSS composite weight。
- 没有 layer 消费者的未来 channel。

缺少当前 layer 需要的 Buffer 项时，shader 允许降级，但 Volume Inspector 必须显示需要 / 可用 / 未使用状态。

## 3. ImageProcess 输入边界

ImageProcess 只读取 camera color / ImageChain。

它不读取 MetadataBuffer、GeometryBuffer、ShadowCast、SSS、OIT 或 CharacterSpecialization。需要这些语义输入的效果必须留在 ScreenProcess。

## 4. Rule Mask Debug

ScreenProcess rule mask debug 不做 RendererFeature 侧独立 debug pass。

原因是 rule 本身已有直出选项：用户在具体 ScreenProcess layer 上开启规则遮罩调试后，由该 layer 的 shader 输出命中结果。公共 debug / tile 只记录这是 ScreenProcess 的 layer-local 观察入口，不额外创建 shader、material 或 pass。

## 5. RenderGraph / compatibility 状态

RenderGraph 是主线。

- ScreenProcess 在 RDG 中声明真实 Buffer 读取需求。
- ImageProcess 在 RDG 中只维护 ImageChain。
- 两者进入 RDG 记录时都释放 compatibility-only RTHandle / camera target 状态。
- 非 RG compatibility path 只作为 fallback，不影响 RDG 主线资源所有权。

## 6. 最终摘要

PostProcess 边界已经从旧 HoPost / ShoostStack 收敛为 ScreenProcess + ImageProcess：前者处理语义屏幕效果，后者处理最终图像链。调试、输入诊断和 RenderGraph 资源所有权都按这个拆分维护。
