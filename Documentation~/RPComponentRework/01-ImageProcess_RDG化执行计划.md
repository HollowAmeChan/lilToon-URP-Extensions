# ImageProcess RDG 化执行计划

> 本文只约束旧仓库 `Runtime/ShoostPostProcessing` 的后续改造。`Shoost` / `ShoostStack` 从现在起只作为旧实现名使用，新语义名统一为 `ImageProcess`。目标不是重写所有效果，而是把旧 ShoostStack 收敛成 RDG 管理的 ImageProcess 链。

---

## 0. 命名结论

新文档、新规划和新增代码应使用：

```text
ImageProcess
ImageProcessStack
ImageProcessLayer
ImageProcessEffect
ImageProcessPass
ImageChain
```

旧名只在引用现有代码路径时出现：

```text
ShoostPostProcessing
ShoostPostProcessRendererFeature
ShoostPostProcessPass
ShoostPostProcessLayer
ShoostPostProcessEffect
```

迁移期允许旧类名继续存在，但新增抽象、文档标题、review 清单和未来 API 不再使用 `ShoostStack` 作为概念名。

---

## 1. 当前事实

旧仓库 ImageProcess 现有实现仍在 `ShoostPostProcessing` 目录下，并已经有 RenderGraph 路径：

- `ShoostPostProcessRendererFeature.EnqueueRenderGraphPass()`
- `ShoostPostProcessPass.RecordRenderGraph()`
- 每个 effect 有 `Record*Layer()` 入口。

但现有长期风险是：

- 普通单 pass layer 会创建独立 destination。
- 多 pass effect 各自创建临时 texture。
- compatibility path 的 RTHandle 逻辑仍然影响代码结构。
- AOV composite / mask 和 ImageProcess 混在同一 stack 中，这是需要删除的旧能力。
- debug AOV mask 与常规 shader 逻辑耦合，应迁到 ScreenProcess 或对应 feature 的局部 debug。

---

## 2. 第一阶段：定义 ImageProcess 执行模型

在旧仓库内建立轻量 ImageChain，不引入新仓库的大注册表。

建议位置：

```text
Runtime/ShoostPostProcessing/Renderer/ImageChain/
```

最小类型：

```text
ImageProcessChain
ImageProcessChainContext
ImageProcessPassContext
ImageProcessResourceRequest
ImageProcessResourceKind
```

核心模型：

```text
Begin(cameraColorCopy)
  WorkA = source copy
  WorkB = transient same-desc texture

For each normal image layer:
  Read  = current
  Write = alternate
  Record(Read -> Write)
  Swap()

End()
  Blit current -> cameraColor
```

layer 顺序使用用户在 ImageProcess UI 中排列的顺序。descriptor 只提供默认插入位置、显示信息和资源声明，不在运行时强制重排用户栈。

验收：

- 10 个普通单 pass layer 不产生 10 张全分辨率独占 RT。
- 所有 texture handle 来自当前 frame 的 RenderGraph。
- WorkA / WorkB 不发布为长期全局纹理。
- 用户拖拽顺序就是 ImageChain 记录 pass 的顺序。

---

## 3. 第二阶段：单 pass effect 接入 ImageChain

优先迁移最简单的 single-pass effect：

- ColorGradingCustom
- CRTEffects
- Distortion
- DitheringCustom
- DownScaleResolution
- Fisheye
- GrainCustom
- LevelAdjustment
- Pixelize
- RGBSplit
- SharpenBefore / SharpenAfter
- Tube
- VignetteCustom
- VHS

要求：

- `RecordSinglePassLayer()` 不再创建 `_lilShoostPostProcessLayer{n}`。
- 单 pass executor 接收 `ImageProcessPassContext`。
- source/destination 由 ImageChain 提供。
- effect 只设置材质参数和记录 pass。

---

## 4. 第三阶段：多 pass effect 声明例外资源

多 pass effect 仍可申请额外 RDG texture，但必须局部声明原因。

第一批：

- Glow：pyramid / blur chain。
- IrisBlur：local ping-pong。
- RGBBlurV2：local ping-pong。
- ApertureBokeh：multi-pass blur / composite。
- BokehZoomBlur：multi-pass 或 radial sample。
- SkyGodRays：可能需要 mask / blur。
- MotionTrail：history。

每个 effect 增加局部资源声明：

```text
NeedsLocalPingPong
NeedsPyramid
NeedsHistory
NeedsOriginalSource
NeedsExternalTexture
```

这些声明不进入全局注册表，只用于 ImageProcess pass 内部 plan 和 debug 输出。
`NeedsAovInput`、`NeedsMaterialBuffer`、`NeedsGeometryBuffer`、`NeedsShadowCast` 这类语义输入声明不属于 ImageProcess。出现这些需求时，effect 应迁到 ScreenProcess。

---

## 5. 第四阶段：删除 AOV composite / mask 输入

旧 Shoost 的 AOV composite、`useAovMask`、`debugAovMask` 不作为新 ImageProcess 能力保留。

要求：

- ImageProcess runtime 不读取 AOV / MaterialBuffer / GeometryBuffer / ShadowCast。
- ImageProcess UI 不显示 AOV mask、semantic mask、object/material rule。
- 旧资源中的 `useAovMask` / `debugAovMask` 应迁移到 ScreenProcess layer，或显示迁移提示。
- AOV debug 输出归 MaterialBuffer/GeometryBuffer 或 ScreenProcess 的局部 debug，不归 ImageProcess。

如果某个 effect 对 object/material/depth/normal/mask/ShadowCast 有任何依赖，应迁出到 ScreenProcess，而不是留在 ImageProcess。

---

## 6. 第五阶段：移除 compatibility path 对主结构的影响

旧 compatibility path 可以短期保留，但不能继续主导结构。

规则：

- 新增 effect 只要求 RDG 路径完整。
- compatibility path 可以调用同一套参数构建逻辑，但不允许反向约束 RDG。
- 如果某个 effect 的 compatibility path 阻碍 RDG 生命周期，应先冻结 compatibility 功能，再迁 RDG。

---

## 7. 代码落点

优先改：

```text
Runtime/ShoostPostProcessing/Renderer/ShoostPostProcessPass.cs
Runtime/ShoostPostProcessing/Renderer/EffectPipeline/ShoostPostProcessPass.EffectDispatch.cs
Runtime/ShoostPostProcessing/Renderer/EffectPipeline/ShoostPostProcessPass.EffectProperties.cs
Runtime/ShoostPostProcessing/Renderer/Effects/*.cs
Runtime/ShoostPostProcessing/ShoostPostProcessEffectDescriptor.cs
Runtime/ShoostPostProcessing/Renderer/ShoostPostProcessAovSupport.cs
```

不要先改 editor UI。先让 runtime 资源生命周期正确，再整理 UI。

---

## 8. 验收清单

- 普通 single-pass stack 只使用 source copy + WorkA/WorkB。
- 多 pass effect 的额外 RT 都是 RenderGraph transient。
- camera color 不被同一 pass 同时读写。
- ImageProcess 没有 AOV composite / semantic mask / NeedsAovInput 路径。
- ImageProcess 不读取 MaterialBuffer / GeometryBuffer / ShadowCast。
- 关闭 ImageProcess 后不保留 stale RT / global texture / debug state。
