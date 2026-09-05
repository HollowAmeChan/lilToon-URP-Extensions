# CharacterSpecialization 眼睛透过（Eye Reveal）

## 1. 目标

让**被前发遮挡的眼睛按"眼睛捕获结果"透出**，并支持**按相机相对角色面部朝向的视锥角度**对眼透做区域控制。
效果为屏幕空间、Opaque 友好，复用现有角色语义输入，不依赖角色材质进入透明队列。

配套语义输入：

| 语义 | 用途 |
|---|---|
| `ObjectCustom1` / Face | 捕获来源（脸部颜色） |
| `ObjectCustom3` / Eye | 捕获来源（眼睛 alpha / 深度 / 角色 ID） |
| `ObjectCustom2` / FrontHair | 遮挡物 |
| `ObjectCustom4` / EyeRevealArea | 可选透出区域限制 |
| `MetadataBuffer MaskId.g` | 与眼睛捕获内的角色 ID 做同角色判定 |
| `GeometryBuffer NormalDepth.a` | "前发在眼睛前方"的深度判断 |

## 2. 工作原理（原始链路）

1. **捕获**（`HoCharacterCaptureCommon.hlsl`）：材质 pass 调用 `LilHoCharacterBuildCaptureOutput`，
   按 `_HoCharacterCaptureMode` 分两遍写入捕获 RT：
   - 模式 1（Face）→ `eyeColor`：脸部颜色（预乘 alpha）；
   - 模式 2（Eye）→ `eyeData`：眼睛 alpha(R)、线性深度(G)、角色 ID(B)，均预乘。
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

### 3.3 数据流与实现说明

```
HoMetadataBufferGroup
  ├─ 面部朝向（Transform，骨骼或空物体均可）
  ├─ 脸前轴 / 右轴 / 上轴（局部轴枚举，默认 +Z / +X / +Y）
  └─ CharacterId（0..255，多角色必须唯一）
        │  TryGetWorldFacing() 输出世界朝向（SDF 等消费者复用同一入口）
        ▼
HoCharacterEyeAngleTable（RendererFeature 持有）
  ├─ 每个渲染相机各一张 256×1 RGBAFloat 表（行号 = CharacterId）
  ├─ AddRenderPasses 时机（URP17 fork 主路径唯一保证被调用的时机）：
  │    CPU 按当前渲染相机算每角色 (平转角°, 俯仰角°)
  │    → 上传该相机的表 → SetGlobalTexture 绑定为全局 _lilHoCharacterEyeAngleTable
  └─ 相机销毁自动清理
        ▼
Composite
  ├─ 按"眼睛捕获的角色 ID"（eyeData.b / eyeData.r）查表（与 SameCharacter 同源，避免错位）
  ├─ 曲线 → angleFactor
  └─ revealMask × angleFactor
```

**为什么是"每相机表 + 渲染前绑定"**（环境约束已逐一核实）：

- `RenderTexture` 没有任何 CPU 写 API（`SetPixelData`/`SetPixels` 均不存在）；
- `RenderGraph.ImportTexture` 只接受 `RTHandle`（不接受 `Texture2D`）；
- `RasterCommandBuffer.SetGlobalTexture` 只接受 `TextureHandle`（不接受裸 `Texture`）。

因此采用 **CPU 侧 `Texture2D`（唯一可靠的 SetPixelData 上传路径）+ 当前相机渲染前全局绑定**。
每个相机的"写表 → 本相机 composite 读取"在同一渲染循环内顺序成对，**多窗口（Scene + Game）、多屏、录制相机各自正确，无需任何"活动相机"判定，也无需区分 Play/编辑模式**。
⚠️ 不要改成 Unsafe pass + `RenderTargetIdentifier(Texture2D)` 绑定——实测会把表读成全黑。

## 4. 配置

### 4.1 HoMetadataBufferGroup（数据入口）

| 字段 | 说明 |
|---|---|
| 面部朝向 | **Transform**：确定角色面部朝向的参考，可以是骨骼，也可以是朝向正确的空物体；留空 = 该角色不参与角度修正。该输入不只服务眼透，未来 SDF 等消费者应复用 `TryGetWorldFacing()` |
| 脸前轴 | 局部轴枚举，默认 **+Z (Forward)** |
| 右轴 | 局部轴枚举，默认 **+X (Right)** |
| 上轴 | 局部轴枚举，默认 **+Y (Up)** |
| 角色组 ID (CharacterId) | 表行号；**多角色必须各自唯一**，相同 ID 互相覆盖（后写者胜） |

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
| `EyeAngleTable (17)` | 表原始数据：R = \|平转角\|/180、G = \|俯仰角\|/180、B = 强度（>0 表示参数已进入渲染） |

**轴向校准判读**（debug 17）：相机在**正脸**应 R≈0 且 G≈0；绕侧转 90° 时 R≈0.5；俯仰 90° 时 G≈0.5。
若某项对不上，调整四个轴向枚举之一（常见约定 +Z 脸前 / +X 右 / +Y 上）。

## 6. 排障速查

| 现象 | 原因 | 处理 |
|---|---|---|
| debug 17 恒纯蓝（R/G=0、B=1） | 表行未写入有效角度：面部朝向为空 / 轴向异常 / CharacterId 不匹配 | 检查 Group 的面部朝向与三轴；检查 charId 是否与眼睛捕获一致 |
| debug 16 恒白 | 因子=1：相机在视锥内，或强度为 0，或修正未启用 | 转相机越过视锥边界；确认开关与强度 |
| 编辑模式下转"摄像机物体"画面不变 | 编辑时 Scene 视图渲染的是**预览相机**，拖动相机物体不触发其渲染 | 旋转 **Scene 视图视角** 验证；或进 Play / 打开 Game 视图 |
| Play 下转 Scene 视图不起作用 | 每相机各自表，Game 相机的画面使用游戏相机的表，属预期 | 在游戏相机视口或 Play 内转真实相机 |
| 多角色互相影响 | CharacterId 重复 | 分配唯一 CharacterId |

## 7. 执行边界

- 未提供面部朝向或未启用时，行 (0,0) → factor=1，行为与旧版一致。
- 每相机表惰性创建（RendererFeature Create 后首次渲染），Dispose 时释放；相机销毁自动清理。
- 角度修正只衰减 `revealMask`，不影响眼睛捕获本身与前发投影 receiver；debug 3 仍显示未修正的 revealMask。
- 双视图同帧各自渲染时，各自画面使用各自相机的表（写读成对），不需要额外配置。
