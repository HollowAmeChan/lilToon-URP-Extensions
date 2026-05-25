# 用户向 RendererFeature 使用与顺序

> 本文面向使用者和后续 UI 实现。目标是让用户知道 Renderer Data 里要加哪些 Feature、按什么顺序放、每个模块在哪里配置、出问题看哪个 debug。

---

## 0. 命名口径

迁移期 Unity 里仍可能看到部分旧类名。用户文档和新 UI 使用新语义名：

| 用户侧名称 | Renderer Data 显示名 | 实现类 | 作用 |
| --- | --- | --- | --- |
| `ShadowCast` | `Ho-ShadowCast` | `HoShadowCastRendererFeature` | 额外投影光源 atlas 与 receiver 数据 |
| `MetadataBuffer` | `Ho-MetadataBuffer` | `HoMetadataBufferRendererFeature` | 材质、对象、mask、metadata 与当前 SSS source 输入 |
| `GeometryBuffer` | `Ho-GeometryBuffer` | `HoGeometryBufferRendererFeature` | normal/depth 几何输入缓存 |
| `SSS` | `Ho-SubsurfaceScattering` | `HoSubsurfaceScatteringRendererFeature` | 屏幕空间皮肤/材质散射 |
| `OIT` | `Ho-WeightedOIT` | `WeightedOITRendererFeature` | 加权透明合成 |
| `CharacterSpecialization` | `Ho-CharacterSpecialization` | `HoCharacterSpecializationRendererFeature` | 眼透、前发、角色局部合成 |
| `ScreenProcess` | `Ho-ScreenProcess` | `ScreenProcessRendererFeature` | 读取 Buffer 的语义屏幕处理 |
| `ImageProcess` | `Ho-ImageProcess` | `ImageProcessRendererFeature` | 最终图像处理链 |

旧名只用于引用历史迁移记录。用户侧主描述不再把 `HoPost`、`ShoostStack` 当作概念名。
RendererFeature 防重名显示、Frame Debugger pass 名、对应 Volume 菜单和缺 shader 日志统一使用 `Ho-<规范模块名>` 前缀，模块名本身不带空格；shader Hidden 名和包名不跟随这条用户显示名规则。

---

## 1. 推荐 RendererFeature 顺序

Renderer Data 中推荐顺序：

```text
1. ShadowCast
2. MetadataBuffer
3. GeometryBuffer
4. SSS
5. OIT
6. CharacterSpecialization
7. ScreenProcess
8. ImageProcess
```

对应执行意图：

- `ShadowCast` 在 forward receiver 绘制前发布 shadow atlas 和采样参数。
- `MetadataBuffer` 在 opaque 后输出材质、对象、mask 和 surface metadata。
- `GeometryBuffer` 在 opaque 后输出 normal/depth，供 SSS、CharacterSpecialization 和 ScreenProcess 等消费者读取。
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
- light GameObject layer / 参与灯光对象层。
- light Rendering Layer / 参与灯光渲染层。
- caster GameObject layer。
- caster Rendering Layer。
- atlas 分辨率。
- PCSS 质量与强度。
- debug view。

Feature 已从当前 camera 的 URP visible lights 自动生成参与列表。当前规则是：跳过 URP main light，只收集当前 camera 可见、类型匹配、启用、GameObject layer 命中、`Light.renderingLayerMask` 命中且 `Light.shadows != None` 的 directional / spot / point light。绘制每个 shadow slice 时再按该灯光 rendering layer 与 Feature 的 caster Rendering Layer 交集过滤 caster，并使用 Feature 上的 atlas、PCSS 和第二方向光设置。

旧 `HoShadowCastController` 场景组件与 active controller override 已删除。手动灯光列表不再作为 ShadowCast 的普通或高级入口；需要参与 ShadowCast 的灯光必须进入当前 camera 的 URP visible lights，并通过 Feature 上的 GameObject layer 与 Rendering Layer 过滤。

Debug 重点看：

- 当前参与灯光。
- 跳过原因。
- slice 范围。
- `ShadowAtlas` / `SecondDirectionalAtlas`。
- receiver 是否在 feature 关闭后恢复正常。

### MetadataBuffer / GeometryBuffer

RendererFeature 只配置各自的全局输出：

- 是否启用。
- layer / render queue 范围。
- render scale。
- `MetadataBuffer` 输出哪些材质、对象、mask、metadata 项。
- `GeometryBuffer` 输出哪些 depth / normal 等几何项。
- debug view。
- fallback / pass event 等高级兼容项。

对象语义不在 RendererFeature 里批量管理。用户应在对象或集合上配置：

- `HoMetadataBufferGroup`：普通入口，用于角色、脸、前发、眼睛、配件等集合标记。
- `HoMetadataBufferSubject`：高级入口，用于 MPB 覆盖材质默认 metadata。
- Material Inspector：配置材质自己的 surface color、thickness、curvature、material class/profile 等默认语义。

Debug 重点看：

- `MaterialBuffer.SurfaceColor / DiffuseColor`。
- `MaterialBuffer.ObjectId / GroupId / CharacterPart`。
- `MaterialBuffer.Thickness / Curvature / MaterialProfile`。
- `GeometryBuffer.Depth / Normal`。

MetadataBuffer debug 不再显示 depth / normal / velocity；这些几何观察入口在 GeometryBuffer debug 里。

### SSS

用户在 SSS Feature 或 Volume/Profile 中配置：

- 启用状态。
- quality / sample budget。
- diffusion profile。
- master strength。
- source / composite debug。

SSS 不拥有 MaterialBuffer。它消费 `SurfaceColor/DiffuseColor`、thickness、curvature、profile、depth、normal。
当前 RenderGraph source pass 的硬依赖是 camera color、MetadataBuffer 的 mask/surfaceData/surfaceColor，以及 GeometryBuffer 的 normalDepth。缺少 MetadataBuffer 或 GeometryBuffer 时，SSS 会跳过该帧并在 Feature Inspector 的运行状态里显示缺失项。

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

用户在对象侧用 `HoMetadataBufferGroup` 标记 Face / FrontHair / Eye / EyeRevealArea 等集合。
Feature 或 Volume 只控制眼透、前发投影、局部合成参数。
当前 RenderGraph composite pass 的硬依赖是 camera color、MetadataBuffer 的 maskId/objectCustom0/objectCustom1，以及 GeometryBuffer 的 normalDepth。缺少 MetadataBuffer 或 GeometryBuffer 时，角色特化会跳过该帧并在 Feature Inspector 的运行状态里显示缺失项。

Debug 重点看：

- Feature Inspector 的运行状态是否显示 MetadataBuffer / GeometryBuffer 可用。
- Face、FrontHair、Eye 标记是否写入。
- 捕获 pass 是否命中材质的 `HoCharacterCapture` pass。
- composite 是否在 ScreenProcess 前后符合预期。

### ScreenProcess

ScreenProcess 是语义屏幕处理。
用户在 Volume/Profile 里配置对象、材质、角色定向效果。
ScreenProcess layer 按用户列表顺序执行，不再长期依赖固定内部排序。
当前 RenderGraph stack pass 按 active layer 聚合 Buffer 需求：`EdgeLight` / `PostLighting` 需要 MetadataBuffer mask 与 GeometryBuffer normalDepth；`DropShadow`、`useRuleMask`、`debugRuleMask` 会按 rule source 追加 surfaceData、material custom 或 object custom。缺少某项时，ScreenProcess 在 Volume Inspector 的运行状态里显示该项缺失，并按 shader 侧 fallback 降级执行。

允许读取：

- MaterialBuffer。
- GeometryBuffer。
- depth / normal。
- object id / group id / part bit。
- ShadowCast 资源，但必须是明确的 ScreenProcess receiver 或 debug。

Debug 重点看：

- Volume Inspector 的运行状态是否显示当前 layer 需要的 Buffer 项可用。
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
- MetadataBuffer / GeometryBuffer debug shader 在 Buffer feature 自己目录。
- SSS debug shader 在 SSS 目录。
- ScreenProcess / ImageProcess 的 debug 由各自 effect 目录提供。
- 各 feature 的 debug view 由自己的 `<Feature>DebugViewInfo.cs` 声明，并通过 `HoDebugViewRegistry` 汇总给公共 UI。

公共 Debug UI 只负责：

- enum / menu 自动生成。
- tile view 布局。
- tile 短命名绘制。
- replace / overlay / channel inspect 等基础显示。

公共 Debug UI 不拥有 feature 的 debug shader、debug material 或 collector。
重 debug shader 默认不编译，用户显式启用 debug profile / define / shader collection 后才进入收集。
当前已登记的 view info 覆盖 MetadataBuffer、GeometryBuffer、ShadowCast、SSS、ScreenProcess rule mask 和 ImageProcess layer chain；ScreenProcess 与 ImageProcess 条目是轻量观察入口，不进入重 debug shader collection。

---

## 4. 用户排障顺序

### 没有阴影

1. 看 `ShadowCast` 是否启用。
2. 看运行时参与灯光列表是否为空。
3. 看跳过原因是否是 layer、main light、容量不足或类型不支持。
4. 打开 `ShadowAtlas` debug。
5. 检查材质 receiver 是否包含 ShadowCast sampling。

### Buffer 没有对象标记

1. 看对象是否被 `HoMetadataBufferGroup` 命中。
2. 看 group priority 是否被其它组覆盖。
3. 看 RendererFeature layer / queue 是否包含该对象。
4. 打开 `ObjectId / GroupId / CharacterPart` debug。
5. 只有高级覆盖才检查 `HoMetadataBufferSubject`。

### SSS 不生效

1. 先看 SSS Feature Inspector 的运行状态，确认缺的是 MetadataBuffer、GeometryBuffer 还是 camera color。
2. 看 `SurfaceColor/DiffuseColor` 是否有数据。
3. 看 thickness / profile 是否写入。
4. 看 depth / normal 是否有效。
5. 打开 SSS source / diffusion / composite debug。

### ScreenProcess 效果命中错误

1. 先看 ScreenProcess Volume Inspector 的运行状态，确认当前 layer 需要的 MetadataBuffer / GeometryBuffer 项是否可用。
2. 看 layer 是否启用。
3. 看它读的是哪个 Buffer 项。
4. 打开该 feature 自己的 mask/debug view。
5. 检查是否把本该属于 ScreenProcess 的效果放进了 ImageProcess。

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
- 不需要 `HoShadowCastController` 场景组件也能产生 ShadowCast 参与列表。
- MetadataBuffer/GeometryBuffer 对象语义主要从 `HoMetadataBufferGroup` 这类局部对象 UI 编辑。
- RendererFeature Inspector 不再变成全场景对象大列表。
- Debug tile 能显示每个 feature 自己声明的短命名。
- ImageProcess 不读取 MaterialBuffer / GeometryBuffer / ShadowCast，也没有 AOV mask UI。
- 关闭 debug profile 后不会编译重 debug shader。
- Frame Debugger / RenderDoc 中的 pass 名能对应本文顺序。
