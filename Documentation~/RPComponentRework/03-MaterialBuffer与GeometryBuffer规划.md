# MaterialBuffer 与 GeometryBuffer 规划

> 这份文档从未来要做的效果反推两个 Buffer 应该承载什么。原则不是复刻当前 HoAOV MRT，而是保留能被多个系统消费的通用输入，删除没有真实消费者的垃圾通道。

---

## 0. 命名

后续统一使用：

```text
MaterialBuffer
GeometryBuffer
```

不用 `MBuffer/GBuffer` 作为新系统组分名，避免和 URP/Deferred 里的既有 GBuffer 语境混淆。

---

## 1. 设计原则

Buffer 里只放“跨系统复用的输入”，不放某个 feature 的运行时中间结果。

必须满足至少一个条件：

- 当前已有真实消费者。
- 下一阶段明确要接入的 feature 会消费。
- 它是多个未来效果共同需要的表面/几何语义。
- 它无法廉价从 URP 内置资源、renderer data 或材质常量重新获得。

不满足这些条件的，不默认输出。

成熟管线参考见 `04-HDRP_UE参考边界.md`。这里的候选项会参考 HDRP/UE 的 buffer、debug visualization 和 RDG 思路，但不直接照搬它们的 GBuffer layout。

---

## 2. 用户侧 UI 原则

MaterialBuffer / GeometryBuffer 的用户 UI 不能继续做成“全局大集合批量设置”。Buffer 是渲染数据层，不应该让用户在一个巨大面板里管理所有对象语义。

用户面对的主入口应是当前对象或当前集合：

- 选中一个角色、部件或 prefab 集合时，只展开这个对象/集合相关的 Buffer 标记。
- `HoAovGroup` 这类集合组件是正确方向：按角色组、部件和已命名 object custom 位拖入对象。
- `HoAovSubject` 只作为高级/兼容覆盖路径，用于 MaterialPropertyBlock 覆盖材质默认值或旧流程。
- RendererFeature 面板只负责全局输出开关、render scale、pass event、debug view 和高级兼容项。
- RendererFeature 面板不列出全场景所有对象，不承担对象语义编辑器职责。

推荐交互：

```text
Renderer Data / MaterialBuffer+GeometryBuffer Feature
    只配置输出、调试和全局兼容策略

Scene Object / Character Root / Prefab Root
    使用 Group UI 展开当前对象或集合
    只编辑当前对象/集合的 CharacterId、PartId、FeatureFlags、已命名 bits

Material Inspector
    只编辑材质自身的 SurfaceColor、Thickness、Curvature、MaterialClass/Profile 等默认语义
```

如果需要“批量看全场景有哪些标记”，应做成只读诊断视图或搜索工具，而不是把它变成主要编辑入口。

---

## 3. 未来消费者

规划 Buffer 时要提前考虑这些方向：

- SSS / skin diffusion。
- 丁达尔效应。
- 大气散射。
- 体积雾。
- 水体与湿润表面。
- 屏幕空间角色合成。
- 风格化 outline / edge light / drop shadow。
- OIT 与透明合成。
- 场景 deferred，角色 forward 的混合路径。
- temporal / motion trail / history-based image process。

这些效果最常共享的是“表面本身是什么”和“屏幕空间几何关系”，而不是旧 HoAOV 的每个实验通道。

---

## 4. MaterialBuffer

MaterialBuffer 表达表面、材质、对象语义。

### 第一批建议保留

```text
MaterialBuffer.SurfaceColor / DiffuseColor
MaterialBuffer.SubsurfaceColor
MaterialBuffer.MaterialClass
MaterialBuffer.MaterialProfile
MaterialBuffer.Thickness
MaterialBuffer.Curvature
MaterialBuffer.ObjectId
MaterialBuffer.GroupId
MaterialBuffer.FeatureFlags
MaterialBuffer.CharacterPart
```

### SurfaceColor / DiffuseColor

旧 HoAOV 里的 `SssSource.rgb` 应改名理解为：

```text
MaterialBuffer.SurfaceColor.rgb
```

它不是 SSS 私有 source，而是通用表面散射颜色。SSS 会读它，未来丁达尔、大气、体积雾、水体透射、湿润表面和屏幕空间合成也可能读它。

如果需要单独表达 subsurface tint，可以再规划：

```text
MaterialBuffer.SubsurfaceColor
```

但不能为了 SSS 私有逻辑把它叫成 `SssSource`。

### Alpha / Weight

旧 `SssSource.a` 不能默认继承为 SSS weight。

只有当 alpha 表达通用材质语义时才进入 MaterialBuffer，例如：

- surface participation
- translucency hint
- density hint
- opacity coverage

如果 alpha 表达的是 SSS feature 的 runtime composite weight，则归 SSS 自己。

### MaterialClass

建议用粗分类服务多个系统：

```text
Skin
Hair
Cloth
Water
Glass
Metal
Foliage
Effect
```

不要把每个 effect 的开关都塞进 class。class 说明“它是什么”，feature flags 才说明“它参与什么”。

### MaterialProfile

profile 是跨 feature 的参数索引，不是某个 shader UI 的历史 enum。

可用于：

- SSS profile。
- fog interaction profile。
- water profile。
- atmospheric response profile。
- stylized material response profile。

### Thickness / Curvature

这两个是当前 SSS 已经需要、未来透射和散射也可能复用的通用材质/表面语义，可以优先保留。

---

## 5. GeometryBuffer

GeometryBuffer 表达屏幕空间几何关系。

### 第一批建议保留

```text
GeometryBuffer.Depth
GeometryBuffer.Normal
```

### 第二批候选

```text
GeometryBuffer.Motion
GeometryBuffer.Coverage
GeometryBuffer.SceneObjectClass
```

第二批只有当 temporal、体积重投影、角色/场景覆盖判定明确需要时再输出。

### 暂不默认输出

```text
GeometryBuffer.TangentNormal
```

切线法线目前没有明确 screen-space 消费者。未来如果有各向异性屏幕滤波、头发高光后处理、特殊材质局部方向滤波，再作为明确需求加入。

---

## 6. 删除或降级的旧 HoAOV 通道

### 整体遮罩

不默认输出“总 mask”。

真实消费者通常需要的是：

- object id
- group id
- part bit
- material class
- feature flag
- thickness / curvature

如果先输出一个整体遮罩，再让消费者 decode 或 remap，会多一层没有语义价值的中间层。

### 预留 Custom

没有命名、编码、范围和消费者的 custom 通道不输出。

允许存在：

- 已命名 object custom bits。
- 已命名 material custom channels。
- 当前 feature 私有 custom，但不能进入公共 Buffer。

### Tangent Normal

默认移出 HoAOV 输出。后续按真实需求进入 GeometryBuffer。

---

## 7. 候选项准入表

| 候选项 | 所属 | 当前状态 | 准入判断 |
| --- | --- | --- | --- |
| SurfaceColor / DiffuseColor | MaterialBuffer | 应补入 | SSS 和未来散射/雾/水体都可能消费 |
| MaterialClass | MaterialBuffer | 保留 | ScreenProcess/SSS/未来 profile 分派需要 |
| MaterialProfile | MaterialBuffer | 保留 | SSS 与未来材质响应 profile 需要 |
| Thickness | MaterialBuffer | 保留 | SSS/透射/薄物体散射需要 |
| Curvature | MaterialBuffer | 保留 | SSS/边缘散射/风格化边缘可能复用 |
| ObjectId / GroupId | MaterialBuffer | 保留 | 角色合成与屏幕处理需要 |
| CharacterPart | MaterialBuffer | 保留已命名位 | 眼透/前发/角色局部合成需要 |
| FeatureFlags | MaterialBuffer | 保留已命名位 | 参与能力 gate |
| Depth | GeometryBuffer | 保留 | 所有屏幕空间效果基础 |
| Normal | GeometryBuffer | 保留 | SSS/outline/fog/deferred 需要 |
| Motion | GeometryBuffer | 候选 | temporal 需求明确后加入 |
| TangentNormal | GeometryBuffer | 删除默认 | 暂无真实消费者 |
| OverallMask | MaterialBuffer | 删除默认 | 语义太宽，消费者应读具体项 |
| ReservedCustom | 不进入公共 Buffer | 删除默认 | 无命名无消费者 |

---

## 8. 执行顺序

1. 盘点当前 HoAOV 每个 MRT 的真实消费者。
2. 把 `SssSource.rgb` 重命名规划为 `SurfaceColor/DiffuseColor`。
3. 删除或关闭 tangent normal 默认输出。
4. 删除整体遮罩默认输出，改成消费者直接读具体语义。
5. 删除未命名 reserved custom。
6. 重做用户 UI：RendererFeature 只做全局输出和 debug，对象/集合标记回到 `HoAovGroup` 这类局部入口。
7. 更新 debug view，只显示仍存在的 Buffer 项。
8. 再迁 ScreenProcess / ImageProcess 消费侧。
9. 每次新增未来效果时，对照 HDRP/UE 参考文档复核它应该进入 Buffer、ScreenProcess、ImageProcess 还是 feature 私有资源。

---

## 9. 2026-05-25 执行记录

已先完成 HoAOV 运行时文件拆分，作为后续 MaterialBuffer / GeometryBuffer 收敛前的可维护性整理：

- `HoAovRendererFeature` 只保留 settings、pass enqueue、材质生命周期和 debug 按需加载决策。
- `HoAovOutputPass` 承担 AOV/SSS source 输出与 RenderGraph 资源发布。
- `HoAovDebugPass` 承担 debug overlay，只有 debug mode 打开且 debug shader/material 可用时入队。
- `HoAovRenderTargets` / `HoAovRenderGraphResources` 集中管理兼容路径 RTHandle 与 RenderGraph texture handle。
- `HoAovDebug.shader` 已移动到 `Runtime/AOV/Shaders/Debug/`，保持 feature-local debug 归属，并同步更新显式 debug shader collection 路径。

这一步没有改动现有 MRT 语义布局；下一步再按真实消费者删除或重命名旧 HoAOV 通道。
