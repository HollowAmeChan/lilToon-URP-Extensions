# 用户向 RendererFeature 使用与顺序

> 本文面向使用者和后续 UI 实现。目标是让用户知道 Renderer Data 里要加哪些 Feature、按什么顺序放、每个模块在哪里配置、出问题看哪个 debug。

---

## 0. 命名口径

迁移期 Unity 里仍可能看到旧类名。用户文档和新 UI 使用新语义名，并在括号里保留旧名：

| 用户侧名称 | 旧实现名 | 作用 |
| --- | --- | --- |
| `ShadowCast` | `HoShadowCastRendererFeature` | 额外投影光源 atlas 与 receiver 数据 |
| `MaterialBuffer / GeometryBuffer` | `HoAOV` | 材质、对象、几何输入缓存 |
| `SSS` | `HoSubsurfaceScatteringRendererFeature` | 屏幕空间皮肤/材质散射 |
| `OIT` | `WeightedOITRendererFeature` | 加权透明合成 |
| `CharacterSpecialization` | `HoCharacterSpecializationRendererFeature` | 眼透、前发、角色局部合成 |
| `ScreenProcess` | `HoPostProcessRendererFeature` | 读取 Buffer 的语义屏幕处理 |
| `ImageProcess` | `ShoostPostProcessRendererFeature` | 最终图像处理链 |

旧名只用于兼容现有 Unity 类型和资源。用户侧主描述不再把 `HoPost`、`ShoostStack` 当作概念名。

---

## 1. 推荐 RendererFeature 顺序

Renderer Data 中推荐顺序：

```text
1. ShadowCast
2. MaterialBuffer / GeometryBuffer
3. SSS
4. OIT
5. CharacterSpecialization
6. ScreenProcess
7. ImageProcess
```

对应执行意图：

- `ShadowCast` 在 forward receiver 绘制前发布 shadow atlas 和采样参数。
- `MaterialBuffer / GeometryBuffer` 在 opaque 后输出材质、对象和几何输入。
- `SSS` 读取 Buffer，完成 source / diffusion / composite。
- `OIT` 处理透明对象，与材质 receiver 保持一致。
- `CharacterSpecialization` 做眼透、前发和角色局部合成。
- `ScreenProcess` 读取 Buffer 做轮廓、边缘光、投影、对象/材质定向处理。
- `ImageProcess` 最后只处理当前画面，如调色、CRT、颗粒、像素化、锐化。

不要把 `ImageProcess` 放在 `ScreenProcess` 前面。ImageProcess 是最终画面链，不应该再生产对象或材质语义。

---

## 2. 每个模块怎么配置

### ShadowCast

用户只在 Renderer Data 的 `ShadowCast` Feature 里配置：

- 是否启用。
- Game View / Scene View 是否启用。
- 自动收集可见灯光。
- caster layer。
- atlas 分辨率。
- PCSS 质量与强度。
- debug view。

默认不需要在场景里创建 `HoShadowCastController`。
Feature 已从当前 camera 的 URP visible lights 自动生成参与列表。第一阶段规则是：跳过 URP main light，只收集当前 camera 可见、类型匹配、启用且 `Light.shadows != None` 的 directional / spot / point light，并使用 Feature 上的 caster layer、atlas、PCSS 和第二方向光设置。

`HoShadowCastController` 现在是 legacy override / 高级手动列表入口。只有在 RendererFeature 打开 `Use Active Controller Override` 且场景里存在 active controller 时，才由 controller 的灯光列表和旧参数覆盖 Feature 设置。

Debug 重点看：

- 当前参与灯光。
- 跳过原因。
- slice 范围。
- `ShadowAtlas` / `SecondDirectionalAtlas`。
- receiver 是否在 feature 关闭后恢复正常。

### MaterialBuffer / GeometryBuffer

RendererFeature 只配置全局输出：

- 是否启用。
- layer / render queue 范围。
- render scale。
- 输出哪些 Buffer 项。
- debug view。
- fallback / pass event 等高级兼容项。

对象语义不在 RendererFeature 里批量管理。用户应在对象或集合上配置：

- `HoAovGroup`：普通入口，用于角色、脸、前发、眼睛、配件等集合标记。
- `HoAovSubject`：高级/兼容入口，用于 MPB 覆盖或旧流程。
- Material Inspector：配置材质自己的 surface color、thickness、curvature、material class/profile 等默认语义。

Debug 重点看：

- `MaterialBuffer.SurfaceColor / DiffuseColor`。
- `MaterialBuffer.ObjectId / GroupId / CharacterPart`。
- `MaterialBuffer.Thickness / Curvature / MaterialProfile`。
- `GeometryBuffer.Depth / Normal`。

### SSS

用户在 SSS Feature 或 Volume/Profile 中配置：

- 启用状态。
- quality / sample budget。
- diffusion profile。
- master strength。
- source / composite debug。

SSS 不拥有 MaterialBuffer。它消费 `SurfaceColor/DiffuseColor`、thickness、curvature、profile、depth、normal。

Debug 重点看：

- source 是否来自 `SurfaceColor/DiffuseColor`。
- diffusion 是否只覆盖目标材质。
- composite weight 是否由 SSS 自己维护，不反写 MaterialBuffer。

### OIT

用户启用 OIT 后，透明材质仍应按自己的材质语义接收 ShadowCast。
OIT 不生产 MaterialBuffer / GeometryBuffer，也不拥有 ShadowCast atlas。

Debug 重点看：

- accumulation / revealage。
- composite 顺序。
- 透明对象是否仍能读取 ShadowCast receiver 数据。

### CharacterSpecialization

用户在对象侧用 `HoAovGroup` 标记 Face / FrontHair / Eye / EyeRevealArea 等集合。
Feature 或 Volume 只控制眼透、前发投影、局部合成参数。

Debug 重点看：

- Face、FrontHair、Eye 标记是否写入。
- 捕获 pass 是否命中材质的 `HoCharacterCapture` pass。
- composite 是否在 ScreenProcess 前后符合预期。

### ScreenProcess

ScreenProcess 是语义屏幕处理。
用户在 Volume/Profile 里配置对象、材质、角色定向效果。
ScreenProcess layer 按用户列表顺序执行，不再长期依赖固定内部排序。

允许读取：

- MaterialBuffer。
- GeometryBuffer。
- depth / normal。
- object id / group id / part bit。
- ShadowCast 资源，但必须是明确的 ScreenProcess receiver 或 debug。

Debug 重点看：

- 每个 layer 的输入 Buffer。
- mask rule 实际命中值。
- effect 是否误放到了 ImageProcess。

### ImageProcess

ImageProcess 是最终图像链。
用户在 Volume/Profile 里配置最终画面效果。
ImageProcess layer 按用户列表顺序执行。ImageChain 做好后，拖拽顺序就是实际 pass 顺序。

默认只处理：

```text
ImageChain.Read -> effect -> ImageChain.Write
```

不应默认读取 MaterialBuffer / GeometryBuffer / ShadowCast。
ImageProcess 不提供 semantic image pass 例外。
如果某个 ImageProcess effect 必须读 mask、MaterialBuffer、GeometryBuffer、ShadowCast、depth/normal 或对象/材质语义，它应迁到 ScreenProcess。

Debug 重点看：

- layer 顺序。
- 用户拖拽顺序是否符合预期。
- RDG 临时 RT 是否只来自 ImageChain 或局部 resource request。
- 是否有 effect 私自长期持有 RT。

---

## 3. Debug 使用方式

Debug 入口可以统一，但资源归属必须局部：

- ShadowCast debug shader 在 `Runtime/ShadowCast/Shaders/Debug/`。
- MaterialBuffer / GeometryBuffer debug shader 在 Buffer feature 自己目录。
- SSS debug shader 在 SSS 目录。
- ScreenProcess / ImageProcess 的 debug 由各自 effect 目录提供。

公共 Debug UI 只负责：

- enum / menu 自动生成。
- tile view 布局。
- tile 短命名绘制。
- replace / overlay / channel inspect 等基础显示。

公共 Debug UI 不拥有 feature 的 debug shader、debug material 或 collector。
重 debug shader 默认不编译，用户显式启用 debug profile / define / shader collection 后才进入收集。

---

## 4. 用户排障顺序

### 没有阴影

1. 看 `ShadowCast` 是否启用。
2. 看运行时参与灯光列表是否为空。
3. 看跳过原因是否是 layer、main light、容量不足或类型不支持。
4. 打开 `ShadowAtlas` debug。
5. 检查材质 receiver 是否包含 ShadowCast sampling。

### Buffer 没有对象标记

1. 看对象是否被 `HoAovGroup` 命中。
2. 看 group priority 是否被其它组覆盖。
3. 看 RendererFeature layer / queue 是否包含该对象。
4. 打开 `ObjectId / GroupId / CharacterPart` debug。
5. 只有高级覆盖才检查 `HoAovSubject`。

### SSS 不生效

1. 看 `SurfaceColor/DiffuseColor` 是否有数据。
2. 看 thickness / profile 是否写入。
3. 看 depth / normal 是否有效。
4. 打开 SSS source / diffusion / composite debug。

### ScreenProcess 效果命中错误

1. 看 layer 是否启用。
2. 看它读的是哪个 Buffer 项。
3. 打开该 feature 自己的 mask/debug view。
4. 检查是否把本该属于 ScreenProcess 的效果放进了 ImageProcess。

### ImageProcess 画面异常

1. 暂停所有 ImageProcess layer，只开单个效果。
2. 检查 effect 是否需要额外 RT。
3. 检查是否读写同一个 camera color。
4. 检查是否误读 MaterialBuffer / GeometryBuffer / ShadowCast / mask；如果需要这些输入，应迁到 ScreenProcess。

---

## 5. UI 设计底线

- RendererFeature 面板只展示全局开关、运行状态、debug 和高级兼容项。
- 对象/集合标记在对象 inspector 里展开，不做全局大集合批量编辑主入口。
- ShadowCast 不要求用户创建场景组件才能工作。
- 每个 feature 的 debug 面板只展示本 feature 的 debug view。
- ImageProcess UI 不显示 AOV mask、semantic mask 或 object/material rule。
- 所有重 debug shader 都需要显式启用。
- Render pass event 默认值优先正确，用户只在高级区调整。
- UI 文案使用用户能理解的语义名，同时在括号里标旧实现名。

---

## 6. 验收清单

- 新用户只看本文能完成 Renderer Data 添加和顺序排列。
- 没有 `HoShadowCastController` 的场景也能产生 ShadowCast 参与列表。
- AOV/Buffer 对象语义主要从 `HoAovGroup` 这类局部对象 UI 编辑。
- RendererFeature Inspector 不再变成全场景对象大列表。
- Debug tile 能显示每个 feature 自己声明的短命名。
- ImageProcess 不读取 MaterialBuffer / GeometryBuffer / ShadowCast，也没有 AOV mask UI。
- 关闭 debug profile 后不会编译重 debug shader。
- Frame Debugger / RenderDoc 中的 pass 名能对应本文顺序。
