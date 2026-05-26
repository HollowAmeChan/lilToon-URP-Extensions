# MaterialBuffer 与 GeometryBuffer 规划

> 2026-05-26 收口版。本文记录当前 Buffer 语义边界和未来准入规则。

## 0. 当前命名

运行时当前落地为：

- `MetadataBuffer`：材质、对象、mask、surface metadata。
- `GeometryBuffer`：屏幕空间 normal / depth。

`MaterialBuffer` 保留为更高层语义名，用来讨论未来材质语义集合；当前实现入口使用 `Ho-MetadataBuffer`。

## 1. 设计原则

Buffer 只保存跨系统复用输入，不保存某个 feature 的 runtime 中间结果。

准入条件至少满足一项：

- 当前已有真实消费者。
- 下一阶段有明确消费者。
- 多个未来效果会共同复用。
- 无法廉价从 URP 内置资源、renderer data 或材质常量重新获得。

不满足则不默认输出。

## 2. MetadataBuffer 当前职责

当前保留：

- `Mask / Coverage`
- `ObjectId`
- `GroupId`
- `FeatureFlags`
- 已命名 object custom bits / character parts
- `MaterialClass`
- `Thickness`
- `Curvature`
- `TransmittanceHint`
- `SurfaceColor`

不负责：

- depth
- normal
- velocity / motion
- ShadowCast attenuation
- SSS composite weight
- OIT accumulation / revealage
- ImageProcess 临时图像结果

对象语义入口在 `HoMetadataBufferGroup` / `HoMetadataBufferSubject` / material inspector，不在 RendererFeature 面板批量管理全场景对象。

## 3. GeometryBuffer 当前职责

当前保留：

- `NormalDepth`
- 独立 `Depth`

暂不默认输出：

- `Motion / Velocity`
- `Coverage`
- `SceneObjectClass`
- `ViewNormal`
- `TangentNormal`

`ViewNormal` 可由 normal + view matrix 派生；`TangentNormal` 暂无真实 screen-space 消费者。

## 4. 当前消费者

- `SSS` 读取 MetadataBuffer mask / surfaceData / surfaceColor 和 GeometryBuffer normalDepth。
- `CharacterSpecialization` 读取 MetadataBuffer maskId / objectCustom0 / objectCustom1 和 GeometryBuffer normalDepth。
- `ScreenProcess` 按 active layer 聚合需求：EdgeLight / PostLighting 读取 mask + normalDepth；DropShadow / rule mask 按 source 追加 surfaceData / custom / objectCustom。
- `ImageProcess` 不读取 Buffer。
- `ShadowCast` 不写 Buffer；未来 ScreenProcess receiver 若出现，必须显式声明资源读取。

## 5. RenderGraph / compatibility 状态

RenderGraph 是主线。

Compatibility RTHandle 仅作为非 RG fallback；进入 RDG 记录时释放旧 RTHandle / camera target 状态。Buffer 禁用或 camera reset 时公开 global texture / active 状态回到空状态。

## 6. 未来准入

只有真实消费者出现后才新增：

- `GeometryBuffer.Motion / Velocity`：temporal、体积重投影、motion trail 等明确消费后加入。
- `MaterialBuffer.RoughnessLike / SmoothnessLike / EmissionHint`：wetness、deferred scene、atmospheric highlight 等明确消费后加入。
- `ShadowCast.Attenuation`：若跨系统复用，应作为 Lighting/Shadow 资源单独声明，不进入 MetadataBuffer / GeometryBuffer。
- 新 custom channel：必须有命名、编码范围、消费者和 debug 解释。

## 7. 最终摘要

本轮已删除旧 AOV 代码目录、旧 `_lilHoAov*` runtime alias、`HoAOV` / `HoAOVSSS` LightMode 运行入口，并完成材质模板对接。当前 Buffer 边界完成，后续不再从旧 HoAOV MRT 角度扩展通道。
