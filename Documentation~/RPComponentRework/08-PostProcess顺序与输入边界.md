# PostProcess 顺序与输入边界

> 本文约束 `ScreenProcess` 与 `ImageProcess` 两个 post 系统。结论：两个系统都应支持用户自定义层顺序；只有 ScreenProcess 能消费语义输入；ImageProcess 只处理当前图像链。

---

## 0. 命名

```text
HoPost / HoPostProcessing         -> ScreenProcess
Shoost / ShoostStack / ShoostPost -> ImageProcess
```

旧类名可作为迁移期实现名保留。用户文档、新 UI、新 descriptor 使用 `ScreenProcess` / `ImageProcess`。

---

## 1. 两个系统的职责

### ScreenProcess

ScreenProcess 是语义感知屏幕处理。

允许读取：

- MaterialBuffer。
- GeometryBuffer。
- camera depth / normal。
- object id / group id / part bit / feature flag。
- ShadowCast 资源，但必须是明确的 ScreenProcess receiver 或 debug。
- SSS / OIT / CharacterSpecialization 明确发布给屏幕处理的资源。

典型效果：

- outline。
- edge light。
- drop shadow。
- post lighting。
- semantic depth of field。
- character/object/material targeted composite。
- screen-space shadow receiver。

### ImageProcess

ImageProcess 是最终图像处理链。

只允许读取：

- 当前 image chain 输入。
- 自己 layer 参数。
- 自己 layer 显式提供的用户贴图，例如 LUT、noise、logo、overlay texture。
- 自己 effect 内部申请的 RDG 临时资源。
- 自己 effect 明确声明的 history，且只能用于图像域 temporal 效果。

禁止读取：

- MaterialBuffer。
- GeometryBuffer。
- HoAOV / MaterialBuffer mask。
- camera depth / normal。
- object id / group id / part bit / feature flag。
- ShadowCast atlas / attenuation / light data。
- SSS diffusion / composite / profile buffer。
- OIT accumulation / revealage。
- CharacterSpecialization capture。

如果一个旧 Shoost/ImageProcess effect 需要这些输入，它不再属于 ImageProcess，应迁到 ScreenProcess。

---

## 2. 顺序规则

旧仓库中 HoPost / Shoost 有较多固定排序逻辑。新方向不继续固定内部顺序。

规则：

- ScreenProcess layer 按用户在 UI 中排列的顺序执行。
- ImageProcess layer 按用户在 UI 中排列的顺序执行。
- descriptor 只提供显示分组、资源需求和迁移兼容信息。
- descriptor 不应在运行时强制重排用户栈。
- 只有依赖关系不可交换的复合效果，才允许在该 effect 内部固定自己的 sub-pass 顺序。

示例：

```text
ScreenProcess:
  Layer 0: Outline
  Layer 1: EdgeLight
  Layer 2: DropShadow

ImageProcess:
  Layer 0: ColorAdjust
  Layer 1: Grain
  Layer 2: CRT
  Layer 3: Vignette
```

用户拖拽顺序就是实际执行顺序。

---

## 3. ImageChain 与顺序

ImageChain 做好后，ImageProcess 的顺序调整应很简单：

```text
Begin(cameraColorCopy)

For each user layer in order:
  Read  = ImageChain.Current
  Write = ImageChain.Next
  Record layer pass
  Swap()

End(write current back to camera color)
```

这意味着普通图像层不再需要固定 effect order，也不需要每层独占同规格 RT。
多 pass 图像效果仍可在自己的 effect 内部申请临时 RDG 资源，但外部层顺序仍由用户栈决定。

---

## 4. 旧 Shoost mask 处理

旧 Shoost 的 AOV composite / useAovMask / debugAovMask 不作为新 ImageProcess 能力保留。

迁移规则：

- 如果某个 Shoost layer 只是纯图像效果，保留为 ImageProcess。
- 如果某个 Shoost layer 需要 mask、object id、part bit、MaterialBuffer、GeometryBuffer、depth/normal 或 ShadowCast，迁到 ScreenProcess。
- ImageProcess 不提供 `NeedsAovInput`、`semantic image pass` 或类似后门。
- ImageProcess debug 只观察图像链、layer 参数和 effect 内部临时资源。

这条规则会减少 ImageProcess 复杂度，也避免它重新变成第二套 ScreenProcess。

---

## 5. UI 规则

ScreenProcess UI：

- 以 layer list 为主。
- 支持拖拽排序。
- 每个 layer 显示输入依赖，例如 MaterialBuffer、GeometryBuffer、ShadowCast。
- mask/rule UI 只出现在 ScreenProcess。

ImageProcess UI：

- 以 layer list 为主。
- 支持拖拽排序。
- 不显示 AOV mask、semantic mask、object/material rule。
- 如果用户需要按对象/材质/区域控制效果，应提示使用 ScreenProcess。

---

## 6. 执行迁移

1. 快速开发阶段不保留旧 ImageProcess AOV mask 序列化兼容，直接删除旧字段。
2. 直接删除旧固定 effect order 的 runtime 元数据和重排门面，不再为旧排序模式保留额外迁移字段。
3. Editor UI 对现有资源统一启用拖拽排序。
4. 需要 AOV mask 的旧 Shoost 效果在 ScreenProcess layer 重新实现，不从 ImageProcess 保留迁移字段。
5. ImageProcess runtime 删除 AOV composite、semantic image pass 和 NeedsAovInput 路径。
6. Frame Debugger pass 名标出用户 layer index，方便对照 UI 顺序。

---

## 7. 2026-05-24 执行记录

已在 ImageProcess 旧实现中完成第一批顺序与输入边界收敛：

- `ShoostPostProcessRuntimeLayerBuilder` 保留 Volume `layers` 列表顺序，不再调用 `ShoostPostProcessEffectOrder.CompareRuntimeLayerOrder()` 强制重排。
- `ShoostPostProcessPass.RecordRenderGraph()` 使用 ImageChain 双工作纹理推进用户层顺序。
- ImageProcess RenderGraph 路径不再读取 AOV / MaterialBuffer mask 资源，不再提供 AOV composite 或 AOV debug 输出。
- 过渡期曾保留旧 `useAovMask` / `debugAovMask` 字段；该兼容外壳已在 2026-05-25 删除。
- `ShoostPostProcessEffectDescriptor` 不再保留 `RuntimeOrder`；旧 `ShoostPostProcessEffectOrder` 兼容门面已删除，用户 layer 顺序成为唯一外部顺序来源。

已继续落地 Editor 侧顺序与迁移提示：

- `ShoostPostProcessStackVolumeEditor` 的 layer list 开启拖拽排序。
- Inspector 不再调用旧固定 effect order 自动重排，只保留废弃 effect slot / 重复 layer 清理。
- 添加 layer 和应用 preset 后不再按旧 effect 顺序移动 layer，用户列表顺序即 ImageProcess 执行顺序。
- Shoost/ImageProcess UI 不再显示 AOV mask 规则编辑器；过渡期迁移提示已在 2026-05-25 删除，语义 mask 只在 ScreenProcess 实现。

已继续收敛 runtime 输入边界：

- ImageProcess compatibility path 不再执行旧 AOV composite；过渡期 `useAovMask` / `debugAovMask` 迁移 warning 已在 2026-05-25 删除。
- 删除旧 AOV composite cache、RDG AOV composite partial、AOV support 门面和 `AovComposite.shader`。
- ImageProcess descriptor 不再携带 `SupportsAovComposite`，新增 effect 不能再声明 AOV mask 支持。
- 普通 ImageProcess layer material 不再写入 `_LayerAov*` property。

## 8. 2026-05-25 执行记录

在不需要旧资源兼容的快速开发阶段，已把 ImageProcess 的旧 AOV mask 迁移外壳直接删除：

- `ShoostPostProcessLayer` 不再序列化 `useAovMask` / `debugAovMask` 和 AOV rule 数据。
- `ShoostPostProcessPass` 不再为 legacy AOV mask 做 warning 或忽略分支；ImageProcess 中没有 semantic mask 状态可读。
- `ImageProcessResourceKind` 只保留 image-chain 内部资源类型，不再包含 `AovInput`、`MaterialBuffer`、`GeometryBuffer`、`ShadowCast`。
- `ShoostPostProcessStackVolumeEditor` 删除 legacy AOV mask 迁移提示与清理按钮。需要对象、材质、区域或 buffer 输入的效果必须在 ScreenProcess 实现。

---

## 9. 验收清单

- ScreenProcess 用户拖拽顺序就是执行顺序。
- ImageProcess 用户拖拽顺序就是执行顺序。
- ImageProcess 没有 AOV mask / semantic mask UI。
- ImageProcess runtime 不读取 MaterialBuffer / GeometryBuffer / ShadowCast。
- ImageProcess layer 序列化数据不再包含旧 Shoost AOV mask 字段。
- 10 个普通 ImageProcess layer 只复用 ImageChain 工作纹理。
- 多 pass ImageProcess effect 的内部 sub-pass 顺序固定，但外部 layer 顺序仍由用户控制。
