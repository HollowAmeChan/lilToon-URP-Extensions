# CharacterSpecialization 眼睛透过（Eye Reveal）

> 状态：**现行功能说明（2026 文档审核核对）**。语义输入全部经 **AC**（OB 身份池 → 部件标签 → 覆盖率 → Selection 池），几何用 **GB**，不再有 MetadataBuffer 读取。
> 相关：RDG 结构与逐趟清单见 `角色特化/Ho-CharacterSpecialization_RDG优化.md`；脸色扩散见 `角色特化/CharacterSpecialization_FaceHairDiffuse.md`。

## 1. 目标

让**被前发遮挡的眼睛按"眼睛捕获结果"透出**，并支持**按相机相对角色面部朝向的视锥角度**对眼透做区域控制。
效果为屏幕空间、Opaque 友好，复用现有角色语义输入，不依赖角色材质进入透明队列。

配套语义输入（**R3 起全部经 AC**：OB 身份池 → 部件标签 → 覆盖率 → AC Selection 池）：

| 语义 | 用途 |
|---|---|
| OB 标签 `脸`（Face） | 捕获来源（脸部颜色）：材质捕获 pass 按标签决定画不画 |
| OB 标签 `眼睛`（Eye） | 捕获来源（眼睛 alpha / 深度 / 角色 ID） |
| OB 标签 `前发`（FrontHair） | 遮挡物（值 = 该标签的覆盖率，天然带亚像素相位） |
| OB 标签 `眼透区`（EyeRevealArea） | 可选透出区域限制 |
| OB 身份池层 0 的**组字节** | 与眼睛捕获里的角色 ID 做同角色判定（两者都来自 RSUV 的组字节） |
| `GeometryBuffer NormalDepth.a` | "前发在眼睛前方"的深度判断 |

语义渠道 = **AC 的 Selection 池**（`_HoACSelection{0..3}Texture`，`(SemanticId, coverage)` 固定 lane）；角色特化把池转置成自己的两张位平面（`_lilHoCharacterObjectSemantic0_3Texture` / `4_7Texture`，`HoCharacterObjectSemantic.shader`），
通道布局与从前的 `objectCustom0_3` / `objectCustom4_7` 一致：`全角色 / 脸 / 前发 / 眼睛` + `眼透区 / 配件 / 人体 / 预留`。
**不再有「读取抗锯齿掩码」开关**：位平面的值是覆盖率之和（MSAA resolve 的产物），本身就是连续场。

## 2. 工作原理（原始链路）

1. **捕获**（`HoCharacterCaptureCommon.hlsl`）：材质 pass 调用 `LilHoCharacterBuildCaptureOutput`，
   先按 RSUV 上的 `partId` 查 OB 部件行表拿到**标签**，再看 `_HoCharacterCaptureMode` 分两遍写入捕获 RT：
   - 模式 1（Face）→ `eyeColor`：带 `脸` 标签的部件写脸部颜色（预乘 alpha）；
   - 模式 2（Eye）→ `eyeData`：带 `眼睛` 标签的部件写眼睛 alpha(R)、线性深度(G)、角色 ID(B)，均预乘。
   没有 OB 身份（RSUV = 0 / 表里没有这一行）的物件标签恒 0 ⇒ 两遍都不画。
2. **合成**（`Composite`，全屏 RDG raster pass）：逐像素计算揭示遮罩
   `revealMask = frontHair × eyeAlpha × revealArea × hairInFront × sameCharacter × 强度`，
   最终 `color = lerp(源画面, eyeColor.rgb, revealMask)`。

**眼透的"不透明度"就是 revealMask**——羽化、扩张、深度、同角色判定都收敛在这一项，角度修正也是乘在它上面。

## 3. 相机角度修正

### 3.1 角度定义

| 名称 | 定义 | 值域 |
|---|---|---|
| 平转角（yaw） | `atan2(dot(vdir, 右轴), dot(vdir, 脸前轴))`，相机方向在"脸前-右"平面内的水平转动 | ±180° |
| 俯仰角（pitch） | `atan2(dot(vdir, 上轴), dot(vdir, 脸前轴))`，相机方向在"脸前-上"平面内的转动 | ±180° |

其中 `vdir = normalize(相机位置 − 面部朝向参考点)`，三根轴为角色"面部朝向"的世界朝向（见 4.1）。
`atan2` 全角域定义，无 `asin` 的 ±90° 截断与奇异映射。

### 3.2 视锥判定与因子

- **视锥内**：`|平转角| ≤ 平转半角范围` 且 `|俯仰角| ≤ 俯仰半角范围` → 因子 = 1（眼透行为不变）。
- **视锥外**：任一轴越界 → 因子 = 0（眼透关闭）。
- **边缘过渡**：每轴 `1 − smoothstep(1 − 柔化/范围, 1, |角|/范围)`；柔化为 0 时是硬边二元。
- **强度**：`因子 = lerp(1, 视锥内因子, 强度)`，1 = 视锥外完全关闭，0 = 不生效。

最终 `revealMask × angleFactor` **只作用于眼透颜色混合**，前发投影的 receiver 与 debug 3 的原始 revealMask 不受影响。

### 3.3 当前数据流（GD ReferenceFrame）

```
GD ReferenceFrame：骨骼/空物体 + 前/右/上轴
  -> 动态世界参考系（独立于相机）
GD：Renderer 作用列表 -> 世界 FrameData
眼透消费适配器：OB 完整身份 -> FrameData 行
  -> Composite pass 声明 frame / 身份索引 buffer 读依赖
  -> 捕获本次相机位置，执行时绑定
  -> shader 计算 yaw/pitch -> angleFactor
  -> revealMask × angleFactor
```

每相机的角度纹理与 `HoCharacterEyeAngleTable` 已删除。参考系未改变时共用上传，相机只作为消费参数；相机参数在 RenderGraph 记录时捕获，不延迟读取会变化的 Camera 对象。

在任意对象上添加 `Rendering/Ho-GeometryData ReferenceFrame`，设置参考骨骼、轴和扁平 Renderer 作用列表。OB 不保存 GD 引用；眼透适配器将 Renderer 的 GD 输出关联到 AC 层 0 的完整身份。眼透发生在前发遮挡像素，前发 Renderer 必须包含在作用列表中。旧场景由用户自行配置，不保留旧朝向字段的兼容读取。

世界参考系与身份索引 buffer 由眼透消费适配器管理寿命，域重载/Play 边界释放后按需重建。未提供或禁用参考系时角度因子为 1。其他眼透遮罩与混色公式保持原行为。

## 4. 配置

### 4.1 GD 参考系与 OB 身份

| 字段 | 说明 |
|---|---|
| GD 的方向来源 | 骨骼或空物体 Transform，留空不参与角度修正 |
| GD 的作用 Renderer | MeshRenderer / SkinnedMeshRenderer 扁平列表，无别名；空列表只尝试组件同对象上的 Renderer |
| 脸前轴 | 局部轴枚举，默认 **+Z (Forward)** |
| 右轴 | 局部轴枚举，默认 **+X (Right)** |
| 上轴 | 局部轴枚举，默认 **+Y (Up)** |
| OB 组 ID | 角色身份，**自动分配**；不再是参考系表行号 |
| 部件的「标签」 | 决定捕获画不画：`脸` → 脸捕获（MRT0），`眼睛` → 眼捕获（MRT1）；`前发 / 眼透区` 决定屏幕空间那两把门 |

GD 可与 OB 分开挂载。不同 GD 来源争用同一 Renderer 或同一屏幕身份时关联无效，没有覆盖优先级。共用 OB 部件 ID 的 Renderer 在屏幕中无法区分，独立作用范围需分开部件身份。

### 4.2 Volume / RendererFeature 参数（默认 90 / 60 / 40）

| 参数 | 默认 | 说明 |
|---|---|---|
| 启用相机角度修正 | false | 关闭时完全透明（factor 恒 1，与旧版一致） |
| 角度修正强度 | 1 | 视锥外衰减总量；1 = 完全关闭视锥外眼透 |
| 平转半角范围 | 90° | 水平转动半角；0 = 只要偏一点就视锥外，180 = 全向 |
| 俯仰半角范围 | 60° | 俯仰半角；同上 |
| 角度柔化 | 40° | 视锥边缘过渡带（度）；0 = 硬边二元 |

修改默认值请同步 `HoCharacterSpecializationSettings`（特征默认）、`HoCharacterSpecializationVolume`（Volume 默认）两处。

## 5. 调试

`HoCharacterSpecializationDebugMode` 新增两项：

| 模式 | 内容 |
|---|---|
| `EyeAngleFactor (16)` | 视锥因子灰度（视锥内白、视锥外黑） |
| `ReferenceFrameView (17)` | 当前参考系视角：R = \|平转角\|/180、G = \|俯仰角\|/180、B = 有效参考系的修正强度；未关联的表面为黑色 |

**轴向校准判读**（debug 17）：相机在**正脸**应 R≈0 且 G≈0；绕侧转 90° 时 R≈0.5；俯仰 90° 时 G≈0.5。
若某项对不上，调整四个轴向枚举之一（常见约定 +Z 脸前 / +X 右 / +Y 上）。

## 6. 排障速查

| 现象 | 原因 | 处理 |
|---|---|---|
| debug 17 黑色 | 表面未关联有效参考系：未列为作用 Renderer、缺骨骼、未启用、无 OB 身份或来源冲突 | 检查 GD 的方向来源、作用列表和启用状态；前发需加入列表；确认 OB 身份已刷新 |
| debug 17 正面纯蓝（R/G≈0、B=1） | 有效参考系正对相机，属预期 | 转相机到侧面检查 R/G 响应 |
| debug 16 恒白 | 因子=1：相机在视锥内，或强度为 0，或修正未启用 | 转相机越过视锥边界；确认开关与强度 |
| 完全没有眼透 | OB 身份池没产出（feature 不在 renderer / RSUV 没刷新），或部件没打 `脸` / `眼睛` / `前发` 标签 | 看 feature 面板的输入自检（OB 身份池 / OB 语义位平面）；给部件打好标签后刷新 RSUV |
| 编辑模式下转"摄像机物体"画面不变 | 编辑时 Scene 视图渲染的是**预览相机**，拖动相机物体不触发其渲染 | 旋转 **Scene 视图视角** 验证；或进 Play / 打开 Game 视图 |
| Play 下转 Scene 视图不起作用 | Game 视口使用游戏相机的位置求值，属预期 | 在游戏相机视口或 Play 内转真实相机 |
| 多角色互相影响 | 两个角色的部件被分到了同一个 OB 组（同一个 `HoObjectBufferGroup`） | 每个角色一个组件（组 ID 自动分配） |

## 7. 执行边界

- 未提供朝向参考系或未启用时，行 (0,0) → factor=1，行为与旧版一致。
- **语义全部来自 OB**：OB feature 不在 renderer 里、或本相机没有 OB 产出时，整支合成 no-op（不是"退化成硬边"）。
- 无每相机角度表；世界参考系共享，相机位置由当前 pass 显式传入。眼透消费者持有资源引用，最后一个消费者 Dispose 后释放。
- 角度修正只衰减 `revealMask`，不影响眼睛捕获本身与前发投影 receiver；debug 3 仍显示未修正的 revealMask。
- 双视图同帧各自渲染时，各自画面使用各自 pass 的相机位置，不需要额外配置。
